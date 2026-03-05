using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IndigoMovieManager.FileIndex.UsnMft
{
    public interface IFileIndexService : IDisposable
    {
        string ActiveBackendName { get; }

        FileIndexBackendMode ActiveBackendMode { get; }

        int IndexedCount { get; }

        Task<int> RebuildIndexAsync(IProgress<IndexProgress> progress, CancellationToken cancellationToken);

        IReadOnlyList<SearchResultItem> Search(string query, int maxResults);
    }
}
