using Microsoft.AspNetCore.Routing;

namespace AdminDesk.Api.Hosting;

// Serves the built web app from wwwroot when that folder exists. Any other route that is not
// under /api or /hangfire answers with the app's start page so client-side routing works.
public sealed class StaticSpaPipelineModule : IPipelineModule
{
    // Matches everything except /api, /hangfire and their sub-paths.
    private const string Pattern = "{*path:regex(^(?!(api|hangfire)(/|$)).*$)}";

    public int Order => 1000;

    public void UseMiddleware(WebApplication app)
    {
        if (!HasWebRoot(app))
        {
            return;
        }
        app.UseDefaultFiles();
        app.UseStaticFiles();
    }

    public void MapEndpoints(WebApplication app)
    {
        if (!HasWebRoot(app))
        {
            return;
        }

        // The generic not-found fallback keeps /api and /hangfire; these win everywhere else.
        // They are anonymous because signed-out visitors must be able to reach the sign-in page.
        foreach (var pattern in new[] { "/", Pattern })
        {
            app.MapFallbackToFile(pattern, "index.html")
                .AllowAnonymous()
                .Add(builder =>
                {
                    if (builder is RouteEndpointBuilder route)
                    {
                        route.Order = int.MaxValue - 1;
                    }
                });
        }
    }

    private static bool HasWebRoot(WebApplication app) =>
        !string.IsNullOrEmpty(app.Environment.WebRootPath)
        && File.Exists(Path.Combine(app.Environment.WebRootPath, "index.html"));
}
