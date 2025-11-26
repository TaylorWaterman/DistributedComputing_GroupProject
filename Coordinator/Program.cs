// Program.cs
// Multithreaded Selenium-based web crawler with SQLite storage

using System; // basic types and Console
using System.Net.Http; // HttpClient for robots.txt
using System.Threading.Tasks; // Task and async/await
using System.Collections.Concurrent; // ConcurrentQueue, ConcurrentDictionary
using System.Collections.Generic; // List<T>
using System.Threading; // Interlocked, Volatile
using System.Threading.Channels; // Channels for async producer/consumer
using System.Linq; // Enumerable helpers
using System.IO; // File and Directory operations
using OpenQA.Selenium; // Selenium WebDriver
using OpenQA.Selenium.Chrome; // ChromeDriver
using System.Text.RegularExpressions;


namespace WebCrawler
{
    public static class Program
    {
        // small immutable record to carry a URL + its current crawl depth
        private record CrawlItem(string Url, int Depth);

        public static async Task Main(string[] args)
        {
            Console.WriteLine("Welcome to our Web Crawler (Selenium/Chromium + SQLite)!");

            Console.Write("Enter the starting URL: ");
            string? startUrl = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(startUrl))
            {
                Console.WriteLine("No URL provided. Exiting.");
                return;
            }

            Console.Write("Enter the maximum depth (non-negative integer): ");
            if (!int.TryParse(Console.ReadLine(), out int maxDepth) || maxDepth < 0)
            {
                Console.WriteLine("Invalid depth. Exiting.");
                return;
            }

            Console.Write($"Enter max parallel requests (default {Environment.ProcessorCount}): ");
            int maxParallel = Environment.ProcessorCount;
            var input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input) && !int.TryParse(input, out maxParallel))
                maxParallel = Environment.ProcessorCount;
            if (maxParallel <= 0) maxParallel = 1;

            // IMPORTANT: Selenium cannot safely support tons of parallel Chrome instances.
            if (maxParallel > 2)
            {
                Console.WriteLine("Selenium mode detected → limiting parallel workers to 2.");
                maxParallel = 2;
            }

            // concurrent work channel and visited set
            var channel = Channel.CreateUnbounded<CrawlItem>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false
            });

            long queued = 0;
            var visited = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

            async ValueTask EnqueueAsync(CrawlItem item)
            {
                Interlocked.Increment(ref queued);
                if (!channel.Writer.TryWrite(item))
                    await channel.Writer.WriteAsync(item);
            }

            await EnqueueAsync(new CrawlItem(startUrl!, 0));

            // Optional: output directory if you ever want to dump debug files
            string outputDir = Path.Combine(Directory.GetCurrentDirectory(), "CrawledData");
            Directory.CreateDirectory(outputDir);
            Console.WriteLine($"(Optional) Debug data directory: {outputDir}");

            // Initialize database storage
            IStorage storage = new SqliteStorage("crawler.db");
            await storage.InitializeAsync();
            


            // Shared HttpClient (used for robots.txt)
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; WebCrawler/1.0; +https://example.com/crawler)");

            var crawlerAgent = "WebCrawler";
            var robotsTasks = new ConcurrentDictionary<string, Task<RobotsRules>>(StringComparer.OrdinalIgnoreCase);

            async Task<RobotsRules> FetchRobotsAsync(string origin)
            {
                try
                {
                    var robotsUrl = origin.TrimEnd('/') + "/robots.txt";
                    // Use HttpClient (NOT Selenium) for robots.txt
                    var text = await httpClient.GetStringAsync(robotsUrl);

                    var lines = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    var currentAgents = new List<string>();
                    var agentRules = new Dictionary<string, RobotsRules>(StringComparer.OrdinalIgnoreCase);

                    foreach (var raw in lines)
                    {
                        var line = raw.Split('#')[0].Trim();
                        if (string.IsNullOrEmpty(line)) continue;
                        var parts = line.Split(':', 2);
                        if (parts.Length != 2) continue;
                        var key = parts[0].Trim().ToLowerInvariant();
                        var value = parts[1].Trim();

                        if (key == "user-agent")
                        {
                            currentAgents.Clear();
                            currentAgents.Add(value);
                            if (!agentRules.ContainsKey(value))
                                agentRules[value] = new RobotsRules();
                        }
                        else if (key == "disallow")
                        {
                            foreach (var ag in currentAgents)
                                agentRules[ag].Disallows.Add(value);
                        }
                        else if (key == "allow")
                        {
                            foreach (var ag in currentAgents)
                                agentRules[ag].Allows.Add(value);
                        }
                    }

                    if (agentRules.TryGetValue(crawlerAgent, out var specific))
                        return specific;
                    if (agentRules.TryGetValue("*", out var wildcard))
                        return wildcard;
                    return new RobotsRules();
                }
                catch
                {
                    // On error, be permissive but log if you want.
                    return new RobotsRules();
                }
            }

            Task<RobotsRules> GetRobotsForOriginAsync(string origin)
            {
                return robotsTasks.GetOrAdd(origin, _ => FetchRobotsAsync(origin));
            }

            async ValueTask<bool> IsUrlAllowedAsync(string url)
            {
                try
                {
                    var uri = new Uri(url);
                    var origin = uri.GetLeftPart(UriPartial.Authority);
                    var rules = await GetRobotsForOriginAsync(origin);
                    return rules.IsAllowed(uri.PathAndQuery, crawlerAgent);
                }
                catch
                {
                    return true;
                }
            }

            int active = 0;
            Console.WriteLine($"Starting crawl from {startUrl} up to depth {maxDepth} with {maxParallel} workers...");

            var processedCount = new long[maxParallel];
            var errorCount = new long[maxParallel];
            var dequeues = new long[maxParallel];
            var enqueues = new long[maxParallel];
            var lastActiveTicks = new long[maxParallel];

            var workers = Enumerable.Range(0, maxParallel)
                .Select(i => Task.Run(async () =>
                {
                    var htmlParser = new HTMLParser();

                    var chromeOptions = new ChromeOptions();
                    chromeOptions.AddArgument("--headless");
                    chromeOptions.AddArgument("--disable-gpu");
                    using var driver = new ChromeDriver(chromeOptions);

                    int processedSinceYield = 0;
                    const int yieldEvery = 5;

                    while (true)
                    {
                        if (channel.Reader.TryRead(out var item))
                        {
                            Interlocked.Decrement(ref queued);
                            Interlocked.Increment(ref dequeues[i]);

                            if (!visited.TryAdd(item.Url, 0))
                                continue;

                            Interlocked.Increment(ref active);
                            try
                            {
                                Console.WriteLine($"[Worker {i}] Crawling: {item.Url} (depth {item.Depth})");

                                string html;
                                if (!await IsUrlAllowedAsync(item.Url))
                                {
                                    Console.WriteLine($"[Worker {i}] Skipping (robots): {item.Url}");
                                    continue;
                                }

                                try
                                {
                                    driver.Navigate().GoToUrl(item.Url);
                                    html = driver.PageSource;
                                    Interlocked.Increment(ref processedCount[i]);
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[Worker {i}] Error fetching {item.Url}: {ex.Message}");
                                    Interlocked.Increment(ref errorCount[i]);
                                    continue;
                                }

                                // Parse HTML
                                htmlParser.ParseHTML(html, item.Url);

                                // Persist page + links to SQLite
                               try
                               {
                                    var page = htmlParser.ToPageRecord(item.Depth, DateTime.UtcNow);
                                    var changed = await storage.UpsertPageAsync(page);
                                    Console.WriteLine($"[Worker {i}] {(changed ? "Upserted (changed)" : "Upserted (no change)")} → {page.CanonicalUrl}");
                                    await storage.BulkUpsertLinksAsync(page.CanonicalUrl, page.Links);

                                // STEP 4 — Inverted Index
                                var wordCounts = TokenizeAndCountWords(page);
                                foreach (var kv in wordCounts)
                                {
                                    await storage.UpsertInvertedIndexAsync(
                                        kv.Key,            // word
                                        page.CanonicalUrl, // URL
                                        kv.Value           // frequency
                                    );
                                }
                            }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[Worker {i}] Storage error for {item.Url}: {ex.Message}");
                                     }



                                // Enqueue discovered links if still under max depth
                                if (item.Depth < maxDepth)
                                {
                                    var links = htmlParser.Links;

                                    foreach (var link in links)
                                    {
                                        if (!visited.ContainsKey(link))
                                        {
                                            if (!await IsUrlAllowedAsync(link))
                                            {
                                                Console.WriteLine($"[Worker {i}] Not enqueueing (robots): {link}");
                                                continue;
                                            }

                                            Interlocked.Increment(ref enqueues[i]);
                                            await EnqueueAsync(new CrawlItem(link, item.Depth + 1));
                                        }
                                    }
                                }

                                Interlocked.Exchange(ref lastActiveTicks[i], DateTime.UtcNow.Ticks);

                                processedSinceYield++;
                                if (processedSinceYield >= yieldEvery)
                                {
                                    processedSinceYield = 0;
                                    await Task.Yield();
                                }
                            }
                            finally
                            {
                                Interlocked.Decrement(ref active);
                            }
                        }
                        else
                        {
                            if (Volatile.Read(ref queued) == 0 && Volatile.Read(ref active) == 0)
                                break;

                            await Task.Delay(150);
                        }
                    }
                }))
                .ToArray();

            await Task.WhenAll(workers);

            Console.WriteLine("Crawl complete. Visited URLs:");
            foreach (var url in visited.Keys)
                Console.WriteLine(url);

            Console.WriteLine();
            Console.WriteLine("Per-worker summary:");
            for (int w = 0; w < processedCount.Length; w++)
            {
                Console.WriteLine($"Worker {w}: processed={processedCount[w]}, errors={errorCount[w]}");
            }

            Console.WriteLine();
            Console.WriteLine("Detailed worker metrics:");
            for (int w = 0; w < processedCount.Length; w++)
            {
                var lastActive = lastActiveTicks[w] == 0
                    ? "never"
                    : new DateTime(lastActiveTicks[w], DateTimeKind.Utc).ToString("o");

                Console.WriteLine(
                    $"Worker {w}: dequeues={dequeues[w]}, enqueues={enqueues[w]}, processed={processedCount[w]}, errors={errorCount[w]}, lastActive={lastActive}");
            }
        }

        // Simple robots.txt rules container
        private class RobotsRules
        {
            public List<string> Allows { get; } = new List<string>();
            public List<string> Disallows { get; } = new List<string>();

            public bool IsAllowed(string pathAndQuery, string agent)
            {
                var path = string.IsNullOrEmpty(pathAndQuery) ? "/" : pathAndQuery;

                string? bestAllow = null;
                foreach (var a in Allows)
                {
                    if (path.StartsWith(a, StringComparison.Ordinal))
                    {
                        if (bestAllow == null || a.Length > bestAllow.Length)
                            bestAllow = a;
                    }
                }

                string? bestDisallow = null;
                foreach (var d in Disallows)
                {
                    if (string.IsNullOrEmpty(d))
                        continue;
                    if (path.StartsWith(d, StringComparison.Ordinal))
                    {
                        if (bestDisallow == null || d.Length > bestDisallow.Length)
                            bestDisallow = d;
                    }
                }

                if (bestAllow != null && (bestDisallow == null || bestAllow.Length >= bestDisallow.Length))
                    return true;
                if (bestDisallow != null)
                    return false;
                return true;
            }
        }

        private static Dictionary<string, int> TokenizeAndCountWords(PageRecord page)
{
    var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    void AddText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var words = System.Text.RegularExpressions.Regex
            .Matches(text.ToLowerInvariant(), "[a-z0-9]+");

        foreach (Match m in words)
        {
            string w = m.Value;
            dict[w] = dict.TryGetValue(w, out var count) ? count + 1 : 1;
        }
    }

    AddText(page.Title);
    AddText(page.MetaDescription);
    AddText(page.MetaKeywords);
    AddText(page.Body);

    if (page.Headings != null)
    {
        foreach (var h in page.Headings)
            AddText(h);
    }

    return dict;
}
}
}