using System.Data.SQLite;
using System.Globalization;
using System.IO;

namespace IndigoMovieManager.Thumbnail.FailureDb
{
    // FailureDbの初期化とappend取得をまとめる。
    public sealed class ThumbnailFailureDbService
    {
        private const string UtcDateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
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
            this.mainDbPathHash = QueueDb.QueueDbPathResolver.GetMainDbPathHash8(mainDbFullPath);
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
                    selectCommand.CommandText = @"
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
    CreatedAtUtc,
    UpdatedAtUtc
FROM ThumbnailFailure
WHERE MainDbPathHash = @MainDbPathHash
  AND Lane = 'normal'
  AND (
      Status = 'pending_rescue'
      OR (Status = 'processing_rescue' AND LeaseUntilUtc <> '' AND LeaseUntilUtc < @NowUtc)
  )
ORDER BY UpdatedAtUtc ASC, FailureId ASC
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

        private SQLiteConnection OpenConnection()
        {
            SQLiteConnection connection = CreateConnection();
            connection.Open();
            ThumbnailFailureDbSchema.ApplyConnectionPragmas(connection);
            return connection;
        }

        private SQLiteConnection CreateConnection()
        {
            return new SQLiteConnection($"Data Source={failureDbFullPath}");
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
                CreatedAtUtc = ParseUtcText(reader.IsDBNull(21) ? "" : reader.GetString(21)),
                UpdatedAtUtc = ParseUtcText(reader.IsDBNull(22) ? "" : reader.GetString(22)),
            };
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
