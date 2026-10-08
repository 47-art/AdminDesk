using AdminDesk.Application.Abstractions;
using AdminDesk.Application.Documents;
using AdminDesk.Infrastructure.Configuration;
using AdminDesk.SharedKernel.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdminDesk.Infrastructure.Documents;

public sealed class DocumentsModule : IServiceModule
{
    public int Order => 35;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var contentRoot = configuration["contentRoot"] ?? AppContext.BaseDirectory;
        var folder = Path.Combine(StoragePaths.DataDirectory(contentRoot, configuration), ConfigKeys.DocumentsFolderName);

        var configured = configuration[ConfigKeys.DocumentsMaxBytes];
        var maxBytes = long.TryParse(configured, out var parsed) && parsed > 0 ? parsed : ConfigKeys.DocumentsMaxBytesDefault;

        services.AddSingleton(new DocumentSettings(maxBytes));
        services.AddSingleton<IDocumentFileStore>(new DocumentFileStore(folder));
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IDocumentService, DocumentService>();
    }
}
