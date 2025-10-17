using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using SharedModels;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var queue = new ConcurrentQueue<CrawlItem>();
var visited = new ConcurrentDictionary<string, bool>();

// Seed starting URL (you can also accept this as a POST)
string startUrl = "https://example.com";
queue.Enqueue(new CrawlItem { Url = startUrl, Depth = 0 });

const int MaxDepth = 2;

// Worker asks for a task
app.MapGet("/task", (HttpContext context) =>
{
    if (queue.TryDequeue(out var item))
    {
        visited[item.Url] = true;
        return Results.Json(item);
    }

    return Results.NoContent();
});

// Worker posts crawl results
app.MapPost("/result", async (HttpContext context) =>
{
    var result = await context.Request.ReadFromJsonAsync<CrawlResult>();
    if (result == null) return Results.BadRequest();

    foreach (var link in result.Links)
    {
        if (!visited.ContainsKey(link) && result.Depth + 1 <= MaxDepth)
        {
            queue.Enqueue(new CrawlItem { Url = link, Depth = result.Depth + 1 });
        }
    }

    return Results.Ok();
});

// Optional: monitor status
app.MapGet("/status", () =>
{
    return Results.Json(new
    {
        QueueSize = queue.Count,
        VisitedCount = visited.Count
    });
});

app.Run("http://localhost:5000");
