using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace IndigoMovieManager.FileIndex.UsnMft
{
    internal sealed class AdminUsnMftIndexBackend : IIndexBackend
    {
        private const int WatchMinDelayMs = 1500;
        private const int WatchMaxDelayMs = 30000;
        private const int WatchBackoffMaxExponent = 5;
        private const int DisposeGateWaitTimeoutMs = 5000;

        private readonly object syncRoot = new object();
        private IReadOnlyList<SearchResultItem> index = Array.Empty<SearchResultItem>();
        private Dictionary<string, VolumeState> volumeStates = new Dictionary<string, VolumeState>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, IReadOnlyList<SearchResultItem>> volumeIndexMap =
            new Dictionary<string, IReadOnlyList<SearchResultItem>>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim rebuildGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetimeCts = new CancellationTokenSource();
        private CancellationTokenSource watchCts;
        private Task watchTask = Task.CompletedTask;
        private volatile bool disposed;

        public string BackendName => "AdminUsnMft";

        public FileIndexBackendMode BackendMode => FileIndexBackendMode.AdminUsnMft;

        public int IndexedCount
        {
            get
            {
                lock (syncRoot)
                {
                    return index.Count;
                }
            }
        }

        public Task<int> RebuildIndexAsync(IProgress<IndexProgress> progress, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            return Task.Run(async () =>
            {
                using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeCts.Token))
                {
                    var token = linkedCts.Token;
                    await rebuildGate.WaitAsync(token).ConfigureAwait(false);
                    Dictionary<string, VolumeState> rebuiltStates = null;
                    var applied = false;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        await StopWatcherAsync().ConfigureAwait(false);

                        rebuiltStates = BuildInitialStates(progress, token);
                        token.ThrowIfCancellationRequested();

                        var rebuiltVolumeIndexMap = BuildVolumeIndexMap(rebuiltStates);
                        var rebuiltIndex = FlattenVolumeIndexMap(rebuiltVolumeIndexMap);

                        Dictionary<string, VolumeState> oldStates;
                        lock (syncRoot)
                        {
                            oldStates = volumeStates;
                            volumeStates = rebuiltStates;
                            volumeIndexMap = rebuiltVolumeIndexMap;
                            index = rebuiltIndex;
                        }

                        applied = true;
                        DisposeStates(oldStates);
                        token.ThrowIfCancellationRequested();

                        if (!disposed)
                        {
                            StartWatcher();
                        }

                        return rebuiltIndex.Count;
                    }
                    finally
                    {
                        if (!applied && rebuiltStates != null)
                        {
                            DisposeStates(rebuiltStates);
                        }

                        rebuildGate.Release();
                    }
                }
            }, cancellationToken);
        }

        public IReadOnlyList<SearchResultItem> Search(string query, int maxResults)
        {
            if (maxResults <= 0)
            {
                return Array.Empty<SearchResultItem>();
            }

            IReadOnlyList<SearchResultItem> snapshot;
            lock (syncRoot)
            {
                snapshot = index;
            }

            if (snapshot.Count == 0)
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
            RequestWatcherStop();
            var gateEntered = false;
            try
            {
                gateEntered = rebuildGate.Wait(DisposeGateWaitTimeoutMs);
                if (!gateEntered)
                {
                    AppStructuredLog.Warn(
                        "ELI1007",
                        "管理者バックエンドの終了待機がタイムアウト",
                        ("timeoutMs", DisposeGateWaitTimeoutMs));
                    return;
                }

                StopWatcherAsync().GetAwaiter().GetResult();

                Dictionary<string, VolumeState> statesToDispose;
                lock (syncRoot)
                {
                    statesToDispose = volumeStates;
                    volumeStates = new Dictionary<string, VolumeState>(StringComparer.OrdinalIgnoreCase);
                    volumeIndexMap = new Dictionary<string, IReadOnlyList<SearchResultItem>>(StringComparer.OrdinalIgnoreCase);
                    index = Array.Empty<SearchResultItem>();
                }

                DisposeStates(statesToDispose);
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

        private void RequestWatcherStop()
        {
            var cts = watchCts;
            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch
            {
            }
        }

        private Dictionary<string, VolumeState> BuildInitialStates(IProgress<IndexProgress> progress, CancellationToken cancellationToken)
        {
            var states = new Dictionary<string, VolumeState>(StringComparer.OrdinalIgnoreCase);
            var fixedDrives = DriveInfo.GetDrives().Where(IsNtfsFixedDrive).ToArray();
            var scanned = 0;

            foreach (var drive in fixedDrives)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VolumeState state = null;
                try
                {
                    state = BuildVolumeState(drive, progress, cancellationToken, ref scanned);
                    if (state != null)
                    {
                        states[state.RootPath] = state;
                    }
                }
                catch (OperationCanceledException)
                {
                    if (state != null)
                    {
                        state.Dispose();
                    }

                    throw;
                }
                catch (Exception ex)
                {
                    AppStructuredLog.Warn(
                        "ELI1001",
                        "ドライブ初期インデックス作成に失敗",
                        ("drive", drive.Name),
                        ("error", ex.Message));
                    if (state != null)
                    {
                        state.Dispose();
                    }
                }
            }

            return states;
        }

        private VolumeState BuildVolumeState(
            DriveInfo drive,
            IProgress<IndexProgress> progress,
            CancellationToken cancellationToken,
            ref int scanned)
        {
            var rootPath = drive.RootDirectory.FullName;
            var volumePath = @"\\.\" + rootPath.TrimEnd('\\');
            var volumeHandle = NtfsNative.OpenVolume(volumePath);
            if (volumeHandle == null || volumeHandle.IsInvalid)
            {
                return null;
            }

            try
            {
                var journalData = NtfsNative.QueryUsnJournal(volumeHandle);
                var rootFrn = NtfsNative.GetPathFrn(rootPath);
                var state = new VolumeState(rootPath, rootFrn, volumeHandle, journalData.UsnJournalID);
                volumeHandle = null;
                state.NextUsn = journalData.NextUsn;

                var enumData = new NtfsNative.MFT_ENUM_DATA_V0
                {
                    StartFileReferenceNumber = 0,
                    LowUsn = 0,
                    HighUsn = long.MaxValue,
                };

                var buffer = new byte[NtfsNative.OutputBufferSize];
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int bytesReturned;
                    var ok = NtfsNative.DeviceIoControlEnumUsn(
                        state.VolumeHandle,
                        ref enumData,
                        buffer,
                        out bytesReturned);

                    if (!ok)
                    {
                        var err = Marshal.GetLastWin32Error();
                        if (err == NtfsNative.ERROR_HANDLE_EOF)
                        {
                            break;
                        }

                        throw new Win32Exception(err);
                    }

                    if (bytesReturned <= 0)
                    {
                        break;
                    }

                    if (bytesReturned <= sizeof(long))
                    {
                        break;
                    }

                    var nextStartFrn = BitConverter.ToUInt64(buffer, 0);
                    enumData.StartFileReferenceNumber = nextStartFrn;

                    foreach (var record in NtfsNative.ParseUsnRecords(buffer, bytesReturned))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        UpsertFromRecord(state, record);
                        scanned++;
                        ReportProgress(scanned, rootPath, progress);
                    }
                }

                var ignored = false;
                ApplyJournalDelta(state, cancellationToken, out ignored);
                return state;
            }
            finally
            {
                if (volumeHandle != null && !volumeHandle.IsInvalid)
                {
                    volumeHandle.Dispose();
                }
            }
        }

        private static bool IsNtfsFixedDrive(DriveInfo drive)
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
            {
                return false;
            }

            try
            {
                return string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static Dictionary<string, IReadOnlyList<SearchResultItem>> BuildVolumeIndexMap(IReadOnlyDictionary<string, VolumeState> states)
        {
            var map = new Dictionary<string, IReadOnlyList<SearchResultItem>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in states)
            {
                map[pair.Key] = BuildFlatIndexForVolume(pair.Value);
            }

            return map;
        }

        private static IReadOnlyList<SearchResultItem> BuildFlatIndexForVolume(VolumeState state)
        {
            var list = new List<SearchResultItem>(400000);

            var pathCache = new Dictionary<ulong, string>();
            pathCache[state.RootFrn] = state.RootPath;

            foreach (var node in state.Nodes.Values)
            {
                if (node.IsDeleted || node.IsRoot || string.IsNullOrEmpty(node.Name))
                {
                    continue;
                }

                string fullPath;
                if (!TryResolvePath(state, node.Frn, pathCache, out fullPath))
                {
                    continue;
                }

                list.Add(new SearchResultItem(
                    node.Name,
                    fullPath,
                    -1,
                    node.LastWriteTimeUtc,
                    node.IsDirectory));
            }

            return list;
        }

        private static IReadOnlyList<SearchResultItem> FlattenVolumeIndexMap(
            IReadOnlyDictionary<string, IReadOnlyList<SearchResultItem>> map)
        {
            var list = new List<SearchResultItem>(400000);
            foreach (var bucket in map.Values)
            {
                if (bucket == null || bucket.Count == 0)
                {
                    continue;
                }

                list.AddRange(bucket);
            }

            return list;
        }

        private static bool TryResolvePath(VolumeState state, ulong frn, IDictionary<ulong, string> cache, out string path)
        {
            string cached;
            if (cache.TryGetValue(frn, out cached))
            {
                path = cached;
                return true;
            }

            var chain = new Stack<NtfsNode>();
            var visited = new HashSet<ulong>();
            var currentFrn = frn;

            while (true)
            {
                if (cache.TryGetValue(currentFrn, out cached))
                {
                    path = cached;
                    break;
                }

                if (!visited.Add(currentFrn))
                {
                    path = null;
                    return false;
                }

                if (currentFrn == state.RootFrn)
                {
                    path = state.RootPath;
                    cache[currentFrn] = path;
                    break;
                }

                NtfsNode node;
                if (!state.Nodes.TryGetValue(currentFrn, out node) || node.IsDeleted || string.IsNullOrEmpty(node.Name))
                {
                    path = null;
                    return false;
                }

                chain.Push(node);
                currentFrn = node.ParentFrn;
            }

            while (chain.Count > 0)
            {
                var node = chain.Pop();
                path = CombinePath(path, node.Name);
                cache[node.Frn] = path;
            }

            return true;
        }

        private static string CombinePath(string parent, string name)
        {
            if (string.IsNullOrEmpty(parent))
            {
                return name;
            }

            if (parent[parent.Length - 1] == '\\')
            {
                return parent + name;
            }

            return parent + "\\" + name;
        }

        private void StartWatcher()
        {
            var cts = new CancellationTokenSource();
            watchCts = cts;
            watchTask = Task.Run(() => WatchLoopAsync(cts.Token), cts.Token);
        }

        private async Task StopWatcherAsync()
        {
            var cts = watchCts;
            var task = watchTask;

            watchCts = null;
            watchTask = Task.CompletedTask;

            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cts.Dispose();
            }
        }

        private async Task WatchLoopAsync(CancellationToken cancellationToken)
        {
            var consecutiveFailures = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                var delayMs = GetBackoffDelayMs(consecutiveFailures);
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    Dictionary<string, VolumeState> snapshot;
                    Dictionary<string, IReadOnlyList<SearchResultItem>> snapshotVolumeIndexMap;
                    lock (syncRoot)
                    {
                        snapshot = new Dictionary<string, VolumeState>(volumeStates, StringComparer.OrdinalIgnoreCase);
                        snapshotVolumeIndexMap =
                            new Dictionary<string, IReadOnlyList<SearchResultItem>>(volumeIndexMap, StringComparer.OrdinalIgnoreCase);
                    }

                    if (snapshot.Count == 0)
                    {
                        consecutiveFailures = 0;
                        continue;
                    }

                    var hasChanges = false;
                    var replacedVolumeState = false;
                    var hasUnrecoveredFailure = false;
                    var changedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var rootPath in snapshot.Keys.ToArray())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var state = snapshot[rootPath];
                        bool volumeChanged;
                        var ok = ApplyJournalDelta(state, cancellationToken, out volumeChanged);
                        if (!ok)
                        {
                            var recoveredState = TryRebuildVolumeState(rootPath, cancellationToken);
                            if (recoveredState != null)
                            {
                                snapshot[rootPath] = recoveredState;
                                replacedVolumeState = true;
                                hasChanges = true;
                                changedRoots.Add(rootPath);
                            }
                            else
                            {
                                hasUnrecoveredFailure = true;
                            }

                            continue;
                        }

                        if (volumeChanged)
                        {
                            hasChanges = true;
                            changedRoots.Add(rootPath);
                        }
                    }

                    if (hasUnrecoveredFailure)
                    {
                        consecutiveFailures = Math.Min(consecutiveFailures + 1, WatchBackoffMaxExponent);
                        AppStructuredLog.Warn(
                            "ELI1002",
                            "USN監視で未復旧失敗を検出",
                            ("consecutiveFailures", consecutiveFailures),
                            ("nextDelayMs", GetBackoffDelayMs(consecutiveFailures)));
                    }
                    else
                    {
                        consecutiveFailures = 0;
                    }

                    if (!hasChanges)
                    {
                        continue;
                    }

                    foreach (var rootPath in changedRoots)
                    {
                        VolumeState changedState;
                        if (!snapshot.TryGetValue(rootPath, out changedState))
                        {
                            continue;
                        }

                        snapshotVolumeIndexMap[rootPath] = BuildFlatIndexForVolume(changedState);
                    }

                    var rebuilt = FlattenVolumeIndexMap(snapshotVolumeIndexMap);

                    if (!replacedVolumeState)
                    {
                        lock (syncRoot)
                        {
                            volumeIndexMap =
                                new Dictionary<string, IReadOnlyList<SearchResultItem>>(snapshotVolumeIndexMap, StringComparer.OrdinalIgnoreCase);
                            index = rebuilt;
                        }

                        continue;
                    }

                    Dictionary<string, VolumeState> oldStates;
                    Dictionary<string, VolumeState> newStates;
                    lock (syncRoot)
                    {
                        oldStates = volumeStates;
                        newStates = new Dictionary<string, VolumeState>(snapshot, StringComparer.OrdinalIgnoreCase);
                        volumeStates = newStates;
                        volumeIndexMap =
                            new Dictionary<string, IReadOnlyList<SearchResultItem>>(snapshotVolumeIndexMap, StringComparer.OrdinalIgnoreCase);
                        index = rebuilt;
                    }

                    DisposeReplacedStates(oldStates, newStates);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    consecutiveFailures = Math.Min(consecutiveFailures + 1, WatchBackoffMaxExponent);
                    AppStructuredLog.Warn(
                        "ELI1003",
                        "USN監視ループ反復処理に失敗",
                        ("error", ex.Message),
                        ("consecutiveFailures", consecutiveFailures),
                        ("nextDelayMs", GetBackoffDelayMs(consecutiveFailures)));
                }
            }
        }

        private static int GetBackoffDelayMs(int consecutiveFailures)
        {
            if (consecutiveFailures <= 0)
            {
                return WatchMinDelayMs;
            }

            var exponent = Math.Min(consecutiveFailures, WatchBackoffMaxExponent);
            var multiplier = 1 << exponent;
            var delay = WatchMinDelayMs * multiplier;
            return Math.Min(delay, WatchMaxDelayMs);
        }

        private bool ApplyJournalDelta(VolumeState state, CancellationToken cancellationToken, out bool changed)
        {
            changed = false;
            var readData = new NtfsNative.READ_USN_JOURNAL_DATA_V0
            {
                StartUsn = state.NextUsn,
                ReasonMask = uint.MaxValue,
                ReturnOnlyOnClose = 0,
                Timeout = 0,
                BytesToWaitFor = 0,
                UsnJournalID = state.JournalId,
            };

            var buffer = new byte[NtfsNative.OutputBufferSize];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int bytesReturned;
                var ok = NtfsNative.DeviceIoControlReadJournal(
                    state.VolumeHandle,
                    ref readData,
                    buffer,
                    out bytesReturned);

                if (!ok)
                {
                    var err = Marshal.GetLastWin32Error();
                    if (err == NtfsNative.ERROR_JOURNAL_DELETE_IN_PROGRESS ||
                        err == NtfsNative.ERROR_JOURNAL_NOT_ACTIVE ||
                        err == NtfsNative.ERROR_INVALID_PARAMETER)
                    {
                        return false;
                    }

                    AppStructuredLog.Warn(
                        "ELI1004",
                        "USNジャーナル読み取りAPIが失敗",
                        ("root", state.RootPath),
                        ("win32Error", err));
                    return false;
                }

                if (bytesReturned < sizeof(long))
                {
                    return true;
                }

                var nextUsn = BitConverter.ToInt64(buffer, 0);
                if (bytesReturned == sizeof(long))
                {
                    state.NextUsn = nextUsn;
                    return true;
                }

                foreach (var record in NtfsNative.ParseUsnRecords(buffer, bytesReturned))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (ApplyDeltaRecord(state, record))
                    {
                        changed = true;
                    }
                }

                state.NextUsn = nextUsn;
                readData.StartUsn = nextUsn;
            }
        }

        private VolumeState TryRebuildVolumeState(string rootPath, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var drive = new DriveInfo(rootPath);
                if (!IsNtfsFixedDrive(drive))
                {
                    return null;
                }

                var scanned = 0;
                var rebuilt = BuildVolumeState(drive, null, cancellationToken, ref scanned);
                if (rebuilt != null)
                {
                    AppStructuredLog.Info(
                        "ELI1005",
                        "ボリューム状態の再構築に成功",
                        ("root", rootPath));
                }

                return rebuilt;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppStructuredLog.Warn(
                    "ELI1006",
                    "ボリューム状態の再構築に失敗",
                    ("root", rootPath),
                    ("error", ex.Message));
                return null;
            }
        }

        private static void DisposeReplacedStates(
            IReadOnlyDictionary<string, VolumeState> oldStates,
            IReadOnlyDictionary<string, VolumeState> newStates)
        {
            foreach (var pair in oldStates)
            {
                VolumeState next;
                if (!newStates.TryGetValue(pair.Key, out next))
                {
                    continue;
                }

                if (!ReferenceEquals(pair.Value, next))
                {
                    pair.Value.Dispose();
                }
            }
        }

        private static void UpsertFromRecord(VolumeState state, NtfsNative.ParsedUsnRecord record)
        {
            if (record.FileReferenceNumber == state.RootFrn)
            {
                return;
            }

            NtfsNode node;
            if (!state.Nodes.TryGetValue(record.FileReferenceNumber, out node))
            {
                node = new NtfsNode(record.FileReferenceNumber);
                state.Nodes[record.FileReferenceNumber] = node;
            }

            node.ParentFrn = record.ParentFileReferenceNumber;
            node.IsDirectory = record.IsDirectory;
            node.LastWriteTimeUtc = record.TimestampUtc;
            node.IsDeleted = false;

            if (!string.IsNullOrEmpty(record.FileName))
            {
                node.Name = record.FileName;
            }
        }

        private static bool ApplyDeltaRecord(VolumeState state, NtfsNative.ParsedUsnRecord record)
        {
            if (record.FileReferenceNumber == state.RootFrn)
            {
                return false;
            }

            if ((record.Reason & NtfsNative.USN_REASON_FILE_DELETE) != 0)
            {
                return RemoveNodeAndDescendants(state, record.FileReferenceNumber);
            }

            NtfsNode node;
            if (!state.Nodes.TryGetValue(record.FileReferenceNumber, out node))
            {
                node = new NtfsNode(record.FileReferenceNumber);
                state.Nodes[record.FileReferenceNumber] = node;
            }

            var changed = false;

            if (node.ParentFrn != record.ParentFileReferenceNumber)
            {
                node.ParentFrn = record.ParentFileReferenceNumber;
                changed = true;
            }

            if (node.IsDirectory != record.IsDirectory)
            {
                node.IsDirectory = record.IsDirectory;
                changed = true;
            }

            if (node.LastWriteTimeUtc != record.TimestampUtc)
            {
                node.LastWriteTimeUtc = record.TimestampUtc;
                changed = true;
            }

            if (!string.IsNullOrEmpty(record.FileName) && !string.Equals(node.Name, record.FileName, StringComparison.Ordinal))
            {
                node.Name = record.FileName;
                changed = true;
            }

            node.IsDeleted = false;
            return changed;
        }

        private static bool RemoveNodeAndDescendants(VolumeState state, ulong rootFrn)
        {
            if (!state.Nodes.ContainsKey(rootFrn))
            {
                return false;
            }

            var childrenByParent = new Dictionary<ulong, List<ulong>>();
            foreach (var node in state.Nodes.Values)
            {
                List<ulong> children;
                if (!childrenByParent.TryGetValue(node.ParentFrn, out children))
                {
                    children = new List<ulong>();
                    childrenByParent[node.ParentFrn] = children;
                }

                children.Add(node.Frn);
            }

            var removed = false;
            var queue = new Queue<ulong>();
            queue.Enqueue(rootFrn);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (state.Nodes.Remove(current))
                {
                    removed = true;
                }

                List<ulong> children;
                if (!childrenByParent.TryGetValue(current, out children))
                {
                    continue;
                }

                foreach (var child in children)
                {
                    queue.Enqueue(child);
                }
            }

            return removed;
        }

        private static void DisposeStates(IDictionary<string, VolumeState> states)
        {
            if (states == null)
            {
                return;
            }

            foreach (var state in states.Values)
            {
                state.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AdminUsnMftIndexBackend));
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

        private sealed class NtfsNode
        {
            public NtfsNode(ulong frn)
            {
                Frn = frn;
            }

            public ulong Frn { get; }

            public ulong ParentFrn { get; set; }

            public string Name { get; set; }

            public bool IsDirectory { get; set; }

            public DateTime LastWriteTimeUtc { get; set; }

            public bool IsDeleted { get; set; }

            public bool IsRoot { get; set; }
        }

        private sealed class VolumeState : IDisposable
        {
            public VolumeState(string rootPath, ulong rootFrn, SafeFileHandle volumeHandle, ulong journalId)
            {
                RootPath = rootPath;
                RootFrn = rootFrn;
                VolumeHandle = volumeHandle;
                JournalId = journalId;
                Nodes = new Dictionary<ulong, NtfsNode>();
                Nodes[rootFrn] = new NtfsNode(rootFrn)
                {
                    Name = rootPath.TrimEnd('\\'),
                    ParentFrn = 0,
                    IsDirectory = true,
                    IsDeleted = false,
                    IsRoot = true,
                    LastWriteTimeUtc = DateTime.UtcNow,
                };
            }

            public string RootPath { get; }

            public ulong RootFrn { get; }

            public SafeFileHandle VolumeHandle { get; }

            public ulong JournalId { get; }

            public long NextUsn { get; set; }

            public Dictionary<ulong, NtfsNode> Nodes { get; }

            public void Dispose()
            {
                if (VolumeHandle != null && !VolumeHandle.IsInvalid)
                {
                    VolumeHandle.Dispose();
                }
            }
        }

        private static class NtfsNative
        {
            public const int OutputBufferSize = 1024 * 1024;
            public const int ERROR_HANDLE_EOF = 38;
            public const int ERROR_INVALID_PARAMETER = 87;
            public const int ERROR_JOURNAL_DELETE_IN_PROGRESS = 1178;
            public const int ERROR_JOURNAL_NOT_ACTIVE = 1179;

            public const uint USN_REASON_FILE_DELETE = 0x00000200;

            private const uint GENERIC_READ = 0x80000000;
            private const uint FILE_SHARE_READ = 0x00000001;
            private const uint FILE_SHARE_WRITE = 0x00000002;
            private const uint FILE_SHARE_DELETE = 0x00000004;
            private const uint OPEN_EXISTING = 3;
            private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
            private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
            private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

            private const uint FSCTL_ENUM_USN_DATA = 0x000900b3;
            private const uint FSCTL_READ_USN_JOURNAL = 0x000900bb;
            private const uint FSCTL_QUERY_USN_JOURNAL = 0x000900f4;

            public static SafeFileHandle OpenVolume(string volumePath)
            {
                return CreateFile(
                    volumePath,
                    GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_ATTRIBUTE_NORMAL,
                    IntPtr.Zero);
            }

            public static SafeFileHandle OpenDirectory(string path)
            {
                return CreateFile(
                    path,
                    0,
                    FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_FLAG_BACKUP_SEMANTICS,
                    IntPtr.Zero);
            }

            public static USN_JOURNAL_DATA_V0 QueryUsnJournal(SafeFileHandle volumeHandle)
            {
                int bytesReturned;
                USN_JOURNAL_DATA_V0 journal;
                var ok = DeviceIoControlQueryJournal(
                    volumeHandle,
                    IntPtr.Zero,
                    0,
                    out journal,
                    Marshal.SizeOf(typeof(USN_JOURNAL_DATA_V0)),
                    out bytesReturned,
                    IntPtr.Zero);
                if (!ok)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return journal;
            }

            public static ulong GetPathFrn(string rootPath)
            {
                var handle = OpenDirectory(rootPath);
                if (handle == null || handle.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                try
                {
                    BY_HANDLE_FILE_INFORMATION info;
                    if (!GetFileInformationByHandle(handle, out info))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    return ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
                }
                finally
                {
                    handle.Dispose();
                }
            }

            public static bool DeviceIoControlEnumUsn(
                SafeFileHandle volumeHandle,
                ref MFT_ENUM_DATA_V0 enumData,
                byte[] outBuffer,
                out int bytesReturned)
            {
                return DeviceIoControlEnum(
                    volumeHandle,
                    FSCTL_ENUM_USN_DATA,
                    ref enumData,
                    Marshal.SizeOf(typeof(MFT_ENUM_DATA_V0)),
                    outBuffer,
                    outBuffer.Length,
                    out bytesReturned,
                    IntPtr.Zero);
            }

            public static bool DeviceIoControlReadJournal(
                SafeFileHandle volumeHandle,
                ref READ_USN_JOURNAL_DATA_V0 readData,
                byte[] outBuffer,
                out int bytesReturned)
            {
                return DeviceIoControlRead(
                    volumeHandle,
                    FSCTL_READ_USN_JOURNAL,
                    ref readData,
                    Marshal.SizeOf(typeof(READ_USN_JOURNAL_DATA_V0)),
                    outBuffer,
                    outBuffer.Length,
                    out bytesReturned,
                    IntPtr.Zero);
            }

            public static IEnumerable<ParsedUsnRecord> ParseUsnRecords(byte[] buffer, int bytesReturned)
            {
                var offset = sizeof(long);
                while (offset + 60 <= bytesReturned)
                {
                    var recordLength = BitConverter.ToInt32(buffer, offset);
                    if (recordLength <= 0 || offset + recordLength > bytesReturned)
                    {
                        yield break;
                    }

                    var majorVersion = BitConverter.ToUInt16(buffer, offset + 4);
                    if (majorVersion == 2 || majorVersion == 3)
                    {
                        var fileRef = BitConverter.ToUInt64(buffer, offset + 8);
                        var parentRef = BitConverter.ToUInt64(buffer, offset + 16);
                        var timestamp = BitConverter.ToInt64(buffer, offset + 32);
                        var reason = BitConverter.ToUInt32(buffer, offset + 40);
                        var fileAttributes = BitConverter.ToUInt32(buffer, offset + 52);
                        var fileNameLength = BitConverter.ToUInt16(buffer, offset + 56);
                        var fileNameOffset = BitConverter.ToUInt16(buffer, offset + 58);
                        var name = string.Empty;
                        if (fileNameLength > 0 && fileNameOffset > 0 && offset + fileNameOffset + fileNameLength <= bytesReturned)
                        {
                            name = System.Text.Encoding.Unicode.GetString(buffer, offset + fileNameOffset, fileNameLength);
                        }

                        yield return new ParsedUsnRecord(
                            fileRef,
                            parentRef,
                            name,
                            (fileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0,
                            reason,
                            DateTime.FromFileTimeUtc(timestamp));
                    }

                    offset += recordLength;
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct USN_JOURNAL_DATA_V0
            {
                public ulong UsnJournalID;
                public long FirstUsn;
                public long NextUsn;
                public long LowestValidUsn;
                public long MaxUsn;
                public ulong MaximumSize;
                public ulong AllocationDelta;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MFT_ENUM_DATA_V0
            {
                public ulong StartFileReferenceNumber;
                public long LowUsn;
                public long HighUsn;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct READ_USN_JOURNAL_DATA_V0
            {
                public long StartUsn;
                public uint ReasonMask;
                public uint ReturnOnlyOnClose;
                public ulong Timeout;
                public ulong BytesToWaitFor;
                public ulong UsnJournalID;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct BY_HANDLE_FILE_INFORMATION
            {
                public uint FileAttributes;
                public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
                public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
                public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
                public uint VolumeSerialNumber;
                public uint FileSizeHigh;
                public uint FileSizeLow;
                public uint NumberOfLinks;
                public uint FileIndexHigh;
                public uint FileIndexLow;
            }

            public sealed class ParsedUsnRecord
            {
                public ParsedUsnRecord(
                    ulong fileReferenceNumber,
                    ulong parentFileReferenceNumber,
                    string fileName,
                    bool isDirectory,
                    uint reason,
                    DateTime timestampUtc)
                {
                    FileReferenceNumber = fileReferenceNumber;
                    ParentFileReferenceNumber = parentFileReferenceNumber;
                    FileName = fileName;
                    IsDirectory = isDirectory;
                    Reason = reason;
                    TimestampUtc = timestampUtc;
                }

                public ulong FileReferenceNumber { get; }

                public ulong ParentFileReferenceNumber { get; }

                public string FileName { get; }

                public bool IsDirectory { get; }

                public uint Reason { get; }

                public DateTime TimestampUtc { get; }
            }

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern SafeFileHandle CreateFile(
                string lpFileName,
                uint dwDesiredAccess,
                uint dwShareMode,
                IntPtr lpSecurityAttributes,
                uint dwCreationDisposition,
                uint dwFlagsAndAttributes,
                IntPtr hTemplateFile);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool GetFileInformationByHandle(
                SafeFileHandle hFile,
                out BY_HANDLE_FILE_INFORMATION lpFileInformation);

            [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
            private static extern bool DeviceIoControlQueryJournal(
                SafeFileHandle hDevice,
                IntPtr lpInBuffer,
                int nInBufferSize,
                out USN_JOURNAL_DATA_V0 lpOutBuffer,
                int nOutBufferSize,
                out int lpBytesReturned,
                IntPtr lpOverlapped);

            [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
            private static extern bool DeviceIoControlEnum(
                SafeFileHandle hDevice,
                uint dwIoControlCode,
                ref MFT_ENUM_DATA_V0 lpInBuffer,
                int nInBufferSize,
                [Out] byte[] lpOutBuffer,
                int nOutBufferSize,
                out int lpBytesReturned,
                IntPtr lpOverlapped);

            [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "DeviceIoControl")]
            private static extern bool DeviceIoControlRead(
                SafeFileHandle hDevice,
                uint dwIoControlCode,
                ref READ_USN_JOURNAL_DATA_V0 lpInBuffer,
                int nInBufferSize,
                [Out] byte[] lpOutBuffer,
                int nOutBufferSize,
                out int lpBytesReturned,
                IntPtr lpOverlapped);
        }
    }
}
