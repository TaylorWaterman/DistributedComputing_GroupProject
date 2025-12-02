# Distributed Web Crawler - Web Interface

## Overview

The web crawler now has a complete ASP.NET Core web interface that allows users to configure and run crawls through a modern, responsive web UI. The backend automatically crawls websites and stores results in SQLite, while the frontend displays real-time status and results.

## Quick Start

### Prerequisites
- .NET 8.0 SDK or later
- Chrome/Chromium browser (for Selenium)
- Port 5000 available on your machine

### Running the Web Crawler

1. **Build the project:**
   ```bash
   dotnet build
   ```

2. **Run the Coordinator (Web Server):**
   ```bash
   cd Coordinator
   dotnet run
   ```

   The server will start on `http://localhost:5000`

3. **Open your browser:**
   Navigate to `http://localhost:5000` to access the web UI

## Features

### Frontend UI
- **Input Form**: Configure crawl parameters
  - Starting URL (required)
  - Maximum depth (0-10)
  - Parallel workers (auto-detected, max 2 for Selenium)

- **Real-Time Status**: Track crawl progress
  - Running/Completed/Failed status
  - Page count updates

- **Results Display**:
  - Expandable result cards showing detailed page information
  - Search functionality (by URL, title, content, host)
  - Sort options (by URL, depth, or title)
  - Detailed metadata (host, fetch time, word count, hash)
  - Content preview (body, keywords, headings, links)

- **Statistics Dashboard**:
  - Total pages crawled
  - Total links discovered
  - Average crawl depth
  - Unique hosts visited

### Backend API

The ASP.NET Core backend provides REST API endpoints:

- **POST /api/crawl** - Start a new crawl
  ```json
  {
    "startUrl": "https://example.com",
    "maxDepth": 2,
    "maxParallel": null
  }
  ```

- **GET /api/crawl/{id}** - Get crawl status
  Returns crawl progress information

- **GET /api/crawl/{id}/results** - Get crawl results
  Returns all discovered pages and statistics

- **POST /api/crawl/{id}/cancel** - Cancel an active crawl

- **GET /api/crawl/list** - List all active crawl sessions

### Data Storage

Results are stored in SQLite (`crawler.db`):
- **Pages**: Crawled page URLs, titles, descriptions, content
- **Links**: Discovered links and their sources
- **Inverted Index**: Word frequency search index

## How It Works

1. **User submits form** with URL and crawl parameters
2. **Backend creates a crawl session** and starts processing in background
3. **Frontend polls status** every second to show progress
4. **Multiple Selenium workers** crawl pages in parallel (limited to 2)
5. **robots.txt is respected** for ethical crawling
6. **HTML is parsed** to extract titles, descriptions, links, and content
7. **Results are stored** in SQLite with full-text searchable index
8. **Frontend displays results** with expandable cards and search/sort capabilities

## Configuration

### Web Server
- **Port**: 5000 (configurable in Program.cs)
- **CORS**: Enabled for all origins
- **Static Files**: Served from `wwwroot/` directory

### Selenium (Chrome Driver)
- **Headless Mode**: Enabled for background operation
- **GPU Acceleration**: Disabled for stability
- **Parallel Workers**: Limited to 2 (Selenium limitation)

### SQLite Database
- **File**: `crawler.db` (auto-created in working directory)
- **Tables**: pages, links, inverted_index

## Development

### Project Structure
```
Coordinator/
├── Program.cs           # ASP.NET Core web server + crawling logic
├── Coordinator.csproj   # Project configuration
├── wwwroot/
│   ├── index.html      # Web UI
│   ├── app.js          # Frontend logic (API integration)
│   └── styles.css      # Modern styling
└── bin/
    └── Debug/          # Built application
```

### Adding Features

To add new API endpoints, edit `Program.cs` and add map routes:
```csharp
app.MapGet("/api/newendpoint", () => {
    // Implementation
});
```

To modify the UI, edit files in `wwwroot/`:
- `index.html` - Structure
- `styles.css` - Styling  
- `app.js` - Client-side logic

## Troubleshooting

### Port Already in Use
If port 5000 is already in use, modify `Program.cs`:
```csharp
builder.WebHost.UseUrls("http://0.0.0.0:5001"); // Change port
```

### Chrome Driver Issues
Ensure Chrome/Chromium is installed. The Selenium package includes ChromeDriver automatically.

### Database Locked Error
Ensure only one instance of the crawler is running. Delete `crawler.db` to start fresh.

### Crawl Not Starting
Check browser console (F12) for network errors. Verify the URL is valid and accessible.

## Performance Notes

- **Parallel Workers**: Limited to 2 for Selenium due to browser driver limitations
- **robots.txt Caching**: Robots rules are cached per domain
- **HTML Parsing**: Uses regex for lightweight extraction
- **Database**: SQLite is suitable for moderate-sized crawls (thousands of pages)

## Next Steps

Future enhancements could include:
- Distributed worker nodes
- Custom crawl scheduling
- Export results to CSV/JSON
- Advanced filtering and analytics
- Crawl history and resumption
- Rate limiting and request throttling

## License

See parent project for licensing information.
