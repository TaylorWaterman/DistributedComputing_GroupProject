using SharedModels;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

HttpClient client = new HttpClient();
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
    try
    {
        var html = await client.GetStringAsync(task.Url);
        var matches = Regex.Matches(html, @"href\s*=\s*[""'](https?://[^""'#]+)[""']", RegexOptions.IgnoreCase);
        foreach (Match match in matches)
        {
            links.Add(match.Groups[1].Value);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error crawling {task.Url}: {ex.Message}");
    }

    var result = new CrawlResult
    {
        SourceUrl = task.Url,
        Depth = task.Depth,
        Links = links
    };

    await client.PostAsJsonAsync($"{coordinatorUrl}/result", result);
}
