using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

// ※このファイルは「実装前」にテストを書くために必要な仮のクラス定義です。
// テスト専用のネームスペース下で動作し、本番コードのI/F（設計書）を表現します。

namespace IndigoMovieManager.Thumbnail.Test
{
    public static class QueueDbPathResolver
    {
        public static string ResolvePath(string mainDbPath)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var inputBytes = System.Text.Encoding.UTF8.GetBytes(mainDbPath.ToLowerInvariant());
                var hashBytes = sha256.ComputeHash(inputBytes);
                var hash8 = BitConverter
                    .ToString(hashBytes, 0, 4)
                    .Replace("-", "")
                    .ToLowerInvariant();

                string mainDbName = Path.GetFileNameWithoutExtension(mainDbPath);
                string localApp = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData
                );
                string dir = Path.Combine(localApp, "IndigoMovieManager", "QueueDb");

                // 本番コード側では Directory.CreateDirectory(dir) される想定
                return Path.Combine(dir, $"{mainDbName}.{hash8}.queue.imm");
            }
        }
    }

    public static class QueueDbSchema
    {
        public static void EnsureSchemaCreated(string connectionString)
        {
            using (var conn = new SQLiteConnection(connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA journal_mode=WAL;";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = "PRAGMA synchronous=NORMAL;";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText = "PRAGMA busy_timeout=5000;";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText =
                        @"
CREATE TABLE IF NOT EXISTS ThumbnailQueue (
    QueueId INTEGER PRIMARY KEY AUTOINCREMENT,
    MainDbPathHash TEXT NOT NULL,
    MoviePath TEXT NOT NULL,
    MoviePathKey TEXT NOT NULL,
    TabIndex INTEGER NOT NULL,
    ThumbPanelPos INTEGER,
    ThumbTimePos INTEGER,
    Status INTEGER NOT NULL DEFAULT 0,
    AttemptCount INTEGER NOT NULL DEFAULT 0,
    LastError TEXT NOT NULL DEFAULT '',
    OwnerInstanceId TEXT NOT NULL DEFAULT '',
    LeaseUntilUtc TEXT NOT NULL DEFAULT '',
    CreatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    UpdatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    UNIQUE (MainDbPathHash, MoviePathKey, TabIndex)
);";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText =
                        @"
CREATE INDEX IF NOT EXISTS IX_ThumbnailQueue_Status_Lease
ON ThumbnailQueue (Status, LeaseUntilUtc, CreatedAtUtc);";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText =
                        @"
CREATE INDEX IF NOT EXISTS IX_ThumbnailQueue_MainDb
ON ThumbnailQueue (MainDbPathHash, Status, CreatedAtUtc);";
                    cmd.ExecuteNonQuery();

                    cmd.CommandText =
                        @"
CREATE INDEX IF NOT EXISTS IX_ThumbnailQueue_DoneRetention
ON ThumbnailQueue (MainDbPathHash, Status, UpdatedAtUtc);";
                    cmd.ExecuteNonQuery();
                }
            }
        }
    }

    public class QueueDbService
    {
        private readonly string _connectionString;

        public QueueDbService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public int UpsertBatch(IEnumerable<QueueRequest> requests)
        {
            int count = 0;
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction())
                {
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText =
                            @"
INSERT INTO ThumbnailQueue (MainDbPathHash, MoviePath, MoviePathKey, TabIndex, ThumbPanelPos, ThumbTimePos, Status, UpdatedAtUtc)
VALUES (@hash, @path, @key, @tab, @ppos, @tpos, 0, strftime('%Y-%m-%dT%H:%M:%fZ','now'))
ON CONFLICT (MainDbPathHash, MoviePathKey, TabIndex) 
DO UPDATE SET Status = 0, UpdatedAtUtc = strftime('%Y-%m-%dT%H:%M:%fZ','now');
";
                        cmd.Parameters.Add(new SQLiteParameter("@hash"));
                        cmd.Parameters.Add(new SQLiteParameter("@path"));
                        cmd.Parameters.Add(new SQLiteParameter("@key"));
                        cmd.Parameters.Add(new SQLiteParameter("@tab"));
                        cmd.Parameters.Add(new SQLiteParameter("@ppos"));
                        cmd.Parameters.Add(new SQLiteParameter("@tpos"));
                        cmd.Prepare();

                        foreach (var req in requests)
                        {
                            cmd.Parameters["@hash"].Value = "HASH_" + req.MainDbPath.GetHashCode();
                            cmd.Parameters["@path"].Value = req.MoviePath;
                            cmd.Parameters["@key"].Value = req.MoviePath.ToLowerInvariant();
                            cmd.Parameters["@tab"].Value = req.TabIndex;
                            cmd.Parameters["@ppos"].Value = req.ThumbPanelPos;
                            cmd.Parameters["@tpos"].Value = req.ThumbTimePos;

                            count += cmd.ExecuteNonQuery();
                        }
                    }
                    tx.Commit();
                }
            }
            return count;
        }

        public List<QueueRecord> GetPendingAndLease(string instanceId, int count, int leaseMinutes)
        {
            var results = new List<QueueRecord>();
            string expiryTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            string newLeaseTime = DateTime
                .UtcNow.AddMinutes(leaseMinutes)
                .ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var tx = conn.BeginTransaction(System.Data.IsolationLevel.Serializable))
                {
                    // 排他用 BEGIN IMMEDIATE TRANSACTION 相当
                    using (var cmdLock = conn.CreateCommand())
                    {
                        cmdLock.CommandText = "UPDATE sqlite_master SET name=name WHERE 1=0;";
                        cmdLock.ExecuteNonQuery();
                    }

                    var tempIds = new List<long>();

                    // TODO: 本番では CreatedAtUtc 順
                    using (var cmdSel = conn.CreateCommand())
                    {
                        cmdSel.CommandText =
                            @"
SELECT QueueId FROM ThumbnailQueue 
WHERE Status = 0 OR (Status = 1 AND LeaseUntilUtc < @now)
ORDER BY QueueId ASC LIMIT @limit;";
                        cmdSel.Parameters.AddWithValue("@now", expiryTime);
                        cmdSel.Parameters.AddWithValue("@limit", count);

                        using (var reader = cmdSel.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                tempIds.Add(reader.GetInt64(0));
                            }
                        }
                    }

                    foreach (var qid in tempIds)
                    {
                        using (var cmdUpd = conn.CreateCommand())
                        {
                            cmdUpd.CommandText =
                                @"
UPDATE ThumbnailQueue SET Status = 1, OwnerInstanceId = @ins, LeaseUntilUtc = @lease
WHERE QueueId = @qid;";
                            cmdUpd.Parameters.AddWithValue("@ins", instanceId);
                            cmdUpd.Parameters.AddWithValue("@lease", newLeaseTime);
                            cmdUpd.Parameters.AddWithValue("@qid", qid);
                            cmdUpd.ExecuteNonQuery();
                        }

                        // 返却用データ再取得 (簡略化)
                        results.Add(
                            new QueueRecord
                            {
                                QueueId = qid,
                                Status = 1,
                                OwnerInstanceId = instanceId,
                                LeaseUntilUtc = DateTime.UtcNow.AddMinutes(leaseMinutes),
                            }
                        );
                    }

                    tx.Commit();
                }
            }

            return results;
        }

        public void UpdateStatus(long queueId, int status, string errorMsg)
        {
            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"
UPDATE ThumbnailQueue 
SET Status = @status, LastError = @err, UpdatedAtUtc = strftime('%Y-%m-%dT%H:%M:%fZ','now')
WHERE QueueId = @id;";
                    cmd.Parameters.AddWithValue("@status", status);
                    cmd.Parameters.AddWithValue("@err", errorMsg ?? "");
                    cmd.Parameters.AddWithValue("@id", queueId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public int DeleteDoneOlderThan(DateTime cutoffLocalDateStart)
        {
            DateTime localDateStart = DateTime.SpecifyKind(
                cutoffLocalDateStart.Date,
                DateTimeKind.Local
            );
            string cutoffUtc = localDateStart
                .ToUniversalTime()
                .ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            using (var conn = new SQLiteConnection(_connectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"
DELETE FROM ThumbnailQueue
WHERE Status = 2
  AND UpdatedAtUtc <> ''
  AND UpdatedAtUtc < @cutoff;";
                    cmd.Parameters.AddWithValue("@cutoff", cutoffUtc);
                    return cmd.ExecuteNonQuery();
                }
            }
        }
    }
}
