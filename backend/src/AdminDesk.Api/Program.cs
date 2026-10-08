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

    var contributors = DiscoverInstances<ILogSinkContributor>(
        (type, ex) => ReportSinkFailure($"Log sink contributor {type.Name} could not be configured", ex),
        infrastructureAssembly);
    foreach (var contributor in contributors)
    {
        try
        {
            contributor.Configure(loggerConfiguration, context.Configuration, ReportSinkFailure);
        }
        catch (Exception ex)
        {
            ReportSinkFailure($"Log sink contributor {contributor.GetType().Name} could not be configured", ex);
        }
    }
});

// Request bodies here are small JSON documents; nothing legitimate comes close to this.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1_048_576);

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

foreach (var module in DiscoverInstances<IServiceModule>(null, infrastructureAssembly, apiAssembly))
{
    module.ConfigureServices(builder.Services, builder.Configuration);
}

var pipelineModules = DiscoverInstances<IPipelineModule>(null, infrastructureAssembly, apiAssembly);

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
        if (!app.Configuration.GetValue<bool>(ConfigKeys.DemoAllowInProduction))
        {
            // Demo mode publishes the demo accounts and their shared password; refuse to start
            // rather than serve them from a Production host by accident.
            throw new InvalidOperationException(
                $"{ConfigKeys.DemoEnabled} is true in a Production environment. Turn demo mode off, or set " +
                $"{ConfigKeys.DemoAllowInProduction} to true if this host is meant to be a public demo.");
        }
        app.Logger.LogWarning("Demo mode is enabled in a Production environment (explicitly allowed). Demo accounts and sample data are served to anyone who can reach this host.");
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

    // Unknown routes answer 404 for everyone instead of a sign-in challenge.
    app.MapFallback(() => Results.Json(
        AdminDesk.SharedKernel.Responses.ApiResponse.Fail(ErrorCodes.NOT_FOUND, "Resource not found."),
        statusCode: StatusCodes.Status404NotFound)).AllowAnonymous();

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

// One instance of each public type of the given assemblies that implements T and has a public
// parameterless constructor, ordered by its Order value and then by type name. A type that cannot
// be created is passed to onFailure and skipped; without onFailure the exception propagates.
static List<T> DiscoverInstances<T>(Action<Type, Exception>? onFailure, params Assembly[] assemblies)
{
    var types = assemblies
        .SelectMany(a => a.GetTypes())
        .Where(t => typeof(T).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false } && t.GetConstructor(Type.EmptyTypes) != null)
        .Distinct();

    var created = new List<(T Instance, int Order, string Name)>();
    foreach (var type in types)
    {
        try
        {
            var instance = (T)Activator.CreateInstance(type)!;
            var order = instance switch
            {
                IServiceModule m => m.Order,
                IPipelineModule p => p.Order,
                ILogSinkContributor c => c.Order,
                _ => 0
            };
            created.Add((instance, order, type.FullName ?? type.Name));
        }
        catch (Exception ex) when (onFailure is not null)
        {
            onFailure(type, ex);
        }
    }

    return created
        .OrderBy(x => x.Order)
        .ThenBy(x => x.Name, StringComparer.Ordinal)
        .Select(x => x.Instance)
        .ToList();
}

public partial class Program;
