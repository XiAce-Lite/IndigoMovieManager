using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace IndigoMovieManager.FileIndex.UsnMft
{
    /// <summary>
    /// IFileIndexService の Windows 向け実装。
    ///
    /// 初回アクセス時にバックエンドを自動選択する（Admin → USN/MFT、通常ユーザー → ファイルシステム走査）。
    /// USN/MFT で権限エラーが出た場合は StandardFileSystem へ自動フォールバックする。
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class FileIndexService : IFileIndexService
    {
        private readonly object syncRoot = new object();
        private readonly FileIndexServiceOptions options;
        private IIndexBackend backend;
        private volatile bool disposed;

        public FileIndexService()
            : this(new FileIndexServiceOptions())
        {
        }

        public FileIndexService(FileIndexServiceOptions options)
        {
            this.options = options ?? new FileIndexServiceOptions();
        }

        public string ActiveBackendName
        {
            get
            {
                lock (syncRoot)
                {
                    return backend == null ? "未選択" : backend.BackendName;
                }
            }
        }

        public FileIndexBackendMode ActiveBackendMode
        {
            get
            {
                lock (syncRoot)
                {
                    return backend == null ? FileIndexBackendMode.Auto : backend.BackendMode;
                }
            }
        }

        public int IndexedCount
        {
            get
            {
                lock (syncRoot)
                {
                    return backend == null ? 0 : backend.IndexedCount;
                }
            }
        }

        public async Task<int> RebuildIndexAsync(IProgress<IndexProgress> progress, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            var selected = EnsureBackend();
            try
            {
                return await selected.RebuildIndexAsync(progress, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (ShouldFallbackToStandard(selected))
            {
                var fallback = SwitchBackend(FileIndexBackendMode.StandardFileSystem);
                return await fallback.RebuildIndexAsync(progress, cancellationToken).ConfigureAwait(false);
            }
            catch (Win32Exception ex) when (ShouldFallbackToStandard(selected) && ex.NativeErrorCode == 5)
            {
                var fallback = SwitchBackend(FileIndexBackendMode.StandardFileSystem);
                return await fallback.RebuildIndexAsync(progress, cancellationToken).ConfigureAwait(false);
            }
        }

        public IReadOnlyList<SearchResultItem> Search(string query, int maxResults)
        {
            IIndexBackend snapshot;
            lock (syncRoot)
            {
                snapshot = backend;
            }

            if (snapshot == null)
            {
                return Array.Empty<SearchResultItem>();
            }

            return snapshot.Search(query, maxResults);
        }

        public void Dispose()
        {
            IIndexBackend old;
            lock (syncRoot)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                old = backend;
                backend = null;
            }

            if (old != null)
            {
                old.Dispose();
            }
        }

        private IIndexBackend EnsureBackend()
        {
            lock (syncRoot)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(FileIndexService));
                }

                if (backend != null)
                {
                    return backend;
                }

                var mode = ResolveInitialMode();
                backend = CreateBackend(mode);
                return backend;
            }
        }

        private IIndexBackend SwitchBackend(FileIndexBackendMode nextMode)
        {
            IIndexBackend old;
            IIndexBackend created;
            lock (syncRoot)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(FileIndexService));
                }

                old = backend;
                created = CreateBackend(nextMode);
                backend = created;
            }

            if (old != null)
            {
                old.Dispose();
            }

            return created;
        }

        private FileIndexBackendMode ResolveInitialMode()
        {
            if (options.BackendMode == FileIndexBackendMode.AdminUsnMft ||
                options.BackendMode == FileIndexBackendMode.StandardFileSystem)
            {
                return options.BackendMode;
            }

            return IsAdministrator() ? FileIndexBackendMode.AdminUsnMft : FileIndexBackendMode.StandardFileSystem;
        }

        private IIndexBackend CreateBackend(FileIndexBackendMode mode)
        {
            if (mode == FileIndexBackendMode.AdminUsnMft)
            {
                return new AdminUsnMftIndexBackend();
            }

            return new StandardFileSystemIndexBackend(GetStandardRoots(), GetStandardExcludePaths());
        }

        private IReadOnlyList<string> GetStandardRoots()
        {
            var roots = options.StandardUserRoots;
            if (roots == null || roots.Count == 0)
            {
                return new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
            }

            return roots
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(ExpandAndNormalizePath)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private IReadOnlyList<string> GetStandardExcludePaths()
        {
            var excludes = options.StandardUserExcludePaths;
            if (excludes == null || excludes.Count == 0)
            {
                return Array.Empty<string>();
            }

            return excludes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(ExpandAndNormalizeToken)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string ExpandAndNormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var expanded = Environment.ExpandEnvironmentVariables(path.Trim()).Trim();
            if (expanded.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(expanded);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExpandAndNormalizeToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            return Environment.ExpandEnvironmentVariables(token.Trim()).Trim();
        }

        private bool ShouldFallbackToStandard(IIndexBackend current)
        {
            return options.BackendMode == FileIndexBackendMode.Auto &&
                   current != null &&
                   current.BackendMode == FileIndexBackendMode.AdminUsnMft;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(FileIndexService));
            }
        }

        [SupportedOSPlatform("windows")]
        private static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                if (identity == null)
                {
                    return false;
                }

                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }
}
