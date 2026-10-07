namespace AdminDesk.Api;

// Adds middleware and endpoints to the request pipeline. Modules with Order below 500
// run before authentication; 500 and above run after authorisation.
public interface IPipelineModule
{
    int Order => 0;

    void UseMiddleware(WebApplication app)
    {
    }

    void MapEndpoints(WebApplication app)
    {
    }
}
