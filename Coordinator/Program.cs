using SharedModels;
using System.Collections.Concurrent;
using System.Linq; // ✅ Required for .OrderByDescending()

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Shared state
ConcurrentQueue<CrawlItem> queue = new();
ConcurrentDictionary<string, bool> visited = new(StringComparer.OrdinalIgnoreCase);
int maxDepth = 0;


Dictionary<string, Dictionary<string, int>> InvertedIndex = new(StringComparer.OrdinalIgnoreCase);


// Seed crawl job
app.MapPost("/seed", (SeedRequest request) =>
{
    maxDepth = request.MaxDepth;

    foreach (var url in request.Seeds)
    {
        queue.Enqueue(new CrawlItem { Url = url, Depth = 0 });
    }

    return Results.Ok($"Seeding {request.Seeds.Count} start URLs (max depth: {maxDepth})");
});

// Worker requesting next task
app.MapGet("/task", () =>
{
    if (queue.TryDequeue(out var item))
        return Results.Ok(item);

    return Results.NotFound();
});

// Worker sends crawl results back
app.MapPost("/result", (CrawlResult result) =>
{
    // Add discovered links to queue
    foreach (var link in result.Links)
    {
        if (!visited.ContainsKey(link) && result.Depth < maxDepth)
        {
            visited.TryAdd(link, true);
            queue.Enqueue(new CrawlItem { Url = link, Depth = result.Depth + 1 });
        }
    }

    
    foreach (var word in result.Words)
    {
        if (!InvertedIndex.TryGetValue(word, out var urlCounts))
        {
            urlCounts = new Dictionary<string, int>();
            InvertedIndex[word] = urlCounts;
        }

        if (!urlCounts.TryGetValue(result.SourceUrl, out var count))
        {
            count = 0;
        }

        urlCounts[result.SourceUrl] = count + 1;
    }

    return Results.Ok();
});


// Search endpoint
app.MapGet("/search", (string query) =>
{
    query = query.ToLowerInvariant();

    if (InvertedIndex.TryGetValue(query, out var matches))
    {
        return Results.Ok(
            matches.OrderByDescending(x => x.Value)
                   .Select(x => new { Url = x.Key, Count = x.Value })
        );
    }

    return Results.Ok(Array.Empty<object>());
});

// Status endpoint
app.MapGet("/status", () => new
{
    QueueSize = queue.Count,
    VisitedCount = visited.Count,
    MaxDepth = maxDepth
});

app.Run("http://localhost:5000");
