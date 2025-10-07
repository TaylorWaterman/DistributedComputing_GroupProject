// New Web Crawler Program
using System; // basic types and Console
using System.Net.Http; // HttpClient for HTTP requests
using System.Threading.Tasks; // Task and async/await
using System.Collections.Concurrent; // ConcurrentQueue, ConcurrentDictionary
using System.Collections.Generic; // List<T>
using System.Text.RegularExpressions; // Regex
using System.Threading; // Interlocked, Volatile
using System.Threading.Channels; // Channels for async producer/consumer
using System.Linq; // Enumerable helpers

/* Multithreaded Web Crawler
 *
 * - Concurrent workers dequeue URLs (with depth) and fetch HTML asynchronously.
 * - New links are enqueued with depth+1 (only if <= maxDepth).
 * - Visited URLs are tracked in a ConcurrentDictionary to avoid duplicates.
 * - Simple termination: when queue is empty and no active workers remain.
 */

namespace WebCrawler
{
    public static class Program
    {
        // small immutable record to carry a URL + its current crawl depth
        private record CrawlItem(string Url, int Depth);

        public static async Task Main(string[] args)
        {
            // welcome and input
            Console.WriteLine("Welcome to our Web Crawler (concurrent)!"); // greet user
            Console.Write("Enter the starting URL: "); // prompt for seed URL
            string? startUrl = Console.ReadLine(); // read seed URL

            if (string.IsNullOrWhiteSpace(startUrl)) // validate input
            {
                Console.WriteLine("No URL provided. Exiting.");
                return;
            }

            // maximum crawl depth
            Console.Write("Enter the maximum depth (non-negative integer): "); // prompt depth
            if (!int.TryParse(Console.ReadLine(), out int maxDepth) || maxDepth < 0) // parse & validate
            {
                Console.WriteLine("Invalid depth. Exiting.");
                return;
            }

            // degree of parallelism
            Console.Write($"Enter max parallel requests (default {Environment.ProcessorCount}): "); // prompt parallelism
            int maxParallel = Environment.ProcessorCount; // default to CPU count
            var input = Console.ReadLine(); // read user input for parallelism
            if (!string.IsNullOrWhiteSpace(input) && !int.TryParse(input, out maxParallel))
                maxParallel = Environment.ProcessorCount; // fallback to default if parse fails
            if (maxParallel <= 0) maxParallel = 1; // ensure at least one worker

            // concurrent work channel and visited set
            var channel = Channel.CreateUnbounded<CrawlItem>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
            // queued tracks number of items currently enqueued (for termination detection)
            long queued = 0;
            var visited = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase); // thread-safe set of visited URLs

            // helper to enqueue into the channel while updating queued count
            async ValueTask EnqueueAsync(CrawlItem item)
            {
                Interlocked.Increment(ref queued);
                // try immediate write, else await write
                if (!channel.Writer.TryWrite(item))
                    await channel.Writer.WriteAsync(item);
            }

            // seed the channel with the user-provided start URL at depth 0
            // (we don't attribute this enqueue to any worker)
            await EnqueueAsync(new CrawlItem(startUrl!, 0));

            // single shared HttpClient for all requests (recommended)
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // set a polite User-Agent so some servers don't reject our requests
            // change this string to identify your crawler and include a contact URL if appropriate
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; WebCrawler/1.0; +https://example.com/crawler)");
            Console.WriteLine($"HttpClient User-Agent: {string.Join(' ', httpClient.DefaultRequestHeaders.UserAgent)}");

            // --- robots.txt support ---
            // We'll cache robots.txt parsing per-origin using a dictionary of fetch tasks so
            // we only fetch/parse once per host. If fetching/parsing fails, we'll default to allowing.
            var crawlerAgent = "WebCrawler"; // short token to match against User-agent lines in robots.txt
            var robotsTasks = new ConcurrentDictionary<string, Task<RobotsRules>>(StringComparer.OrdinalIgnoreCase);

            // Fetch and parse robots.txt for a given origin (e.g. "https://example.com")
            async Task<RobotsRules> FetchRobotsAsync(string origin)
            {
                try
                {
                    var robotsUrl = origin.TrimEnd('/') + "/robots.txt";
                    var text = await httpClient.GetStringAsync(robotsUrl);
                    // simple parser: group directives by the most recent User-agent lines
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
                            // ensure an entry exists
                            if (!agentRules.ContainsKey(value)) agentRules[value] = new RobotsRules();
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

                    // choose the best matching rules: specific agent first, then '*', else empty (allow all)
                    if (agentRules.TryGetValue(crawlerAgent, out var specific))
                        return specific;
                    if (agentRules.TryGetValue("*", out var wildcard))
                        return wildcard;
                    return new RobotsRules();
                }
                catch
                {
                    // on any failure, return permissive rules (allow everything)
                    return new RobotsRules();
                }
            }

            // get or start a fetch task for robots rules for an origin
            Task<RobotsRules> GetRobotsForOriginAsync(string origin)
            {
                return robotsTasks.GetOrAdd(origin, _ => FetchRobotsAsync(origin));
            }

            // check whether a URL is allowed by robots for our crawlerAgent
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
                    // on parse/fetch errors, be permissive
                    return true;
                }
            }

            // active counter tracks how many workers are currently processing; used for termination
            int active = 0; // number of workers currently processing an item
            Console.WriteLine($"Starting crawl from {startUrl} up to depth {maxDepth} with {maxParallel} workers...");

            // per-worker statistics
            var processedCount = new long[maxParallel]; // number of successful fetch attempts per worker
            var errorCount = new long[maxParallel]; // number of failed fetch attempts per worker
            var dequeues = new long[maxParallel]; // how many times each worker dequeued an item
            var enqueues = new long[maxParallel]; // how many items each worker enqueued
            var lastActiveTicks = new long[maxParallel]; // DateTime.UtcNow.Ticks when worker last processed an item

            // Start worker tasks equal to maxParallel
            var workers = Enumerable.Range(0, maxParallel)
                .Select(i => Task.Run(async () =>
                {
                    // per-worker burst counter for conditional yield
                    int processedSinceYield = 0;
                    int yieldEvery = 5; // yield after this many processed items (tune as needed)

                    while (true) // worker loop
                    {
                        if (channel.Reader.TryRead(out var item)) // try to get work
                        {
                            Interlocked.Decrement(ref queued);
                            Interlocked.Increment(ref dequeues[i]);
                            // avoid processing the same URL twice: atomically add to visited set
                            if (!visited.TryAdd(item.Url, 0))
                                continue; // someone else already processed it

                            Interlocked.Increment(ref active); // mark worker as active
                            try
                            {
                                Console.WriteLine($"[Worker {i}] Crawling: {item.Url} (depth {item.Depth})"); // log work start

                                string html;
                                // Respect robots.txt: check whether URL is allowed for our crawler
                                if (!await IsUrlAllowedAsync(item.Url))
                                {
                                    Console.WriteLine($"[Worker {i}] Skipping (robots): {item.Url}");
                                    continue;
                                }

                                try
                                {
                                    // asynchronous HTTP GET
                                    html = await httpClient.GetStringAsync(item.Url);
                                    Interlocked.Increment(ref processedCount[i]);
                                }
                                catch (Exception ex) // per-URL errors shouldn't kill the worker loop
                                {
                                    Console.WriteLine($"[Worker {i}] Error fetching {item.Url}: {ex.Message}");
                                    Interlocked.Increment(ref errorCount[i]);
                                    continue; // go back to try next queue item
                                }

                                // If we're still under the configured max depth, extract links and enqueue them
                                if (item.Depth < maxDepth)
                                {
                                    var links = ExtractLinks(html); // extract absolute http(s) hrefs
                                    foreach (var link in links)
                                    {
                                        // cheap dedupe before enqueueing (visited set still authoritative)
                                        if (!visited.ContainsKey(link))
                                        {
                                            // check robots for the discovered link before enqueueing
                                            if (!await IsUrlAllowedAsync(link))
                                            {
                                                // optionally attribute this as a skipped enqueue (not increasing enqueues)
                                                Console.WriteLine($"[Worker {i}] Not enqueueing (robots): {link}");
                                                continue;
                                            }

                                            // attribute enqueues to this worker
                                            Interlocked.Increment(ref enqueues[i]);
                                            await EnqueueAsync(new CrawlItem(link, item.Depth + 1));
                                        }
                                    }
                                }

                                // record last active time for this worker
                                Interlocked.Exchange(ref lastActiveTicks[i], DateTime.UtcNow.Ticks);

                                // conditional yield: yield every `yieldEvery` processed items to limit long bursts
                                processedSinceYield++;
                                if (processedSinceYield >= yieldEvery)
                                {
                                    processedSinceYield = 0;
                                    await Task.Yield();
                                }
                            }
                            finally
                            {
                                Interlocked.Decrement(ref active); // mark worker as no longer active
                            }
                        }
                        else
                        {
                            // if there's no queued work and nobody is actively processing, we're done
                            if (Volatile.Read(ref queued) == 0 && Volatile.Read(ref active) == 0)
                                break; // exit worker loop

                            // otherwise sleep briefly to avoid busy-waiting
                            await Task.Delay(150);
                        }
                    }
                }))
                .ToArray();

            // wait for all workers to finish
            await Task.WhenAll(workers);

            // summary output
            Console.WriteLine("Crawl complete. Visited URLs:");
            foreach (var url in visited.Keys)
                Console.WriteLine(url);

            // per-worker summary report
            Console.WriteLine();
            Console.WriteLine("Per-worker summary:");
            for (int w = 0; w < processedCount.Length; w++)
            {
                Console.WriteLine($"Worker {w}: processed={processedCount[w]}, errors={errorCount[w]}");
            }

            // detailed per-worker metrics
            Console.WriteLine();
            Console.WriteLine("Detailed worker metrics:");
            for (int w = 0; w < processedCount.Length; w++)
            {
                var lastActive = lastActiveTicks[w] == 0 ? "never" : new DateTime(lastActiveTicks[w], DateTimeKind.Utc).ToString("o");
                Console.WriteLine($"Worker {w}: dequeues={dequeues[w]}, enqueues={enqueues[w]}, processed={processedCount[w]}, errors={errorCount[w]}, lastActive={lastActive}");
            }
        }

        // simple regex-based link extractor that returns absolute http(s) URLs
        private static List<string> ExtractLinks(string html)
        {
            var links = new List<string>();
            var regex = new Regex("href=[\\\"'](https?://[^\\\"'#>\\\\s]+)[\\\"']", RegexOptions.IgnoreCase);
            var matches = regex.Matches(html);
            foreach (Match match in matches)
                links.Add(match.Groups[1].Value);
            return links;
        }

        // Simple container for robots.txt rules used by our crawler. This is minimal: lists of
        // allow/disallow path prefixes and a coarse IsAllowed check. It prefers more specific
        // allow entries over disallow ones when both match the same prefix.
        private class RobotsRules
        {
            public List<string> Allows { get; } = new List<string>();
            public List<string> Disallows { get; } = new List<string>();

            // Determine whether the given path (PathAndQuery) is allowed for the provided agent.
            // This is a conservative, prefix-based check: if any Allow prefix matches and is
            // longer than a matching Disallow, it wins. Otherwise any matching Disallow denies.
            public bool IsAllowed(string pathAndQuery, string agent)
            {
                // Normalize empty path
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
                    if (string.IsNullOrEmpty(d)) // 'Disallow:' with empty value means allow all
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
                return true; // default allow
            }
        }
    }
}