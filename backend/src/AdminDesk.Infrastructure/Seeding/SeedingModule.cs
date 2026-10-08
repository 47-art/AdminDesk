using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Demo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdminDesk.Infrastructure.Seeding;

public sealed class SeedingModule : IServiceModule
{
    public int Order => 60;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDemoActorFactory, DemoActorFactory>();
        services.AddScoped<IStartupTask, DemoRequestSeedTask>();
    }
}
