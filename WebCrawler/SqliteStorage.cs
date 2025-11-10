using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace WebCrawler
{
    public sealed class SqliteStorage : IStorage
    {
        private readonly string _dbPath;
        private readonly string _connStr;

        public SqliteStorage(string dbPath = "crawler.db")
        {
            _dbPath = dbPath;
            _connStr = $"Data Source={dbPath};Version=3;Journal Mode=WAL;Synchronous=NORMAL;";
        }

        public async Task InitializeAsync()
        {
            var newDb = !File.Exists(_dbPath);
            if (newDb) SQLiteConnection.CreateFile(_dbPath);

            using var conn = new SQLiteConnection(_connStr);
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;

CREATE TABLE IF NOT EXISTS Pages (
    CanonicalUrl TEXT PRIMARY KEY,
    Host TEXT NOT NULL,
    Title TEXT,
    MetaDescription TEXT,
    MetaKeywords TEXT,
    Body TEXT,
    HeadingsJson TEXT,
    LinksJson TEXT,
    ContentHash TEXT NOT NULL,
    CrawlDepth INTEGER NOT NULL,
    FirstSeenUtc DATETIME NOT NULL,
    LastUpdatedUtc DATETIME NOT NULL
);

-- Edge table for graph analytics
CREATE TABLE IF NOT EXISTS PageLinks (
    FromUrl TEXT NOT NULL,
    ToUrl   TEXT NOT NULL,
    PRIMARY KEY (FromUrl, ToUrl),
    FOREIGN KEY (FromUrl) REFERENCES Pages(CanonicalUrl) ON DELETE CASCADE
);

-- Optional version history (append-only when content changes)
CREATE TABLE IF NOT EXISTS PageHistory (
    CanonicalUrl TEXT NOT NULL,
    VersionUtc   DATETIME NOT NULL,
    Title TEXT,
    MetaDescription TEXT,
    MetaKeywords TEXT,
    Body TEXT,
    HeadingsJson TEXT,
    LinksJson TEXT,
    ContentHash TEXT NOT NULL,
    PRIMARY KEY (CanonicalUrl, VersionUtc)
);

CREATE INDEX IF NOT EXISTS IX_Pages_Host ON Pages(Host);
";
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<bool> UpsertPageAsync(PageRecord p)
        {
            using var conn = new SQLiteConnection(_connStr);
            await conn.OpenAsync();

           
            string? existingHash = null;
            {
                using var chk = conn.CreateCommand();
                chk.CommandText = "SELECT ContentHash FROM Pages WHERE CanonicalUrl = @u LIMIT 1";
                chk.Parameters.AddWithValue("@u", p.CanonicalUrl);
                var obj = await chk.ExecuteScalarAsync();
                existingHash = obj as string;
            }

            var changed = existingHash == null || !existingHash.Equals(p.ContentHash, StringComparison.Ordinal);

            
            using (var up = conn.CreateCommand())
            {
                up.CommandText = @"
INSERT INTO Pages
(CanonicalUrl, Host, Title, MetaDescription, MetaKeywords, Body, HeadingsJson, LinksJson, ContentHash, CrawlDepth, FirstSeenUtc, LastUpdatedUtc)
VALUES (@u,@h,@t,@md,@mk,@b,@hj,@lj,@ch,@d,@fs,@lu)
ON CONFLICT(CanonicalUrl) DO UPDATE SET
    Host = excluded.Host,
    Title = excluded.Title,
    MetaDescription = excluded.MetaDescription,
    MetaKeywords = excluded.MetaKeywords,
    Body = excluded.Body,
    HeadingsJson = excluded.HeadingsJson,
    LinksJson = excluded.LinksJson,
    ContentHash = excluded.ContentHash,
    CrawlDepth = MIN(Pages.CrawlDepth, excluded.CrawlDepth), -- keep the shallowest
    LastUpdatedUtc = excluded.LastUpdatedUtc
";
                up.Parameters.AddWithValue("@u", p.CanonicalUrl);
                up.Parameters.AddWithValue("@h", p.Host);
                up.Parameters.AddWithValue("@t", (object?)p.Title ?? DBNull.Value);
                up.Parameters.AddWithValue("@md", (object?)p.MetaDescription ?? DBNull.Value);
                up.Parameters.AddWithValue("@mk", (object?)p.MetaKeywords ?? DBNull.Value);
                up.Parameters.AddWithValue("@b", (object?)p.Body ?? DBNull.Value);
                up.Parameters.AddWithValue("@hj", JsonSerializer.Serialize(p.Headings ?? Array.Empty<string>()));
                up.Parameters.AddWithValue("@lj", JsonSerializer.Serialize(p.Links ?? Array.Empty<string>()));
                up.Parameters.AddWithValue("@ch", p.ContentHash);
                up.Parameters.AddWithValue("@d", p.CrawlDepth);
                up.Parameters.AddWithValue("@fs", p.FetchedAtUtc);
                up.Parameters.AddWithValue("@lu", p.FetchedAtUtc);
                await up.ExecuteNonQueryAsync();
            }

            
            if (changed)
            {
                using var hist = conn.CreateCommand();
                hist.CommandText = @"
INSERT INTO PageHistory
(CanonicalUrl, VersionUtc, Title, MetaDescription, MetaKeywords, Body, HeadingsJson, LinksJson, ContentHash)
VALUES (@u,@ts,@t,@md,@mk,@b,@hj,@lj,@ch)";
                hist.Parameters.AddWithValue("@u", p.CanonicalUrl);
                hist.Parameters.AddWithValue("@ts", p.FetchedAtUtc);
                hist.Parameters.AddWithValue("@t", (object?)p.Title ?? DBNull.Value);
                hist.Parameters.AddWithValue("@md", (object?)p.MetaDescription ?? DBNull.Value);
                hist.Parameters.AddWithValue("@mk", (object?)p.MetaKeywords ?? DBNull.Value);
                hist.Parameters.AddWithValue("@b", (object?)p.Body ?? DBNull.Value);
                hist.Parameters.AddWithValue("@hj", JsonSerializer.Serialize(p.Headings ?? Array.Empty<string>()));
                hist.Parameters.AddWithValue("@lj", JsonSerializer.Serialize(p.Links ?? Array.Empty<string>()));
                hist.Parameters.AddWithValue("@ch", p.ContentHash);
                await hist.ExecuteNonQueryAsync();
            }

            return changed;
        }

        public async Task BulkUpsertLinksAsync(string fromUrl, IEnumerable<string> links)
        {
            var batch = links.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (batch.Length == 0) return;

            using var conn = new SQLiteConnection(_connStr);
            await conn.OpenAsync();

            using var tx = conn.BeginTransaction();
            foreach (var to in batch)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT OR IGNORE INTO PageLinks(FromUrl, ToUrl) VALUES (@f,@t)";
                cmd.Parameters.AddWithValue("@f", fromUrl);
                cmd.Parameters.AddWithValue("@t", to);
                await cmd.ExecuteNonQueryAsync();
            }
            tx.Commit();
        }
    }
}
