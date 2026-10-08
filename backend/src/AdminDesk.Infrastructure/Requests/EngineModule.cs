using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Engine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdminDesk.Infrastructure.Requests;

public sealed class EngineModule : IServiceModule
{
    public int Order => 0;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IRequestRepository, RequestRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();

        services.AddScoped<DefinitionPayloadValidator>();
        services.AddScoped<SubjectRenderer>();
        services.AddScoped<RequestAccessPolicy>();
        services.AddScoped<IRequestWorkflowService, RequestWorkflowService>();
    }
}
