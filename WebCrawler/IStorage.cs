using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebCrawler
{
    public interface IStorage
    {
        Task InitializeAsync();

        // Upsert page (dedupe by canonical URL). Returns true if content changed.
        Task<bool> UpsertPageAsync(PageRecord page);

        // Bulk upsert discovered edges (canonicalUrl -> link)
        Task BulkUpsertLinksAsync(string canonicalUrl, IEnumerable<string> links);
    }
}
