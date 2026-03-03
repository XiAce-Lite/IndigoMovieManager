using System;
using System.Data.SQLite;
using System.Threading;

namespace IndigoMovieManager.Thumbnail.QueueDb
{
    // QueueDBのスキーマとPRAGMA設定を、初期化時に一括適用する。
    public static class QueueDbSchema
    {
        private const string CreateTableSql = @"
CREATE TABLE IF NOT EXISTS ThumbnailQueue (
    QueueId INTEGER PRIMARY KEY AUTOINCREMENT,
    MainDbPathHash TEXT NOT NULL,
    MoviePath TEXT NOT NULL,
    MoviePathKey TEXT NOT NULL,
    TabIndex INTEGER NOT NULL,
    MovieSizeBytes INTEGER NOT NULL DEFAULT 0,
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
        private const string AddMovieSizeColumnSql = @"
ALTER TABLE ThumbnailQueue
ADD COLUMN MovieSizeBytes INTEGER NOT NULL DEFAULT 0;";

        private const string CreateIndexStatusLeaseSql = @"
CREATE INDEX IF NOT EXISTS IX_ThumbnailQueue_Status_Lease
ON ThumbnailQueue (Status, LeaseUntilUtc, CreatedAtUtc);";

        private const string CreateIndexMainDbSql = @"
CREATE INDEX IF NOT EXISTS IX_ThumbnailQueue_MainDb
ON ThumbnailQueue (MainDbPathHash, Status, CreatedAtUtc);";

        private const string CreateIndexDoneRetentionSql = @"
CREATE INDEX IF NOT EXISTS IX_ThumbnailQueue_DoneRetention
ON ThumbnailQueue (MainDbPathHash, Status, UpdatedAtUtc);";

        // スキーマ作成の入り口。接続単位でPRAGMAを先に適用してからDDLを流す。
        public static void EnsureCreated(SQLiteConnection connection)
        {
            ApplyConnectionPragmas(connection);
            EnsureWalMode(connection);
            ExecuteNonQuery(connection, CreateTableSql);
            EnsureColumnExists(
                connection,
                "ThumbnailQueue",
                "MovieSizeBytes",
                AddMovieSizeColumnSql
            );
            ExecuteNonQuery(connection, CreateIndexStatusLeaseSql);
            ExecuteNonQuery(connection, CreateIndexMainDbSql);
            ExecuteNonQuery(connection, CreateIndexDoneRetentionSql);
        }

        // QueueDB接続ごとに必要なPRAGMAを適用する。
        // journal_mode はDB永続設定なので、毎接続では変更しない。
        public static void ApplyConnectionPragmas(SQLiteConnection connection)
        {
            ExecuteNonQuery(connection, "PRAGMA busy_timeout=5000;");
            ExecuteNonQuery(connection, "PRAGMA synchronous=NORMAL;");
        }

        // 互換用: 旧呼び出しを残しつつ内部を新構成へ委譲する。
        public static void ApplyPragmas(SQLiteConnection connection)
        {
            ApplyConnectionPragmas(connection);
            EnsureWalMode(connection);
        }

        // WAL切替はDBファイル単位で永続化されるため、初期化時にのみ実施する。
        // 他プロセス書き込みと競合した場合は短いリトライで吸収する。
        private static void EnsureWalMode(SQLiteConnection connection)
        {
            // 既にWALなら再設定しない。不要なPRAGMA発行を減らしてロック競合を避ける。
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
                        // 他プロセスのトランザクション競合時は、接続単位設定だけ適用して続行する。
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

        // 既存QueueDBに対して列追加を後方互換で適用する。
        private static void EnsureColumnExists(
            SQLiteConnection connection,
            string tableName,
            string columnName,
            string alterSql
        )
        {
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName});";
            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string existingName = Convert.ToString(reader["name"]) ?? "";
                if (string.Equals(existingName, columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            ExecuteNonQuery(connection, alterSql);
        }
    }
}
