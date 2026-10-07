using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdminDesk.Api;
using AdminDesk.Api.Filters;
using AdminDesk.Api.Middleware;
using AdminDesk.Application.Abstractions;
using AdminDesk.Infrastructure.Configuration;
using AdminDesk.Infrastructure.Logging;
using AdminDesk.SharedKernel.Constants;
using FluentValidation;
using Serilog;

const string CorsPolicyName = "AdminDeskCors";
const string LogOutputTemplate =
    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] {SourceContext} {Message:lj}{NewLine}{Exception}";

var infrastructureAssembly = typeof(StoragePaths).Assembly;
var apiAssembly = typeof(Program).Assembly;
var applicationAssembly = typeof(IStartupTask).Assembly;

// Reports raised while the log sinks are being configured are held here until the
// host logger exists; afterwards they go straight to the logger.
var pendingReports = new List<(string Message, Exception? Error)>();
var hostLoggerReady = false;

void ReportSinkFailure(string message, Exception? error)
{
    if (!hostLoggerReady)
    {
        lock (pendingReports)
        {
            pendingReports.Add((message, error));
        }
        return;
    }
    Log.ForContext(LogConstants.SkipDbSinkProperty, true).Warning(error, "{Message}", message);
}

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    var logsDirectory = StoragePaths.LogsDirectory(context.HostingEnvironment.ContentRootPath);

    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: LogOutputTemplate)
        .WriteTo.File(
            Path.Combine(logsDirectory, "adminDesk-.log"),
            outputTemplate: LogOutputTemplate,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            shared: true);

    foreach (var type in DiscoverTypes<ILogSinkContributor>(infrastructureAssembly))
    {
        try
        {
            var contributor = (ILogSinkContributor)Activator.CreateInstance(type)!;
            contributor.Configure(loggerConfiguration, context.Configuration, ReportSinkFailure);
        }
        catch (Exception ex)
        {
            ReportSinkFailure($"Log sink contributor {type.Name} could not be configured", ex);
        }
    }
});

var jsonNaming = JsonNamingPolicy.CamelCase;

builder.Services
    .AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = jsonNaming;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
    });

// The exception middleware writes the envelope with the same JSON rules as the controllers.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = jsonNaming;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
});

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

var allowedOrigins = builder.Configuration.GetSection(ConfigKeys.CorsAllowedOrigins).Get<string[]>();
if (allowedOrigins is null || allowedOrigins.Length == 0)
{
    allowedOrigins = new[] { "http://localhost:4200" };
}

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders(CorrelationIdMiddleware.HeaderName));
});

builder.Services.AddValidatorsFromAssemblies(new[] { applicationAssembly, apiAssembly });
builder.Services.AddSingleton<ICorrelationIdAccessor, AsyncLocalCorrelationIdAccessor>();

foreach (var type in DiscoverTypes<IServiceModule>(infrastructureAssembly, apiAssembly))
{
    var module = (IServiceModule)Activator.CreateInstance(type)!;
    module.ConfigureServices(builder.Services, builder.Configuration);
}

var pipelineModules = DiscoverTypes<IPipelineModule>(infrastructureAssembly, apiAssembly)
    .Select(type => (IPipelineModule)Activator.CreateInstance(type)!)
    .ToList();

try
{
    var app = builder.Build();

    // Creating the logger factory forces the logger configuration to run, so the
    // sink reports collected so far can be written now.
    app.Services.GetRequiredService<ILoggerFactory>();
    hostLoggerReady = true;
    lock (pendingReports)
    {
        foreach (var (message, error) in pendingReports)
        {
            Log.ForContext(LogConstants.SkipDbSinkProperty, true).Warning(error, "{Message}", message);
        }
        pendingReports.Clear();
    }

    if (app.Configuration.GetValue<bool>(ConfigKeys.DemoEnabled) && app.Environment.IsProduction())
    {
        app.Logger.LogWarning("Demo mode is enabled in a Production environment. Demo accounts and sample data must not be used outside a local demo.");
    }

    // Startup tasks run one at a time, each inside its own scope, before requests are accepted.
    var taskTypes = new List<(int Order, Type Type)>();
    await using (var discoveryScope = app.Services.CreateAsyncScope())
    {
        foreach (var task in discoveryScope.ServiceProvider.GetServices<IStartupTask>())
        {
            taskTypes.Add((task.Order, task.GetType()));
        }
    }

    foreach (var (_, taskType) in taskTypes.OrderBy(t => t.Order).ThenBy(t => t.Type.FullName, StringComparer.Ordinal))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var task = scope.ServiceProvider.GetServices<IStartupTask>().First(t => t.GetType() == taskType);
        app.Logger.LogInformation("Running startup task {Task}", taskType.Name);
        await task.RunAsync(app.Lifetime.ApplicationStopping);
    }

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<ExceptionHandlingMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseCors(CorsPolicyName);

    foreach (var module in pipelineModules.Where(m => m.Order < 500).OrderBy(m => m.Order))
    {
        module.UseMiddleware(app);
    }

    app.UseAuthentication();
    app.UseAuthorization();

    foreach (var module in pipelineModules.Where(m => m.Order >= 500).OrderBy(m => m.Order))
    {
        module.UseMiddleware(app);
    }

    app.MapControllers();

    foreach (var module in pipelineModules.OrderBy(m => m.Order))
    {
        module.MapEndpoints(app);
    }

    await app.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "The host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Public types of the given assemblies that implement T, created through a public
// parameterless constructor, ordered by their Order value and then by type name.
static List<Type> DiscoverTypes<T>(params Assembly[] assemblies)
{
    var found = assemblies
        .SelectMany(a => a.GetTypes())
        .Where(t => typeof(T).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false } && t.GetConstructor(Type.EmptyTypes) != null)
        .Distinct()
        .Select(t => (Type: t, Order: OrderOf<T>(t)))
        .OrderBy(x => x.Order)
        .ThenBy(x => x.Type.FullName, StringComparer.Ordinal)
        .Select(x => x.Type)
        .ToList();
    return found;
}

static int OrderOf<T>(Type type)
{
    try
    {
        var instance = Activator.CreateInstance(type)!;
        return instance switch
        {
            IServiceModule m => m.Order,
            IPipelineModule p => p.Order,
            ILogSinkContributor c => c.Order,
            _ => 0
        };
    }
    catch
    {
        return 0;
    }
}

public partial class Program;
