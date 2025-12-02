# Frontend Testing Guide

## Current Status ✅
The web crawler frontend is now running on **http://localhost:5000**

## Testing Steps

### 1. **Basic UI Verification**
- [x] The page should load with a dark theme
- [x] Header displays "🕷️ Distributed Web Crawler"
- [x] All sections are visible:
  - Crawler Configuration (with input form)
  - Crawl Status
  - Crawl Results
  - Statistics

### 2. **Form Inputs Testing**
Try entering the following test cases:

#### Test Case 1: Valid URL
- **Starting URL**: `https://example.com`
- **Max Depth**: `2`
- **Max Parallel**: (leave empty - will use default)
- **Expected Result**: Form submits, crawl starts, status updates to "Running"

#### Test Case 2: Invalid URL
- **Starting URL**: `invalid-url`
- **Expected Result**: Error message appears: "Invalid URL format"

#### Test Case 3: Depth Validation
- **Starting URL**: `https://example.com`
- **Max Depth**: `15` (exceeds max of 10)
- **Expected Result**: Error message should appear

### 3. **Crawl Execution Flow**

When you submit a valid URL:

1. **Form becomes disabled** - Prevent multiple simultaneous crawls
2. **Status shows "🔄 Starting crawler..."** with spinner animation
3. **Backend starts crawl** - Check terminal output for crawler messages
4. **Polling begins** - Frontend polls `/api/crawl/{id}` every 1 second
5. **Results display** - When crawl completes, results appear in expandable cards
6. **Statistics update** - Shows total pages, links, avg depth, unique hosts

### 4. **Results Interaction**

Once results appear, you can:

- **Click result cards** to expand/collapse detailed information
- **Search results** using the search box (filters by URL, title, content, host)
- **Sort results** by URL, Depth, or Title
- **View metadata** for each page:
  - Title and description
  - Host and fetch timestamp
  - Word count and content hash
  - Keywords and headings
  - Discovered links

### 5. **API Endpoints Being Called**

The frontend makes these API calls:

```
POST   /api/crawl                    - Start a new crawl
GET    /api/crawl/{id}               - Get crawl status
GET    /api/crawl/{id}/results       - Get crawl results
POST   /api/crawl/{id}/cancel        - Cancel a crawl
GET    /api/crawl/list               - List all sessions
```

### 6. **Common Issues & Solutions**

| Issue | Solution |
|-------|----------|
| Page doesn't load | Ensure server is running: `dotnet run` in Coordinator folder |
| CSS not loading | Check browser console for 404 errors on static files |
| Form won't submit | Check browser console for JavaScript errors |
| Crawl never completes | Check terminal for crawler errors, timeout is 2 minutes |
| Results show "error" | The crawl may have failed - check API response in browser DevTools |

### 7. **Browser DevTools Tips**

1. **Open DevTools**: Press `F12`
2. **Network Tab**: Watch API calls as they happen
3. **Console Tab**: Check for JavaScript errors
4. **Check actual responses**:
   - Click on `/api/crawl` POST request
   - Check Response tab to see if crawl was created with ID
   - Check `/api/crawl/{id}` responses to see status progression

### 8. **Expected Behavior Timeline**

```
T+0s    : Form submitted, status = "🔄 Starting crawler..."
T+1s    : Backend processes request, crawl ID returned (202 Accepted)
T+2s    : Status = "Running" with page count
T+5s+   : Crawler downloads pages, counts increase
T+30s+  : Depending on URL complexity, results may appear
T+N     : Status = "✅ Crawl complete! Found X pages."
         Results and statistics display
```

### 9. **Manual API Testing**

You can also test endpoints directly using PowerShell:

```powershell
# Start a crawl
$response = Invoke-RestMethod -Uri "http://localhost:5000/api/crawl" `
  -Method Post `
  -Headers @{"Content-Type"="application/json"} `
  -Body @{startUrl="https://example.com"; maxDepth=2} | ConvertTo-Json

# Get crawl status
Invoke-RestMethod -Uri "http://localhost:5000/api/crawl/$($response.id)"

# Get results (once completed)
Invoke-RestMethod -Uri "http://localhost:5000/api/crawl/$($response.id)/results"
```

## Test Checklist

- [ ] Page loads and displays correctly
- [ ] Form validation works (rejects invalid URLs)
- [ ] Submit button triggers crawl
- [ ] Status updates during crawl
- [ ] Results display after completion
- [ ] Search filter works
- [ ] Sort dropdown works
- [ ] Statistics calculate correctly
- [ ] Result cards expand/collapse on click
- [ ] Links appear formatted correctly

## Notes

- The crawler respects robots.txt
- Selenium is limited to 2 parallel workers
- First crawl may take longer as Chrome driver initializes
- Results are stored in memory for this session
- Database (crawler.db) persists data across runs

---

**Server Running At**: http://localhost:5000
**Press Ctrl+C in terminal to stop the server**
