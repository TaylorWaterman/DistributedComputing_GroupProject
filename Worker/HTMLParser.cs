// HTMLParser.cs
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebCrawler
{
    public class HTMLParser
    {
        public string URL { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Body { get; set; }
        public string? MetaDescription { get; set; }
        public string? MetaKeywords { get; set; }
        public List<string> Headings { get; set; } = new();
        public List<string> Links { get; set; } = new();
        public Dictionary<string, int> WordFrequency { get; private set; } = new();


        public HTMLParser() { }

        public void ParseHTML(string html, string url)
        {
            URL = url;
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Title
            var titleNode = doc.DocumentNode.SelectSingleNode("//title");
            Title = DecodeHtml(titleNode?.InnerText);

            // Meta description
            var metaDesNode = doc.DocumentNode.SelectSingleNode("//meta[@name='description']");
            var metaDesVal = metaDesNode?.GetAttributeValue("content", "");
            MetaDescription = string.IsNullOrWhiteSpace(metaDesVal) ? null : DecodeHtml(metaDesVal);

            // Meta keywords
            var metaKeyNode = doc.DocumentNode.SelectSingleNode("//meta[@name='keywords']");
            var metaKeyVal = metaKeyNode?.GetAttributeValue("content", "");
            MetaKeywords = string.IsNullOrWhiteSpace(metaKeyVal) ? null : DecodeHtml(metaKeyVal);

            // Body text
            var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
            if (bodyNode != null)
            {
                var rawBody = bodyNode.InnerText;
                Body = DecodeHtml(rawBody, normalize: true);
            }
            else
            {
                Body = null;
            }
        
            WordFrequency = BuildWordFrequency(Body);



            // Headings
            Headings.Clear();
            for (int i = 1; i <= 6; i++)
            {
                var headingNodes = doc.DocumentNode.SelectNodes($"//h{i}");
                if (headingNodes != null)
                {
                    foreach (var node in headingNodes)
                    {
                        Headings.Add(DecodeHtml(node.InnerText, normalize: true) ?? string.Empty);
                    }
                }
            }

            // Links
            Links.Clear();
            NormalizeLinks(url, doc);
        }

        public void NormalizeLinks(string url, HtmlDocument doc)
        {
            foreach (var linkNode in doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
            {
                var href = linkNode.GetAttributeValue("href", string.Empty).Trim();

                if (string.IsNullOrEmpty(href) ||
                    href.StartsWith("#") ||
                    href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                    href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Uri uri;
                if (Uri.TryCreate(href, UriKind.Absolute, out var absoluteUri))
                {
                    uri = absoluteUri;
                }
                else if (Uri.TryCreate(new Uri(url), href, out var relativeUri))
                {
                    uri = relativeUri;
                }
                else
                {
                    continue;
                }

                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                {
                    continue;
                }

                var builder = new UriBuilder(uri) { Fragment = string.Empty };
                if ((builder.Scheme == Uri.UriSchemeHttp && builder.Port == 80) ||
                    (builder.Scheme == Uri.UriSchemeHttps && builder.Port == 443))
                {
                    builder.Port = -1;
                }
                builder.Host = builder.Host.ToLowerInvariant();

                Links.Add(builder.Uri.ToString());
            }
        }

        private static string? DecodeHtml(string? input, bool normalize = false)
        {
            if (input == null) return null;
            var decoded = HtmlEntity.DeEntitize(input);

            if (normalize)
            {
                decoded = decoded.Replace("\u00A0", " ")
                                 .Replace("\u200B", " ")
                                 .Replace("\uFEFF", " ");
                decoded = Regex.Replace(decoded, "\\s+", " ").Trim();
            }
            else
            {
                decoded = decoded.Trim();
            }
            return decoded;
        }

        private static string? UnescapeJsonUnicode(string? input)
        {
            if (input == null) return null;
            return Regex.Replace(input, "\\\\u([0-9A-Fa-f]{4})", match =>
            {
                var hex = match.Groups[1].Value;
                int code = int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                return char.ConvertFromUtf32(code);
            });
        }

        // JSON dump helper (not used by DB, but nice for debugging)
        public void SaveToDB(string filePath)
        {
            try
            {
                Title = UnescapeJsonUnicode(DecodeHtml(Title)) ?? Title;
                Body = UnescapeJsonUnicode(DecodeHtml(Body, normalize: true)) ?? Body;
                MetaDescription = UnescapeJsonUnicode(DecodeHtml(MetaDescription)) ?? MetaDescription;
                MetaKeywords = UnescapeJsonUnicode(DecodeHtml(MetaKeywords)) ?? MetaKeywords;

                if (Headings != null)
                {
                    for (int i = 0; i < Headings.Count; i++)
                        Headings[i] = UnescapeJsonUnicode(DecodeHtml(Headings[i], normalize: true)) ?? Headings[i];
                }
                if (Links != null)
                {
                    for (int i = 0; i < Links.Count; i++)
                        Links[i] = UnescapeJsonUnicode(DecodeHtml(Links[i])) ?? Links[i];
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error saving JSON to `{filePath}`: {ex.Message}");
            }
        }

        // NEW: build a PageRecord for the SQLite storage layer
        public PageRecord ToPageRecord(int depth, DateTime fetchedAtUtc)
        {
            string ComputeHash()
            {
                using var sha = SHA256.Create();
                string combined =
                    (Title ?? "") +
                    (Body ?? "") +
                    (MetaDescription ?? "") +
                    (MetaKeywords ?? "");
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(combined));
                return Convert.ToHexString(bytes);
            }

            var uri = new Uri(URL);

            return new PageRecord
            {
                CanonicalUrl = uri.ToString(),
                Host = uri.Host.ToLowerInvariant(),
                Title = Title,
                Body = Body,
                MetaDescription = MetaDescription,
                MetaKeywords = MetaKeywords,
                ContentHash = ComputeHash(),
                CrawlDepth = depth,
                FetchedAtUtc = fetchedAtUtc,
                Headings = Headings.ToArray(),
                Links = Links.ToArray()
            };
        }
        // NEW: Tokenize and count word frequencies for inverted index
        private Dictionary<string, int> BuildWordFrequency(string? text)
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(text))
                return dict;

        // Match alphanumeric words
            var words = Regex.Matches(text.ToLower(), @"[a-z0-9]+");

            foreach (Match m in words)
         {
                var w = m.Value;
                 if (dict.TryGetValue(w, out int count))
                    dict[w] = count + 1;
                else
                    dict[w] = 1;
        }

            return dict;
}

    }
}
