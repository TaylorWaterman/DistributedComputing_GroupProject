# Quick Reference Guide - Web Crawler

## Getting Started in 3 Steps

### 1. Build
```bash
dotnet build
```

### 2. Run
```bash
cd Coordinator && dotnet run
```

### 3. Open Browser
Navigate to: **http://localhost:5000**

---

## Web UI Overview

### 📝 Input Form
- **Starting URL**: Where to begin crawling (required)
- **Max Depth**: Follow links up to N levels deep (0-10)
- **Parallel Workers**: Number of browser instances (auto: CPU cores, max: 2)

### 🔄 Status Section
- Shows real-time crawl progress
- Updates every second
- Displays page count found

### 📋 Results Section
- **Search Box**: Find pages by URL, title, content, or host
- **Sort Dropdown**: Organize by URL, depth, or title
- **Expandable Cards**: Click to see full details:
  - Body content preview
  - Keywords and headings
  - Discovered links
  - Metadata (fetch time, word count, etc.)

### 📊 Statistics
- Total pages crawled
- Total links discovered
- Average crawl depth
- Unique hosts visited

---

## API Reference

### Start Crawl
```bash
POST /api/crawl
Content-Type: application/json

{
  "startUrl": "https://example.com",
  "maxDepth": 2,
  "maxParallel": null
}

Response:
{
  "id": "uuid",
  "status": "Queued",
  "startTime": "2025-12-01T...",
  "pagesFound": 0,
  "linksFound": 0
}
```

### Get Status
```bash
GET /api/crawl/{id}

Response:
{
  "id": "uuid",
  "status": "Running|Completed|Failed|Cancelled",
  "startTime": "...",
  "pagesFound": 15,
  "linksFound": 42,
  "errorMessage": null
}
```

### Get Results
```bash
GET /api/crawl/{id}/results

Response:
{
  "results": [
    {
      "canonicalUrl": "https://...",
      "host": "example.com",
      "title": "Page Title",
      "body": "Page content...",
      "crawlDepth": 1,
      "links": ["url1", "url2", ...]
    }
  ],
  "totalPages": 15,
  "totalLinks": 42,
  "avgDepth": 1.5,
  "uniqueHosts": 1
}
```

### Cancel Crawl
```bash
POST /api/crawl/{id}/cancel

Response:
{
  "message": "Crawl cancelled",
  "id": "uuid"
}
```

### List Active Crawls
```bash
GET /api/crawl/list

Response: [
  { "id": "...", "status": "Running", ... }
]
```

---

## Configuration

### Change Port
Edit `Coordinator/Program.cs`, find this line:
```csharp
builder.WebHost.UseUrls("http://0.0.0.0:5000");
```
Change `5000` to your desired port.

### Database Location
The SQLite database (`crawler.db`) is created in your working directory. To change:
```csharp
IStorage storage = new SqliteStorage("path/to/crawler.db");
```

### Increase Worker Limit (Advanced)
Find this code and increase the limit:
```csharp
if (session.MaxParallel > 2)
    session.MaxParallel = 2; // Change this number
```
**Note**: Selenium has limitations above 2 concurrent browsers on most systems.

---

## Features Checklist

✅ Start crawls with custom parameters  
✅ Monitor progress in real-time  
✅ View detailed results for each page  
✅ Search across all discovered pages  
✅ Sort results by multiple criteria  
✅ See statistics and metrics  
✅ Run multiple crawls simultaneously  
✅ Cancel active crawls  
✅ Responsive mobile-friendly design  
✅ Dark theme optimized for readability  
✅ RESTful API for programmatic access  
✅ Respects robots.txt  
✅ Automatic robots.txt caching  
✅ Headless Chrome for background crawling  
✅ SQLite persistence  

---

## Data Fields Explained

### Each Crawled Page Contains:
- **Canonical URL**: Normalized, deduplicated URL
- **Host**: Domain name extracted from URL
- **Title**: HTML `<title>` tag content
- **Body**: Extracted text content (cleaned)
- **Meta Description**: SEO meta tag value
- **Meta Keywords**: SEO keywords
- **Content Hash**: SHA1 hash for duplicate detection
- **Crawl Depth**: How many links deep (0 = starting URL)
- **Fetch Time**: When page was crawled
- **Headings**: `<h1>`, `<h2>`, etc. extracted
- **Links**: All discovered outbound links

---

## Common Tasks

### Crawl a Website
1. Enter URL: `https://www.example.com`
2. Set depth: `2`
3. Click "Start Crawling"
4. Wait for completion
5. Review results

### Find a Specific Page
1. Use Search Box
2. Enter partial URL or title
3. Results filter in real-time

### Export Results Manually
1. Open DevTools (F12)
2. Go to Network tab
3. Call: `GET /api/crawl/{id}/results`
4. Copy JSON response
5. Save as `results.json`

### Monitor Multiple Crawls
1. Each crawl gets a unique ID
2. Status automatically refreshes
3. Can track all sessions via `/api/crawl/list`

---

## Limits & Constraints

- **Max Depth**: 10 levels
- **Parallel Workers**: 2 (Selenium limitation)
- **Polling Timeout**: 120 seconds (2 minutes)
- **URL Size**: No limit (depends on filesystem)
- **Concurrent Crawls**: Limited by RAM/CPU
- **Page Size**: No limit (entire page loaded in memory)

---

## Error Messages

| Error | Cause | Solution |
|-------|-------|----------|
| "Invalid URL format" | URL parse failed | Ensure valid URL with http:// or https:// |
| "Port 5000 already in use" | Another process using port | Change port in Program.cs |
| "Crawl is still running" | Results requested too early | Wait for status = "Completed" |
| "Chrome driver not found" | Selenium missing driver | Reinstall Selenium.WebDriver.ChromeDriver |
| "Crawl timeout" | Took longer than 2 minutes | Increase maxAttempts in app.js |

---

## File Structure

```
Coordinator/
├── Program.cs              # Web server + crawling logic
├── Coordinator.csproj      # Project file
├── README.md              # Full documentation
├── wwwroot/               # Static files
│   ├── index.html         # Web UI
│   ├── app.js            # Frontend logic
│   └── styles.css        # Styling
└── bin/Debug/            # Compiled output
```

---

## Tips & Tricks

### Tip 1: Test with Small Crawls First
Start with depth 1-2 on small sites to verify functionality

### Tip 2: Monitor Browser Resources
Watch task manager - too many workers will consume RAM

### Tip 3: robots.txt Matters
Ethical crawling means respecting robots.txt rules

### Tip 4: Use Developer Tools
F12 > Network tab shows actual API calls being made

### Tip 5: Clear Old Data
Delete `crawler.db` to start fresh

---

## Support

For issues or questions, check:
1. **Console Output**: Server logs appear in terminal
2. **Browser Console**: F12 > Console tab for client errors
3. **Network Tab**: F12 > Network to see API requests
4. **Full Docs**: See `Coordinator/README.md`

---

**Version**: 1.0  
**Last Updated**: December 1, 2025  
**Built With**: ASP.NET Core 8.0, Selenium, SQLite, HTML5, CSS3, JavaScript
