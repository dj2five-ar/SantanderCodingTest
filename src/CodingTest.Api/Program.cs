using Microsoft.OpenApi;
using CodingTest.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddOpenApi(options =>
{
    // Describe the Stories tag so API explorers show a useful group summary
    options.AddDocumentTransformer((document, _, ct) =>
    {
        document.Tags ??= new HashSet<OpenApiTag>();
        document.Tags.Clear();
        document.Tags.Add(new OpenApiTag
        {
            Name = "Stories",
            Description =
                "Retrieve top-ranked stories from the Hacker News public API, " +
                "ordered by score descending. Responses are served from an in-memory " +
                "cache with a proactive background refresh to minimise live HN round-trips.",
        });
        return Task.CompletedTask;
    });

    // Document the X-Cache-Status response header that indicates stale data
    // The header is only present on degraded responses; it is absent on live responses
    options.AddOperationTransformer((operation, _, ct) =>
    {
        if (operation.Responses is null || !operation.Responses.TryGetValue("200", out var okResponse) || okResponse is null)
            return Task.CompletedTask;

        var headers = okResponse.Headers as IDictionary<string, IOpenApiHeader>;
        if (headers is null)
            return Task.CompletedTask;

        headers["X-Cache-Status"] = new OpenApiHeader
        {
            Description =
                "When present (value: `stale`), indicates that the Hacker News API was " +
                "unreachable and the response body contains data from the last successful " +
                "fetch. Absent on fully live responses. GUI consumers may use this to " +
                "display a staleness warning without any change to the JSON body.",
        };

        return Task.CompletedTask;
    });
});

var app = builder.Build();

// removing Environment.IsDevelopment() check to always expose Swagger/OpenAPI docs, just for convenience in this coding test.
// In production, you would typically restrict this to dev/test environments.
app.MapOpenApi();
app.MapGet("/", () => Results.Redirect("/openapi/v1.json"));

app.UseHttpsRedirection();
app.MapControllers();
app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
