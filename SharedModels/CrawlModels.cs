namespace SharedModels;

public class CrawlItem
{
    public string Url { get; set; } = "";
    public int Depth { get; set; }
}

public class CrawlResult
{
    public string SourceUrl { get; set; } = "";
    public int Depth { get; set; }
    public List<string> Links { get; set; } = new();

    public List<string> Words { get; set; } = new();

}

public class SeedRequest
{
    public List<string> Seeds { get; set; } = new();
    public int MaxDepth { get; set; } = 2;
}
