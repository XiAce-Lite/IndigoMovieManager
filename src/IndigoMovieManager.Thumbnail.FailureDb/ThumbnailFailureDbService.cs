using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Text;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Thumbnail.SQLite;

namespace IndigoMovieManager.Thumbnail.FailureDb
{
    // FailureDbの初期化とappend取得をまとめる。
    public sealed class ThumbnailFailureDbService
    {
        private const string UtcDateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        private const string MainFailureLanePredicateSql = "Lane IN ('normal', 'slow')";
        private const int DeleteMainFailureBatchSize = 200;
        private readonly object initializeLock = new();
        private readonly string mainDbFullPath;
        private readonly string mainDbPathHash;
        private readonly string failureDbFullPath;
        private bool isInitialized;

        public ThumbnailFailureDbService(string mainDbFullPath)
        {
            if (string.IsNullOrWhiteSpace(mainDbFullPath))
            {
                throw new ArgumentException("mainDbFullPath is required.", nameof(mainDbFullPath));
            }

            this.mainDbFullPath = mainDbFullPath;
            this.mainDbPathHash = ThumbnailPathKeyHelper.GetMainDbPathHash8(mainDbFullPath);
            failureDbFullPath = ThumbnailFailureDbPathResolver.ResolveFailureDbPath(mainDbFullPath);
        }

        public string MainDbFullPath => mainDbFullPath;
        public string MainDbPathHash => mainDbPathHash;
        public string FailureDbFullPath => failureDbFullPath;

        public void EnsureInitialized()
        {
            if (isInitialized)
            {
                return;
            }

            lock (initializeLock)
            {
                if (isInitialized)
                {
                    return;
                }

                string directory = Path.GetDirectoryName(failureDbFullPath) ?? "";
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using SQLiteConnection connection = CreateConnection();
                connection.Open();
                ThumbnailFailureDbSchema.EnsureCreated(connection);
                isInitialized = true;
            }
        }

        public long AppendFailureRecord(ThumbnailFailureRecord record)
        {
            EnsureInitialized();
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO ThumbnailFailure (
    MainDbFullPath,
    MainDbPathHash,
    MoviePath,
    MoviePathKey,
    TabIndex,
    Lane,
    AttemptGroupId,
    AttemptNo,
    Status,
    LeaseOwner,
    LeaseUntilUtc,
    Engine,
    FailureKind,
    FailureReason,
    ElapsedMs,
    SourcePath,
    OutputThumbPath,
    RepairApplied,
    ResultSignature,
    ExtraJson,
    Priority,
    PriorityUntilUtc,
    CreatedAtUtc,
    UpdatedAtUtc
) VALUES (
    @MainDbFullPath,
    @MainDbPathHash,
    @MoviePath,
    @MoviePathKey,
    @TabIndex,
    @Lane,
    @AttemptGroupId,
    @AttemptNo,
    @Status,
    @LeaseOwner,
    @LeaseUntilUtc,
    @Engine,
    @FailureKind,
    @FailureReason,
    @ElapsedMs,
    @SourcePath,
    @OutputThumbPath,
    @RepairApplied,
    @ResultSignature,
    @ExtraJson,
    @Priority,
    @PriorityUntilUtc,
    @CreatedAtUtc,
    @UpdatedAtUtc
);
SELECT last_insert_rowid();";
            command.Parameters.AddWithValue("@MainDbFullPath", ResolveMainDbFullPath(record));
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@MoviePath", record.MoviePath ?? "");
            command.Parameters.AddWithValue(
                "@MoviePathKey",
                ResolveMoviePathKey(record)
            );
            command.Parameters.AddWithValue("@TabIndex", record.TabIndex);
            command.Parameters.AddWithValue("@Lane", record.Lane ?? "");
            command.Parameters.AddWithValue("@AttemptGroupId", record.AttemptGroupId ?? "");
            command.Parameters.AddWithValue("@AttemptNo", Math.Max(0, record.AttemptNo));
            command.Parameters.AddWithValue("@Status", record.Status ?? "");
            command.Parameters.AddWithValue("@LeaseOwner", record.LeaseOwner ?? "");
            command.Parameters.AddWithValue("@LeaseUntilUtc", record.LeaseUntilUtc ?? "");
            command.Parameters.AddWithValue("@Engine", record.Engine ?? "");
            command.Parameters.AddWithValue("@FailureKind", record.FailureKind.ToString());
            command.Parameters.AddWithValue("@FailureReason", record.FailureReason ?? "");
            command.Parameters.AddWithValue("@ElapsedMs", Math.Max(0, record.ElapsedMs));
            command.Parameters.AddWithValue("@SourcePath", record.SourcePath ?? "");
            command.Parameters.AddWithValue("@OutputThumbPath", record.OutputThumbPath ?? "");
            command.Parameters.AddWithValue("@RepairApplied", record.RepairApplied ? 1 : 0);
            command.Parameters.AddWithValue("@ResultSignature", record.ResultSignature ?? "");
            command.Parameters.AddWithValue("@ExtraJson", record.ExtraJson ?? "");
            command.Parameters.AddWithValue(
                "@Priority",
                (int)ThumbnailQueuePriorityHelper.Normalize(record.Priority)
            );
            command.Parameters.AddWithValue(
                "@PriorityUntilUtc",
                NormalizePriorityUntilUtcText(
                    ThumbnailQueuePriorityHelper.Normalize(record.Priority),
                    record.PriorityUntilUtc
                )
            );
            command.Parameters.AddWithValue("@CreatedAtUtc", ToUtcText(record.CreatedAtUtc));
            command.Parameters.AddWithValue("@UpdatedAtUtc", ToUtcText(record.UpdatedAtUtc));
            object insertedId = command.ExecuteScalar();
            return Convert.ToInt64(insertedId, CultureInfo.InvariantCulture);
        }

        public List<ThumbnailFailureRecord> GetFailureRecords(int limit = 1000)
        {
            EnsureInitialized();
            int safeLimit = limit < 1 ? 1 : limit;

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    FailureId,
    MainDbFullPath,
    MainDbPathHash,
    MoviePath,
    MoviePathKey,
    TabIndex,
    Lane,
    AttemptGroupId,
    AttemptNo,
    Status,
    LeaseOwner,
    LeaseUntilUtc,
    Engine,
    FailureKind,
    FailureReason,
    ElapsedMs,
    SourcePath,
    OutputThumbPath,
    RepairApplied,
    ResultSignature,
    ExtraJson,
    Priority,
    PriorityUntilUtc,
    CreatedAtUtc,
    UpdatedAtUtc
FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash
ORDER BY CreatedAtUtc DESC, FailureId DESC
LIMIT @Limit;";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@Limit", safeLimit);

            List<ThumbnailFailureRecord> records = [];
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                records.Add(ReadRecord(reader));
            }

            return records;
        }

        // 手動救済の即時反映では、現在DB配下の1行だけを軽く引き直す。
        public ThumbnailFailureRecord GetFailureRecordById(long failureId)
        {
            EnsureInitialized();
            if (failureId < 1)
            {
                return null;
            }

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    FailureId,
    MainDbFullPath,
    MainDbPathHash,
    MoviePath,
    MoviePathKey,
    TabIndex,
    Lane,
    AttemptGroupId,
    AttemptNo,
    Status,
    LeaseOwner,
    LeaseUntilUtc,
    Engine,
    FailureKind,
    FailureReason,
    ElapsedMs,
    SourcePath,
    OutputThumbPath,
    RepairApplied,
    ResultSignature,
    ExtraJson,
    Priority,
    PriorityUntilUtc,
    CreatedAtUtc,
    UpdatedAtUtc
FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash
  AND FailureId = @FailureId
LIMIT 1;";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@FailureId", failureId);

            using SQLiteDataReader reader = command.ExecuteReader();
            return reader.Read() ? ReadRecord(reader) : null;
        }

        // 親行だけを moviePathKey + tab 単位で畳み、一覧UIが今の救済状態を軽く読めるようにする。
        public List<ThumbnailFailureRecord> GetLatestMainFailureRecords()
        {
            EnsureInitialized();

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $@"
SELECT
    FailureId,
    MainDbFullPath,
    MainDbPathHash,
    MoviePath,
    MoviePathKey,
    TabIndex,
    Lane,
    AttemptGroupId,
    AttemptNo,
    Status,
    LeaseOwner,
    LeaseUntilUtc,
    Engine,
    FailureKind,
    FailureReason,
    ElapsedMs,
    SourcePath,
    OutputThumbPath,
    RepairApplied,
    ResultSignature,
    ExtraJson,
    Priority,
    PriorityUntilUtc,
    CreatedAtUtc,
    UpdatedAtUtc
FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash
  AND {MainFailureLanePredicateSql}
ORDER BY MoviePathKey ASC, TabIndex ASC, UpdatedAtUtc DESC, FailureId DESC;";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);

            List<ThumbnailFailureRecord> records = [];
            string lastMoviePathKey = "";
            int lastTabIndex = int.MinValue;

            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                ThumbnailFailureRecord record = ReadRecord(reader);
                record.MoviePathKey = ResolveMoviePathKey(record);
                if (
                    string.Equals(lastMoviePathKey, record.MoviePathKey, StringComparison.Ordinal)
                    && lastTabIndex == record.TabIndex
                )
                {
                    continue;
                }

                records.Add(record);
                lastMoviePathKey = record.MoviePathKey;
                lastTabIndex = record.TabIndex;
            }

            return records;
        }

        // 起動側が「今 worker を立てるべきか」を軽く判定する。
        public bool HasPendingRescueWork(DateTime utcNow)
        {
            EnsureInitialized();

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $@"
SELECT 1
FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash
  AND {MainFailureLanePredicateSql}
  AND (
      Status = 'pending_rescue'
      OR (Status = 'processing_rescue' AND LeaseUntilUtc <> '' AND LeaseUntilUtc < @NowUtc)
  )
LIMIT 1;";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));
            object value = command.ExecuteScalar();
            return value != null && value != DBNull.Value;
        }

        // 期限切れ lease を持つ main 行だけを pending_rescue へ戻し、残留 processing を整理する。
        public int RecoverExpiredProcessingToPendingRescue(DateTime utcNow)
        {
            EnsureInitialized();

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $@"
UPDATE ThumbnailFailure
SET
    Status = 'pending_rescue',
    LeaseOwner = '',
    LeaseUntilUtc = '',
    UpdatedAtUtc = @NowUtc
WHERE MainDbPathHash = @MainDbPathHash
  AND {MainFailureLanePredicateSql}
  AND Status = 'processing_rescue'
  AND LeaseUntilUtc <> ''
  AND LeaseUntilUtc < @NowUtc;";
            command.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            return command.ExecuteNonQuery();
        }

        // 同一動画・同一タブに未完了の救済要求が残っているかを軽く確認する。
        public bool HasOpenRescueRequest(string moviePathKey, int tabIndex)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(moviePathKey))
            {
                return false;
            }

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $@"
SELECT 1
FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash
  AND {MainFailureLanePredicateSql}
  AND MoviePathKey = @MoviePathKey
  AND TabIndex = @TabIndex
  AND Status IN ('pending_rescue', 'processing_rescue', 'rescued')
LIMIT 1;";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@MoviePathKey", moviePathKey);
            command.Parameters.AddWithValue("@TabIndex", tabIndex);
            object value = command.ExecuteScalar();
            return value != null && value != DBNull.Value;
        }

        // 手動救済の直後に対象行を worker へ直渡しできるよう、最新の未完了 main 行IDを返す。
        public long GetOpenRescueRequestFailureId(string moviePathKey, int tabIndex)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(moviePathKey))
            {
                return 0;
            }

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $@"
SELECT FailureId
FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash
  AND {MainFailureLanePredicateSql}
  AND MoviePathKey = @MoviePathKey
  AND TabIndex = @TabIndex
  AND Status IN ('pending_rescue', 'processing_rescue', 'rescued')
ORDER BY UpdatedAtUtc DESC, FailureId DESC
LIMIT 1;";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@MoviePathKey", moviePathKey);
            command.Parameters.AddWithValue("@TabIndex", tabIndex);
            object value = command.ExecuteScalar();
            return value == null || value == DBNull.Value
                ? 0
                : Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }

        // placeholder 起点の初回だけ通常キューへ戻すため、同一動画・同一タブの履歴有無を薄く見る。
        public bool HasFailureHistory(string moviePathKey, int tabIndex)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(moviePathKey))
            {
                return false;
            }

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT 1
FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash
  AND MoviePathKey = @MoviePathKey
  AND TabIndex = @TabIndex
LIMIT 1;";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@MoviePathKey", moviePathKey);
            command.Parameters.AddWithValue("@TabIndex", tabIndex);
            object value = command.ExecuteScalar();
            return value != null && value != DBNull.Value;
        }

        // Watcherの欠損救済で1件ずつDB照会しないよう、未完了のmain rescueをキー集合で返す。
        public HashSet<string> GetOpenRescueRequestKeys()
        {
            EnsureInitialized();

            HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $@"
SELECT DISTINCT
    MoviePathKey,
    TabIndex
FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash
  AND {MainFailureLanePredicateSql}
  AND Status IN ('pending_rescue', 'processing_rescue', 'rescued');";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);

            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string moviePathKey = Convert.ToString(reader["MoviePathKey"]) ?? "";
                if (string.IsNullOrWhiteSpace(moviePathKey))
                {
                    continue;
                }

                int tabIndex = Convert.ToInt32(reader["TabIndex"], CultureInfo.InvariantCulture);
                keys.Add($"{moviePathKey}|{tabIndex}");
            }

            return keys;
        }

        // 既存pending_rescueがある時だけ、通常から優先へ昇格させる。
        public int PromotePendingRescueRequest(
            string moviePathKey,
            int tabIndex,
            ThumbnailQueuePriority priority,
            DateTime utcNow,
            string extraJson = "",
            DateTime? priorityUntilUtc = null
        )
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(moviePathKey))
            {
                return 0;
            }

            ThumbnailQueuePriority normalizedPriority = ThumbnailQueuePriorityHelper.Normalize(
                priority
            );
            if (!ThumbnailQueuePriorityHelper.IsPreferred(normalizedPriority))
            {
                return 0;
            }

            using SQLiteConnection connection = OpenConnection();
            BeginImmediateTransaction(connection);

            try
            {
                List<ThumbnailFailureRecord> candidates = [];
                using (SQLiteCommand selectCommand = connection.CreateCommand())
                {
                    selectCommand.CommandText = $@"
SELECT
    FailureId,
    MainDbFullPath,
    MainDbPathHash,
    MoviePath,
    MoviePathKey,
    TabIndex,
    Lane,
    AttemptGroupId,
    AttemptNo,
    Status,
    LeaseOwner,
    LeaseUntilUtc,
    Engine,
    FailureKind,
    FailureReason,
    ElapsedMs,
    SourcePath,
    OutputThumbPath,
    RepairApplied,
    ResultSignature,
    ExtraJson,
    Priority,
    PriorityUntilUtc,
    CreatedAtUtc,
    UpdatedAtUtc
FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash
  AND {MainFailureLanePredicateSql}
  AND MoviePathKey = @MoviePathKey
  AND TabIndex = @TabIndex
  AND Status = 'pending_rescue'
ORDER BY UpdatedAtUtc DESC, FailureId DESC;";
                    selectCommand.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
                    selectCommand.Parameters.AddWithValue("@MoviePathKey", moviePathKey);
                    selectCommand.Parameters.AddWithValue("@TabIndex", tabIndex);

                    using SQLiteDataReader reader = selectCommand.ExecuteReader();
                    while (reader.Read())
                    {
                        candidates.Add(ReadRecord(reader));
                    }
                }

                if (candidates.Count < 1)
                {
                    CommitTransaction(connection);
                    return 0;
                }

                int updatedCount = 0;
                foreach (ThumbnailFailureRecord candidate in candidates)
                {
                    ThumbnailQueuePriority nextPriority = ResolvePromotedPriority(
                        candidate.Priority,
                        normalizedPriority
                    );
                    string nextPriorityUntilUtc = ResolvePromotedPriorityUntilUtc(
                        candidate.Priority,
                        candidate.PriorityUntilUtc,
                        normalizedPriority,
                        priorityUntilUtc
                    );
                    bool priorityChanged =
                        nextPriority != ThumbnailQueuePriorityHelper.Normalize(candidate.Priority)
                        || !string.Equals(
                            nextPriorityUntilUtc,
                            NormalizePriorityUntilUtcText(candidate.Priority, candidate.PriorityUntilUtc),
                            StringComparison.Ordinal
                        );
                    bool extraJsonChanged = !string.IsNullOrWhiteSpace(extraJson);
                    if (!priorityChanged && !extraJsonChanged)
                    {
                        continue;
                    }

                    using SQLiteCommand updateCommand = connection.CreateCommand();
                    updateCommand.CommandText = @"
UPDATE ThumbnailFailure
SET
    Priority = @Priority,
    PriorityUntilUtc = @PriorityUntilUtc,
    ExtraJson = CASE WHEN @ExtraJson <> '' THEN @ExtraJson ELSE ExtraJson END,
    UpdatedAtUtc = @NowUtc
WHERE FailureId = @FailureId
  AND MainDbPathHash = @MainDbPathHash
  AND Status = 'pending_rescue';";
                    updateCommand.Parameters.AddWithValue("@Priority", (int)nextPriority);
                    updateCommand.Parameters.AddWithValue("@PriorityUntilUtc", nextPriorityUntilUtc);
                    updateCommand.Parameters.AddWithValue("@ExtraJson", extraJson ?? "");
                    updateCommand.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));
                    updateCommand.Parameters.AddWithValue("@FailureId", candidate.FailureId);
                    updateCommand.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
                    updatedCount += updateCommand.ExecuteNonQuery();
                }

                CommitTransaction(connection);
                return updatedCount;
            }
            catch
            {
                RollbackTransaction(connection);
                throw;
            }
        }

        // 右パネル表示用に、今見せるべき救済親行を1件だけ返す。
        public ThumbnailFailureRecord GetLatestRescueDisplayRecord(DateTime utcNow)
        {
            EnsureInitialized();

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $@"
SELECT
    FailureId,
    MainDbFullPath,
    MainDbPathHash,
    MoviePath,
    MoviePathKey,
    TabIndex,
    Lane,
    AttemptGroupId,
    AttemptNo,
    Status,
    LeaseOwner,
    LeaseUntilUtc,
    Engine,
    FailureKind,
    FailureReason,
    ElapsedMs,
    SourcePath,
    OutputThumbPath,
    RepairApplied,
    ResultSignature,
    ExtraJson,
    Priority,
    PriorityUntilUtc,
    CreatedAtUtc,
    UpdatedAtUtc
FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash
  AND {MainFailureLanePredicateSql}
  AND Status IN ('pending_rescue', 'processing_rescue', 'rescued')
ORDER BY
  CASE
    WHEN Status = 'processing_rescue' AND (LeaseUntilUtc = '' OR LeaseUntilUtc >= @NowUtc) THEN 0
    WHEN Status = 'pending_rescue' THEN 1
    WHEN Status = 'processing_rescue' THEN 2
    WHEN Status = 'rescued' THEN 3
    ELSE 4
  END ASC,
  CASE
    WHEN Status = 'pending_rescue' AND Priority = 1 AND (PriorityUntilUtc = '' OR PriorityUntilUtc > @NowUtc) THEN 0
    WHEN Status = 'pending_rescue' THEN 1
    ELSE 2
  END ASC,
  CASE
    WHEN Status = 'pending_rescue' THEN UpdatedAtUtc
    ELSE '9999-12-31T23:59:59.999Z'
  END ASC,
  UpdatedAtUtc DESC,
  FailureId DESC
LIMIT 1;";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));

            using SQLiteDataReader reader = command.ExecuteReader();
            return reader.Read() ? ReadRecord(reader) : null;
        }

        // 一覧から消したい行だけを対象に、現在DBの main lane 記録をまとめて削除する。
        public int DeleteMainFailureRecords(IEnumerable<(string MoviePathKey, int TabIndex)> targets)
        {
            EnsureInitialized();
            if (targets == null)
            {
                return 0;
            }

            List<(string MoviePathKey, int TabIndex)> filteredTargets = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach ((string moviePathKey, int tabIndex) in targets)
            {
                if (string.IsNullOrWhiteSpace(moviePathKey))
                {
                    continue;
                }

                string dedupeKey = $"{moviePathKey}|{tabIndex}";
                if (!seen.Add(dedupeKey))
                {
                    continue;
                }

                filteredTargets.Add((moviePathKey, tabIndex));
            }

            if (filteredTargets.Count < 1)
            {
                return 0;
            }

            using SQLiteConnection connection = OpenConnection();
            BeginImmediateTransaction(connection);
            try
            {
                int deletedCount = 0;
                for (int offset = 0; offset < filteredTargets.Count; offset += DeleteMainFailureBatchSize)
                {
                    int batchCount = Math.Min(DeleteMainFailureBatchSize, filteredTargets.Count - offset);
                    deletedCount += DeleteMainFailureRecordBatch(connection, filteredTargets, offset, batchCount);
                }

                CommitTransaction(connection);
                return deletedCount;
            }
            catch
            {
                RollbackTransaction(connection);
                throw;
            }
        }

        // Failure DB を現行 MainDB 視点でまるごとクリアする。
        public int ClearMainFailureRecords()
        {
            EnsureInitialized();

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash;";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            return command.ExecuteNonQuery();
        }

        // 本exeが未反映の救済成功行だけを拾い、UI反映の入口に使う。
        public List<ThumbnailFailureRecord> GetRescuedRecordsForSync(int limit = 100)
        {
            EnsureInitialized();
            int safeLimit = limit < 1 ? 1 : limit;

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $@"
SELECT
    FailureId,
    MainDbFullPath,
    MainDbPathHash,
    MoviePath,
    MoviePathKey,
    TabIndex,
    Lane,
    AttemptGroupId,
    AttemptNo,
    Status,
    LeaseOwner,
    LeaseUntilUtc,
    Engine,
    FailureKind,
    FailureReason,
    ElapsedMs,
    SourcePath,
    OutputThumbPath,
    RepairApplied,
    ResultSignature,
    ExtraJson,
    Priority,
    PriorityUntilUtc,
    CreatedAtUtc,
    UpdatedAtUtc
FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash
  AND {MainFailureLanePredicateSql}
  AND Status = 'rescued'
ORDER BY UpdatedAtUtc ASC, FailureId ASC
LIMIT @Limit;";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@Limit", safeLimit);

            List<ThumbnailFailureRecord> records = [];
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                records.Add(ReadRecord(reader));
            }

            return records;
        }

        // 救済exeが処理対象を1本だけ確保し、二重救済を防ぐ。
        public ThumbnailFailureRecord GetPendingRescueAndLease(
            string leaseOwner,
            TimeSpan leaseDuration,
            DateTime utcNow
        )
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(leaseOwner))
            {
                throw new ArgumentException("leaseOwner is required.", nameof(leaseOwner));
            }

            using SQLiteConnection connection = OpenConnection();
            BeginImmediateTransaction(connection);

            try
            {
                ThumbnailFailureRecord record = null;
                using (SQLiteCommand selectCommand = connection.CreateCommand())
                {
                    selectCommand.CommandText = $@"
SELECT
    FailureId,
    MainDbFullPath,
    MainDbPathHash,
    MoviePath,
    MoviePathKey,
    TabIndex,
    Lane,
    AttemptGroupId,
    AttemptNo,
    Status,
    LeaseOwner,
    LeaseUntilUtc,
    Engine,
    FailureKind,
    FailureReason,
    ElapsedMs,
    SourcePath,
    OutputThumbPath,
    RepairApplied,
    ResultSignature,
    ExtraJson,
    Priority,
    PriorityUntilUtc,
    CreatedAtUtc,
    UpdatedAtUtc
FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash
  AND {MainFailureLanePredicateSql}
  AND (
      Status = 'pending_rescue'
      OR (Status = 'processing_rescue' AND LeaseUntilUtc <> '' AND LeaseUntilUtc < @NowUtc)
  )
ORDER BY
  CASE
    WHEN Priority = 1 AND (PriorityUntilUtc = '' OR PriorityUntilUtc > @NowUtc) THEN 1
    ELSE 0
  END DESC,
  UpdatedAtUtc ASC,
  FailureId ASC
LIMIT 1;";
                    selectCommand.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
                    selectCommand.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));

                    using SQLiteDataReader reader = selectCommand.ExecuteReader();
                    if (reader.Read())
                    {
                        record = ReadRecord(reader);
                    }
                }

                if (record == null)
                {
                    CommitTransaction(connection);
                    return null;
                }

                string attemptGroupId = string.IsNullOrWhiteSpace(record.AttemptGroupId)
                    ? Guid.NewGuid().ToString("N")
                    : record.AttemptGroupId;
                DateTime leaseUntilUtc = utcNow.Add(leaseDuration);

                using SQLiteCommand updateCommand = connection.CreateCommand();
                updateCommand.CommandText = @"
UPDATE ThumbnailFailure
SET
    Status = 'processing_rescue',
    LeaseOwner = @LeaseOwner,
    LeaseUntilUtc = @LeaseUntilUtc,
    AttemptGroupId = @AttemptGroupId,
    UpdatedAtUtc = @NowUtc
WHERE FailureId = @FailureId
  AND MainDbPathHash = @MainDbPathHash;";
                updateCommand.Parameters.AddWithValue("@LeaseOwner", leaseOwner);
                updateCommand.Parameters.AddWithValue("@LeaseUntilUtc", ToUtcText(leaseUntilUtc));
                updateCommand.Parameters.AddWithValue("@AttemptGroupId", attemptGroupId);
                updateCommand.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));
                updateCommand.Parameters.AddWithValue("@FailureId", record.FailureId);
                updateCommand.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
                int updated = updateCommand.ExecuteNonQuery();
                if (updated < 1)
                {
                    RollbackTransaction(connection);
                    return null;
                }

                CommitTransaction(connection);
                record.Status = "processing_rescue";
                record.LeaseOwner = leaseOwner;
                record.LeaseUntilUtc = ToUtcText(leaseUntilUtc);
                record.AttemptGroupId = attemptGroupId;
                record.UpdatedAtUtc = utcNow;
                return record;
            }
            catch
            {
                RollbackTransaction(connection);
                throw;
            }
        }

        // 明示救済では enqueue 直後の failure_id を優先して取り、古い pending へ吸われないようにする。
        public ThumbnailFailureRecord GetPendingRescueAndLeaseById(
            long failureId,
            string leaseOwner,
            TimeSpan leaseDuration,
            DateTime utcNow
        )
        {
            EnsureInitialized();
            if (failureId < 1)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(leaseOwner))
            {
                throw new ArgumentException("leaseOwner is required.", nameof(leaseOwner));
            }

            using SQLiteConnection connection = OpenConnection();
            BeginImmediateTransaction(connection);

            try
            {
                ThumbnailFailureRecord record = null;
                using (SQLiteCommand selectCommand = connection.CreateCommand())
                {
                    selectCommand.CommandText = $@"
SELECT
    FailureId,
    MainDbFullPath,
    MainDbPathHash,
    MoviePath,
    MoviePathKey,
    TabIndex,
    Lane,
    AttemptGroupId,
    AttemptNo,
    Status,
    LeaseOwner,
    LeaseUntilUtc,
    Engine,
    FailureKind,
    FailureReason,
    ElapsedMs,
    SourcePath,
    OutputThumbPath,
    RepairApplied,
    ResultSignature,
    ExtraJson,
    Priority,
    PriorityUntilUtc,
    CreatedAtUtc,
    UpdatedAtUtc
FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash
  AND {MainFailureLanePredicateSql}
  AND FailureId = @FailureId
  AND (
      Status = 'pending_rescue'
      OR (Status = 'processing_rescue' AND LeaseUntilUtc <> '' AND LeaseUntilUtc < @NowUtc)
  )
LIMIT 1;";
                    selectCommand.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
                    selectCommand.Parameters.AddWithValue("@FailureId", failureId);
                    selectCommand.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));

                    using SQLiteDataReader reader = selectCommand.ExecuteReader();
                    if (reader.Read())
                    {
                        record = ReadRecord(reader);
                    }
                }

                if (record == null)
                {
                    CommitTransaction(connection);
                    return null;
                }

                string attemptGroupId = string.IsNullOrWhiteSpace(record.AttemptGroupId)
                    ? Guid.NewGuid().ToString("N")
                    : record.AttemptGroupId;
                DateTime leaseUntilUtc = utcNow.Add(leaseDuration);

                using SQLiteCommand updateCommand = connection.CreateCommand();
                updateCommand.CommandText = @"
UPDATE ThumbnailFailure
SET
    Status = 'processing_rescue',
    LeaseOwner = @LeaseOwner,
    LeaseUntilUtc = @LeaseUntilUtc,
    AttemptGroupId = @AttemptGroupId,
    UpdatedAtUtc = @NowUtc
WHERE FailureId = @FailureId
  AND MainDbPathHash = @MainDbPathHash;";
                updateCommand.Parameters.AddWithValue("@LeaseOwner", leaseOwner);
                updateCommand.Parameters.AddWithValue("@LeaseUntilUtc", ToUtcText(leaseUntilUtc));
                updateCommand.Parameters.AddWithValue("@AttemptGroupId", attemptGroupId);
                updateCommand.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));
                updateCommand.Parameters.AddWithValue("@FailureId", record.FailureId);
                updateCommand.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
                int updated = updateCommand.ExecuteNonQuery();
                if (updated < 1)
                {
                    RollbackTransaction(connection);
                    return null;
                }

                CommitTransaction(connection);
                record.Status = "processing_rescue";
                record.LeaseOwner = leaseOwner;
                record.LeaseUntilUtc = ToUtcText(leaseUntilUtc);
                record.AttemptGroupId = attemptGroupId;
                record.UpdatedAtUtc = utcNow;
                return record;
            }
            catch
            {
                RollbackTransaction(connection);
                throw;
            }
        }

        // 長時間救済中も lease を延長し、他workerへの奪取を防ぐ。
        public void ExtendLease(
            long failureId,
            string leaseOwner,
            DateTime leaseUntilUtc,
            DateTime utcNow
        )
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(leaseOwner))
            {
                throw new ArgumentException("leaseOwner is required.", nameof(leaseOwner));
            }

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
UPDATE ThumbnailFailure
SET
    LeaseUntilUtc = @LeaseUntilUtc,
    UpdatedAtUtc = @NowUtc
WHERE FailureId = @FailureId
  AND MainDbPathHash = @MainDbPathHash
  AND Status = 'processing_rescue'
  AND LeaseOwner = @LeaseOwner;";
            command.Parameters.AddWithValue("@LeaseUntilUtc", ToUtcText(leaseUntilUtc));
            command.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));
            command.Parameters.AddWithValue("@FailureId", failureId);
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@LeaseOwner", leaseOwner);
            command.ExecuteNonQuery();
        }

        // 親行へ progress snapshot を残し、今どの段階で止まっているかを読めるようにする。
        public int UpdateProcessingSnapshot(
            long failureId,
            string leaseOwner,
            DateTime utcNow,
            string extraJson,
            string failureReason = "",
            string resultSignature = ""
        )
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(leaseOwner))
            {
                throw new ArgumentException("leaseOwner is required.", nameof(leaseOwner));
            }

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
UPDATE ThumbnailFailure
SET
    ExtraJson = CASE WHEN @ExtraJson <> '' THEN @ExtraJson ELSE ExtraJson END,
    FailureReason = CASE WHEN @FailureReason <> '' THEN @FailureReason ELSE FailureReason END,
    ResultSignature = CASE WHEN @ResultSignature <> '' THEN @ResultSignature ELSE ResultSignature END,
    UpdatedAtUtc = @NowUtc
WHERE FailureId = @FailureId
  AND MainDbPathHash = @MainDbPathHash
  AND Status = 'processing_rescue'
  AND LeaseOwner = @LeaseOwner;";
            command.Parameters.AddWithValue("@ExtraJson", extraJson ?? "");
            command.Parameters.AddWithValue("@FailureReason", failureReason ?? "");
            command.Parameters.AddWithValue("@ResultSignature", resultSignature ?? "");
            command.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));
            command.Parameters.AddWithValue("@FailureId", failureId);
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@LeaseOwner", leaseOwner);
            return command.ExecuteNonQuery();
        }

        // 救済完了後の終端状態を元レコードへ反映する。
        public int UpdateFailureStatus(
            long failureId,
            string leaseOwner,
            string status,
            DateTime utcNow,
            string outputThumbPath = "",
            string resultSignature = "",
            string extraJson = "",
            bool clearLease = true,
            ThumbnailFailureKind? failureKind = null,
            string failureReason = ""
        )
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(leaseOwner))
            {
                throw new ArgumentException("leaseOwner is required.", nameof(leaseOwner));
            }

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
UPDATE ThumbnailFailure
SET
    Status = @Status,
    LeaseOwner = CASE WHEN @ClearLease = 1 THEN '' ELSE LeaseOwner END,
    LeaseUntilUtc = CASE WHEN @ClearLease = 1 THEN '' ELSE LeaseUntilUtc END,
    OutputThumbPath = CASE WHEN @OutputThumbPath <> '' THEN @OutputThumbPath ELSE OutputThumbPath END,
    ResultSignature = CASE WHEN @ResultSignature <> '' THEN @ResultSignature ELSE ResultSignature END,
    ExtraJson = CASE WHEN @ExtraJson <> '' THEN @ExtraJson ELSE ExtraJson END,
    FailureKind = CASE WHEN @FailureKind <> '' THEN @FailureKind ELSE FailureKind END,
    FailureReason = CASE WHEN @FailureReason <> '' THEN @FailureReason ELSE FailureReason END,
    UpdatedAtUtc = @NowUtc
WHERE FailureId = @FailureId
  AND MainDbPathHash = @MainDbPathHash
  AND Status = 'processing_rescue'
  AND LeaseOwner = @LeaseOwner;";
            command.Parameters.AddWithValue("@Status", status ?? "");
            command.Parameters.AddWithValue("@ClearLease", clearLease ? 1 : 0);
            command.Parameters.AddWithValue("@OutputThumbPath", outputThumbPath ?? "");
            command.Parameters.AddWithValue("@ResultSignature", resultSignature ?? "");
            command.Parameters.AddWithValue("@ExtraJson", extraJson ?? "");
            command.Parameters.AddWithValue(
                "@FailureKind",
                failureKind.HasValue ? failureKind.Value.ToString() : ""
            );
            command.Parameters.AddWithValue("@FailureReason", failureReason ?? "");
            command.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));
            command.Parameters.AddWithValue("@FailureId", failureId);
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@LeaseOwner", leaseOwner);
            return command.ExecuteNonQuery();
        }

        // 本exeがUI反映を終えたら reflected へ倒し、同じ rescued を何度も拾わないようにする。
        public int MarkRescuedAsReflected(long failureId, DateTime utcNow, string extraJson = "")
        {
            EnsureInitialized();

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $@"
UPDATE ThumbnailFailure
SET
    Status = 'reflected',
    ExtraJson = CASE WHEN @ExtraJson <> '' THEN @ExtraJson ELSE ExtraJson END,
    UpdatedAtUtc = @NowUtc
WHERE FailureId = @FailureId
  AND MainDbPathHash = @MainDbPathHash
  AND {MainFailureLanePredicateSql}
  AND Status = 'rescued';";
            command.Parameters.AddWithValue("@ExtraJson", extraJson ?? "");
            command.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));
            command.Parameters.AddWithValue("@FailureId", failureId);
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            return command.ExecuteNonQuery();
        }

        // 反映時に出力が消えていた場合は pending_rescue へ戻し、次回workerへ再委譲する。
        public int ResetRescuedToPendingRescue(
            long failureId,
            DateTime utcNow,
            string failureReason = "",
            string extraJson = ""
        )
        {
            EnsureInitialized();

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $@"
UPDATE ThumbnailFailure
SET
    Status = 'pending_rescue',
    FailureReason = CASE WHEN @FailureReason <> '' THEN @FailureReason ELSE FailureReason END,
    ExtraJson = CASE WHEN @ExtraJson <> '' THEN @ExtraJson ELSE ExtraJson END,
    UpdatedAtUtc = @NowUtc
WHERE FailureId = @FailureId
  AND MainDbPathHash = @MainDbPathHash
  AND {MainFailureLanePredicateSql}
  AND Status = 'rescued';";
            command.Parameters.AddWithValue("@FailureReason", failureReason ?? "");
            command.Parameters.AddWithValue("@ExtraJson", extraJson ?? "");
            command.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));
            command.Parameters.AddWithValue("@FailureId", failureId);
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            return command.ExecuteNonQuery();
        }

        private SQLiteConnection OpenConnection()
        {
            SQLiteConnection connection = CreateConnection();
            connection.Open();
            ThumbnailFailureDbSchema.ApplyConnectionPragmas(connection);
            return connection;
        }

        private SQLiteConnection CreateConnection()
        {
            return new SQLiteConnection(BuildConnectionString(failureDbFullPath));
        }

        public static string BuildConnectionString(string failureDbFullPath)
        {
            // FailureDB も他DBと同じ helper を通し、UNC 対応ルールを一箇所に寄せる。
            SQLiteConnectionStringBuilder builder = new()
            {
                // 接続文字列に入る瞬間だけ UNC の連続 "\" を逃がしておく。
                DataSource = SQLiteConnectionStringPathHelper.EscapeDataSourcePath(failureDbFullPath),
            };

            return builder.ToString();
        }

        private string ResolveMainDbFullPath(ThumbnailFailureRecord record)
        {
            if (!string.IsNullOrWhiteSpace(record.MainDbFullPath))
            {
                return record.MainDbFullPath;
            }

            return mainDbFullPath;
        }

        private static string ResolveMoviePathKey(ThumbnailFailureRecord record)
        {
            if (!string.IsNullOrWhiteSpace(record.MoviePathKey))
            {
                return record.MoviePathKey;
            }

            return ThumbnailFailureDbPathResolver.CreateMoviePathKey(record.MoviePath);
        }

        private static string ToUtcText(DateTime dateTime)
        {
            return dateTime.ToUniversalTime().ToString(UtcDateFormat, CultureInfo.InvariantCulture);
        }

        private static DateTime ParseUtcText(string text)
        {
            if (
                DateTime.TryParseExact(
                    text ?? "",
                    UtcDateFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime parsed
                )
            )
            {
                return parsed;
            }

            return DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        }

        private static ThumbnailFailureKind ParseFailureKind(string raw)
        {
            return Enum.TryParse(raw ?? "", ignoreCase: true, out ThumbnailFailureKind parsed)
                ? parsed
                : ThumbnailFailureKind.Unknown;
        }

        private static ThumbnailQueuePriority ParsePriority(long raw)
        {
            return ThumbnailQueuePriorityHelper.Normalize((ThumbnailQueuePriority)raw);
        }

        private static ThumbnailFailureRecord ReadRecord(SQLiteDataReader reader)
        {
            return new ThumbnailFailureRecord
            {
                FailureId = reader.GetInt64(0),
                MainDbFullPath = reader.IsDBNull(1) ? "" : reader.GetString(1),
                MainDbPathHash = reader.IsDBNull(2) ? "" : reader.GetString(2),
                MoviePath = reader.IsDBNull(3) ? "" : reader.GetString(3),
                MoviePathKey = reader.IsDBNull(4) ? "" : reader.GetString(4),
                TabIndex = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                Lane = reader.IsDBNull(6) ? "" : reader.GetString(6),
                AttemptGroupId = reader.IsDBNull(7) ? "" : reader.GetString(7),
                AttemptNo = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                Status = reader.IsDBNull(9) ? "" : reader.GetString(9),
                LeaseOwner = reader.IsDBNull(10) ? "" : reader.GetString(10),
                LeaseUntilUtc = reader.IsDBNull(11) ? "" : reader.GetString(11),
                Engine = reader.IsDBNull(12) ? "" : reader.GetString(12),
                FailureKind = ParseFailureKind(reader.IsDBNull(13) ? "" : reader.GetString(13)),
                FailureReason = reader.IsDBNull(14) ? "" : reader.GetString(14),
                ElapsedMs = reader.IsDBNull(15) ? 0 : reader.GetInt64(15),
                SourcePath = reader.IsDBNull(16) ? "" : reader.GetString(16),
                OutputThumbPath = reader.IsDBNull(17) ? "" : reader.GetString(17),
                RepairApplied = !reader.IsDBNull(18) && reader.GetInt32(18) != 0,
                ResultSignature = reader.IsDBNull(19) ? "" : reader.GetString(19),
                ExtraJson = reader.IsDBNull(20) ? "" : reader.GetString(20),
                Priority = ParsePriority(reader.IsDBNull(21) ? 0 : reader.GetInt64(21)),
                PriorityUntilUtc = reader.IsDBNull(22) ? "" : reader.GetString(22),
                CreatedAtUtc = ParseUtcText(reader.IsDBNull(23) ? "" : reader.GetString(23)),
                UpdatedAtUtc = ParseUtcText(reader.IsDBNull(24) ? "" : reader.GetString(24)),
            };
        }

        private static ThumbnailQueuePriority ResolvePromotedPriority(
            ThumbnailQueuePriority currentPriority,
            ThumbnailQueuePriority requestedPriority
        )
        {
            ThumbnailQueuePriority normalizedCurrent = ThumbnailQueuePriorityHelper.Normalize(
                currentPriority
            );
            ThumbnailQueuePriority normalizedRequested = ThumbnailQueuePriorityHelper.Normalize(
                requestedPriority
            );
            return normalizedRequested > normalizedCurrent ? normalizedRequested : normalizedCurrent;
        }

        private static string ResolvePromotedPriorityUntilUtc(
            ThumbnailQueuePriority currentPriority,
            string currentPriorityUntilUtc,
            ThumbnailQueuePriority requestedPriority,
            DateTime? requestedPriorityUntilUtc
        )
        {
            ThumbnailQueuePriority normalizedCurrent = ThumbnailQueuePriorityHelper.Normalize(
                currentPriority
            );
            ThumbnailQueuePriority normalizedRequested = ThumbnailQueuePriorityHelper.Normalize(
                requestedPriority
            );
            string normalizedCurrentUntilUtc = NormalizePriorityUntilUtcText(
                normalizedCurrent,
                currentPriorityUntilUtc
            );
            if (!ThumbnailQueuePriorityHelper.IsPreferred(normalizedRequested))
            {
                return normalizedCurrentUntilUtc;
            }

            if (!requestedPriorityUntilUtc.HasValue)
            {
                return "";
            }

            if (
                ThumbnailQueuePriorityHelper.IsPreferred(normalizedCurrent)
                && string.IsNullOrWhiteSpace(normalizedCurrentUntilUtc)
            )
            {
                return "";
            }

            string requestedUntilUtc = NormalizePriorityUntilUtcText(
                normalizedRequested,
                requestedPriorityUntilUtc
            );
            if (string.IsNullOrWhiteSpace(normalizedCurrentUntilUtc))
            {
                return requestedUntilUtc;
            }

            DateTime currentUntil = ParseUtcText(normalizedCurrentUntilUtc);
            DateTime requestedUntil = ParseUtcText(requestedUntilUtc);
            return currentUntil > requestedUntil ? normalizedCurrentUntilUtc : requestedUntilUtc;
        }

        private static string NormalizePriorityUntilUtcText(
            ThumbnailQueuePriority priority,
            string priorityUntilUtc
        )
        {
            if (!ThumbnailQueuePriorityHelper.IsPreferred(priority))
            {
                return "";
            }

            DateTime parsed = ParseUtcText(priorityUntilUtc);
            return parsed > DateTime.MinValue ? ToUtcText(parsed) : "";
        }

        private static string NormalizePriorityUntilUtcText(
            ThumbnailQueuePriority priority,
            DateTime? priorityUntilUtc
        )
        {
            if (!ThumbnailQueuePriorityHelper.IsPreferred(priority) || !priorityUntilUtc.HasValue)
            {
                return "";
            }

            return ToUtcText(priorityUntilUtc.Value);
        }

        private static void BeginImmediateTransaction(SQLiteConnection connection)
        {
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = "BEGIN IMMEDIATE TRANSACTION;";
            command.ExecuteNonQuery();
        }

        // SQLiteの式木上限を踏まないように、削除条件は小さな塊へ分けて流す。
        private int DeleteMainFailureRecordBatch(
            SQLiteConnection connection,
            List<(string MoviePathKey, int TabIndex)> targets,
            int startIndex,
            int batchCount
        )
        {
            using SQLiteCommand command = connection.CreateCommand();
            StringBuilder predicateBuilder = new();

            for (int i = 0; i < batchCount; i++)
            {
                if (i > 0)
                {
                    predicateBuilder.Append(" OR ");
                }

                int targetIndex = startIndex + i;
                predicateBuilder.Append(
                    $"(MoviePathKey = @MoviePathKey{i} AND TabIndex = @TabIndex{i})"
                );
                command.Parameters.AddWithValue(
                    $"@MoviePathKey{i}",
                    targets[targetIndex].MoviePathKey
                );
                command.Parameters.AddWithValue($"@TabIndex{i}", targets[targetIndex].TabIndex);
            }

            command.CommandText = $@"
DELETE FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash
  AND {MainFailureLanePredicateSql}
  AND ({predicateBuilder});";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            return command.ExecuteNonQuery();
        }

        private static void CommitTransaction(SQLiteConnection connection)
        {
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = "COMMIT;";
            command.ExecuteNonQuery();
        }

        private static void RollbackTransaction(SQLiteConnection connection)
        {
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = "ROLLBACK;";
            command.ExecuteNonQuery();
        }
    }
}
