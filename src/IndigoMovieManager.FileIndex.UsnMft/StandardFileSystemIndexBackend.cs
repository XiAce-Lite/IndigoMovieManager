using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IndigoMovieManager.FileIndex.UsnMft
{
    internal sealed class StandardFileSystemIndexBackend : IIndexBackend
    {
        private const int WatcherRecoveryDebounceMs = 1500;
        private const int DisposeGateWaitTimeoutMs = 5000;

        private readonly object syncRoot = new object();
        private readonly string[] roots;
        private readonly string[] excludedPathPrefixes;
        private readonly string[] excludedDirectoryNames;
        private readonly Dictionary<string, SearchResultItem> entries = new Dictionary<string, SearchResultItem>(StringComparer.OrdinalIgnoreCase);
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly SemaphoreSlim rebuildGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetimeCts = new CancellationTokenSource();
        private int watcherRecoveryRequested;
        private int watcherRecoveryWorkerRunning;
        private volatile bool disposed;

        public StandardFileSystemIndexBackend(IEnumerable<string> roots, IEnumerable<string> excludedPaths)
        {
            var normalizedRoots = (roots ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (normalizedRoots.Length == 0)
            {
                normalizedRoots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
            }

            this.roots = normalizedRoots;

            var excludedPathPrefixesList = new List<string>();
            var excludedDirectoryNamesList = new List<string>();
            foreach (var raw in excludedPaths ?? Array.Empty<string>())
            {
                var token = (raw ?? string.Empty).Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                if (Path.IsPathRooted(token))
                {
                    try
                    {
                        excludedPathPrefixesList.Add(NormalizePath(Path.GetFullPath(token)));
                        continue;
                    }
                    catch
                    {
                    }
                }

                excludedDirectoryNamesList.Add(token);
            }

            excludedPathPrefixes = excludedPathPrefixesList
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            excludedDirectoryNames = excludedDirectoryNamesList
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public string BackendName => "StandardFileSystem";

        public FileIndexBackendMode BackendMode => FileIndexBackendMode.StandardFileSystem;

        public int IndexedCount
        {
            get
            {
                lock (syncRoot)
                {
                    return entries.Count;
                }
            }
        }

        public Task<int> RebuildIndexAsync(IProgress<IndexProgress> progress, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            return Task.Run(() => ExecuteRebuild(progress, cancellationToken), cancellationToken);
        }

        public IReadOnlyList<SearchResultItem> Search(string query, int maxResults)
        {
            if (maxResults <= 0)
            {
                return Array.Empty<SearchResultItem>();
            }

            SearchResultItem[] snapshot;
            lock (syncRoot)
            {
                snapshot = entries.Values.ToArray();
            }

            if (snapshot.Length == 0)
            {
                return Array.Empty<SearchResultItem>();
            }

            var normalized = (query ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized.Length == 0)
            {
                return snapshot
                    .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(maxResults)
                    .ToArray();
            }

            var terms = normalized
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Distinct()
                .ToArray();

            var matched = new List<ScoredItem>(maxResults * 2);
            foreach (var item in snapshot)
            {
                var score = CalculateScore(item, terms);
                if (score < 0)
                {
                    continue;
                }

                matched.Add(new ScoredItem(item, score));
            }

            return matched
                .OrderBy(x => x.Score)
                .ThenBy(x => x.Item.Name, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults)
                .Select(x => x.Item)
                .ToArray();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lifetimeCts.Cancel();
            // 終了シグナル後はウォッチャーを先に止め、終了待機タイムアウト時のイベント残留を避ける。
            StopWatchers();
            var gateEntered = false;
            try
            {
                gateEntered = rebuildGate.Wait(DisposeGateWaitTimeoutMs);
                if (!gateEntered)
                {
                    AppStructuredLog.Warn(
                        "ELI2005",
                        "標準監視バックエンドの終了待機がタイムアウト",
                        ("timeoutMs", DisposeGateWaitTimeoutMs));
                    return;
                }

                StopWatchers();

                lock (syncRoot)
                {
                    entries.Clear();
                }
            }
            finally
            {
                if (gateEntered)
                {
                    rebuildGate.Release();
                    rebuildGate.Dispose();
                    lifetimeCts.Dispose();
                }
            }
        }

        private int ExecuteRebuild(IProgress<IndexProgress> progress, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeCts.Token))
            {
                var token = linkedCts.Token;
                rebuildGate.Wait(token);
                try
                {
                    token.ThrowIfCancellationRequested();
                    return RebuildIndexCore(progress, token);
                }
                finally
                {
                    rebuildGate.Release();
                }
            }
        }

        private int RebuildIndexCore(IProgress<IndexProgress> progress, CancellationToken cancellationToken)
        {
            StopWatchers();
            try
            {
                var fresh = new Dictionary<string, SearchResultItem>(StringComparer.OrdinalIgnoreCase);
                var scanned = 0;

                foreach (var root in roots)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    EnumerateRoot(root, fresh, ref scanned, progress, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                lock (syncRoot)
                {
                    entries.Clear();
                    foreach (var pair in fresh)
                    {
                        entries[pair.Key] = pair.Value;
                    }
                }

                return fresh.Count;
            }
            finally
            {
                if (!disposed && !cancellationToken.IsCancellationRequested)
                {
                    StartWatchers();
                }
            }
        }

        private void StartWatchers()
        {
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                if (IsExcludedPath(root))
                {
                    continue;
                }

                try
                {
                    var watcher = new FileSystemWatcher(root)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName |
                                       NotifyFilters.DirectoryName |
                                       NotifyFilters.LastWrite |
                                       NotifyFilters.Size,
                        EnableRaisingEvents = false,
                    };

                    watcher.Created += OnCreatedOrChanged;
                    watcher.Changed += OnCreatedOrChanged;
                    watcher.Deleted += OnDeleted;
                    watcher.Renamed += OnRenamed;
                    watcher.Error += OnWatcherError;
                    watcher.EnableRaisingEvents = true;

                    lock (syncRoot)
                    {
                        watchers.Add(watcher);
                    }
                }
                catch (Exception ex)
                {
                    AppStructuredLog.Warn(
                        "ELI2001",
                        "標準監視バックエンドでウォッチャー開始に失敗",
                        ("root", root),
                        ("error", ex.Message));
                }
            }
        }

        private void StopWatchers()
        {
            List<FileSystemWatcher> snapshot;
            lock (syncRoot)
            {
                snapshot = watchers.ToList();
                watchers.Clear();
            }

            foreach (var watcher in snapshot)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Created -= OnCreatedOrChanged;
                    watcher.Changed -= OnCreatedOrChanged;
                    watcher.Deleted -= OnDeleted;
                    watcher.Renamed -= OnRenamed;
                    watcher.Error -= OnWatcherError;
                    watcher.Dispose();
                }
                catch
                {
                }
            }
        }

        private void EnumerateRoot(
            string root,
            IDictionary<string, SearchResultItem> sink,
            ref int scanned,
            IProgress<IndexProgress> progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return;
            }

            if (IsExcludedPath(root))
            {
                return;
            }

            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = stack.Pop();

                if (IsExcludedPath(current))
                {
                    continue;
                }

                IEnumerable<string> dirs;
                try
                {
                    dirs = Directory.EnumerateDirectories(current);
                }
                catch
                {
                    continue;
                }

                foreach (var dir in dirs)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (IsExcludedPath(dir))
                    {
                        continue;
                    }

                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        sink[dirInfo.FullName] = new SearchResultItem(
                            dirInfo.Name,
                            dirInfo.FullName,
                            -1,
                            dirInfo.LastWriteTimeUtc,
                            true);
                        stack.Push(dir);
                        scanned++;
                        ReportProgress(scanned, dir, progress);
                    }
                    catch
                    {
                    }
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(current);
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (IsExcludedPath(file))
                    {
                        continue;
                    }

                    try
                    {
                        var fileInfo = new FileInfo(file);
                        sink[fileInfo.FullName] = new SearchResultItem(
                            fileInfo.Name,
                            fileInfo.FullName,
                            fileInfo.Length,
                            fileInfo.LastWriteTimeUtc,
                            false);
                        scanned++;
                        ReportProgress(scanned, file, progress);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void OnCreatedOrChanged(object sender, FileSystemEventArgs e)
        {
            if (disposed)
            {
                return;
            }

            if (IsExcludedPath(e.FullPath))
            {
                return;
            }

            UpsertPathOrTree(e.FullPath);
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            if (disposed)
            {
                return;
            }

            if (IsExcludedPath(e.FullPath))
            {
                return;
            }

            RemovePathAndDescendants(e.FullPath);
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            if (disposed)
            {
                return;
            }

            if (IsExcludedPath(e.OldFullPath) && IsExcludedPath(e.FullPath))
            {
                return;
            }

            RemovePathAndDescendants(e.OldFullPath);
            UpsertPathOrTree(e.FullPath);
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            var ex = e.GetException();
            var watcher = sender as FileSystemWatcher;
            var root = watcher == null ? string.Empty : watcher.Path;
            if (ex != null)
            {
                AppStructuredLog.Warn(
                    "ELI2002",
                    "標準監視バックエンドのウォッチャーでエラー",
                    ("root", root),
                    ("error", ex.Message));
            }
            else
            {
                AppStructuredLog.Warn(
                    "ELI2002",
                    "標準監視バックエンドのウォッチャーでエラー",
                    ("root", root));
            }

            ScheduleRecoveryRebuild();
        }

        private void ScheduleRecoveryRebuild()
        {
            if (disposed)
            {
                return;
            }

            Interlocked.Exchange(ref watcherRecoveryRequested, 1);
            if (Interlocked.CompareExchange(ref watcherRecoveryWorkerRunning, 1, 0) != 0)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!disposed)
                    {
                        if (Interlocked.Exchange(ref watcherRecoveryRequested, 0) == 0)
                        {
                            return;
                        }

                        await Task.Delay(WatcherRecoveryDebounceMs).ConfigureAwait(false);
                        if (disposed)
                        {
                            return;
                        }

                        try
                        {
                            var total = ExecuteRebuild(null, CancellationToken.None);
                            AppStructuredLog.Info(
                                "ELI2003",
                                "標準監視バックエンドの再同期が完了",
                                ("indexedCount", total));
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (ObjectDisposedException)
                        {
                            return;
                        }
                        catch (Exception ex)
                        {
                            AppStructuredLog.Warn(
                                "ELI2004",
                                "標準監視バックエンドの再同期に失敗",
                                ("error", ex.Message));
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref watcherRecoveryWorkerRunning, 0);
                    if (!disposed && Interlocked.Exchange(ref watcherRecoveryRequested, 0) == 1)
                    {
                        ScheduleRecoveryRebuild();
                    }
                }
            });
        }

        private void UpsertPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (IsExcludedPath(path))
            {
                return;
            }

            try
            {
                SearchResultItem item = null;

                if (File.Exists(path))
                {
                    var file = new FileInfo(path);
                    item = new SearchResultItem(
                        file.Name,
                        file.FullName,
                        file.Length,
                        file.LastWriteTimeUtc,
                        false);
                }
                else if (Directory.Exists(path))
                {
                    var dir = new DirectoryInfo(path);
                    item = new SearchResultItem(
                        dir.Name,
                        dir.FullName,
                        -1,
                        dir.LastWriteTimeUtc,
                        true);
                }

                if (item == null)
                {
                    RemovePath(path);
                    return;
                }

                lock (syncRoot)
                {
                    entries[item.FullPath] = item;
                }
            }
            catch
            {
            }
        }

        private void UpsertPathOrTree(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (IsExcludedPath(path))
            {
                return;
            }

            if (Directory.Exists(path))
            {
                var items = EnumerateDirectoryTree(path);
                lock (syncRoot)
                {
                    foreach (var item in items)
                    {
                        entries[item.FullPath] = item;
                    }
                }

                return;
            }

            UpsertPath(path);
        }

        private IReadOnlyList<SearchResultItem> EnumerateDirectoryTree(string root)
        {
            var result = new List<SearchResultItem>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return result;
            }

            if (IsExcludedPath(root))
            {
                return result;
            }

            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (IsExcludedPath(current))
                {
                    continue;
                }

                try
                {
                    var dirInfo = new DirectoryInfo(current);
                    result.Add(new SearchResultItem(
                        dirInfo.Name,
                        dirInfo.FullName,
                        -1,
                        dirInfo.LastWriteTimeUtc,
                        true));
                }
                catch
                {
                    continue;
                }

                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(current))
                    {
                        if (IsExcludedPath(dir))
                        {
                            continue;
                        }

                        stack.Push(dir);
                    }
                }
                catch
                {
                }

                try
                {
                    foreach (var file in Directory.EnumerateFiles(current))
                    {
                        if (IsExcludedPath(file))
                        {
                            continue;
                        }

                        try
                        {
                            var info = new FileInfo(file);
                            result.Add(new SearchResultItem(
                                info.Name,
                                info.FullName,
                                info.Length,
                                info.LastWriteTimeUtc,
                                false));
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                }
            }

            return result;
        }

        private void RemovePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            lock (syncRoot)
            {
                entries.Remove(path);
            }
        }

        private void RemovePathAndDescendants(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            lock (syncRoot)
            {
                entries.Remove(path);

                var prefix = path.TrimEnd('\\') + "\\";
                var targets = entries.Keys
                    .Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                foreach (var key in targets)
                {
                    entries.Remove(key);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(StandardFileSystemIndexBackend));
            }
        }

        private static void ReportProgress(int scanned, string path, IProgress<IndexProgress> progress)
        {
            if (progress == null)
            {
                return;
            }

            if (scanned % 5000 == 0)
            {
                progress.Report(new IndexProgress(scanned, path));
            }
        }

        private static int CalculateScore(SearchResultItem item, IReadOnlyList<string> terms)
        {
            var score = 0;
            foreach (var term in terms)
            {
                var idx = item.NameLower.IndexOf(term, StringComparison.Ordinal);
                if (idx < 0)
                {
                    return -1;
                }

                score += idx;
            }

            if (item.IsDirectory)
            {
                score += 5;
            }

            return score;
        }

        private bool IsExcludedPath(string path)
        {
            var normalized = NormalizePath(path);
            if (normalized.Length == 0)
            {
                return false;
            }

            foreach (var prefix in excludedPathPrefixes)
            {
                if (string.Equals(normalized, prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (normalized.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (excludedDirectoryNames.Length == 0)
            {
                return false;
            }

            var segments = normalized.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                foreach (var name in excludedDirectoryNames)
                {
                    if (string.Equals(segment, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path).TrimEnd('\\', '/');
            }
            catch
            {
                return (path ?? string.Empty).Trim().TrimEnd('\\', '/');
            }
        }

        private sealed class ScoredItem
        {
            public ScoredItem(SearchResultItem item, int score)
            {
                Item = item;
                Score = score;
            }

            public SearchResultItem Item { get; }

            public int Score { get; }
        }
    }
}
