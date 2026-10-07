using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Abstractions.Persistence;
using AdminDesk.SharedKernel.Actors;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdminDesk.Infrastructure.Persistence;

public sealed class PersistenceModule : IServiceModule
{
    public int Order => 10;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        DapperTypeHandlers.Register();

        var contentRoot = configuration["contentRoot"] ?? AppContext.BaseDirectory;
        services.AddSingleton(new DatabaseLocation(contentRoot, configuration));

        services.AddSingleton<IDbConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<IUnitOfWork, SqliteUnitOfWork>();
        services.AddSingleton<ISqlDialect, SqliteDialect>();

        var clock = new AdjustableTimeProvider();
        services.AddSingleton(clock);
        services.AddSingleton<TimeProvider>(clock);

        services.AddScoped<IActorAccessor, ScopedActorAccessor>();
        services.AddScoped<AuditStamper>();

        services.AddScoped<IStartupTask, DatabaseMigrationTask>();
    }
}
