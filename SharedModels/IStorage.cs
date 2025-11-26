// IStorage.cs
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WebCrawler
{
    public interface IStorage
    {
        Task InitializeAsync();
        Task<bool> UpsertPageAsync(PageRecord p);
        Task BulkUpsertLinksAsync(string fromUrl, IEnumerable<string> links);
        Task UpsertInvertedIndexAsync(string word, string url, int freq);

    }
}
