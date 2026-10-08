using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Engine;
using AdminDesk.SharedKernel.Constants;
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

        // The probe is opt-in; its test hook exists only while the probe is switched on.
        services.AddScoped<IStartupTask, EngineProbeTask>();
        var probe = configuration[ConfigKeys.DiagnosticsRunEngineProbe]?.Trim();
        if (string.Equals(probe, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(probe, "pinning", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ProbeSwitch>();
            services.AddSingleton<IRequestHook, ProbeRequestHook>();
        }
    }
}
