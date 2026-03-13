using System;
using System.Data.SQLite;
using System.Threading;

namespace IndigoMovieManager.Thumbnail.FailureDb
{
    // FailureDbの最小スキーマと接続設定をここへ集約する。
    public static class ThumbnailFailureDbSchema
    {
        private const string CreateTableSql = @"
CREATE TABLE IF NOT EXISTS ThumbnailFailure (
    FailureId INTEGER PRIMARY KEY AUTOINCREMENT,
    MainDbFullPath TEXT NOT NULL DEFAULT '',
    MainDbPathHash TEXT NOT NULL DEFAULT '',
    MoviePath TEXT NOT NULL DEFAULT '',
    MoviePathKey TEXT NOT NULL DEFAULT '',
    TabIndex INTEGER NOT NULL DEFAULT 0,
    Lane TEXT NOT NULL DEFAULT '',
    AttemptGroupId TEXT NOT NULL DEFAULT '',
    AttemptNo INTEGER NOT NULL DEFAULT 0,
    Status TEXT NOT NULL DEFAULT '',
    LeaseOwner TEXT NOT NULL DEFAULT '',
    LeaseUntilUtc TEXT NOT NULL DEFAULT '',
    Engine TEXT NOT NULL DEFAULT '',
    FailureKind TEXT NOT NULL DEFAULT 'Unknown',
    FailureReason TEXT NOT NULL DEFAULT '',
    ElapsedMs INTEGER NOT NULL DEFAULT 0,
    SourcePath TEXT NOT NULL DEFAULT '',
    OutputThumbPath TEXT NOT NULL DEFAULT '',
    RepairApplied INTEGER NOT NULL DEFAULT 0,
    ResultSignature TEXT NOT NULL DEFAULT '',
    ExtraJson TEXT NOT NULL DEFAULT '',
    CreatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
    UpdatedAtUtc TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now'))
);";

        private const string CreateIndexStatusSql = @"
CREATE INDEX IF NOT EXISTS IX_ThumbnailFailure_MainDb_Status_Updated
ON ThumbnailFailure (MainDbPathHash, Status, UpdatedAtUtc DESC, FailureId DESC);";

        private const string CreateIndexMovieSql = @"
CREATE INDEX IF NOT EXISTS IX_ThumbnailFailure_MainDb_Movie_Created
ON ThumbnailFailure (MainDbPathHash, MoviePathKey, CreatedAtUtc DESC, FailureId DESC);";

        public static void EnsureCreated(SQLiteConnection connection)
        {
            ApplyConnectionPragmas(connection);
            EnsureWalMode(connection);
            ExecuteNonQuery(connection, CreateTableSql);
            ExecuteNonQuery(connection, CreateIndexStatusSql);
            ExecuteNonQuery(connection, CreateIndexMovieSql);
        }

        public static void ApplyConnectionPragmas(SQLiteConnection connection)
        {
            ExecuteNonQuery(connection, "PRAGMA busy_timeout=5000;");
            ExecuteNonQuery(connection, "PRAGMA synchronous=NORMAL;");
        }

        // FailureDbもQueueDbと同じくWALを使うが、責務を明確にするため実装はここへ閉じる。
        private static void EnsureWalMode(SQLiteConnection connection)
        {
            string currentMode = ReadJournalMode(connection);
            if (string.Equals(currentMode, "wal", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            const int maxRetryCount = 3;
            const int retryDelayMs = 100;

            for (int attempt = 1; attempt <= maxRetryCount; attempt++)
            {
                try
                {
                    using SQLiteCommand command = connection.CreateCommand();
                    command.CommandText = "PRAGMA journal_mode=WAL;";
                    object result = command.ExecuteScalar();
                    string mode = Convert.ToString(result) ?? "";
                    if (string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
                catch (SQLiteException ex) when (IsBusyOrLocked(ex))
                {
                    if (attempt >= maxRetryCount)
                    {
                        return;
                    }

                    Thread.Sleep(retryDelayMs);
                }
            }
        }

        private static string ReadJournalMode(SQLiteConnection connection)
        {
            try
            {
                using SQLiteCommand command = connection.CreateCommand();
                command.CommandText = "PRAGMA journal_mode;";
                object result = command.ExecuteScalar();
                return Convert.ToString(result) ?? "";
            }
            catch (SQLiteException ex) when (IsBusyOrLocked(ex))
            {
                return "";
            }
        }

        private static bool IsBusyOrLocked(SQLiteException exception)
        {
            string message = exception?.Message ?? "";
            return message.IndexOf("database is locked", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("database is busy", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ExecuteNonQuery(SQLiteConnection connection, string sql)
        {
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
