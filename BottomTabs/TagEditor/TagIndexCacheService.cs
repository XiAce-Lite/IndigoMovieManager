using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IndigoMovieManager.DB;
using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.BottomTabs.TagEditor
{
    internal sealed class TagIndexSnapshot
    {
        public TagIndexSnapshot(
            string dbFullPath,
            DateTime builtAtUtc,
            IReadOnlyDictionary<string, int> tagCounts,
            IReadOnlyDictionary<long, string[]> movieTags
        )
        {
            DbFullPath = dbFullPath ?? "";
            BuiltAtUtc = builtAtUtc;
            TagCounts = tagCounts ?? new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
            MovieTags = movieTags ?? new Dictionary<long, string[]>();
        }

        public string DbFullPath { get; }

        public DateTime BuiltAtUtc { get; }

        public IReadOnlyDictionary<string, int> TagCounts { get; }

        public IReadOnlyDictionary<long, string[]> MovieTags { get; }
    }

    internal sealed class TagIndexSnapshotChangedEventArgs : EventArgs
    {
        public TagIndexSnapshotChangedEventArgs(string dbFullPath, int tagCount)
        {
            DbFullPath = dbFullPath ?? "";
            TagCount = tagCount;
        }

        public string DbFullPath { get; }

        public int TagCount { get; }
    }

    /// <summary>
    /// movie.tag の集計結果だけを握り、タグ候補表示を毎回全件走査しないための cache にする。
    /// UI からは snapshot の参照だけを許し、構築と差分更新の責務をここへ閉じ込める。
    /// </summary>
    internal sealed class TagIndexCacheService
    {
        private readonly object _syncRoot = new();
        private TagIndexSnapshot _snapshot;
        private string _buildingDbFullPath = "";
        private long _buildRequestVersion;
        private Task _buildingTask = Task.CompletedTask;

        public event EventHandler<TagIndexSnapshotChangedEventArgs> SnapshotUpdated;

        public TagIndexSnapshot TryGetSnapshot(string dbFullPath)
        {
            string normalizedDbFullPath = NormalizeDbPath(dbFullPath);
            if (string.IsNullOrWhiteSpace(normalizedDbFullPath))
            {
                return null;
            }

            lock (_syncRoot)
            {
                if (!HasSnapshotLocked(normalizedDbFullPath))
                {
                    return null;
                }

                return _snapshot;
            }
        }

        public void EnsureSnapshot(string dbFullPath)
        {
            string normalizedDbFullPath = NormalizeDbPath(dbFullPath);
            if (string.IsNullOrWhiteSpace(normalizedDbFullPath) || !File.Exists(normalizedDbFullPath))
            {
                return;
            }

            lock (_syncRoot)
            {
                if (HasSnapshotLocked(normalizedDbFullPath) || IsBuildInFlightLocked(normalizedDbFullPath))
                {
                    return;
                }

                _buildingDbFullPath = normalizedDbFullPath;
                long buildVersion = ++_buildRequestVersion;
                _buildingTask = Task.Run(() => BuildSnapshotCore(normalizedDbFullPath, buildVersion));
            }
        }

        public void UpdateMovieTags(string dbFullPath, long movieId, IEnumerable<string> tagItems)
        {
            string normalizedDbFullPath = NormalizeDbPath(dbFullPath);
            if (string.IsNullOrWhiteSpace(normalizedDbFullPath) || movieId <= 0)
            {
                return;
            }

            lock (_syncRoot)
            {
                if (!HasSnapshotLocked(normalizedDbFullPath))
                {
                    return;
                }

                TagIndexSnapshot currentSnapshot = _snapshot;
                Dictionary<string, int> tagCounts = new(
                    currentSnapshot.TagCounts,
                    StringComparer.CurrentCultureIgnoreCase
                );
                Dictionary<long, string[]> movieTags = currentSnapshot
                    .MovieTags.ToDictionary(
                        entry => entry.Key,
                        entry => entry.Value?.ToArray() ?? Array.Empty<string>()
                    );

                string[] oldTags = movieTags.TryGetValue(movieId, out string[] existingTags)
                    ? existingTags ?? Array.Empty<string>()
                    : Array.Empty<string>();
                string[] newTags = TagTextParser.SplitDistinct(
                    tagItems,
                    StringComparer.CurrentCultureIgnoreCase
                );

                UpdateRemovedTags(tagCounts, oldTags, newTags);
                UpdateAddedTags(tagCounts, oldTags, newTags);

                if (newTags.Length == 0)
                {
                    movieTags.Remove(movieId);
                }
                else
                {
                    movieTags[movieId] = newTags;
                }

                _snapshot = new TagIndexSnapshot(
                    normalizedDbFullPath,
                    DateTime.UtcNow,
                    tagCounts,
                    movieTags
                );
            }
        }

        private void BuildSnapshotCore(string dbFullPath, long buildVersion)
        {
            TagIndexSnapshot builtSnapshot;
            try
            {
                builtSnapshot = LoadSnapshot(dbFullPath);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "tag-index",
                    $"build failed: db='{dbFullPath}' err='{ex.GetType().Name}: {ex.Message}'"
                );

                lock (_syncRoot)
                {
                    if (string.Equals(_buildingDbFullPath, dbFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _buildingDbFullPath = "";
                    }
                }
                return;
            }

            bool shouldRaise = false;
            lock (_syncRoot)
            {
                if (
                    buildVersion != _buildRequestVersion
                    || !string.Equals(_buildingDbFullPath, dbFullPath, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return;
                }

                _snapshot = builtSnapshot;
                _buildingDbFullPath = "";
                shouldRaise = true;
            }

            if (shouldRaise)
            {
                SnapshotUpdated?.Invoke(
                    this,
                    new TagIndexSnapshotChangedEventArgs(
                        builtSnapshot.DbFullPath,
                        builtSnapshot.TagCounts.Count
                    )
                );
            }
        }

        private static TagIndexSnapshot LoadSnapshot(string dbFullPath)
        {
            Dictionary<string, int> tagCounts = new(StringComparer.CurrentCultureIgnoreCase);
            Dictionary<long, string[]> movieTags = [];

            using SQLiteConnection connection = SQLite.CreateReadOnlyConnection(dbFullPath);
            connection.Open();

            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = "select movie_id, tag from movie where tag <> ''";

            using SQLiteDataReader reader = command.ExecuteReader();
            int movieIdOrdinal = reader.GetOrdinal("movie_id");
            int tagOrdinal = reader.GetOrdinal("tag");

            while (reader.Read())
            {
                long movieId = reader.IsDBNull(movieIdOrdinal) ? 0 : reader.GetInt64(movieIdOrdinal);
                if (movieId <= 0)
                {
                    continue;
                }

                string tagText = reader.IsDBNull(tagOrdinal) ? "" : reader.GetString(tagOrdinal);
                string[] tags = TagTextParser.SplitDistinct(
                    tagText,
                    StringComparer.CurrentCultureIgnoreCase
                );
                if (tags.Length == 0)
                {
                    continue;
                }

                movieTags[movieId] = tags;
                foreach (string tagName in tags)
                {
                    tagCounts[tagName] = tagCounts.TryGetValue(tagName, out int currentCount)
                        ? currentCount + 1
                        : 1;
                }
            }

            DebugRuntimeLog.Write(
                "tag-index",
                $"build completed: db='{dbFullPath}' tags={tagCounts.Count} movies={movieTags.Count}"
            );

            return new TagIndexSnapshot(dbFullPath, DateTime.UtcNow, tagCounts, movieTags);
        }

        private bool HasSnapshotLocked(string dbFullPath)
        {
            return _snapshot != null
                && string.Equals(_snapshot.DbFullPath, dbFullPath, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsBuildInFlightLocked(string dbFullPath)
        {
            return !_buildingTask.IsCompleted
                && string.Equals(_buildingDbFullPath, dbFullPath, StringComparison.OrdinalIgnoreCase);
        }

        private static void UpdateRemovedTags(
            IDictionary<string, int> tagCounts,
            IReadOnlyCollection<string> oldTags,
            IReadOnlyCollection<string> newTags
        )
        {
            HashSet<string> nextSet = new(newTags, StringComparer.CurrentCultureIgnoreCase);
            foreach (string oldTag in oldTags)
            {
                if (nextSet.Contains(oldTag))
                {
                    continue;
                }

                if (!tagCounts.TryGetValue(oldTag, out int currentCount))
                {
                    continue;
                }

                if (currentCount <= 1)
                {
                    tagCounts.Remove(oldTag);
                    continue;
                }

                tagCounts[oldTag] = currentCount - 1;
            }
        }

        private static void UpdateAddedTags(
            IDictionary<string, int> tagCounts,
            IReadOnlyCollection<string> oldTags,
            IReadOnlyCollection<string> newTags
        )
        {
            HashSet<string> previousSet = new(oldTags, StringComparer.CurrentCultureIgnoreCase);
            foreach (string newTag in newTags)
            {
                if (previousSet.Contains(newTag))
                {
                    continue;
                }

                tagCounts[newTag] = tagCounts.TryGetValue(newTag, out int currentCount)
                    ? currentCount + 1
                    : 1;
            }
        }

        private static string NormalizeDbPath(string dbFullPath)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return "";
            }

            string normalized = dbFullPath.Trim().Trim('"');
            try
            {
                return Path.GetFullPath(normalized);
            }
            catch
            {
                return normalized;
            }
        }
    }
}
