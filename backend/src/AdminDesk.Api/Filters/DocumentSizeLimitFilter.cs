using AdminDesk.Application.Documents;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AdminDesk.Api.Filters;

// Caps the request body of an upload before the form is read: the configured file maximum plus
// room for the multipart framing. A file just over the maximum still reaches the service, which
// answers with a field error; anything much larger is refused unread.
public sealed class DocumentSizeLimitFilter : IResourceFilter
{
    private const long FramingAllowance = 1024 * 1024;

    private readonly DocumentSettings _settings;

    public DocumentSizeLimitFilter(DocumentSettings settings)
    {
        _settings = settings;
    }

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var feature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = _settings.MaxBytes + FramingAllowance;
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
