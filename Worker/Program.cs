using SharedModels;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Linq;

HttpClient client = new HttpClient();
client.DefaultRequestHeaders.UserAgent.ParseAdd(
    "Mozilla/5.0 (compatible; DistributedCrawler/1.0; +https://example.edu/project)");

string coordinatorUrl = "http://localhost:5000";
Console.WriteLine("Worker started...");


while (true)
{
    var taskResponse = await client.GetAsync($"{coordinatorUrl}/task");
    if (!taskResponse.IsSuccessStatusCode)
    {
        Console.WriteLine("No more tasks. Waiting...");
        await Task.Delay(3000);
        continue;
    }

    var task = await taskResponse.Content.ReadFromJsonAsync<CrawlItem>();
    if (task == null) continue;

    Console.WriteLine($"[Worker] Crawling: {task.Url}");

    List<string> links = new();
    List<string> words = new();
    try
    {
        var html = await client.GetStringAsync(task.Url);

        // Extract links
        var matches = Regex.Matches(html, @"href\s*=\s*[""'](https?://[^""'#]+)[""']", RegexOptions.IgnoreCase);
        foreach (Match match in matches)
            links.Add(match.Groups[1].Value);

        // Extract text for indexing
        string textOnly = Regex.Replace(html, "<.*?>", " ");
        textOnly = Regex.Replace(textOnly, @"\s+", " ");
        textOnly = textOnly.ToLowerInvariant();

        words = textOnly.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Where(w => w.Length > 2 && w.Length < 20)
                        .ToList();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error crawling {task.Url}: {ex.Message}");
    }

    var result = new CrawlResult
    {
        SourceUrl = task.Url,
        Depth = task.Depth,
        Links = links,
        Words = words
    };

    await client.PostAsJsonAsync($"{coordinatorUrl}/result", result);
}

