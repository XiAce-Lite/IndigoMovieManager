using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.SQLite;

namespace IndigoMovieManager.DB
{
    /// <summary>
    /// SQLiteデータベースとの熱い語らい（CRUD処理）を全て引き受ける最強の裏方クラス！🛡️
    /// テーブルの初期化からレコードの追加・更新・削除まで、データの命運はこいつが握ってるぜ！🔥
    /// </summary>
    internal class SQLite
    {
        private static readonly string[] DbDateTimeAcceptedFormats =
        [
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss.FFFFFFF",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
            "yyyy/MM/dd HH:mm:ss",
            "yyyy/MM/dd H:mm:ss",
            "yyyy/M/d HH:mm:ss",
            "yyyy/M/d H:mm:ss"
        ];

        private const int SinkuNameByteLength = 512;
        private const int SinkuContainerByteLength = 32;
        private const int SinkuTextByteLength = 512;
        private const int SinkuVideoStreamMaxCount = 10;
        private const int SinkuExtraStreamMaxCount = 5;
        private const int SinkuFileTypeErrorEnd = 3;
        private const int SinkuOffsetType = SinkuNameByteLength;
        private const int SinkuOffsetContainer = SinkuOffsetType + sizeof(int);
        private const int SinkuOffsetError = SinkuOffsetContainer + SinkuContainerByteLength;
        private const int SinkuOffsetVideo = SinkuOffsetError + SinkuTextByteLength;
        private const int SinkuOffsetVideoCount =
            SinkuOffsetVideo + (SinkuTextByteLength * SinkuVideoStreamMaxCount);
        private const int SinkuOffsetAudio = SinkuOffsetVideoCount + sizeof(int);
        private const int SinkuOffsetAudioCount =
            SinkuOffsetAudio + (SinkuTextByteLength * SinkuVideoStreamMaxCount);
        private const int SinkuOffsetExtra = SinkuOffsetAudioCount + sizeof(int);
        private const int SinkuOffsetExtraCount =
            SinkuOffsetExtra + (SinkuTextByteLength * SinkuExtraStreamMaxCount);
        private const int SinkuOffsetPlaytime = SinkuOffsetExtraCount + sizeof(int);
        private const int SinkuFileInfoByteLength =
            SinkuOffsetPlaytime + sizeof(double) + sizeof(ulong);

        static SQLite()
        {
            // CP932文字列を扱うため、CodePagesプロバイダを有効化する。
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private static readonly HashSet<string> AllowedMovieColumns =
        [
            "movie_path",
            "movie_name",
            "kana",
            "roma",
            "tag",
            "score",
            "view_count",
            "last_date",
            "movie_length",
        ];

        // Sinku.dll の読み込みに致命的な失敗が出たら、同一プロセス中は再試行しない。
        private static int _sinkuDisabledForProcess = 0;

        // watch-checkの重い1件をDB側でも追跡する。
        private const string DbInsertProbeMovieIdentity = "MH922SNIgTs_gggggggggg.mkv";
        private const long DbInsertProbeSlowThresholdMs = 200;
        private const int MainDbDefaultTimeoutSec = 5;
        private const int MainDbBusyTimeoutMs = 5000;
        private const int UncSchemaValidationRetryBudgetMs = 4000;
        private const int UncSchemaValidationRetryDelayMs = 500;
        // TODO 2026-04-11: DBエラー連鎖の根本原因を潰したら false に戻し、popup を復帰する。
        // 復帰条件の整理は DB/Implementation Note_DBエラーダイアログ一時抑止_2026-04-11.md を参照。
        private static readonly bool SuppressDbErrorDialogTemporarily = true;

        /// <summary>
        /// 指定されたSQLクエリをブン回し、結果をDataTableとしてガッチリ返すぜ！
        /// 読み取り（SELECT）専用の汎用データ抽出マシーンだ！🔍
        /// </summary>
        /// <param name="dbFullPath">DBファイルのフルパス</param>
        /// <param name="sql">実行するSELECT文</param>
        public static DataTable GetData(string dbFullPath, string sql)
        {
            try
            {
                DataTable dt = new();
                using (SQLiteConnection connection = CreateReadOnlyConnection(dbFullPath))
                {
                    connection.Open();

                    using SQLiteCommand cmd = connection.CreateCommand();
                    cmd.CommandText = sql;

                    SQLiteDataAdapter da = new(cmd);

                    da.Fill(dt);
                }
                return dt;
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }
            return null;
        }

        private static void ReportDbError(Exception exception, string title)
        {
            string errorType = exception?.GetType().Name ?? nameof(Exception);
            string message = exception?.Message ?? "DBエラーの詳細を取得できませんでした。";

            // 今は popup を止め、ログだけ残して原因調査を優先する。
            DebugRuntimeLog.Write(
                "db",
                $"db error dialog {(SuppressDbErrorDialogTemporarily ? "suppressed" : "shown")}: title='{title}' err='{errorType}: {message}'"
            );

            if (SuppressDbErrorDialogTemporarily)
            {
                return;
            }

            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        /// <summary>
        /// メインDBの必須テーブル/列が揃っているかを開く前に検証する。
        /// ここで弾ければ、既存DBを閉じる前に安全に中断できる。
        /// </summary>
        public static bool TryValidateMainDatabaseSchema(
            string dbFullPath,
            out string errorMessage
        )
        {
            errorMessage = "";

            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                errorMessage = "DBパスが空です。";
                return false;
            }

            if (!File.Exists(dbFullPath))
            {
                errorMessage = $"DBファイルが見つかりません: {dbFullPath}";
                return false;
            }

            // 開く処理で実際に使うテーブルだけを対象に、必要列を明示的に検証する。
            Dictionary<string, string[]> requiredSchema = new(StringComparer.OrdinalIgnoreCase)
            {
                ["system"] = ["attr", "value"],
                ["movie"] =
                [
                    "movie_id",
                    "movie_name",
                    "movie_path",
                    "movie_length",
                    "movie_size",
                    "last_date",
                    "file_date",
                    "regist_date",
                    "score",
                    "view_count",
                    "hash",
                    "container",
                    "video",
                    "audio",
                    "extra",
                    "title",
                    "artist",
                    "album",
                    "grouping",
                    "writer",
                    "genre",
                    "track",
                    "camera",
                    "create_time",
                    "kana",
                    "roma",
                    "tag",
                    "comment1",
                    "comment2",
                    "comment3",
                ],
                ["history"] = ["find_id", "find_text", "find_date"],
                ["watch"] = ["dir", "auto", "watch", "sub"],
                ["bookmark"] =
                [
                    "movie_id",
                    "movie_name",
                    "movie_path",
                    "movie_length",
                    "movie_size",
                    "last_date",
                    "file_date",
                    "regist_date",
                    "score",
                    "view_count",
                    "hash",
                    "container",
                    "video",
                    "audio",
                    "extra",
                    "title",
                    "artist",
                    "album",
                    "grouping",
                    "writer",
                    "genre",
                    "track",
                    "camera",
                    "create_time",
                    "kana",
                    "roma",
                    "tag",
                    "comment1",
                    "comment2",
                    "comment3",
                ],
            };

            bool isUncPath = IsUncPath(dbFullPath);
            Stopwatch retryStopwatch = Stopwatch.StartNew();
            int attempt = 0;

            while (true)
            {
                attempt++;
                try
                {
                    using SQLiteConnection connection = CreateReadOnlyConnection(dbFullPath);
                    connection.Open();

                    foreach (var entry in requiredSchema)
                    {
                        string tableName = entry.Key;
                        string[] requiredColumns = entry.Value;

                        if (!TableExists(connection, tableName))
                        {
                            errorMessage = $"必須テーブル '{tableName}' が見つかりません。";
                            return false;
                        }

                        HashSet<string> actualColumns = GetTableColumns(connection, tableName);
                        List<string> missingColumns = [];
                        foreach (string requiredColumn in requiredColumns)
                        {
                            if (!actualColumns.Contains(requiredColumn))
                            {
                                missingColumns.Add(requiredColumn);
                            }
                        }

                        if (missingColumns.Count > 0)
                        {
                            errorMessage =
                                $"テーブル '{tableName}' に必須列が不足しています: {string.Join(", ", missingColumns)}";
                            return false;
                        }
                    }

                    if (isUncPath && attempt > 1)
                    {
                        DebugRuntimeLog.Write(
                            "db",
                            $"schema validation recovered after retry: db='{dbFullPath}' attempts={attempt} elapsed_ms={retryStopwatch.ElapsedMilliseconds}"
                        );
                    }

                    return true;
                }
                catch (Exception ex)
                    when (
                        ShouldRetryMainDbSchemaValidation(
                            isUncPath,
                            ex,
                            retryStopwatch.ElapsedMilliseconds,
                            out int delayMs
                        )
                    )
                {
                    DebugRuntimeLog.Write(
                        "db",
                        $"schema validation retry: db='{dbFullPath}' attempt={attempt} elapsed_ms={retryStopwatch.ElapsedMilliseconds} wait_ms={delayMs} reason='{ex.Message}'"
                    );
                    Thread.Sleep(delayMs);
                }
                catch (Exception ex)
                {
                    if (isUncPath && attempt > 1)
                    {
                        DebugRuntimeLog.Write(
                            "db",
                            $"schema validation retry exhausted: db='{dbFullPath}' attempts={attempt} elapsed_ms={retryStopwatch.ElapsedMilliseconds} reason='{ex.Message}'"
                        );
                    }

                    errorMessage = ex.Message;
                    return false;
                }
            }
        }

        // sqlite_master で対象テーブルの存在を確認する。
        private static bool TableExists(SQLiteConnection connection, string tableName)
        {
            using SQLiteCommand cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name LIMIT 1";
            cmd.Parameters.Add(new SQLiteParameter("@name", tableName));
            return cmd.ExecuteScalar() != null;
        }

        // PRAGMA table_info で列一覧を取得し、大文字小文字を無視して比較できる形にする。
        private static HashSet<string> GetTableColumns(SQLiteConnection connection, string tableName)
        {
            HashSet<string> columns = new(StringComparer.OrdinalIgnoreCase);
            string safeTableName = tableName.Replace("]", "]]");

            using SQLiteCommand cmd = connection.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info([{safeTableName}])";
            using SQLiteDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string name = reader["name"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(name))
                {
                    columns.Add(name);
                }
            }

            return columns;
        }

        // 旧 schema を壊さず吸収するため、必要な列だけ存在確認して分岐する。
        private static bool HasTableColumn(
            SQLiteConnection connection,
            string tableName,
            string columnName
        )
        {
            if (connection == null || string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(columnName))
            {
                return false;
            }

            return GetTableColumns(connection, tableName).Contains(columnName);
        }

        // UNC共有の瞬断だけは短い retry で吸収し、即失敗を減らす。
        private static bool ShouldRetryMainDbSchemaValidation(
            bool isUncPath,
            Exception exception,
            long elapsedMilliseconds,
            out int delayMilliseconds
        )
        {
            delayMilliseconds = 0;
            if (!isUncPath || !IsTransientMainDbOpenException(exception))
            {
                return false;
            }

            long remainingMilliseconds = UncSchemaValidationRetryBudgetMs - elapsedMilliseconds;
            if (remainingMilliseconds <= 0)
            {
                return false;
            }

            delayMilliseconds = (int)Math.Min(
                UncSchemaValidationRetryDelayMs,
                remainingMilliseconds
            );
            return delayMilliseconds > 0;
        }

        // MainDB openでよく出る共有/NAS起因の一時失敗だけを retry 対象に限定する。
        internal static bool IsTransientMainDbOpenException(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                string message = current.Message ?? "";
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                if (
                    message.IndexOf(
                        "unable to open database file",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0
                    || message.IndexOf(
                        "database is locked",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0
                    || message.IndexOf("database is busy", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf(
                        "being used by another process",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0
                    || message.IndexOf(
                        "semaphore timeout period has expired",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0
                    || message.IndexOf(
                        "specified network name is no longer available",
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0
                )
                {
                    return true;
                }
            }

            return false;
        }

        // 接続文字列側と file/path 側の判定を揃えるため、実パス基準で UNC を見分ける。
        internal static bool IsUncPath(string dbFullPath)
        {
            return !string.IsNullOrWhiteSpace(dbFullPath)
                && dbFullPath.Trim().StartsWith(@"\\", StringComparison.Ordinal);
        }

        // 読み取りだけの経路は ReadOnly 接続へ寄せ、不要な書き込みロックの入口を減らす。
        internal static SQLiteConnection CreateReadOnlyConnection(string dbFullPath)
        {
            return new SQLiteConnection(BuildConnectionString(dbFullPath, readOnly: true));
        }

        // 書き込み系は従来どおり通常接続を使う。将来の統一用に生成口だけ先に持っておく。
        internal static SQLiteConnection CreateReadWriteConnection(string dbFullPath)
        {
            return new SQLiteConnection(BuildConnectionString(dbFullPath, readOnly: false));
        }

        internal static string BuildConnectionString(string dbFullPath, bool readOnly)
        {
            // 実パスはそのまま保持し、接続文字列化の直前だけ UNC 逃がしを入れる。
            // これでファイル操作系の Path/Directory 判定と、SQLite 接続文字列の都合を分離する。
            SQLiteConnectionStringBuilder builder = new()
            {
                // UNC は接続文字列へ載せる直前だけ公式仕様どおりに連続 "\" を二重化する。
                DataSource = SQLiteConnectionStringPathHelper.EscapeDataSourcePath(dbFullPath),
                FailIfMissing = true,
                ReadOnly = readOnly,
                // MainDB も Queue/Failure と同じく、短い待機でロック競合を吸収する。
                BusyTimeout = MainDbBusyTimeoutMs,
                DefaultTimeout = MainDbDefaultTimeoutSec,
                // WB互換DBでは日時を ISO 文字列で固定し、利用者カルチャ依存の揺れを防ぐ。
                DateTimeFormat = SQLiteDateFormats.ISO8601,
                DateTimeKind = DateTimeKind.Local,
                LegacyFormat = false,
            };
            return builder.ToString();
        }

        /// <summary>
        /// WB互換DBへ流す日時は、環境差で化けない ISO 文字列へそろえる。
        /// </summary>
        internal static string FormatDbDateTime(DateTime value)
        {
            DateTime normalized = value.Kind == DateTimeKind.Utc ? value.ToLocalTime() : value;
            normalized = normalized.AddTicks(-(normalized.Ticks % TimeSpan.TicksPerSecond));
            return normalized.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// まずWB既定の ISO 文字列として読み、だめなら最後に現在カルチャへ逃がす。
        /// </summary>
        internal static bool TryParseDbDateTimeText(string value, out DateTime result)
        {
            result = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            DateTimeStyles styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal;
            if (
                DateTime.TryParseExact(
                    value,
                    DbDateTimeAcceptedFormats,
                    CultureInfo.InvariantCulture,
                    styles,
                    out result
                )
            )
            {
                return true;
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, styles, out result))
            {
                return true;
            }

            return DateTime.TryParse(value, CultureInfo.CurrentCulture, styles, out result);
        }

        internal static DateTime ReadDbDateTimeOrDefault(object value, DateTime defaultValue)
        {
            if (value == DBNull.Value || value == null)
            {
                return defaultValue;
            }

            if (value is DateTime dateTime)
            {
                return dateTime;
            }

            return TryParseDbDateTimeText(value.ToString() ?? "", out DateTime parsed)
                ? parsed
                : defaultValue;
        }

        internal static string ReadDbDateTimeTextOrEmpty(object value)
        {
            if (value == DBNull.Value || value == null)
            {
                return "";
            }

            if (value is DateTime dateTime)
            {
                return FormatDbDateTime(dateTime);
            }

            return TryParseDbDateTimeText(value.ToString() ?? "", out DateTime parsed)
                ? FormatDbDateTime(parsed)
                : value.ToString() ?? "";
        }

        private static object NormalizeDbParameterValue(object value)
        {
            return value switch
            {
                null => DBNull.Value,
                DateTime dateTime => FormatDbDateTime(dateTime),
                _ => value
            };
        }

        /// <summary>
        /// まっさらな大地にSQLiteファイルを生み出し、アプリの命とも言える9つのテーブル群
        /// (bookmark, history, movie, watch等) を怒涛の建国ラッシュで一斉構築する始まりの儀式！🏗️✨
        /// </summary>
        public static bool TryCreateDatabase(string dbFullPath, out string errorMessage)
        {
            errorMessage = "";
            try
            {
                if (string.IsNullOrWhiteSpace(dbFullPath))
                {
                    errorMessage = "DBパスが空です。";
                    return false;
                }

                string parentDirectory = Path.GetDirectoryName(dbFullPath) ?? "";
                if (string.IsNullOrWhiteSpace(parentDirectory))
                {
                    errorMessage = $"DB保存先フォルダを特定できません: {dbFullPath}";
                    return false;
                }

                if (!Directory.Exists(parentDirectory))
                {
                    errorMessage = $"DB保存先フォルダが見つかりません: {parentDirectory}";
                    return false;
                }

                SQLiteConnection.CreateFile(dbFullPath);
                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    //bookmark
                    cmd.CommandText =
                        @"
                        CREATE TABLE bookmark(
                        movie_id integer primary key not null, 
                        movie_name text not null default '', 
                        movie_path text not null default '', 
                        movie_length integer not null default 0, 
                        movie_size integer not null default 0, 
                        last_date datetime not null, 
                        file_date datetime not null, 
                        regist_date datetime not null, 
                        score integer not null default 0, 
                        view_count integer not null default 0, 
                        hash text not null default '', 
                        container text not null default '', 
                        video text not null default '', 
                        audio text not null default '', 
                        extra text not null default '', 
                        title text not null default '', 
                        artist text not null default '', 
                        album text not null default '', 
                        grouping text not null default '', 
                        writer text not null default '', 
                        genre text not null default '', 
                        track text not null default '', 
                        camera text not null default '', 
                        create_time text not null default '', 
                        kana text not null default '', 
                        roma text not null default '', 
                        tag text not null default '', 
                        comment1 text not null default '', 
                        comment2 text not null default '', 
                        comment3 text not null default '' )";
                    cmd.ExecuteNonQuery();
                    //findfact
                    cmd.CommandText =
                        @"
                        CREATE TABLE findfact(
                        find_text text primary key not null, 
                        find_count integer not null default 0, 
                        last_date datetime not null )";
                    cmd.ExecuteNonQuery();
                    //history
                    cmd.CommandText =
                        @"
                        CREATE TABLE history(
                        find_id integer primary key not null, 
                        find_text text not null, 
                        find_date datetime not null )";
                    cmd.ExecuteNonQuery();
                    //movie
                    cmd.CommandText =
                        @"
                        CREATE TABLE movie(movie_id integer primary key not null, 
                        movie_name text not null default '', 
                        movie_path text not null default '', 
                        movie_length integer not null default 0, 
                        movie_size integer not null default 0, 
                        last_date datetime not null, 
                        file_date datetime not null, 
                        regist_date datetime not null, 
                        score integer not null default 0, 
                        view_count integer not null default 0, 
                        hash text not null default '', 
                        container text not null default '', 
                        video text not null default '', 
                        audio text not null default '', 
                        extra text not null default '', 
                        title text not null default '', 
                        artist text not null default '', 
                        album text not null default '', 
                        grouping text not null default '', 
                        writer text not null default '', 
                        genre text not null default '', 
                        track text not null default '', 
                        camera text not null default '', 
                        create_time text not null default '', 
                        kana text not null default '', 
                        roma text not null default '', 
                        tag text not null default '', 
                        comment1 text not null default '', 
                        comment2 text not null default '', 
                        comment3 text not null default '' )";
                    cmd.ExecuteNonQuery();
                    //profile
                    cmd.CommandText =
                        @"
                        CREATE TABLE profile(
                        skin text not null, 
                        key text not null, 
                        value text not null, 
                        primary key(skin, key))";
                    cmd.ExecuteNonQuery();
                    //sysbin
                    cmd.CommandText =
                        @"
                        CREATE TABLE sysbin(attr text primary key not null, value blob not null )";
                    cmd.ExecuteNonQuery();
                    //system
                    cmd.CommandText =
                        @"
                        CREATE TABLE system(attr text primary key not null, value text not null )";
                    cmd.ExecuteNonQuery();
                    //tagbar
                    cmd.CommandText =
                        @"
                        CREATE TABLE tagbar(item_id integer primary key not null, 
                        parent_id integer not null default 0, 
                        order_id integer not null default 0, 
                        group_id integer not null default 0, 
                        title text not null default '', 
                        contents text not null default '' )";
                    cmd.ExecuteNonQuery();
                    //watch
                    cmd.CommandText =
                        @"
                        CREATE TABLE watch(dir text primary key not null, 
                        auto integer not null default 0, 
                        watch integer not null default 0, 
                        sub integer not null default 1 )";
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
                return true;
            }
            catch (Exception e)
            {
                errorMessage = e.Message;

                // 作成途中で倒れた0KBファイルは、次回の誤診断を防ぐため可能なら片付ける。
                try
                {
                    if (File.Exists(dbFullPath))
                    {
                        FileInfo fileInfo = new(dbFullPath);
                        if (fileInfo.Length == 0)
                        {
                            fileInfo.Delete();
                        }
                    }
                }
                catch
                {
                    // 掃除失敗は元エラーを優先し、ここでは握って返す。
                }

                return false;
            }
        }

        /// <summary>
        /// watch(監視)テーブルの記憶を全てあの世へ送り放つ、無慈悲な全消去魔法（DELETE文）！💣💥
        /// </summary>
        public static void DeleteWatchTable(string dbFullPath)
        {
            try
            {
                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "delete from watch";
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }
        }

        /// <summary>
        /// watch(監視)テーブルへ新たな監視対象（ディレクトリ）をブチ込む！👁️✨
        /// </summary>
        public static void InsertWatchTable(string dbFullPath, WatchRecords watchRec)
        {
            try
            {
                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();
                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        "insert into watch (dir,auto,watch,sub) values (@dir,@auto,@watch,@sub)";
                    cmd.Parameters.Add(new SQLiteParameter("@dir", watchRec.Dir));
                    cmd.Parameters.Add(new SQLiteParameter("@auto", watchRec.Auto == true ? 1 : 0));
                    cmd.Parameters.Add(
                        new SQLiteParameter("@watch", watchRec.Watch == true ? 1 : 0)
                    );
                    cmd.Parameters.Add(new SQLiteParameter("@sub", watchRec.Sub == true ? 1 : 0));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }
        }

        /// <summary>
        /// movieテーブルの「たった一つ」のパラメータを狙い撃ちで書き換える、スナイパー的更新処理！🎯
        /// 許可されてない列名を渡すと即座に例外をブッパする、セキュリティも万全の漢(おとこ)仕様だぜ！
        /// </summary>
        public static void UpdateMovieSingleColumn(
            string dbFullPath,
            long movieId,
            string columnName,
            object value
        )
        {
            try
            {
                if (!AllowedMovieColumns.Contains(columnName))
                {
                    throw new ArgumentException(
                        $"許可されていない列名です: {columnName}",
                        nameof(columnName)
                    );
                }

                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();
                if (!HasTableColumn(connection, "movie", columnName))
                {
                    // 想定外の旧DBでは popup を出さず、更新不能列だけ静かに諦める。
                    return;
                }

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        $"update movie set {columnName} = @value where movie_id = @id";
                    cmd.Parameters.Add(new SQLiteParameter("@id", movieId));
                    cmd.Parameters.Add(
                        new SQLiteParameter("@value", NormalizeDbParameterValue(value))
                    );
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }
        }

        /// <summary>
        /// system(設定)テーブルをチェック！すでにキーがあればUpdate、無ければInsertする賢きUpsert（上書き追加）処理だ！🧠
        /// </summary>
        public static void UpsertSystemTable(string dbFullPath, string attr, string value)
        {
            TryUpsertSystemTable(dbFullPath, attr, value);
        }

        internal static bool TryUpsertSystemTable(string dbFullPath, string attr, string value)
        {
            try
            {
                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using SQLiteCommand cmd = connection.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO system (attr, value)
                    VALUES (@attr, @value)
                    ON CONFLICT(attr) DO UPDATE SET value = excluded.value
                    """;
                cmd.Parameters.Add(new SQLiteParameter("@attr", attr ?? ""));
                cmd.Parameters.Add(new SQLiteParameter("@value", value ?? ""));
                cmd.ExecuteNonQuery();
                transaction.Commit();
                return true;
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }

            return false;
        }

        /// <summary>
        /// profile テーブルへスキン固有の設定を保存する。
        /// 外部スキン利用時だけ現在タブなどの補助状態を逃がす。
        /// </summary>
        public static void UpsertProfileTable(string dbFullPath, string skin, string key, string value)
        {
            TryUpsertProfileTable(dbFullPath, skin, key, value);
        }

        internal static bool TryUpsertProfileTable(string dbFullPath, string skin, string key, string value)
        {
            try
            {
                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using SQLiteCommand cmd = connection.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO profile (skin, key, value)
                    VALUES (@skin, @key, @value)
                    ON CONFLICT(skin, key) DO UPDATE SET value = excluded.value
                    """;
                cmd.Parameters.Add(new SQLiteParameter("@skin", skin ?? ""));
                cmd.Parameters.Add(new SQLiteParameter("@key", key ?? ""));
                cmd.Parameters.Add(new SQLiteParameter("@value", value ?? ""));
                cmd.ExecuteNonQuery();
                transaction.Commit();
                return true;
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }

            return false;
        }

        /// <summary>
        /// profile テーブルからスキン固有設定を 1 件だけ読む。
        /// 値が無い時は空文字で返して呼び出し側のフォールバックへ任せる。
        /// </summary>
        public static string SelectProfileValue(string dbFullPath, string skin, string key)
        {
            try
            {
                using SQLiteConnection connection = CreateReadOnlyConnection(dbFullPath);
                connection.Open();

                using SQLiteCommand cmd = connection.CreateCommand();
                cmd.CommandText =
                    "SELECT value FROM profile WHERE skin = @skin AND key = @key LIMIT 1";
                cmd.Parameters.Add(new SQLiteParameter("@skin", skin ?? ""));
                cmd.Parameters.Add(new SQLiteParameter("@key", key ?? ""));
                object result = cmd.ExecuteScalar();
                return result?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }
        /// <summary>
        /// 指定されたムービーをmovieテーブルから完全に消し飛ばす！さらば友よ！👋
        /// </summary>
        public static int DeleteMovieTable(string dbFullPath, long movieId)
        {
            try
            {
                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "delete from movie where movie_id = @id";
                    cmd.Parameters.Add(new SQLiteParameter("@id", movieId));
                    int deletedCount = cmd.ExecuteNonQuery();
                    transaction.Commit();
                    return deletedCount;
                }
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }

            return 0;
        }

        /// <summary>
        /// Debug運用で中身だけ初期化したい時のために、設定系を残して主要レコードを丸ごと消す。
        /// </summary>
        public static void ClearMainDataRecords(string dbFullPath)
        {
            try
            {
                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    // 設定やスキンは残し、動画・履歴・監視系の実データだけ空に戻す。
                    cmd.CommandText =
                        @"DELETE FROM movie;
DELETE FROM bookmark;
DELETE FROM history;
DELETE FROM findfact;
DELETE FROM watch;";
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }
        }

        /// <summary>
        /// history(検索履歴)から不要な過去を一つだけ抹消する黒歴史クリーナーだ！🧽
        /// </summary>
        public static void DeleteHistoryTable(string dbFullPath, long findId)
        {
            try
            {
                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "delete from history where find_id = @id";
                    cmd.Parameters.Add(new SQLiteParameter("@id", findId));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }
        }

        public static Task<int> InsertMovieTable(string dbFullPath, MovieInfo mvi)
        {
            ArgumentNullException.ThrowIfNull(mvi);
            return InsertMovieTable(dbFullPath, mvi.ToMovieCore());
        }

        /// <summary>
        /// movieテーブルへ新規メンバー（動画）を招き入れる心臓部の処理！
        /// アプリ中から集まったMovieInfoやMovieRecordsたちは、すべてMovieCore型に姿を変えてここに集結するぜ！🦸‍♂️
        /// </summary>
        private static (string Kana, string Roma) ComputeReadingValues(
            string movieName,
            string moviePath
        )
        {
            string kana = JapaneseKanaProvider.GetKanaForPersistence(movieName, moviePath);
            string roma = JapaneseKanaProvider.GetRomaFromKanaForPersistence(kana);
            return (kana ?? "", roma ?? "");
        }

        public static Task<int> InsertMovieTable(string dbFullPath, MovieCore movie)
        {
            // DB登録の本体処理フロー:
            // 1. 引数チェック
            ArgumentNullException.ThrowIfNull(movie);
            try
            {
                (string kana, string roma) = ComputeReadingValues(movie.MovieName, movie.MoviePath);
                Stopwatch totalStopwatch = Stopwatch.StartNew();
                bool isProbeTarget = IsDbInsertProbeTargetMoviePath(movie.MoviePath ?? "");
                long sinkuMs = 0;
                long insertMs = 0;
                bool sinkuSucceeded = false;

                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();
                bool hasKanaColumn = HasTableColumn(connection, "movie", "kana");
                bool hasRomaColumn = HasTableColumn(connection, "movie", "roma");
                string container = "";
                string video = "";
                string extra = "";
                string audio = "";
                long movieLengthLong = movie.MovieLength;

                // 3. Sinku.dll を直接呼び出して動画メタ情報の詳細を取得する。
                Stopwatch sinkuStopwatch = Stopwatch.StartNew();
                if (TryReadBySinkuDll(movie.MoviePath ?? "", out SinkuMediaMeta sinkuMeta))
                {
                    sinkuSucceeded = true;
                    container = sinkuMeta.Container;
                    video = sinkuMeta.Video;
                    audio = sinkuMeta.Audio;
                    extra = sinkuMeta.Extra;
                    if (movieLengthLong < 1 && sinkuMeta.PlaytimeSeconds > 0)
                    {
                        movieLengthLong = sinkuMeta.PlaytimeSeconds;
                    }
                }
                sinkuStopwatch.Stop();
                sinkuMs = sinkuStopwatch.ElapsedMilliseconds;

                // 4. 採番したIDと抽出したメタ情報をまとめて、movieテーブルへINSERTするトランザクション処理
                using var transaction = connection.BeginTransaction();
                Stopwatch insertStopwatch = Stopwatch.StartNew();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    List<string> columns =
                    [
                        "movie_name",
                        "movie_path",
                        "movie_length",
                        "movie_size",
                        "last_date",
                        "file_date",
                        "regist_date",
                        "hash",
                        "container",
                        "video",
                        "audio"
                    ];
                    List<string> values =
                    [
                        "@movie_name",
                        "@movie_path",
                        "@movie_length",
                        "@movie_size",
                        "@last_date",
                        "@file_date",
                        "@regist_date",
                        "@hash",
                        "@container",
                        "@video",
                        "@audio"
                    ];

                    if (hasKanaColumn)
                    {
                        columns.Add("kana");
                        values.Add("@kana");
                    }

                    if (hasRomaColumn)
                    {
                        columns.Add("roma");
                        values.Add("@roma");
                    }

                    columns.Add("extra");
                    values.Add("@extra");
                    cmd.CommandText =
                        $"insert into movie ({string.Join(",", columns)}) values ({string.Join(",", values)})";
                    cmd.Parameters.Add(
                        new SQLiteParameter("@movie_name", (movie.MovieName ?? "").ToLower())
                    );
                    cmd.Parameters.Add(new SQLiteParameter("@movie_path", movie.MoviePath ?? ""));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_length", movieLengthLong));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_size", movie.MovieSize / 1024));
                    cmd.Parameters.Add(new SQLiteParameter("@last_date", FormatDbDateTime(movie.LastDate)));
                    cmd.Parameters.Add(new SQLiteParameter("@file_date", FormatDbDateTime(movie.FileDate)));
                    cmd.Parameters.Add(
                        new SQLiteParameter("@regist_date", FormatDbDateTime(movie.RegistDate))
                    );
                    cmd.Parameters.Add(new SQLiteParameter("@hash", movie.Hash ?? ""));
                    cmd.Parameters.Add(new SQLiteParameter("@container", container));
                    cmd.Parameters.Add(new SQLiteParameter("@video", video));
                    cmd.Parameters.Add(new SQLiteParameter("@audio", audio));
                    if (hasKanaColumn)
                    {
                        cmd.Parameters.Add(new SQLiteParameter("@kana", kana));
                    }
                    if (hasRomaColumn)
                    {
                        cmd.Parameters.Add(new SQLiteParameter("@roma", roma));
                    }
                    cmd.Parameters.Add(new SQLiteParameter("@extra", extra));
                    cmd.ExecuteNonQuery();
                }

                // INSERTした行のrowidを採番IDとして取得し、呼び出し元で使えるように保持する。
                using (SQLiteCommand idCmd = connection.CreateCommand())
                {
                    idCmd.CommandText = "select last_insert_rowid()";
                    object scalar = idCmd.ExecuteScalar();
                    if (long.TryParse(scalar?.ToString(), out long movieId))
                    {
                        movie.MovieId = movieId;
                    }
                }
                insertStopwatch.Stop();
                insertMs = insertStopwatch.ElapsedMilliseconds;

                transaction.Commit();
                totalStopwatch.Stop();
                if (
                    isProbeTarget
                    || totalStopwatch.ElapsedMilliseconds >= DbInsertProbeSlowThresholdMs
                )
                {
                    DebugRuntimeLog.Write(
                        "watch-db-probe",
                        $"single path='{movie.MoviePath}' sinku_ok={sinkuSucceeded} sinku_ms={sinkuMs} insert_ms={insertMs} total_ms={totalStopwatch.ElapsedMilliseconds}"
                    );
                }

                return Task.FromResult(1);
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }
            return Task.FromResult(0);
        }

        /// <summary>
        /// movieテーブルへ大量の猛者たち（複数レコード）を一撃必殺の1トランザクションでブチ込む大魔法だ！🌪️
        /// 呼び出し元への土産として、採番されたピカピカのID(MovieId)をそれぞれに刻み込んで返す気の利いた仕様だぜ！😎
        /// </summary>
        public static Task<int> InsertMovieTableBatch(string dbFullPath, List<MovieCore> movies)
        {
            ArgumentNullException.ThrowIfNull(movies);
            if (movies.Count < 1)
            {
                return Task.FromResult(0);
            }

            try
            {
                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();
                bool hasKanaColumn = HasTableColumn(connection, "movie", "kana");
                bool hasRomaColumn = HasTableColumn(connection, "movie", "roma");

                using var transaction = connection.BeginTransaction();
                using SQLiteCommand insertCmd = connection.CreateCommand();
                int insertedCount = 0;
                List<string> columns =
                [
                    "movie_name",
                    "movie_path",
                    "movie_length",
                    "movie_size",
                    "last_date",
                    "file_date",
                    "regist_date",
                    "hash",
                    "container",
                    "video",
                    "audio"
                ];
                List<string> values =
                [
                    "@movie_name",
                    "@movie_path",
                    "@movie_length",
                    "@movie_size",
                    "@last_date",
                    "@file_date",
                    "@regist_date",
                    "@hash",
                    "@container",
                    "@video",
                    "@audio"
                ];

                if (hasKanaColumn)
                {
                    columns.Add("kana");
                    values.Add("@kana");
                }

                if (hasRomaColumn)
                {
                    columns.Add("roma");
                    values.Add("@roma");
                }

                columns.Add("extra");
                values.Add("@extra");
                insertCmd.CommandText =
                    $"insert into movie ({string.Join(",", columns)}) values ({string.Join(",", values)})";

                using SQLiteCommand idCmd = connection.CreateCommand();
                idCmd.CommandText = "select last_insert_rowid()";

                foreach (MovieCore movie in movies)
                {
                    if (movie == null)
                    {
                        continue;
                    }

                    Stopwatch totalStopwatch = Stopwatch.StartNew();
                    bool isProbeTarget = IsDbInsertProbeTargetMoviePath(movie.MoviePath ?? "");
                    long sinkuMs = 0;
                    long insertMs = 0;
                    bool sinkuSucceeded = false;
                    (string kana, string roma) = ComputeReadingValues(movie.MovieName, movie.MoviePath);
                    string container = "";
                    string video = "";
                    string extra = "";
                    string audio = "";
                    long movieLengthLong = movie.MovieLength;

                    // 既存の単体登録と同じ補完ロジックを維持して互換性を保つ。
                    Stopwatch sinkuStopwatch = Stopwatch.StartNew();
                    if (TryReadBySinkuDll(movie.MoviePath ?? "", out SinkuMediaMeta sinkuMeta))
                    {
                        sinkuSucceeded = true;
                        container = sinkuMeta.Container;
                        video = sinkuMeta.Video;
                        audio = sinkuMeta.Audio;
                        extra = sinkuMeta.Extra;
                        if (movieLengthLong < 1 && sinkuMeta.PlaytimeSeconds > 0)
                        {
                            movieLengthLong = sinkuMeta.PlaytimeSeconds;
                        }
                    }
                    sinkuStopwatch.Stop();
                    sinkuMs = sinkuStopwatch.ElapsedMilliseconds;

                    Stopwatch insertStopwatch = Stopwatch.StartNew();
                    insertCmd.Parameters.Clear();
                    insertCmd.Parameters.Add(
                        new SQLiteParameter("@movie_name", (movie.MovieName ?? "").ToLower())
                    );
                    insertCmd.Parameters.Add(
                        new SQLiteParameter("@movie_path", movie.MoviePath ?? "")
                    );
                    insertCmd.Parameters.Add(new SQLiteParameter("@movie_length", movieLengthLong));
                    insertCmd.Parameters.Add(
                        new SQLiteParameter("@movie_size", movie.MovieSize / 1024)
                    );
                    insertCmd.Parameters.Add(
                        new SQLiteParameter("@last_date", FormatDbDateTime(movie.LastDate))
                    );
                    insertCmd.Parameters.Add(
                        new SQLiteParameter("@file_date", FormatDbDateTime(movie.FileDate))
                    );
                    insertCmd.Parameters.Add(
                        new SQLiteParameter("@regist_date", FormatDbDateTime(movie.RegistDate))
                    );
                    insertCmd.Parameters.Add(new SQLiteParameter("@hash", movie.Hash ?? ""));
                    insertCmd.Parameters.Add(new SQLiteParameter("@container", container));
                    insertCmd.Parameters.Add(new SQLiteParameter("@video", video));
                    insertCmd.Parameters.Add(new SQLiteParameter("@audio", audio));
                    if (hasKanaColumn)
                    {
                        insertCmd.Parameters.Add(new SQLiteParameter("@kana", kana));
                    }
                    if (hasRomaColumn)
                    {
                        insertCmd.Parameters.Add(new SQLiteParameter("@roma", roma));
                    }
                    insertCmd.Parameters.Add(new SQLiteParameter("@extra", extra));
                    insertCmd.ExecuteNonQuery();
                    insertedCount += 1;

                    object scalar = idCmd.ExecuteScalar();
                    if (long.TryParse(scalar?.ToString(), out long movieId))
                    {
                        movie.MovieId = movieId;
                    }
                    insertStopwatch.Stop();
                    insertMs = insertStopwatch.ElapsedMilliseconds;
                    totalStopwatch.Stop();
                    if (
                        isProbeTarget
                        || totalStopwatch.ElapsedMilliseconds >= DbInsertProbeSlowThresholdMs
                    )
                    {
                        DebugRuntimeLog.Write(
                            "watch-db-probe",
                            $"batch path='{movie.MoviePath}' sinku_ok={sinkuSucceeded} sinku_ms={sinkuMs} insert_ms={insertMs} total_ms={totalStopwatch.ElapsedMilliseconds}"
                        );
                    }
                }

                transaction.Commit();
                return Task.FromResult(insertedCount);
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }

            return Task.FromResult(0);
        }

        /// <summary>
        /// 究極の解析ツール『Sinku.dll』を直接叩き起こして動画のメタ情報（長さやコーデック）を抉り出すぜ！🕵️‍♂️
        /// もしコイツが力尽きたらfalseを返し、素知らぬ顔で既定値のままDB登録を続行する強かな仕様だ！
        /// </summary>
        private static bool TryReadBySinkuDll(string moviePath, out SinkuMediaMeta sinkuMeta)
        {
            sinkuMeta = new SinkuMediaMeta();
            if (string.IsNullOrWhiteSpace(moviePath))
            {
                return false;
            }

            if (Volatile.Read(ref _sinkuDisabledForProcess) != 0)
            {
                return false;
            }

            string sinkuDllPath = ResolveSinkuDllPath();
            if (string.IsNullOrWhiteSpace(sinkuDllPath))
            {
                DebugRuntimeLog.Write("sinku", "Sinku.dll not found under tools/sinku.");
                return false;
            }

            nint dllHandle = 0;
            try
            {
                dllHandle = NativeLibrary.Load(sinkuDllPath);

                SinkuUnicodeDelegate unicode = GetSinkuDelegate<SinkuUnicodeDelegate>(
                    dllHandle,
                    "Unicode"
                );
                bool isUnicode = unicode != null && unicode() != 0;

                SinkuSetPathDelegate setPath = GetSinkuDelegate<SinkuSetPathDelegate>(
                    dllHandle,
                    "SetPath"
                );
                SinkuGetFileInfoAutoDelegate getFileInfoAuto =
                    GetSinkuDelegate<SinkuGetFileInfoAutoDelegate>(dllHandle, "GetFileInfoAuto");
                if (getFileInfoAuto == null)
                {
                    DebugRuntimeLog.Write("sinku", "GetFileInfoAuto export not found.");
                    return false;
                }

                // codecs.ini / format.ini は Sinku.dll と同じフォルダを優先する。
                string sinkuDir = Path.GetDirectoryName(sinkuDllPath) ?? "";
                if (!string.IsNullOrWhiteSpace(sinkuDir) && setPath != null)
                {
                    string sinkuIniDir = sinkuDir.EndsWith(Path.DirectorySeparatorChar)
                        ? sinkuDir
                        : sinkuDir + Path.DirectorySeparatorChar;
                    nint pathPtr = nint.Zero;
                    try
                    {
                        pathPtr = isUnicode
                            ? Marshal.StringToHGlobalUni(sinkuIniDir)
                            : Marshal.StringToHGlobalAnsi(sinkuIniDir);
                        setPath(pathPtr);
                    }
                    finally
                    {
                        if (pathPtr != nint.Zero)
                        {
                            Marshal.FreeHGlobal(pathPtr);
                        }
                    }
                }

                byte[] fileInfo = new byte[SinkuFileInfoByteLength];
                WriteMoviePathToFileInfo(fileInfo, moviePath, isUnicode);

                nint fileInfoPtr = Marshal.AllocHGlobal(fileInfo.Length);
                try
                {
                    Marshal.Copy(fileInfo, 0, fileInfoPtr, fileInfo.Length);
                    getFileInfoAuto(fileInfoPtr);
                    Marshal.Copy(fileInfoPtr, fileInfo, 0, fileInfo.Length);
                }
                finally
                {
                    Marshal.FreeHGlobal(fileInfoPtr);
                }

                int fileType = BitConverter.ToInt32(fileInfo, SinkuOffsetType);
                string error = ReadShiftJisString(fileInfo, SinkuOffsetError, SinkuTextByteLength);
                long playtimeSeconds = (long)Math.Truncate(ReadPlaytimeSeconds(fileInfo));

                sinkuMeta = new SinkuMediaMeta
                {
                    Container = ReadShiftJisString(
                        fileInfo,
                        SinkuOffsetContainer,
                        SinkuContainerByteLength
                    ),
                    Video = ReadDetailList(fileInfo, SinkuOffsetVideo, SinkuVideoStreamMaxCount),
                    Audio = ReadDetailList(fileInfo, SinkuOffsetAudio, SinkuVideoStreamMaxCount),
                    Extra = ReadDetailList(fileInfo, SinkuOffsetExtra, SinkuExtraStreamMaxCount),
                    PlaytimeSeconds = playtimeSeconds,
                };

                bool hasUsefulData =
                    !string.IsNullOrWhiteSpace(sinkuMeta.Container)
                    || !string.IsNullOrWhiteSpace(sinkuMeta.Video)
                    || !string.IsNullOrWhiteSpace(sinkuMeta.Audio)
                    || !string.IsNullOrWhiteSpace(sinkuMeta.Extra)
                    || sinkuMeta.PlaytimeSeconds > 0;

                if (!hasUsefulData && fileType <= SinkuFileTypeErrorEnd)
                {
                    DebugRuntimeLog.Write(
                        "sinku",
                        $"Sinku.dll returned error type={fileType}, detail='{error}'"
                    );
                }

                return hasUsefulData;
            }
            catch (Exception ex)
            {
                if (
                    ex is BadImageFormatException
                    || ex is DllNotFoundException
                    || ex is EntryPointNotFoundException
                    || ex is FileLoadException
                )
                {
                    // DLL形式不一致や依存不足は動画ごとの再試行で改善しないため、以後の呼び出しを止める。
                    if (Interlocked.Exchange(ref _sinkuDisabledForProcess, 1) == 0)
                    {
                        DebugRuntimeLog.Write(
                            "sinku",
                            $"Sinku.dll disabled for this process: type={ex.GetType().Name}, message={ex.Message}"
                        );
                    }
                }

                DebugRuntimeLog.Write(
                    "sinku",
                    $"Sinku.dll read failed: type={ex.GetType().Name}, message={ex.Message}"
                );
                return false;
            }
            finally
            {
                if (dllHandle != 0)
                {
                    NativeLibrary.Free(dllHandle);
                }
            }
        }

        /// <summary>
        /// 同梱された最強ツール「Sinku.dll」だけを己の嗅覚で探し出す専用レーダーだ！📡💥
        /// </summary>
        private static string ResolveSinkuDllPath()
        {
            string[] candidates =
            [
                Path.Combine(AppContext.BaseDirectory, "tools", "sinku", "Sinku.dll"),
                Path.Combine(Directory.GetCurrentDirectory(), "tools", "sinku", "Sinku.dll"),
            ];

            foreach (string candidate in candidates)
            {
                try
                {
                    if (Path.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // 壊れたパスは無視して次候補へ進む。
                }
            }

            return "";
        }

        /// <summary>
        /// DB挿入時の激重トレース対象を見極めるための高精度スキャナー！👁️✨
        /// 動画ID部分だけでピンポイント照合することで、長いパスの揺らぎすらも無力化する鉄壁の判定だぜ！
        /// </summary>
        private static bool IsDbInsertProbeTargetMoviePath(string moviePath)
        {
            if (string.IsNullOrWhiteSpace(moviePath))
            {
                return false;
            }

            return moviePath.Contains(
                DbInsertProbeMovieIdentity,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static T GetSinkuDelegate<T>(nint dllHandle, string exportName)
            where T : Delegate
        {
            if (!NativeLibrary.TryGetExport(dllHandle, exportName, out nint procAddress))
            {
                return null;
            }

            return Marshal.GetDelegateForFunctionPointer<T>(procAddress);
        }

        /// <summary>
        /// ネイティブ側の領域(FILE_INFO.nameのTCHAR配列)に、魂の動画パスを直接ブチ込む禁断のメモリ書き込み術！💉🔥
        /// </summary>
        private static void WriteMoviePathToFileInfo(
            byte[] fileInfo,
            string moviePath,
            bool isUnicode
        )
        {
            int writeLength = Math.Min(SinkuNameByteLength, fileInfo.Length);
            Array.Clear(fileInfo, 0, writeLength);
            if (string.IsNullOrWhiteSpace(moviePath) || writeLength < 1)
            {
                return;
            }

            Encoding textEncoding = isUnicode ? Encoding.Unicode : Encoding.GetEncoding(932);
            byte[] source = textEncoding.GetBytes(moviePath);
            int terminatorLength = isUnicode ? 2 : 1;
            int copyLength = Math.Min(source.Length, writeLength - terminatorLength);
            if (copyLength > 0)
            {
                Array.Copy(source, 0, fileInfo, 0, copyLength);
            }
        }

        /// <summary>
        /// バイト配列に散らばった複数ストリームの断片をかき集め、" / " の絆で一つに結びつける錬金術！🔗✨
        /// </summary>
        private static string ReadDetailList(byte[] source, int offset, int streamCount)
        {
            List<string> values = [];
            for (int i = 0; i < streamCount; i++)
            {
                int currentOffset = offset + (i * SinkuTextByteLength);
                string value = ReadShiftJisString(source, currentOffset, SinkuTextByteLength);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }

            return values.Count > 0 ? string.Join(" / ", values) : "";
        }

        private static string ReadShiftJisString(byte[] source, int offset, int length)
        {
            if (offset < 0 || length < 1 || offset >= source.Length)
            {
                return "";
            }

            int max = Math.Min(source.Length, offset + length);
            int end = offset;
            while (end < max && source[end] != 0)
            {
                end++;
            }

            int valueLength = end - offset;
            if (valueLength < 1)
            {
                return "";
            }

            return Encoding.GetEncoding(932).GetString(source, offset, valueLength).Trim();
        }

        private static double ReadPlaytimeSeconds(byte[] source)
        {
            if (source.Length < SinkuOffsetPlaytime + sizeof(double))
            {
                return 0;
            }

            double value = BitConverter.ToDouble(source, SinkuOffsetPlaytime);
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            {
                return 0;
            }

            return value;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SinkuUnicodeDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void SinkuSetPathDelegate(nint path);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void SinkuGetFileInfoAutoDelegate(nint fileInfo);

        private sealed class SinkuMediaMeta
        {
            public string Container { get; set; } = "";
            public string Video { get; set; } = "";
            public string Audio { get; set; } = "";
            public string Extra { get; set; } = "";
            public long PlaytimeSeconds { get; set; }
        }

        /// <summary>
        /// 検索履歴(history)に新たな歴史の1ページを刻む！📖
        /// 既に存在するワードなら華麗にスルーする、無駄なことはしないスマートな登録処理だぜ！😎
        /// </summary>
        public static void InsertHistoryTable(string dbFullPath, string find_text)
        {
            try
            {
                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();

                string checkSql = "select 1 from history where find_text = @find_text limit 1";
                using (SQLiteCommand checkCmd = connection.CreateCommand())
                {
                    checkCmd.CommandText = checkSql;
                    checkCmd.Parameters.Add(new SQLiteParameter("@find_text", find_text));
                    var exists = checkCmd.ExecuteScalar();
                    if (exists != null)
                    {
                        return;
                    }
                }

                string sql = "select max(find_id) from history";
                using SQLiteCommand selectCmd = connection.CreateCommand();
                selectCmd.CommandText = sql;

                SQLiteDataAdapter da = new(selectCmd);

                long find_id = 0;
                DataTable dt = new();
                da.Fill(dt);
                if (dt.Rows.Count < 1)
                {
                    find_id = 1;
                }
                else
                {
                    if (dt.Rows[0][0].ToString() != "")
                    {
                        find_id = (long)dt.Rows[0][0] + 1; //Max + 1
                    }
                    else { }
                }

                var now = DateTime.Now;
                var result = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        "insert into history (find_id,find_text,find_date) values (@find_id,@find_text,@find_date)";

                    cmd.Parameters.Add(new SQLiteParameter("@find_id", find_id));
                    cmd.Parameters.Add(new SQLiteParameter("@find_text", find_text));
                    cmd.Parameters.Add(new SQLiteParameter("@find_date", FormatDbDateTime(result)));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }
        }

        /// <summary>
        /// 履歴があふれそうな時に、指定した件数を超えた「古き良き思い出」を一網打尽に消し飛ばす断捨離ロジック！🗑️🔥
        /// </summary>
        public static void DeleteHistoryTable(string dbFullPath, int keepHistoryCount)
        {
            try
            {
                if (keepHistoryCount < 1)
                {
                    return;
                }

                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        "DELETE from history where find_id < "
                        + "(select find_id from "
                        + "  (select find_id from history order by find_id desc LIMIT @keepHistoryCount) "
                        + " order by find_id limit 1)";
                    cmd.Parameters.Add(new SQLiteParameter("@keepHistoryCount", keepHistoryCount));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }
        }

        /// <summary>
        /// 検索ワードの歴史(findfact)テーブルに新たな1ページを刻むぜ！📝
        /// 既知のキーワードなら「またお前か！」と利用回数(find_count)をインクリメントしてやる親切設計だ！
        /// </summary>
        public static void InsertFindFactTable(string dbFullPath, string find_text)
        {
            try
            {
                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();

                string sql = "select * from findfact where find_text = @find_text";
                using SQLiteCommand selectCmd = connection.CreateCommand();
                selectCmd.CommandText = sql;
                selectCmd.Parameters.Add(new SQLiteParameter("@find_text", find_text));

                SQLiteDataAdapter da = new(selectCmd);

                long find_count = 0;
                DataTable dt = new();
                da.Fill(dt);
                bool existFlg = false;
                if (dt.Rows.Count < 1)
                {
                    find_count = 1;
                    existFlg = false;
                }
                else
                {
                    if (dt.Rows[0][0].ToString() != "")
                    {
                        find_count = (long)dt.Rows[0][1] + 1;
                        existFlg = true;
                    }
                    else
                    {
                        find_count = 1;
                        existFlg = false;
                    }
                }

                var now = DateTime.Now;
                var result = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));

                using var transaction = connection.BeginTransaction();

                using SQLiteCommand cmd = connection.CreateCommand();
                if (existFlg == false)
                {
                    cmd.CommandText =
                        "insert into findfact (find_text,find_count,last_date) values (@find_text,@find_count, @last_date)";
                }
                else
                {
                    cmd.CommandText =
                        "update findfact set find_count = @find_count , last_date = @last_date where find_text = @find_text";
                }
                cmd.Parameters.Add(new SQLiteParameter("@find_text", find_text));
                cmd.Parameters.Add(new SQLiteParameter("@find_count", find_count));
                cmd.Parameters.Add(new SQLiteParameter("@last_date", FormatDbDateTime(result)));
                cmd.ExecuteNonQuery();
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }
        }

        public static void InsertBookmarkTable(string dbFullPath, MovieInfo mvi)
        {
            ArgumentNullException.ThrowIfNull(mvi);
            InsertBookmarkTable(dbFullPath, mvi.ToMovieCore());
        }

        public static void InsertBookmarkTable(string dbFullPath, MovieRecords record)
        {
            ArgumentNullException.ThrowIfNull(record);
            InsertBookmarkTable(dbFullPath, record.ToMovieCore());
        }

        /// <summary>
        /// 大事なお気に入り動画をブックマークテーブルへ丁重にお出迎えするぜ！👑
        /// </summary>
        public static void InsertBookmarkTable(string dbFullPath, MovieCore movie)
        {
            try
            {
                (string kana, string roma) = ComputeReadingValues(movie.MovieName, movie.MoviePath);
                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();
                bool hasKanaColumn = HasTableColumn(connection, "bookmark", "kana");
                bool hasRomaColumn = HasTableColumn(connection, "bookmark", "roma");
                string sql = "select max(movie_id) from bookmark";
                using SQLiteCommand selectCmd = connection.CreateCommand();
                selectCmd.CommandText = sql;
                SQLiteDataAdapter da = new(selectCmd);
                DataTable dt = new();
                da.Fill(dt);
                long movieId = 1;
                if (dt.Rows.Count > 0 && dt.Rows[0][0].ToString() != "")
                {
                    movieId = (long)dt.Rows[0][0] + 1;
                }
                var now = DateTime.Now;
                var result = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    List<string> columns =
                    ["movie_id", "movie_name", "movie_path", "last_date", "file_date"];
                    List<string> values =
                    ["@movie_id", "@movie_name", "@movie_path", "@last_date", "@file_date"];

                    if (hasKanaColumn)
                    {
                        columns.Add("kana");
                        values.Add("@kana");
                    }

                    if (hasRomaColumn)
                    {
                        columns.Add("roma");
                        values.Add("@roma");
                    }

                    columns.Add("regist_date");
                    values.Add("@regist_date");
                    cmd.CommandText =
                        $"insert into bookmark ({string.Join(",", columns)}) values ({string.Join(",", values)})";
                    cmd.Parameters.Add(new SQLiteParameter("@movie_id", movieId));
                    cmd.Parameters.Add(
                        new SQLiteParameter("@movie_name", (movie.MovieName ?? "").ToLower())
                    );
                    cmd.Parameters.Add(
                        new SQLiteParameter("@movie_path", (movie.MoviePath ?? "").ToLower())
                    );
                    cmd.Parameters.Add(new SQLiteParameter("@last_date", FormatDbDateTime(result)));
                    cmd.Parameters.Add(new SQLiteParameter("@file_date", FormatDbDateTime(result)));
                    if (hasKanaColumn)
                    {
                        cmd.Parameters.Add(new SQLiteParameter("@kana", kana));
                    }
                    if (hasRomaColumn)
                    {
                        cmd.Parameters.Add(new SQLiteParameter("@roma", roma));
                    }
                    cmd.Parameters.Add(
                        new SQLiteParameter("@regist_date", FormatDbDateTime(result))
                    );
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }
        }

        /// <summary>
        /// ブックマーク動画の再生回数(view_count)をインクリメント！「また見たな！」と刻み込むぜ！👀✨
        /// </summary>
        public static void UpdateBookmarkViewCount(string dbFullPath, long movieId)
        {
            try
            {
                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        "update bookmark set view_count = view_count + 1 where movie_id = @id";
                    cmd.Parameters.Add(new SQLiteParameter("@id", movieId));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }
        }

        /// <summary>
        /// リネームされたファイルに合わせて、ブックマーク内の名前とパスをまとめて書き換える一斉更新マジック！🪄
        /// </summary>
        public static void UpdateBookmarkRename(string dbFullPath, string oldName, string newName)
        {
            try
            {
                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();
                oldName = oldName.ToLower();
                newName = newName.ToLower();
                (string kana, string roma) = ComputeReadingValues(newName, "");
                bool hasKanaColumn = HasTableColumn(connection, "bookmark", "kana");
                bool hasRomaColumn = HasTableColumn(connection, "bookmark", "roma");

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    List<string> assignments =
                    [
                        "movie_name = replace(movie_name, @oldName, @newName)",
                        "movie_path = replace(movie_path, @oldName, @newName)"
                    ];
                    if (hasKanaColumn)
                    {
                        assignments.Add("kana = @kana");
                    }
                    if (hasRomaColumn)
                    {
                        assignments.Add("roma = @roma");
                    }

                    cmd.CommandText =
                        "update bookmark set "
                        + string.Join(", ", assignments)
                        + " where lower(movie_name) like @likePattern";
                    cmd.Parameters.Add(new SQLiteParameter("@oldName", oldName));
                    cmd.Parameters.Add(new SQLiteParameter("@newName", newName));
                    if (hasKanaColumn)
                    {
                        cmd.Parameters.Add(new SQLiteParameter("@kana", kana));
                    }
                    if (hasRomaColumn)
                    {
                        cmd.Parameters.Add(new SQLiteParameter("@roma", roma));
                    }
                    cmd.Parameters.Add(new SQLiteParameter("@likePattern", $"%{oldName}%"));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }
        }

        /// <summary>
        /// お気に入り登録(bookmark)から対象の動画を容赦なく消し去る！さよならのお時間だ！💔
        /// </summary>
        public static void DeleteBookmarkTable(string dbFullPath, long movie_id)
        {
            try
            {
                using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "DELETE from bookmark where movie_id = @id";
                    cmd.Parameters.Add(new SQLiteParameter("@id", movie_id));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                ReportDbError(e, title);
            }
        }

        internal static List<KanaBackfillTarget> ReadMovieKanaBackfillTargets(
            string dbFullPath,
            int limit
        )
        {
            return ReadKanaBackfillTargets(dbFullPath, "movie", limit);
        }

        internal static List<KanaBackfillTarget> ReadBookmarkKanaBackfillTargets(
            string dbFullPath,
            int limit
        )
        {
            return ReadKanaBackfillTargets(dbFullPath, "bookmark", limit);
        }

        internal static int UpdateMovieKanaBatch(
            string dbFullPath,
            IReadOnlyList<KanaBackfillUpdate> updates
        )
        {
            return UpdateKanaBatch(dbFullPath, "movie", updates);
        }

        internal static int UpdateBookmarkKanaBatch(
            string dbFullPath,
            IReadOnlyList<KanaBackfillUpdate> updates
        )
        {
            return UpdateKanaBatch(dbFullPath, "bookmark", updates);
        }

        // 歴史的なメソッド名は維持しつつ、空かな/空ローマ字を少量ずつ拾って後追い補完する。
        private static List<KanaBackfillTarget> ReadKanaBackfillTargets(
            string dbFullPath,
            string tableName,
            int limit
        )
        {
            List<KanaBackfillTarget> result = [];
            if (string.IsNullOrWhiteSpace(dbFullPath) || limit <= 0)
            {
                return result;
            }

            using SQLiteConnection connection = CreateReadOnlyConnection(dbFullPath);
            connection.Open();
            bool hasKanaColumn = HasTableColumn(connection, tableName, "kana");
            bool hasRomaColumn = HasTableColumn(connection, tableName, "roma");
            if (!hasKanaColumn && !hasRomaColumn)
            {
                return result;
            }

            string kanaSelect = hasKanaColumn ? "kana" : "'' AS kana";
            string romaSelect = hasRomaColumn ? "roma" : "'' AS roma";
            string readingMissingCondition = hasKanaColumn && hasRomaColumn
                ? "(kana = '' OR roma = '')"
                : hasKanaColumn
                    ? "kana = ''"
                    : "roma = ''";

            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText =
                $@"
SELECT
    movie_id,
    movie_name,
    movie_path,
    {kanaSelect},
    {romaSelect}
FROM {tableName}
WHERE {readingMissingCondition}
  AND (movie_name <> '' OR movie_path <> '')
ORDER BY movie_id
LIMIT @limit";
            command.Parameters.Add(new SQLiteParameter("@limit", limit));

            using SQLiteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!long.TryParse(reader["movie_id"]?.ToString(), out long movieId))
                {
                    continue;
                }

                result.Add(
                    new KanaBackfillTarget(
                        movieId,
                        reader["movie_name"]?.ToString() ?? "",
                        reader["movie_path"]?.ToString() ?? "",
                        reader["kana"]?.ToString() ?? "",
                        reader["roma"]?.ToString() ?? ""
                    )
                );
            }

            return result;
        }

        // DB更新は1接続1トランザクションにまとめ、空かな/空ローマ字の後追い補完でもロックを短く保つ。
        private static int UpdateKanaBatch(
            string dbFullPath,
            string tableName,
            IReadOnlyList<KanaBackfillUpdate> updates
        )
        {
            if (string.IsNullOrWhiteSpace(dbFullPath) || updates == null || updates.Count < 1)
            {
                return 0;
            }

            using SQLiteConnection connection = CreateReadWriteConnection(dbFullPath);
            connection.Open();
            bool hasKanaColumn = HasTableColumn(connection, tableName, "kana");
            bool hasRomaColumn = HasTableColumn(connection, tableName, "roma");
            if (!hasKanaColumn && !hasRomaColumn)
            {
                return 0;
            }

            using var transaction = connection.BeginTransaction();
            using SQLiteCommand command = connection.CreateCommand();
            List<string> assignments = [];
            if (hasKanaColumn)
            {
                assignments.Add("kana = CASE WHEN @kana <> '' THEN @kana ELSE kana END");
            }
            if (hasRomaColumn)
            {
                assignments.Add("roma = CASE WHEN @roma <> '' THEN @roma ELSE roma END");
            }

            command.CommandText =
                $"UPDATE {tableName} SET {string.Join(", ", assignments)} WHERE movie_id = @id";

            SQLiteParameter idParameter = new("@id", 0L);
            SQLiteParameter kanaParameter = new("@kana", "");
            SQLiteParameter romaParameter = new("@roma", "");
            command.Parameters.Add(idParameter);
            if (hasKanaColumn)
            {
                command.Parameters.Add(kanaParameter);
            }
            if (hasRomaColumn)
            {
                command.Parameters.Add(romaParameter);
            }

            int updatedCount = 0;
            foreach (KanaBackfillUpdate update in updates)
            {
                bool kanaMissing = hasKanaColumn && string.IsNullOrWhiteSpace(update.Kana);
                bool romaMissing = hasRomaColumn && string.IsNullOrWhiteSpace(update.Roma);
                if (update.MovieId <= 0 || (kanaMissing && romaMissing))
                {
                    continue;
                }

                idParameter.Value = update.MovieId;
                if (hasKanaColumn)
                {
                    kanaParameter.Value = update.Kana ?? "";
                }
                if (hasRomaColumn)
                {
                    romaParameter.Value = update.Roma ?? "";
                }
                updatedCount += command.ExecuteNonQuery();
            }

            transaction.Commit();
            return updatedCount;
        }
    }

    internal readonly record struct KanaBackfillTarget(
        long MovieId,
        string MovieName,
        string MoviePath,
        string Kana,
        string Roma
    );

    internal readonly record struct KanaBackfillUpdate(long MovieId, string Kana, string Roma);
}
