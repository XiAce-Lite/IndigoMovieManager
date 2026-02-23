using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Xml.Linq;
using IndigoMovieManager;

namespace IndigoMovieManager.DB
{
    internal class SQLite
    {
        private static readonly HashSet<string> AllowedMovieColumns =
        [
            "movie_path",
            "movie_name",
            "tag",
            "score",
            "view_count",
            "last_date",
            "movie_length"
        ];

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
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return null;
        }

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
                    cmd.CommandText = @"
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
                    cmd.CommandText = @"
                        CREATE TABLE findfact(
                        find_text text primary key not null, 
                        find_count integer not null default 0, 
                        last_date datetime not null )";
                    cmd.ExecuteNonQuery();
                    //history
                    cmd.CommandText = @"
                        CREATE TABLE history(
                        find_id integer primary key not null, 
                        find_text text not null, 
                        find_date datetime not null )";
                    cmd.ExecuteNonQuery();
                    //movie
                    cmd.CommandText = @"
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
                    cmd.CommandText = @"
                        CREATE TABLE profile(
                        skin text not null, 
                        key text not null, 
                        value text not null, 
                        primary key(skin, key))";
                    cmd.ExecuteNonQuery();
                    //sysbin
                    cmd.CommandText = @"
                        CREATE TABLE sysbin(attr text primary key not null, value blob not null )";
                    cmd.ExecuteNonQuery();
                    //system
                    cmd.CommandText = @"
                        CREATE TABLE system(attr text primary key not null, value text not null )";
                    cmd.ExecuteNonQuery();
                    //tagbar
                    cmd.CommandText = @"
                        CREATE TABLE tagbar(item_id integer primary key not null, 
                        parent_id integer not null default 0, 
                        order_id integer not null default 0, 
                        group_id integer not null default 0, 
                        title text not null default '', 
                        contents text not null default '' )";
                    cmd.ExecuteNonQuery();
                    //watch
                    cmd.CommandText = @"
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
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void InsertWatchTable(string dbFullPath, WatchRecords watchRec)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();
                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "insert into watch (dir,auto,watch,sub) values (@dir,@auto,@watch,@sub)";
                    cmd.Parameters.Add(new SQLiteParameter("@dir", watchRec.Dir));
                    cmd.Parameters.Add(new SQLiteParameter("@auto", watchRec.Auto == true ? 1 : 0));
                    cmd.Parameters.Add(new SQLiteParameter("@watch", watchRec.Watch == true ? 1 : 0));
                    cmd.Parameters.Add(new SQLiteParameter("@sub", watchRec.Sub == true ? 1 : 0));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }


            catch (Exception e)
            {
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void UpdateMovieSingleColumn(string dbFullPath, long movieId, string columnName, object value)
        {
            try
            {
                if (!AllowedMovieColumns.Contains(columnName))
                {
                    throw new ArgumentException($"許可されていない列名です: {columnName}", nameof(columnName));
                }

                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText = $"update movie set {columnName} = @value where movie_id = @id";
                    cmd.Parameters.Add(new SQLiteParameter("@id", movieId));
                    cmd.Parameters.Add(new SQLiteParameter("@value", value));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }


            catch (Exception e)
            {
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
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
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void UpdateSystemTable(string dbFullPath,string attr, string value)
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
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        public static async Task InsertMovieTable(string dbFullPath, MovieInfo mvi)
        {
            ArgumentNullException.ThrowIfNull(mvi);
            await InsertMovieTable(dbFullPath, mvi.ToMovieCore());
        }
        public static async Task InsertMovieTable(string dbFullPath, MovieCore movie)
        {
            // DB登録の本体処理。
            // 呼び出し側は MovieInfo / MovieRecords から MovieCore に寄せてここへ集約する。
            ArgumentNullException.ThrowIfNull(movie);
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();
                string sql = "select max(movie_id) from movie";
                using SQLiteCommand selectCmd = connection.CreateCommand();
                selectCmd.CommandText = sql;
                SQLiteDataAdapter da = new(selectCmd);
                DataTable dt = new();
                da.Fill(dt);
                if (dt.Rows.Count > 0 && dt.Rows[0][0].ToString() != "")
                {
                    movie.MovieId = (long)dt.Rows[0][0] + 1;
                }
                else
                {
                    movie.MovieId = 1;
                }
                string container = "";
                string video = "";
                string extra = "";
                string audio = "";
                string movieLengthText = "";
                long movieLengthLong = movie.MovieLength;
                if (Path.Exists("sinku.exe"))
                {
                    var moviePath = $"\"{movie.MoviePath}\"";
                    var arg = $"{moviePath}";
                    using Process ps1 = new();
                    ps1.StartInfo.Arguments = arg;
                    ps1.StartInfo.FileName = "sinku.exe";
                    ps1.StartInfo.CreateNoWindow = true;
                    ps1.StartInfo.RedirectStandardOutput = true;
                    ps1.Start();
                    ps1.WaitForExit();
                    string output = ps1.StandardOutput.ReadToEnd();
                    if (!string.IsNullOrEmpty(output))
                    {
                        XDocument doc = XDocument.Parse(output);
                        IEnumerable<XElement> infos = from item in doc.Elements("fields") select item;
                        foreach (XElement info in infos)
                        {
                            container = info.Element("container")?.Value ?? "";
                            video = info.Element("video")?.Value ?? "";
                            audio = info.Element("audio")?.Value ?? "";
                            extra = info.Element("extra")?.Value ?? "";
                            movieLengthText = info.Element("movie_length")?.Value ?? "";
                        }
                    }
                    if (movieLengthLong < 1 && long.TryParse(movieLengthText, out var sinkuLength))
                    {
                        movieLengthLong = sinkuLength;
                    }
                }
                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        "insert into movie (" +
                        "   movie_id," +
                        "   movie_name," +
                        "   movie_path," +
                        "   movie_length," +
                        "   movie_size," +
                        "   last_date," +
                        "   file_date," +
                        "   regist_date," +
                        "   hash, " +
                        "   container," +
                        "   video," +
                        "   audio," +
                        "   extra)" +
                        "   values (" +
                        "   @movie_id," +
                        "   @movie_name," +
                        "   @movie_path," +
                        "   @movie_length," +
                        "   @movie_size," +
                        "   @last_date," +
                        "   @file_date," +
                        "   @regist_date," +
                        "   @hash," +
                        "   @container," +
                        "   @video," +
                        "   @audio," +
                        "   @extra" +
                        ")";
                    cmd.Parameters.Add(new SQLiteParameter("@movie_id", movie.MovieId));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_name", (movie.MovieName ?? "").ToLower()));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_path", movie.MoviePath ?? ""));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_length", movieLengthLong));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_size", movie.MovieSize / 1024));
                    cmd.Parameters.Add(new SQLiteParameter("@last_date", movie.LastDate.ToLocalTime()));
                    cmd.Parameters.Add(new SQLiteParameter("@file_date", movie.FileDate.ToLocalTime()));
                    cmd.Parameters.Add(new SQLiteParameter("@regist_date", movie.RegistDate.ToLocalTime()));
                    cmd.Parameters.Add(new SQLiteParameter("@hash", movie.Hash ?? ""));
                    cmd.Parameters.Add(new SQLiteParameter("@container", container));
                    cmd.Parameters.Add(new SQLiteParameter("@video", video));
                    cmd.Parameters.Add(new SQLiteParameter("@audio", audio));
                    cmd.Parameters.Add(new SQLiteParameter("@extra", extra));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            await Task.Delay(5);
        }
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
                        find_id = (long)dt.Rows[0][0] + 1;  //Max + 1
                    }
                    else
                    {
                    }
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
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void DeleteHistoryTable(string dbFullPath, int keepHistoryCount)
        {
            try
            {
                if (keepHistoryCount < 1) { return; }

                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText =
                        "DELETE from history where find_id < " +
                        "(select find_id from " +
                        "  (select find_id from history order by find_id desc LIMIT @keepHistoryCount) " +
                        " order by find_id limit 1)";
                    cmd.Parameters.Add(new SQLiteParameter("@keepHistoryCount", keepHistoryCount));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }


            catch (Exception e)
            {
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
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
                        "insert into bookmark (" +
                        "   movie_id," +
                        "   movie_name," +
                        "   movie_path," +
                        "   last_date," +
                        "   file_date," +
                        "   regist_date)" +
                        "   values (" +
                        "   @movie_id," +
                        "   @movie_name," +
                        "   @movie_path," +
                        "   @last_date," +
                        "   @file_date," +
                        "   @regist_date)";
                    cmd.Parameters.Add(new SQLiteParameter("@movie_id", movieId));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_name", (movie.MovieName ?? "").ToLower()));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_path", (movie.MoviePath ?? "").ToLower()));
                    cmd.Parameters.Add(new SQLiteParameter("@last_date", result));
                    cmd.Parameters.Add(new SQLiteParameter("@file_date", result));
                    cmd.Parameters.Add(new SQLiteParameter("@regist_date", result));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch (Exception e)
            {
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        public static void UpdateBookmarkViewCount(string dbFullPath, long movieId)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText = "update bookmark set view_count = view_count + 1 where movie_id = @id";
                    cmd.Parameters.Add(new SQLiteParameter("@id", movieId));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }


            catch (Exception e)
            {
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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
                        "update bookmark set " +
                        "movie_name = replace(movie_name, @oldName, @newName), " +
                        "movie_path = replace(movie_path, @oldName, @newName) " +
                        "where lower(movie_name) like @likePattern";
                    cmd.Parameters.Add(new SQLiteParameter("@oldName", oldName));
                    cmd.Parameters.Add(new SQLiteParameter("@newName", newName));
                    cmd.Parameters.Add(new SQLiteParameter("@likePattern", $"%{oldName}%"));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }


            catch (Exception e)
            {
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
