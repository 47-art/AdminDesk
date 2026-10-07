using AdminDesk.Application.Abstractions;
using AdminDesk.SharedKernel.Constants;
using Serilog.Context;

namespace AdminDesk.Api.Middleware;

public class AsyncLocalCorrelationIdAccessor : ICorrelationIdAccessor
{
    private static readonly AsyncLocal<string?> Current = new();

    public string? CorrelationId
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}

public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private const int MaxLength = 64;

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationIdAccessor accessor)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var id = IsAcceptable(incoming) ? incoming : Guid.NewGuid().ToString("N");

        if (accessor is AsyncLocalCorrelationIdAccessor writable)
        {
            writable.CorrelationId = id;
        }

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = id;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(LogConstants.CorrelationIdProperty, id))
        {
            await _next(context);
        }
    }

    // Only short, printable values are echoed back, so the header cannot be used to inject content.
    private static bool IsAcceptable(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return false;
        }
        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))
            {
                return false;
            }
        }
        return true;
    }
}
