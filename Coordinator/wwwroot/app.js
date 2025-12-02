// app.js - Web Crawler Frontend Logic

class WebCrawlerUI {
    constructor() {
        this.form = document.getElementById('crawlerForm');
        this.statusContainer = document.getElementById('statusContainer');
        this.resultsContainer = document.getElementById('resultsContainer');
        this.searchBox = document.getElementById('searchBox');
        this.sortBy = document.getElementById('sortBy');
        this.results = [];
        this.filteredResults = [];
        this.currentCrawlId = null;
        
        this.init();
    }

    init() {
        this.form.addEventListener('submit', (e) => this.handleFormSubmit(e));
        this.searchBox.addEventListener('input', (e) => this.handleSearch(e));
        this.sortBy.addEventListener('change', (e) => this.handleSort(e));
    }

    async handleFormSubmit(e) {
        e.preventDefault();

        const startUrl = document.getElementById('startUrl').value.trim();
        const maxDepth = parseInt(document.getElementById('maxDepth').value) || 2;
        const maxParallel = document.getElementById('maxParallel').value 
            ? parseInt(document.getElementById('maxParallel').value) 
            : null;

        if (!this.validateUrl(startUrl)) {
            this.showStatus('Invalid URL format', 'error');
            return;
        }

        this.clearResults();
        this.setFormDisabled(true);
        this.showStatus('🔄 Starting crawler...', 'running');

        try {
            await this.startCrawl(startUrl, maxDepth, maxParallel);
        } catch (error) {
            this.showStatus(`❌ Error: ${error.message}`, 'error');
        } finally {
            this.setFormDisabled(false);
        }
    }

    async startCrawl(startUrl, maxDepth, maxParallel) {
        const response = await fetch('/api/crawl', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify({
                startUrl: startUrl,
                maxDepth: maxDepth,
                maxParallel: maxParallel
            })
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to start crawl');
        }

        const crawlSession = await response.json();
        this.currentCrawlId = crawlSession.id;
        
        // Poll for results
        await this.pollCrawlStatus(crawlSession.id);
    }

    async pollCrawlStatus(crawlId) {
        const maxAttempts = 120; // 2 minutes with 1 second polls
        let attempts = 0;

        while (attempts < maxAttempts) {
            try {
                const response = await fetch(`/api/crawl/${crawlId}`);
                if (!response.ok) throw new Error('Failed to get crawl status');

                const status = await response.json();

                if (status.status === 'Completed') {
                    await this.fetchResults(crawlId);
                    return;
                } else if (status.status === 'Failed') {
                    this.showStatus(`❌ Crawl failed: ${status.errorMessage}`, 'error');
                    return;
                } else if (status.status === 'Cancelled') {
                    this.showStatus('Crawl was cancelled', 'info');
                    return;
                } else {
                    this.showStatus(`🔄 Crawling... (${status.pagesFound} pages found)`, 'running');
                }

                await new Promise(resolve => setTimeout(resolve, 1000));
                attempts++;
            } catch (error) {
                console.error('Error polling status:', error);
                await new Promise(resolve => setTimeout(resolve, 1000));
                attempts++;
            }
        }

        this.showStatus('❌ Crawl timeout', 'error');
    }

    async fetchResults(crawlId) {
        const response = await fetch(`/api/crawl/${crawlId}/results`);
        
        if (!response.ok) {
            const error = await response.json();
            this.showStatus(`Error fetching results: ${error.error}`, 'error');
            return;
        }

        const data = await response.json();
        this.results = data.results || [];
        this.filteredResults = [...this.results];

        this.displayResults();
        this.updateStats();
        this.searchBox.disabled = false;
        this.sortBy.disabled = false;

        this.showStatus(`✅ Crawl complete! Found ${this.results.length} pages.`, 'success');
    }

    validateUrl(url) {
        try {
            new URL(url);
            return true;
        } catch {
            return false;
        }
    }

    setFormDisabled(disabled) {
        document.querySelector('.btn-submit').disabled = disabled;
        document.getElementById('startUrl').disabled = disabled;
        document.getElementById('maxDepth').disabled = disabled;
        document.getElementById('maxParallel').disabled = disabled;
    }

    showStatus(message, type = 'info') {
        const statusDiv = document.createElement('div');
        statusDiv.className = `status-container status-${type}`;
        
        if (type === 'running') {
            statusDiv.innerHTML = `<span class="spinner"></span><span>${message}</span>`;
        } else {
            statusDiv.textContent = message;
        }
        
        this.statusContainer.innerHTML = '';
        this.statusContainer.appendChild(statusDiv);
    }

    clearResults() {
        this.results = [];
        this.filteredResults = [];
        this.resultsContainer.innerHTML = '<p class="results-empty">Crawling in progress...</p>';
        this.updateStats();
        this.searchBox.disabled = true;
        this.sortBy.disabled = true;
    }



    displayResults() {
        if (this.filteredResults.length === 0) {
            this.resultsContainer.innerHTML = '<p class="results-empty">No results found.</p>';
            return;
        }

        this.resultsContainer.innerHTML = '';
        
        this.filteredResults.forEach(result => {
            const card = this.createResultCard(result);
            this.resultsContainer.appendChild(card);
        });
    }

    createResultCard(result) {
        const card = document.createElement('div');
        card.className = 'result-card';

        const formattedDate = new Date(result.fetchedAtUtc).toLocaleString();
        const wordCount = result.body ? result.body.split(/\s+/).length : 0;

        card.innerHTML = `
            <div class="result-header">
                <div class="result-url">${this.escapeHtml(result.canonicalUrl)}</div>
                <span class="result-depth">Depth: ${result.crawlDepth}</span>
            </div>
            
            <div class="result-title">${this.escapeHtml(result.title || 'Untitled')}</div>
            
            <div class="result-description">
                ${this.escapeHtml(result.metaDescription || result.body || 'No description available')}
            </div>

            <div class="result-meta">
                <div class="meta-item">
                    <span class="meta-label">Host</span>
                    <span>${this.escapeHtml(result.host)}</span>
                </div>
                <div class="meta-item">
                    <span class="meta-label">Fetched</span>
                    <span>${formattedDate}</span>
                </div>
                <div class="meta-item">
                    <span class="meta-label">Words</span>
                    <span>${wordCount}</span>
                </div>
                <div class="meta-item">
                    <span class="meta-label">Hash</span>
                    <span title="${result.contentHash}" style="word-break: break-all;">
                        ${result.contentHash.substring(0, 16)}...
                    </span>
                </div>
            </div>

            <div class="result-content">
                ${result.body ? `
                    <div class="content-section">
                        <div class="content-title">Body Content</div>
                        <div class="content-body">${this.escapeHtml(result.body)}</div>
                    </div>
                ` : ''}

                ${result.metaKeywords ? `
                    <div class="content-section">
                        <div class="content-title">Keywords</div>
                        <div>${this.escapeHtml(result.metaKeywords)}</div>
                    </div>
                ` : ''}

                ${result.headings && result.headings.length > 0 ? `
                    <div class="content-section">
                        <div class="content-title">Headings</div>
                        <div class="content-body">
                            ${result.headings.map(h => `• ${this.escapeHtml(h)}`).join('<br>')}
                        </div>
                    </div>
                ` : ''}

                ${result.links && result.links.length > 0 ? `
                    <div class="content-section">
                        <div class="content-title">Found Links (${result.links.length})</div>
                        <div class="links-list">
                            ${result.links.slice(0, 5).map(link => 
                                `<span class="link-tag" title="${this.escapeHtml(link)}">${this.escapeHtml(link)}</span>`
                            ).join('')}
                            ${result.links.length > 5 ? 
                                `<span class="link-tag">+${result.links.length - 5} more</span>` : ''}
                        </div>
                    </div>
                ` : ''}
            </div>
        `;

        card.addEventListener('click', () => card.classList.toggle('expanded'));
        
        return card;
    }

    handleSearch(e) {
        const query = e.target.value.toLowerCase();
        
        if (!query) {
            this.filteredResults = [...this.results];
        } else {
            this.filteredResults = this.results.filter(result => 
                result.canonicalUrl.toLowerCase().includes(query) ||
                (result.title && result.title.toLowerCase().includes(query)) ||
                (result.body && result.body.toLowerCase().includes(query)) ||
                result.host.toLowerCase().includes(query)
            );
        }

        this.displayResults();
    }

    handleSort(e) {
        const sortType = e.target.value;

        switch(sortType) {
            case 'url':
                this.filteredResults.sort((a, b) => 
                    a.canonicalUrl.localeCompare(b.canonicalUrl)
                );
                break;
            case 'depth':
                this.filteredResults.sort((a, b) => 
                    a.crawlDepth - b.crawlDepth
                );
                break;
            case 'title':
                this.filteredResults.sort((a, b) => 
                    (a.title || '').localeCompare(b.title || '')
                );
                break;
        }

        this.displayResults();
    }

    updateStats() {
        if (this.results.length === 0) {
            document.getElementById('statPages').textContent = '0';
            document.getElementById('statLinks').textContent = '0';
            document.getElementById('statAvgDepth').textContent = '0';
            document.getElementById('statHosts').textContent = '0';
            return;
        }

        const totalPages = this.results.length;
        const totalLinks = this.results.reduce((sum, r) => sum + (r.links?.length || 0), 0);
        const avgDepth = (this.results.reduce((sum, r) => sum + r.crawlDepth, 0) / totalPages).toFixed(2);
        const uniqueHosts = new Set(this.results.map(r => r.host)).size;

        document.getElementById('statPages').textContent = totalPages;
        document.getElementById('statLinks').textContent = totalLinks;
        document.getElementById('statAvgDepth').textContent = avgDepth;
        document.getElementById('statHosts').textContent = uniqueHosts;
    }

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
}

// Initialize the UI when the page loads
document.addEventListener('DOMContentLoaded', () => {
    new WebCrawlerUI();
});
