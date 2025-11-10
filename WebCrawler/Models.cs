using System;
using System.Collections.Generic;

namespace WebCrawler
{
    public sealed class PageRecord
    {
        public string CanonicalUrl { get; init; } = "";
        public string Host { get; init; } = "";
        public string? Title { get; init; }
        public string? Body { get; init; }
        public string? MetaDescription { get; init; }
        public string? MetaKeywords { get; init; }
        public string ContentHash { get; init; } = "";  // SHA-256 of normalized text
        public int CrawlDepth { get; init; }
        public DateTime FetchedAtUtc { get; init; }
        public IReadOnlyList<string> Headings { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Links { get; init; } = Array.Empty<string>();
    }
}
