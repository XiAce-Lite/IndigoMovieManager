using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using IndigoMovieManager;

namespace IndigoMovieManager.DB
{
    /// <summary>
    /// SQLiteデータベースとの熱い語らい（CRUD処理）を全て引き受ける最強の裏方クラス！🛡️
    /// テーブルの初期化からレコードの追加・更新・削除まで、データの命運はこいつが握ってるぜ！🔥
    /// </summary>
    internal class SQLite
    {
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
                using (SQLiteConnection connection = new($"Data Source={dbFullPath}"))
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
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return null;
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

            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
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

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
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

        /// <summary>
        /// まっさらな大地にSQLiteファイルを生み出し、アプリの命とも言える9つのテーブル群
        /// (bookmark, history, movie, watch等) を怒涛の建国ラッシュで一斉構築する始まりの儀式！🏗️✨
        /// </summary>
        public static void CreateDatabase(string dbFullPath)
        {
            try
            {
                SQLiteConnection.CreateFile(dbFullPath);
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
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
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// watch(監視)テーブルの記憶を全てあの世へ送り放つ、無慈悲な全消去魔法（DELETE文）！💣💥
        /// </summary>
        public static void DeleteWatchTable(string dbFullPath)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
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
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// watch(監視)テーブルへ新たな監視対象（ディレクトリ）をブチ込む！👁️✨
        /// </summary>
        public static void InsertWatchTable(string dbFullPath, WatchRecords watchRec)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
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
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
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

                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        $"update movie set {columnName} = @value where movie_id = @id";
                    cmd.Parameters.Add(new SQLiteParameter("@id", movieId));
                    cmd.Parameters.Add(new SQLiteParameter("@value", value));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// system(設定)テーブルをチェック！すでにキーがあればUpdate、無ければInsertする賢きUpsert（上書き追加）処理だ！🧠
        /// </summary>
        public static void UpsertSystemTable(string dbFullPath, string attr, string value)
        {
            bool exists = false;
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();

                using SQLiteCommand cmd = connection.CreateCommand();
                cmd.CommandText = "select 1 from system where attr = @attr limit 1";
                cmd.Parameters.Add(new SQLiteParameter("@attr", attr));
                exists = cmd.ExecuteScalar() != null;
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (exists)
            {
                UpdateSystemTable(dbFullPath, attr, value);
                return;
            }

            InsertSystemTable(dbFullPath, attr, value);
        }

        private static void InsertSystemTable(string dbFullPath, string attr, string value)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "insert into system (attr, value) values (@attr, @value)";
                    cmd.Parameters.Add(new SQLiteParameter("@attr", attr));
                    cmd.Parameters.Add(new SQLiteParameter("@value", value));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void UpdateSystemTable(string dbFullPath, string attr, string value)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "update system set value = @value where attr = @attr";
                    cmd.Parameters.Add(new SQLiteParameter("@attr", attr));
                    cmd.Parameters.Add(new SQLiteParameter("@value", value));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 指定されたムービーをmovieテーブルから完全に消し飛ばす！さらば友よ！👋
        /// </summary>
        public static void DeleteMovieTable(string dbFullPath, long movieId)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "delete from movie where movie_id = @id";
                    cmd.Parameters.Add(new SQLiteParameter("@id", movieId));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// history(検索履歴)から不要な過去を一つだけ抹消する黒歴史クリーナーだ！🧽
        /// </summary>
        public static void DeleteHistoryTable(string dbFullPath, long findId)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
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
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static Task InsertMovieTable(string dbFullPath, MovieInfo mvi)
        {
            ArgumentNullException.ThrowIfNull(mvi);
            return InsertMovieTable(dbFullPath, mvi.ToMovieCore());
        }

        /// <summary>
        /// movieテーブルへ新規メンバー（動画）を招き入れる心臓部の処理！
        /// アプリ中から集まったMovieInfoやMovieRecordsたちは、すべてMovieCore型に姿を変えてここに集結するぜ！🦸‍♂️
        /// </summary>
        public static Task InsertMovieTable(string dbFullPath, MovieCore movie)
        {
            // DB登録の本体処理フロー:
            // 1. 引数チェック
            ArgumentNullException.ThrowIfNull(movie);
            try
            {
                Stopwatch totalStopwatch = Stopwatch.StartNew();
                bool isProbeTarget = IsDbInsertProbeTargetMoviePath(movie.MoviePath ?? "");
                long sinkuMs = 0;
                long insertMs = 0;
                bool sinkuSucceeded = false;

                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();
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
                    cmd.CommandText =
                        "insert into movie ("
                        + "   movie_name,"
                        + "   movie_path,"
                        + "   movie_length,"
                        + "   movie_size,"
                        + "   last_date,"
                        + "   file_date,"
                        + "   regist_date,"
                        + "   hash, "
                        + "   container,"
                        + "   video,"
                        + "   audio,"
                        + "   extra)"
                        + "   values ("
                        + "   @movie_name,"
                        + "   @movie_path,"
                        + "   @movie_length,"
                        + "   @movie_size,"
                        + "   @last_date,"
                        + "   @file_date,"
                        + "   @regist_date,"
                        + "   @hash,"
                        + "   @container,"
                        + "   @video,"
                        + "   @audio,"
                        + "   @extra"
                        + ")";
                    cmd.Parameters.Add(
                        new SQLiteParameter("@movie_name", (movie.MovieName ?? "").ToLower())
                    );
                    cmd.Parameters.Add(new SQLiteParameter("@movie_path", movie.MoviePath ?? ""));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_length", movieLengthLong));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_size", movie.MovieSize / 1024));
                    cmd.Parameters.Add(
                        new SQLiteParameter("@last_date", movie.LastDate.ToLocalTime())
                    );
                    cmd.Parameters.Add(
                        new SQLiteParameter("@file_date", movie.FileDate.ToLocalTime())
                    );
                    cmd.Parameters.Add(
                        new SQLiteParameter("@regist_date", movie.RegistDate.ToLocalTime())
                    );
                    cmd.Parameters.Add(new SQLiteParameter("@hash", movie.Hash ?? ""));
                    cmd.Parameters.Add(new SQLiteParameter("@container", container));
                    cmd.Parameters.Add(new SQLiteParameter("@video", video));
                    cmd.Parameters.Add(new SQLiteParameter("@audio", audio));
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
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// movieテーブルへ大量の猛者たち（複数レコード）を一撃必殺の1トランザクションでブチ込む大魔法だ！🌪️
        /// 呼び出し元への土産として、採番されたピカピカのID(MovieId)をそれぞれに刻み込んで返す気の利いた仕様だぜ！😎
        /// </summary>
        public static Task InsertMovieTableBatch(string dbFullPath, List<MovieCore> movies)
        {
            ArgumentNullException.ThrowIfNull(movies);
            if (movies.Count < 1)
            {
                return Task.CompletedTask;
            }

            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using SQLiteCommand insertCmd = connection.CreateCommand();
                insertCmd.CommandText =
                    "insert into movie ("
                    + "   movie_name,"
                    + "   movie_path,"
                    + "   movie_length,"
                    + "   movie_size,"
                    + "   last_date,"
                    + "   file_date,"
                    + "   regist_date,"
                    + "   hash, "
                    + "   container,"
                    + "   video,"
                    + "   audio,"
                    + "   extra)"
                    + "   values ("
                    + "   @movie_name,"
                    + "   @movie_path,"
                    + "   @movie_length,"
                    + "   @movie_size,"
                    + "   @last_date,"
                    + "   @file_date,"
                    + "   @regist_date,"
                    + "   @hash,"
                    + "   @container,"
                    + "   @video,"
                    + "   @audio,"
                    + "   @extra"
                    + ")";

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
                        new SQLiteParameter("@last_date", movie.LastDate.ToLocalTime())
                    );
                    insertCmd.Parameters.Add(
                        new SQLiteParameter("@file_date", movie.FileDate.ToLocalTime())
                    );
                    insertCmd.Parameters.Add(
                        new SQLiteParameter("@regist_date", movie.RegistDate.ToLocalTime())
                    );
                    insertCmd.Parameters.Add(new SQLiteParameter("@hash", movie.Hash ?? ""));
                    insertCmd.Parameters.Add(new SQLiteParameter("@container", container));
                    insertCmd.Parameters.Add(new SQLiteParameter("@video", video));
                    insertCmd.Parameters.Add(new SQLiteParameter("@audio", audio));
                    insertCmd.Parameters.Add(new SQLiteParameter("@extra", extra));
                    insertCmd.ExecuteNonQuery();

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
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return Task.CompletedTask;
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
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
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
                    cmd.Parameters.Add(new SQLiteParameter("@find_date", result));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
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

                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
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
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
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
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
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
                cmd.Parameters.Add(new SQLiteParameter("@last_date", result));
                cmd.ExecuteNonQuery();
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
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
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();
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
                    cmd.CommandText =
                        "insert into bookmark ("
                        + "   movie_id,"
                        + "   movie_name,"
                        + "   movie_path,"
                        + "   last_date,"
                        + "   file_date,"
                        + "   regist_date)"
                        + "   values ("
                        + "   @movie_id,"
                        + "   @movie_name,"
                        + "   @movie_path,"
                        + "   @last_date,"
                        + "   @file_date,"
                        + "   @regist_date)";
                    cmd.Parameters.Add(new SQLiteParameter("@movie_id", movieId));
                    cmd.Parameters.Add(
                        new SQLiteParameter("@movie_name", (movie.MovieName ?? "").ToLower())
                    );
                    cmd.Parameters.Add(
                        new SQLiteParameter("@movie_path", (movie.MoviePath ?? "").ToLower())
                    );
                    cmd.Parameters.Add(new SQLiteParameter("@last_date", result));
                    cmd.Parameters.Add(new SQLiteParameter("@file_date", result));
                    cmd.Parameters.Add(new SQLiteParameter("@regist_date", result));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// ブックマーク動画の再生回数(view_count)をインクリメント！「また見たな！」と刻み込むぜ！👀✨
        /// </summary>
        public static void UpdateBookmarkViewCount(string dbFullPath, long movieId)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
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
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// リネームされたファイルに合わせて、ブックマーク内の名前とパスをまとめて書き換える一斉更新マジック！🪄
        /// </summary>
        public static void UpdateBookmarkRename(string dbFullPath, string oldName, string newName)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();

                oldName = oldName.ToLower();
                newName = newName.ToLower();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        "update bookmark set "
                        + "movie_name = replace(movie_name, @oldName, @newName), "
                        + "movie_path = replace(movie_path, @oldName, @newName) "
                        + "where lower(movie_name) like @likePattern";
                    cmd.Parameters.Add(new SQLiteParameter("@oldName", oldName));
                    cmd.Parameters.Add(new SQLiteParameter("@newName", newName));
                    cmd.Parameters.Add(new SQLiteParameter("@likePattern", $"%{oldName}%"));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title =
                    $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// お気に入り登録(bookmark)から対象の動画を容赦なく消し去る！さよならのお時間だ！💔
        /// </summary>
        public static void DeleteBookmarkTable(string dbFullPath, long movie_id)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
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
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
