using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Masters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdminDesk.Infrastructure.Masters;

public sealed class MastersModule : IServiceModule
{
    public int Order => 30;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();

        services.AddScoped<ILookupProvider, EmployeeLookupProvider>();
        services.AddScoped<ILookupProvider, DepartmentLookupProvider>();
        services.AddScoped<ILookupProvider, LocationLookupProvider>();
        services.AddScoped<ILookupProvider, ProjectLookupProvider>();
        services.AddScoped<ILookupProvider, CostCentreLookupProvider>();
        services.AddScoped<ILookupRegistry, LookupRegistry>();

        services.AddScoped<IMasterService, MasterService>();
    }
}
