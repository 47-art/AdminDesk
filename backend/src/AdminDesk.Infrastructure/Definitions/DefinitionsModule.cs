using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Definitions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdminDesk.Infrastructure.Definitions;

public sealed class DefinitionsModule : IServiceModule
{
    public int Order => 30;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<DefinitionFileReader>();
        services.AddSingleton<IDefinitionProvider, DefinitionProvider>();

        services.AddScoped<IDefinitionRepository, DefinitionRepository>();
        services.AddScoped<ILimitRepository, LimitRepository>();
        services.AddScoped<IModuleCatalogService, ModuleCatalogService>();

        services.AddScoped<IStartupTask, DefinitionSyncTask>();
    }
}
