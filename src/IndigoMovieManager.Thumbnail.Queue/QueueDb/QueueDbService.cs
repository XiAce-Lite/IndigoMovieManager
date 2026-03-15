using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Text;

namespace IndigoMovieManager.Thumbnail.QueueDb
{
    // キューの状態を数値で保存するための列挙体。
    public enum ThumbnailQueueStatus
    {
        Pending = 0,
        Processing = 1,
        Done = 2,
        Failed = 3,
        Skipped = 4,
    }

    // Producer/Persisterから受ける永続化要求。
    public sealed class QueueDbUpsertItem
    {
        public string MoviePath { get; set; } = "";
        public string MoviePathKey { get; set; } = "";
        public int TabIndex { get; set; }
        public long MovieSizeBytes { get; set; }
        public int? ThumbPanelPos { get; set; }
        public int? ThumbTimePos { get; set; }
    }

    // Upsert実行結果。投入件数と実際にDBへ反映された内訳を保持する。
    public sealed class QueueDbUpsertResult
    {
        public int SubmittedCount { get; set; }
        public int AffectedCount { get; set; }
        public int InsertedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int SkippedProcessingCount { get; set; }
    }

    // Consumerがリース取得後に処理するジョブ情報。
    public sealed class QueueDbLeaseItem
    {
        public long QueueId { get; set; }
        public string MoviePath { get; set; } = "";
        public string MoviePathKey { get; set; } = "";
        public int TabIndex { get; set; }
        public long MovieSizeBytes { get; set; }
        public int? ThumbPanelPos { get; set; }
        public int? ThumbTimePos { get; set; }
        public int AttemptCount { get; set; }
        public string OwnerInstanceId { get; set; } = "";
        public DateTime LeaseUntilUtc { get; set; }
    }

    // QueueDBに対するCRUDとリース制御をここへ集約する。
    public sealed class QueueDbService
    {
        private const string UtcDateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        private readonly object initializeLock = new();
        private readonly string mainDbFullPath;
        private readonly string queueDbFullPath;
        private readonly string mainDbPathHash;
        private bool isInitialized;

        public QueueDbService(string mainDbFullPath)
        {
            if (string.IsNullOrWhiteSpace(mainDbFullPath))
            {
                throw new ArgumentException("mainDbFullPath is required.", nameof(mainDbFullPath));
            }

            this.mainDbFullPath = mainDbFullPath;
            queueDbFullPath = QueueDbPathResolver.ResolveQueueDbPath(mainDbFullPath);
            mainDbPathHash = QueueDbPathResolver.GetMainDbPathHash8(mainDbFullPath);
        }

        public string MainDbFullPath => mainDbFullPath;
        public string QueueDbFullPath => queueDbFullPath;
        public string MainDbPathHash => mainDbPathHash;

        // 初回利用時にスキーマ作成まで完了させ、以後の呼び出しコストを抑える。
        public void EnsureInitialized()
        {
            if (isInitialized && !ShouldReinitializeQueueDb())
            {
                return;
            }

            lock (initializeLock)
            {
                if (isInitialized && !ShouldReinitializeQueueDb())
                {
                    return;
                }

                string directory = Path.GetDirectoryName(queueDbFullPath) ?? "";
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using SQLiteConnection connection = CreateConnection();
                connection.Open();
                QueueDbSchema.EnsureCreated(connection);
                isInitialized = true;
            }
        }

        // 実行中にQueueDBファイルが消された場合、既存serviceの初期化済み印だけでは追従できない。
        // 0byteや欠損を検知したら、次回利用時にスキーマを張り直して自力復旧する。
        private bool ShouldReinitializeQueueDb()
        {
            try
            {
                if (!File.Exists(queueDbFullPath))
                {
                    return true;
                }

                FileInfo fileInfo = new(queueDbFullPath);
                return fileInfo.Length <= 0;
            }
            catch
            {
                return true;
            }
        }

        // 追加要求をUPSERTし、既存行があればPendingへ戻して再処理可能にする。
        // 戻り値で「投入」「実反映」「新規」「更新」「Processing保護で未反映」を返す。
        public QueueDbUpsertResult Upsert(IEnumerable<QueueDbUpsertItem> items, DateTime utcNow)
        {
            EnsureInitialized();
            List<QueueDbUpsertItem> safeItems = items?.Where(x => x != null).ToList() ?? [];
            QueueDbUpsertResult result = new();
            if (safeItems.Count < 1) { return result; }

            string nowText = ToUtcText(utcNow);
            int processingStatus = (int)ThumbnailQueueStatus.Processing;
            int pendingStatus = (int)ThumbnailQueueStatus.Pending;

            using SQLiteConnection connection = OpenConnection();
            using SQLiteTransaction transaction = connection.BeginTransaction();

            using SQLiteCommand selectStatusCommand = connection.CreateCommand();
            selectStatusCommand.Transaction = transaction;
            selectStatusCommand.CommandText = @"
SELECT Status
FROM ThumbnailQueue
WHERE MainDbPathHash = @MainDbPathHash
  AND MoviePathKey = @MoviePathKey
  AND TabIndex = @TabIndex
LIMIT 1;";
            selectStatusCommand.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            selectStatusCommand.Parameters.AddWithValue("@MoviePathKey", "");
            selectStatusCommand.Parameters.AddWithValue("@TabIndex", 0);
            SQLiteParameter statusMoviePathKeyParameter = selectStatusCommand.Parameters["@MoviePathKey"];
            SQLiteParameter statusTabIndexParameter = selectStatusCommand.Parameters["@TabIndex"];

            using SQLiteCommand upsertCommand = connection.CreateCommand();
            upsertCommand.Transaction = transaction;
            upsertCommand.CommandText = @"
INSERT INTO ThumbnailQueue (
    MainDbPathHash,
    MoviePath,
    MoviePathKey,
    TabIndex,
    MovieSizeBytes,
    ThumbPanelPos,
    ThumbTimePos,
    Status,
    AttemptCount,
    LastError,
    OwnerInstanceId,
    LeaseUntilUtc,
    CreatedAtUtc,
    UpdatedAtUtc
) VALUES (
    @MainDbPathHash,
    @MoviePath,
    @MoviePathKey,
    @TabIndex,
    @MovieSizeBytes,
    @ThumbPanelPos,
    @ThumbTimePos,
    @Status,
    0,
    '',
    '',
    '',
    @NowUtc,
    @NowUtc
)
ON CONFLICT (MainDbPathHash, MoviePathKey, TabIndex)
DO UPDATE SET
    MoviePath = excluded.MoviePath,
    MovieSizeBytes = excluded.MovieSizeBytes,
    ThumbPanelPos = excluded.ThumbPanelPos,
    ThumbTimePos = excluded.ThumbTimePos,
    Status = @Status,
    AttemptCount = 0,
    LastError = '',
    OwnerInstanceId = '',
    LeaseUntilUtc = '',
    UpdatedAtUtc = @NowUtc
WHERE ThumbnailQueue.Status <> @Processing;";
            upsertCommand.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            upsertCommand.Parameters.AddWithValue("@MoviePath", "");
            upsertCommand.Parameters.AddWithValue("@MoviePathKey", "");
            upsertCommand.Parameters.AddWithValue("@TabIndex", 0);
            upsertCommand.Parameters.AddWithValue("@MovieSizeBytes", 0L);
            upsertCommand.Parameters.AddWithValue("@ThumbPanelPos", DBNull.Value);
            upsertCommand.Parameters.AddWithValue("@ThumbTimePos", DBNull.Value);
            upsertCommand.Parameters.AddWithValue("@Status", pendingStatus);
            upsertCommand.Parameters.AddWithValue("@Processing", processingStatus);
            upsertCommand.Parameters.AddWithValue("@NowUtc", nowText);
            SQLiteParameter upsertMoviePathParameter = upsertCommand.Parameters["@MoviePath"];
            SQLiteParameter upsertMoviePathKeyParameter = upsertCommand.Parameters["@MoviePathKey"];
            SQLiteParameter upsertTabIndexParameter = upsertCommand.Parameters["@TabIndex"];
            SQLiteParameter upsertMovieSizeBytesParameter =
                upsertCommand.Parameters["@MovieSizeBytes"];
            SQLiteParameter upsertThumbPanelPosParameter = upsertCommand.Parameters["@ThumbPanelPos"];
            SQLiteParameter upsertThumbTimePosParameter = upsertCommand.Parameters["@ThumbTimePos"];

            foreach (QueueDbUpsertItem item in safeItems)
            {
                string moviePath = item.MoviePath ?? "";
                if (string.IsNullOrWhiteSpace(moviePath)) { continue; }
                result.SubmittedCount++;

                string moviePathKey = string.IsNullOrWhiteSpace(item.MoviePathKey)
                    ? QueueDbPathResolver.CreateMoviePathKey(moviePath)
                    : item.MoviePathKey;

                statusMoviePathKeyParameter.Value = moviePathKey;
                statusTabIndexParameter.Value = item.TabIndex;
                object statusObject = selectStatusCommand.ExecuteScalar();
                bool existsBeforeUpsert = statusObject != null && statusObject != DBNull.Value;
                int existingStatus = existsBeforeUpsert ? Convert.ToInt32(statusObject, CultureInfo.InvariantCulture) : -1;

                upsertMoviePathParameter.Value = moviePath;
                upsertMoviePathKeyParameter.Value = moviePathKey;
                upsertTabIndexParameter.Value = item.TabIndex;
                upsertMovieSizeBytesParameter.Value = Math.Max(0, item.MovieSizeBytes);
                upsertThumbPanelPosParameter.Value = item.ThumbPanelPos.HasValue
                    ? item.ThumbPanelPos.Value
                    : (object)DBNull.Value;
                upsertThumbTimePosParameter.Value = item.ThumbTimePos.HasValue
                    ? item.ThumbTimePos.Value
                    : (object)DBNull.Value;
                int affected = upsertCommand.ExecuteNonQuery();

                if (affected > 0)
                {
                    result.AffectedCount += affected;
                    if (existsBeforeUpsert)
                    {
                        result.UpdatedCount += affected;
                    }
                    else
                    {
                        result.InsertedCount += affected;
                    }
                    continue;
                }

                if (existsBeforeUpsert && existingStatus == processingStatus)
                {
                    result.SkippedProcessingCount++;
                }
            }

            transaction.Commit();
            return result;
        }

        // Pendingと期限切れProcessingを取得し、同一トランザクション内でリースを付与する。
        public List<QueueDbLeaseItem> GetPendingAndLease(
            string ownerInstanceId,
            int takeCount,
            TimeSpan leaseDuration,
            DateTime utcNow,
            int? preferredTabIndex = null,
            IReadOnlyList<string> preferredMoviePathKeys = null)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(ownerInstanceId))
            {
                throw new ArgumentException("ownerInstanceId is required.", nameof(ownerInstanceId));
            }
            if (takeCount < 1) { return []; }

            string nowText = ToUtcText(utcNow);
            string leaseUntilText = ToUtcText(utcNow.Add(leaseDuration));
            List<QueueDbLeaseItem> leasedItems = [];
            List<string> normalizedPreferredMoviePathKeys = NormalizePreferredMoviePathKeys(
                preferredMoviePathKeys
            );

            using SQLiteConnection connection = OpenConnection();
            BeginImmediateTransaction(connection);

            try
            {
                using (SQLiteCommand selectCommand = connection.CreateCommand())
                {
                    // ユーザーが見ているタブを優先しつつ、同順位はFIFOで取得する。
                    string preferredMoviePathOrderSql = BuildPreferredMoviePathOrderSql(
                        normalizedPreferredMoviePathKeys
                    );
                    selectCommand.CommandText = $@"
SELECT
    QueueId,
    MoviePath,
    MoviePathKey,
    TabIndex,
    MovieSizeBytes,
    ThumbPanelPos,
    ThumbTimePos,
    AttemptCount
FROM ThumbnailQueue
WHERE MainDbPathHash = @MainDbPathHash
  AND (
      Status = @Pending
      OR (Status = @Processing AND LeaseUntilUtc <> '' AND LeaseUntilUtc < @NowUtc)
  )
ORDER BY
    CASE
        WHEN @HasPreferredTab = 1 AND TabIndex = @PreferredTab THEN 0
        ELSE 1
    END ASC,
    {preferredMoviePathOrderSql}
    CreatedAtUtc ASC
LIMIT @TakeCount;";
                    selectCommand.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
                    selectCommand.Parameters.AddWithValue("@Pending", (int)ThumbnailQueueStatus.Pending);
                    selectCommand.Parameters.AddWithValue("@Processing", (int)ThumbnailQueueStatus.Processing);
                    selectCommand.Parameters.AddWithValue("@NowUtc", nowText);
                    selectCommand.Parameters.AddWithValue("@TakeCount", takeCount);
                    selectCommand.Parameters.AddWithValue("@HasPreferredTab", preferredTabIndex.HasValue ? 1 : 0);
                    selectCommand.Parameters.AddWithValue("@PreferredTab", preferredTabIndex ?? -1);
                    selectCommand.Parameters.AddWithValue(
                        "@HasPreferredMoviePathKeys",
                        normalizedPreferredMoviePathKeys.Count > 0 ? 1 : 0
                    );
                    for (int i = 0; i < normalizedPreferredMoviePathKeys.Count; i++)
                    {
                        selectCommand.Parameters.AddWithValue(
                            $"@PreferredMoviePathKey{i}",
                            normalizedPreferredMoviePathKeys[i]
                        );
                    }

                    using SQLiteDataReader reader = selectCommand.ExecuteReader();
                    while (reader.Read())
                    {
                        QueueDbLeaseItem leaseItem = new()
                        {
                            QueueId = reader.GetInt64(0),
                            MoviePath = reader.GetString(1),
                            MoviePathKey = reader.GetString(2),
                            TabIndex = reader.GetInt32(3),
                            MovieSizeBytes = reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
                            ThumbPanelPos = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                            ThumbTimePos = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                            AttemptCount = reader.GetInt32(7),
                            OwnerInstanceId = ownerInstanceId,
                            LeaseUntilUtc = utcNow.Add(leaseDuration),
                        };
                        leasedItems.Add(leaseItem);
                    }
                }

                if (leasedItems.Count > 0)
                {
                    using SQLiteCommand leaseCommand = connection.CreateCommand();
                    leaseCommand.CommandText = @"
UPDATE ThumbnailQueue
SET
    Status = @Processing,
    OwnerInstanceId = @OwnerInstanceId,
    LeaseUntilUtc = @LeaseUntilUtc,
    UpdatedAtUtc = @NowUtc
WHERE QueueId = @QueueId
  AND MainDbPathHash = @MainDbPathHash;";
                    leaseCommand.Parameters.AddWithValue("@Processing", (int)ThumbnailQueueStatus.Processing);
                    leaseCommand.Parameters.AddWithValue("@OwnerInstanceId", ownerInstanceId);
                    leaseCommand.Parameters.AddWithValue("@LeaseUntilUtc", leaseUntilText);
                    leaseCommand.Parameters.AddWithValue("@NowUtc", nowText);
                    leaseCommand.Parameters.AddWithValue("@QueueId", 0L);
                    leaseCommand.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);

                    SQLiteParameter queueIdParameter = leaseCommand.Parameters["@QueueId"];
                    foreach (QueueDbLeaseItem leaseItem in leasedItems)
                    {
                        queueIdParameter.Value = leaseItem.QueueId;
                        leaseCommand.ExecuteNonQuery();
                    }
                }

                CommitTransaction(connection);
                return leasedItems;
            }
            catch
            {
                RollbackTransaction(connection);
                throw;
            }
        }

        private static List<string> NormalizePreferredMoviePathKeys(
            IReadOnlyList<string> preferredMoviePathKeys
        )
        {
            List<string> result = [];
            if (preferredMoviePathKeys == null || preferredMoviePathKeys.Count < 1)
            {
                return result;
            }

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < preferredMoviePathKeys.Count; i++)
            {
                string normalized = QueueDbPathResolver.CreateMoviePathKey(preferredMoviePathKeys[i]);
                if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
                {
                    continue;
                }

                result.Add(normalized);
                if (result.Count >= 128)
                {
                    break;
                }
            }

            return result;
        }

        private static string BuildPreferredMoviePathOrderSql(
            IReadOnlyList<string> preferredMoviePathKeys
        )
        {
            if (preferredMoviePathKeys == null || preferredMoviePathKeys.Count < 1)
            {
                return "CASE WHEN 1 = 1 THEN 0 END ASC,";
            }

            StringBuilder sql = new();
            sql.AppendLine("CASE");
            for (int i = 0; i < preferredMoviePathKeys.Count; i++)
            {
                sql.AppendLine(
                    $"        WHEN @HasPreferredMoviePathKeys = 1 AND @HasPreferredTab = 1 AND TabIndex = @PreferredTab AND MoviePathKey = @PreferredMoviePathKey{i} THEN {i}"
                );
            }

            sql.AppendLine(
                $"        WHEN @HasPreferredTab = 1 AND TabIndex = @PreferredTab THEN {preferredMoviePathKeys.Count}"
            );
            sql.AppendLine($"        ELSE {preferredMoviePathKeys.Count + 1}");
            sql.Append("    END ASC,");
            return sql.ToString();
        }

        // 進捗ダイアログ表示判定用に、未完了キュー件数を返す。
        // Pending は全体対象、Processing はこのインスタンス所有分のみを数える。
        public int GetActiveQueueCount(string ownerInstanceId)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(ownerInstanceId))
            {
                throw new ArgumentException("ownerInstanceId is required.", nameof(ownerInstanceId));
            }

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(1)
FROM ThumbnailQueue
WHERE MainDbPathHash = @MainDbPathHash
  AND (
      Status = @Pending
      OR (Status = @Processing AND OwnerInstanceId = @OwnerInstanceId)
  );";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@Pending", (int)ThumbnailQueueStatus.Pending);
            command.Parameters.AddWithValue("@Processing", (int)ThumbnailQueueStatus.Processing);
            command.Parameters.AddWithValue("@OwnerInstanceId", ownerInstanceId);
            object value = command.ExecuteScalar();
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        // 処理結果に応じて状態を更新し、リース所有者が一致する場合のみ反映する。
        // 戻り値0は「リース喪失や対象不在で更新されなかった」ことを示す。
        public int UpdateStatus(
            long queueId,
            string ownerInstanceId,
            ThumbnailQueueStatus status,
            DateTime utcNow,
            string lastError = "",
            bool incrementAttemptCount = false)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(ownerInstanceId))
            {
                throw new ArgumentException("ownerInstanceId is required.", nameof(ownerInstanceId));
            }

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
UPDATE ThumbnailQueue
SET
    Status = @Status,
    AttemptCount = CASE WHEN @IncrementAttempt = 1 THEN AttemptCount + 1 ELSE AttemptCount END,
    LastError = @LastError,
    OwnerInstanceId = '',
    LeaseUntilUtc = '',
    UpdatedAtUtc = @NowUtc
WHERE QueueId = @QueueId
  AND MainDbPathHash = @MainDbPathHash
  AND Status = @Processing
  AND OwnerInstanceId = @OwnerInstanceId;";
            command.Parameters.AddWithValue("@Status", (int)status);
            command.Parameters.AddWithValue("@IncrementAttempt", incrementAttemptCount ? 1 : 0);
            command.Parameters.AddWithValue("@LastError", lastError ?? "");
            command.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));
            command.Parameters.AddWithValue("@QueueId", queueId);
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@Processing", (int)ThumbnailQueueStatus.Processing);
            command.Parameters.AddWithValue("@OwnerInstanceId", ownerInstanceId);
            return command.ExecuteNonQuery();
        }

        // 処理中ジョブのリース期限を延長し、他プロセスへの奪取を防ぐ。
        public void ExtendLease(long queueId, string ownerInstanceId, DateTime leaseUntilUtc, DateTime utcNow)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(ownerInstanceId))
            {
                throw new ArgumentException("ownerInstanceId is required.", nameof(ownerInstanceId));
            }

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
UPDATE ThumbnailQueue
SET
    LeaseUntilUtc = @LeaseUntilUtc,
    UpdatedAtUtc = @NowUtc
WHERE QueueId = @QueueId
  AND MainDbPathHash = @MainDbPathHash
  AND Status = @Processing
  AND OwnerInstanceId = @OwnerInstanceId;";
            command.Parameters.AddWithValue("@LeaseUntilUtc", ToUtcText(leaseUntilUtc));
            command.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));
            command.Parameters.AddWithValue("@QueueId", queueId);
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@Processing", (int)ThumbnailQueueStatus.Processing);
            command.Parameters.AddWithValue("@OwnerInstanceId", ownerInstanceId);
            command.ExecuteNonQuery();
        }

        // 手動再試行用に、FailedジョブをPendingへ戻す。
        public int ResetFailedToPending(DateTime utcNow)
        {
            EnsureInitialized();

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
UPDATE ThumbnailQueue
SET
    Status = @Pending,
    AttemptCount = 0,
    LastError = '',
    OwnerInstanceId = '',
    LeaseUntilUtc = '',
    UpdatedAtUtc = @NowUtc
WHERE MainDbPathHash = @MainDbPathHash
  AND Status = @Failed;";
            command.Parameters.AddWithValue("@Pending", (int)ThumbnailQueueStatus.Pending);
            command.Parameters.AddWithValue("@Failed", (int)ThumbnailQueueStatus.Failed);
            command.Parameters.AddWithValue("@NowUtc", ToUtcText(utcNow));
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            return command.ExecuteNonQuery();
        }

        // DB切り替え後に旧QueueDBへ残った未着手ジョブだけを捨てる。
        public int DeletePending()
        {
            EnsureInitialized();

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM ThumbnailQueue
WHERE MainDbPathHash = @MainDbPathHash
  AND Status = @Pending;";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@Pending", (int)ThumbnailQueueStatus.Pending);
            return command.ExecuteNonQuery();
        }

        // Debug運用用に、現在QueueDBの全レコードを空にする。
        public int ClearAll()
        {
            EnsureInitialized();

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM ThumbnailQueue
WHERE MainDbPathHash = @MainDbPathHash;";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            return command.ExecuteNonQuery();
        }

        // Done履歴の肥大化を防ぐため、指定ローカル日付より前の完了行を削除する。
        // cutoffLocalDateStart には「当日00:00(ローカル)」を渡す運用を想定する。
        public int DeleteDoneOlderThan(DateTime cutoffLocalDateStart)
        {
            EnsureInitialized();

            DateTime localDateStart = cutoffLocalDateStart.Kind switch
            {
                DateTimeKind.Utc => cutoffLocalDateStart.ToLocalTime().Date,
                DateTimeKind.Local => cutoffLocalDateStart.Date,
                _ => DateTime.SpecifyKind(cutoffLocalDateStart, DateTimeKind.Local).Date,
            };

            using SQLiteConnection connection = OpenConnection();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM ThumbnailQueue
WHERE MainDbPathHash = @MainDbPathHash
  AND Status = @Done
  AND UpdatedAtUtc <> ''
  AND UpdatedAtUtc < @CutoffUtc;";
            command.Parameters.AddWithValue("@MainDbPathHash", mainDbPathHash);
            command.Parameters.AddWithValue("@Done", (int)ThumbnailQueueStatus.Done);
            command.Parameters.AddWithValue("@CutoffUtc", ToUtcText(localDateStart));
            return command.ExecuteNonQuery();
        }

        private SQLiteConnection OpenConnection()
        {
            SQLiteConnection connection = CreateConnection();
            connection.Open();
            QueueDbSchema.ApplyConnectionPragmas(connection);
            return connection;
        }

        private SQLiteConnection CreateConnection()
        {
            return new SQLiteConnection($"Data Source={queueDbFullPath}");
        }

        private static string ToUtcText(DateTime dateTime)
        {
            return dateTime.ToUniversalTime().ToString(UtcDateFormat, CultureInfo.InvariantCulture);
        }

        private static void BeginImmediateTransaction(SQLiteConnection connection)
        {
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = "BEGIN IMMEDIATE TRANSACTION;";
            command.ExecuteNonQuery();
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
