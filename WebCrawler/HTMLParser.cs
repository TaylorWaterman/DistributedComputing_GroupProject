using HtmlAgilityPack;
using System;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;

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
        public HTMLParser() {}

		public void ParseHTML(string html, string url)
		{
			URL = url;
			var doc = new HtmlDocument();
			doc.LoadHtml(html);

			// Extract title
				var titleNode = doc.DocumentNode.SelectSingleNode("//title");
				Title = DecodeHtml(titleNode?.InnerText);

			// Extract meta description
			var metaDesNode = doc.DocumentNode.SelectSingleNode("//meta[@name='description']");
			var metaDesVal = metaDesNode?.GetAttributeValue("content", "");
			MetaDescription = string.IsNullOrWhiteSpace(metaDesVal) ? null : DecodeHtml(metaDesVal);

			// Extract meta keywords
			var metaKeyNode = doc.DocumentNode.SelectSingleNode("//meta[@name='keywords']");
			var metaKeyVal = metaKeyNode?.GetAttributeValue("content", "");
			MetaKeywords = string.IsNullOrWhiteSpace(metaKeyVal) ? null : DecodeHtml(metaKeyVal);

			// Extract body text and normalize whitespace
			var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
			if (bodyNode != null)
			{
				var rawBody = bodyNode.InnerText;
				// Decode HTML entities and normalize whitespace
				Body = DecodeHtml(rawBody, normalize: true);
			}
			else
			{
				Body = null;
			}

			// Extract headings
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

			// Normalize links
			Links.Clear();
			NormalizeLinks(url, doc);

        }

		public void NormalizeLinks(string url, HtmlDocument doc)
		{
			foreach (var linkNode in doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
			{
				var href = linkNode.GetAttributeValue("href", string.Empty).Trim();

				// Ignore empty, anchors, mailto, javascript, tel links
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
					// Skip malformed URLs
					continue;
				}

				// Continue if scheme is http or https
				if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
				{
					continue;
				}

				// Strip fragment and normalize host/port
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
				// Replace non-breaking and zero-width spaces with a normal space, then collapse runs of whitespace
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

		/// <summary>
		/// Convert literal JSON-style unicode escapes (e.g. "\\u0026") inside a string into the actual characters.
		/// This handles cases where the text already contains backslash-u sequences and we want the real character
		/// before serializing to JSON.
		/// </summary>
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

        public void SaveToDB(string filePath)
		{
			// Using JSON for now
			// TODO: Implement actual database here

			try
			{
				// Ensure fields are decoded of HTML entities before saving
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
			} catch (Exception ex)
			{
				Console.Error.WriteLine($"Error saving JSON to `{filePath}`: {ex.Message}");
			}

        }
	}

}