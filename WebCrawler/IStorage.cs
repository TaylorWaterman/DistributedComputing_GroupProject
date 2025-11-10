using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebCrawler
{
    public interface IStorage
    {
        Task InitializeAsync();

        // Returns true if content changed.
        Task<bool> UpsertPageAsync(PageRecord page);

        // Bulk upsert discovered edges.
        Task BulkUpsertLinksAsync(string canonicalUrl, IEnumerable<string> links);
    }
}
