namespace SharedModels
{
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
    }
}
