using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Requests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdminDesk.Infrastructure.Requests;

public sealed class RequestsApiModule : IServiceModule
{
    public int Order => 100;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IRequestQueryRepository, RequestQueryRepository>();
        services.AddScoped<IRequestQueryService, RequestQueryService>();
    }
}
