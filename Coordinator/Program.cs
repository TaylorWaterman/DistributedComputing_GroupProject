// Program.cs
// ASP.NET Core Web API for Web Crawler with Selenium and SQLite

using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Linq;
using System.IO;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Text.RegularExpressions;

namespace WebCrawler
{
    // DTO for API requests/responses
    public record CrawlRequest(string StartUrl, int MaxDepth, int? MaxParallel = null, bool UseSelenium = false);
    public record CrawlResponse(string Id, string Status, DateTime StartTime, int PagesFound, int LinksFound, string? ErrorMessage = null);
    public record CrawlResultsResponse(List<PageRecord> Results, int TotalPages, int TotalLinks, double AvgDepth, int UniqueHosts);

    public static class Program
    {
        private static readonly ConcurrentDictionary<string, CrawlSession> ActiveSessions = new();

        public class CrawlSession
        {
            public string Id { get; } = Guid.NewGuid().ToString();
            public string StartUrl { get; set; } = "";
            public int MaxDepth { get; set; }
            public int MaxParallel { get; set; }
            public bool UseSelenium { get; set; } = false;
            public DateTime StartTime { get; } = DateTime.UtcNow;
            public CrawlStatus Status { get; set; } = CrawlStatus.Queued;
            public List<PageRecord> Results { get; } = new();
            public string? ErrorMessage { get; set; }
            public int PagesProcessed { get; set; }
            public int LinksDiscovered { get; set; }
        }

        public enum CrawlStatus
        {
            Queued,
            Running,
            Completed,
            Failed,
            Cancelled
        }

        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Configure web server to listen on port 5000
            builder.WebHost.UseUrls("http://0.0.0.0:5000");

            // Add CORS support
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });

            var app = builder.Build();

            app.UseCors("AllowAll");
            app.UseDefaultFiles();
            app.UseStaticFiles();

            // Initialize storage
            IStorage storage = new SqliteStorage("crawler.db");
            await storage.InitializeAsync();

            // ========================
            // API ENDPOINTS
            // ========================

            // POST /api/crawl - Start a new crawl
            app.MapPost("/api/crawl", (CrawlRequest request) =>
            {
                if (string.IsNullOrWhiteSpace(request.StartUrl))
                    return Results.BadRequest(new { error = "StartUrl is required" });

                if (!IsValidUrl(request.StartUrl))
                    return Results.BadRequest(new { error = "Invalid URL format" });

                if (request.MaxDepth < 0 || request.MaxDepth > 10)
                    return Results.BadRequest(new { error = "MaxDepth must be between 0 and 10" });

                var session = new CrawlSession
                {
                    StartUrl = request.StartUrl,
                    MaxDepth = request.MaxDepth,
                    MaxParallel = request.MaxParallel ?? (request.UseSelenium ? 2 : Environment.ProcessorCount),
                    UseSelenium = request.UseSelenium
                };

                // Limit to 2 workers for Selenium, allow more for HTTP
                if (request.UseSelenium && session.MaxParallel > 2)
                    session.MaxParallel = 2;
                else if (!request.UseSelenium && session.MaxParallel > 16)
                    session.MaxParallel = 16;

                ActiveSessions.TryAdd(session.Id, session);

                // Start crawl in background
                _ = RunCrawlAsync(session, storage);

                return Results.Accepted($"/api/crawl/{session.Id}", new CrawlResponse(
                    session.Id,
                    session.Status.ToString(),
                    session.StartTime,
                    session.PagesProcessed,
                    session.LinksDiscovered
                ));
            });

            // GET /api/crawl/{id} - Get crawl status
            app.MapGet("/api/crawl/{id}", (string id) =>
            {
                if (!ActiveSessions.TryGetValue(id, out var session))
                    return Results.NotFound(new { error = "Crawl session not found" });

                return Results.Ok(new CrawlResponse(
                    session.Id,
                    session.Status.ToString(),
                    session.StartTime,
                    session.PagesProcessed,
                    session.LinksDiscovered,
                    session.ErrorMessage
                ));
            });

            // GET /api/crawl/{id}/results - Get crawl results
            app.MapGet("/api/crawl/{id}/results", (string id) =>
            {
                if (!ActiveSessions.TryGetValue(id, out var session))
                    return Results.NotFound(new { error = "Crawl session not found" });

                if (session.Status != CrawlStatus.Completed)
                    return Results.BadRequest(new { error = $"Crawl is still {session.Status.ToString().ToLower()}" });

                var avgDepth = session.Results.Count > 0
                    ? session.Results.Average(r => r.CrawlDepth)
                    : 0;

                var uniqueHosts = new HashSet<string>(
                    session.Results.Select(r => r.Host),
                    StringComparer.OrdinalIgnoreCase
                ).Count;

                var totalLinks = session.Results.Sum(r => r.Links?.Count ?? 0);

                return Results.Ok(new CrawlResultsResponse(
                    session.Results,
                    session.Results.Count,
                    totalLinks,
                    avgDepth,
                    uniqueHosts
                ));
            });

            // GET /api/crawl/{id}/cancel - Cancel a crawl
            app.MapPost("/api/crawl/{id}/cancel", (string id) =>
            {
                if (!ActiveSessions.TryGetValue(id, out var session))
                    return Results.NotFound(new { error = "Crawl session not found" });

                if (session.Status == CrawlStatus.Running)
                    session.Status = CrawlStatus.Cancelled;

                return Results.Ok(new { message = "Crawl cancelled", id = session.Id });
            });

            // GET /api/crawl/list - List all active sessions
            app.MapGet("/api/crawl/list", () =>
            {
                var sessions = ActiveSessions.Values
                    .Select(s => new CrawlResponse(
                        s.Id,
                        s.Status.ToString(),
                        s.StartTime,
                        s.PagesProcessed,
                        s.LinksDiscovered,
                        s.ErrorMessage
                    ))
                    .ToList();

                return Results.Ok(sessions);
            });

            app.MapFallbackToFile("index.html");

            Console.WriteLine("🕷️  Web Crawler API starting on http://localhost:5000");
            Console.WriteLine("Open http://localhost:5000 in your browser to access the crawler UI.");
            
            await app.RunAsync();
        }

        private record CrawlItem(string Url, int Depth);

        private static bool IsValidUrl(string url)
        {
            try
            {
                new Uri(url);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task RunCrawlAsync(CrawlSession session, IStorage storage)
        {
            try
            {
                session.Status = CrawlStatus.Running;

                // Crawl timeout: 5 minutes max per crawl session
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                const int MaxPages = 500; // Limit pages to prevent runaway crawls

                // Concurrent work channel and visited set
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

                await EnqueueAsync(new CrawlItem(session.StartUrl, 0));

                // Robots.txt handling
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
                var processedCount = new long[session.MaxParallel];
                var errorCount = new long[session.MaxParallel];

                var workers = Enumerable.Range(0, session.MaxParallel)
                    .Select(i => Task.Run(async () =>
                    {
                        if (session.Status == CrawlStatus.Cancelled)
                            return;

                        var htmlParser = new HTMLParser();
                        IWebDriver? driver = null;

                        // Only create Chrome driver if using Selenium
                        if (session.UseSelenium)
                        {
                            var chromeOptions = new ChromeOptions();
                            chromeOptions.AddArgument("--headless");
                            chromeOptions.AddArgument("--disable-gpu");
                            chromeOptions.AddArgument("--no-sandbox");
                            chromeOptions.AddArgument("--disable-dev-shm-usage");
                            driver = new ChromeDriver(chromeOptions);
                        }

                        try
                        {
                            int processedSinceYield = 0;
                            const int yieldEvery = 5;

                            while (session.Status != CrawlStatus.Cancelled)
                            {
                                // Stop if we've hit max pages or timeout occurred
                                if (session.PagesProcessed >= MaxPages || cts.Token.IsCancellationRequested)
                                    break;

                                if (channel.Reader.TryRead(out var item))
                                {
                                    Interlocked.Decrement(ref queued);

                                    if (!visited.TryAdd(item.Url, 0))
                                        continue;

                                    Interlocked.Increment(ref active);
                                    try
                                    {
                                        string html;
                                        if (!await IsUrlAllowedAsync(item.Url))
                                        {
                                            continue;
                                        }

                                        try
                                        {
                                            if (session.UseSelenium)
                                            {
                                                // Selenium approach - full rendering
                                                driver!.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(15);
                                                driver.Navigate().GoToUrl(item.Url);
                                                html = driver.PageSource;
                                            }
                                            else
                                            {
                                                // HTTP approach - fast, lightweight
                                                using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                                                var response = await httpClient.GetAsync(item.Url, HttpCompletionOption.ResponseContentRead, cts2.Token);
                                                if (!response.IsSuccessStatusCode)
                                                    continue;
                                                html = await response.Content.ReadAsStringAsync();
                                            }
                                            Interlocked.Increment(ref processedCount[i]);
                                        }
                                        catch
                                        {
                                            Interlocked.Increment(ref errorCount[i]);
                                            continue;
                                        }

                                        htmlParser.ParseHTML(html, item.Url);

                                        try
                                        {
                                            var page = htmlParser.ToPageRecord(item.Depth, DateTime.UtcNow);
                                            await storage.UpsertPageAsync(page);
                                            await storage.BulkUpsertLinksAsync(page.CanonicalUrl, page.Links);

                                            var wordCounts = TokenizeAndCountWords(page);
                                            foreach (var kv in wordCounts)
                                            {
                                                await storage.UpsertInvertedIndexAsync(kv.Key, page.CanonicalUrl, kv.Value);
                                            }

                                            lock (session.Results)
                                            {
                                                session.Results.Add(page);
                                                session.PagesProcessed++;
                                                session.LinksDiscovered += page.Links?.Count ?? 0;
                                            }
                                        }
                                        catch
                                        {
                                            // Storage error - continue
                                        }

                                        // Enqueue discovered links if under max depth and haven't hit page limit
                                        if (item.Depth < session.MaxDepth && session.PagesProcessed < MaxPages)
                                        {
                                            var links = htmlParser.Links;
                                            foreach (var link in links)
                                            {
                                                if (!visited.ContainsKey(link) && await IsUrlAllowedAsync(link))
                                                {
                                                    await EnqueueAsync(new CrawlItem(link, item.Depth + 1));
                                                }
                                            }
                                        }

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

                                    await Task.Delay(100);
                                }
                            }
                        }
                        finally
                        {
                            driver?.Dispose();
                        }
                    }))
                    .ToArray();

                await Task.WhenAll(workers);
                session.Status = CrawlStatus.Completed;
            }
            catch (Exception ex)
            {
                session.Status = CrawlStatus.Failed;
                session.ErrorMessage = ex.Message;
                Console.WriteLine($"Crawl {session.Id} failed: {ex.Message}");
            }
        }

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

                var words = Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]+");
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