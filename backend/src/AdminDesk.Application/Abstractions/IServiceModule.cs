using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdminDesk.Application.Abstractions;

// A unit of service registration. Implementations are discovered by the host,
// created with a parameterless constructor and called in Order.
public interface IServiceModule
{
    int Order => 0;

    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}
