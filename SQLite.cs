using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Xml.Linq;

namespace IndigoMovieManager
{
    internal class SQLite
    {
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

                    // DataAdapterの生成
                    SQLiteDataAdapter da = new(cmd);

                    // データベースからデータを取得
                    da.Fill(dt);
                }
                return dt;

            }
            catch (Exception e)
            {
                // 例外の内容を表示します。
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return null;
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

            // 例外が発生した場合
            catch (Exception e)
            {
                // 例外の内容を表示します。
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

            // 例外が発生した場合
            catch (Exception e)
            {
                // 例外の内容を表示します。
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void UpdateMovieSingleColumn(string dbFullPath, long movieId, string columnName, object value)
        {
            try
            {
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

            // 例外が発生した場合
            catch (Exception e)
            {
                // 例外の内容を表示します。
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void InsertSystemTable(string dbFullPath, string attr, string value)
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

            // 例外が発生した場合
            catch (Exception e)
            {
                // 例外の内容を表示します。
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void UpdateSystemTable(string dbFullPath,string attr, string value)
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

            // 例外が発生した場合
            catch (Exception e)
            {
                // 例外の内容を表示します。
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
                    cmd.CommandText = $"delete from movie where movie_id = {movieId}";
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }

            // 例外が発生した場合
            catch (Exception e)
            {
                // 例外の内容を表示します。
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void InsertMovieTable(string dbFullPath, MovieInfo mvi)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();

                // データベースから最大IDを取得
                string sql = "select max(movie_id) from movie";
                using SQLiteCommand selectCmd = connection.CreateCommand();
                selectCmd.CommandText = sql;

                // DataAdapterの生成
                SQLiteDataAdapter da = new(selectCmd);

                DataTable dt = new();
                da.Fill(dt);
                if (dt.Rows.Count < 1) 
                {
                    mvi.MovieId = 1;    //ゼロ行なので、1
                }
                else
                {
                    if (dt.Rows[0][0].ToString() != "")
                    {
                        mvi.MovieId = (long)dt.Rows[0][0] + 1;  //Max + 1
                    }
                    else
                    {
                        //ここ、通らない気がする。
                        mvi.MovieId = 1;    //ゼロ行なので、1
                    }
                }

                string container = "";
                string video = "";
                string extra = "";
                string audio = "";
                string movie_length = "";
                long movieLengthLong = mvi.MovieLength;

                //結局、断念してsinku.dllから取得。sinku.exeにパラメータ渡して、実態はsinku.dll。
                //フォーマットやコーデックのマッチングに、codecs.ini, format.ini は必要。
                //実行条件としては、sinku.exe の存在有無。これがないと転けるが、あれば出力時のエラー表記なので。
                if (Path.Exists("sinku.exe"))
                {
                    var moviePath = $"\"{mvi.MoviePath}\"";
                    var arg = $"{moviePath}";

                    using Process ps1 = new();
                    //設定ファイルのプログラムも既定のプログラムも空だった場合にはここのはず。
                    ps1.StartInfo.Arguments = arg;
                    ps1.StartInfo.FileName = "sinku.exe";
                    ps1.StartInfo.CreateNoWindow = true;
                    ps1.StartInfo.RedirectStandardOutput = true;

                    ps1.Start();
                    ps1.WaitForExit();

                    string output = ps1.StandardOutput.ReadToEnd();

                    XDocument doc = XDocument.Parse(output);

                    //パクリ元：https://www.sejuku.net/blog/86867
                    IEnumerable<XElement> infos = from item in doc.Elements("fields") select item;

                    //多分構造的に一周しかしない。
                    foreach (XElement info in infos)
                    {
                        container = info.Element("container").Value;
                        video = info.Element("video").Value;
                        audio = info.Element("audio").Value;
                        extra = info.Element("extra").Value;
                        movie_length = info.Element("movie_length").Value;
                    }
                    if (movieLengthLong < 1)
                    {
                        movieLengthLong = Convert.ToInt64(movie_length);
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

                    cmd.Parameters.Add(new SQLiteParameter("@movie_id", mvi.MovieId));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_name", mvi.MovieName.ToLower()));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_path", mvi.MoviePath));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_length", movieLengthLong));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_size", mvi.MovieSize / 1024));
                    cmd.Parameters.Add(new SQLiteParameter("@last_date", mvi.LastDate.ToLocalTime()));
                    cmd.Parameters.Add(new SQLiteParameter("@file_date", mvi.FileDate.ToLocalTime()));
                    cmd.Parameters.Add(new SQLiteParameter("@regist_date", mvi.RegistDate.ToLocalTime()));
                    cmd.Parameters.Add(new SQLiteParameter("@hash", mvi.Hash));
                    cmd.Parameters.Add(new SQLiteParameter("@container", container));
                    cmd.Parameters.Add(new SQLiteParameter("@video", video));
                    cmd.Parameters.Add(new SQLiteParameter("@audio", audio));
                    cmd.Parameters.Add(new SQLiteParameter("@extra", extra));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }

            // 例外が発生した場合
            catch (Exception e)
            {
                // 例外の内容を表示します。
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void InsertHistoryTable(string dbFullPath, string find_text)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();

                // データベースから最大IDを取得
                string sql = "select max(find_id) from history";
                using SQLiteCommand selectCmd = connection.CreateCommand();
                selectCmd.CommandText = sql;

                // DataAdapterの生成
                SQLiteDataAdapter da = new(selectCmd);

                long find_id = 0;
                DataTable dt = new();
                da.Fill(dt);
                if (dt.Rows.Count < 1) 
                {
                    find_id = 1;    //ゼロ行なので、1
                }
                else
                {
                    if (dt.Rows[0][0].ToString() != "")
                    {
                        find_id = (long)dt.Rows[0][0] + 1;  //Max + 1
                    }
                    else
                    {
                        //ここ、通らない気がする。
                        find_id = 1;    //ゼロ行なので、1
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

            // 例外が発生した場合
            catch (Exception e)
            {
                // 例外の内容を表示します。
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void DeleteHistoryTable(string dbFullPath, int keepHistoryCount)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();

                using var transaction = connection.BeginTransaction();
                using (SQLiteCommand cmd = connection.CreateCommand())
                {
                    cmd.CommandText = 
                        $"DELETE from history where find_id < " +
                        $"(select find_id from " +
                        $"  (select find_id from history order by find_id desc LIMIT {keepHistoryCount}) " +
                        $" order by find_id limit 1)";
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }

            // 例外が発生した場合
            catch (Exception e)
            {
                // 例外の内容を表示します。
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

                // データベースから既存レコードを取得
                string sql = $"select * from findfact where find_text = '{find_text}'";
                using SQLiteCommand selectCmd = connection.CreateCommand();
                selectCmd.CommandText = sql;

                // DataAdapterの生成
                SQLiteDataAdapter da = new(selectCmd);

                long find_count = 0;
                DataTable dt = new();
                da.Fill(dt);
                bool existFlg = false;
                if (dt.Rows.Count < 1) 
                {
                    //新規レコード
                    find_count = 1;
                    existFlg = false;
                }
                else
                {
                    if (dt.Rows[0][0].ToString() != "")
                    {
                        //既にある。
                        find_count = (long)dt.Rows[0][1] + 1;
                        existFlg = true;
                    }
                    else
                    {
                        //新規レコード
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

            // 例外が発生した場合
            catch (Exception e)
            {
                // 例外の内容を表示します。
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void InsertBookmarkTable(string dbFullPath, MovieInfo mvi)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();

                // データベースから最大IDを取得
                string sql = "select max(movie_id) from bookmark";
                using SQLiteCommand selectCmd = connection.CreateCommand();
                selectCmd.CommandText = sql;

                // DataAdapterの生成
                SQLiteDataAdapter da = new(selectCmd);

                DataTable dt = new();
                da.Fill(dt);
                if (dt.Rows.Count < 1) 
                {
                    mvi.MovieId = 1;    //ゼロ行なので、1
                }
                else
                {
                    if (dt.Rows[0][0].ToString() != "")
                    {
                        mvi.MovieId = (long)dt.Rows[0][0] + 1;  //Max + 1
                    }
                    else
                    {
                        mvi.MovieId = 1;    //ゼロ行なので、1
                    }
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

                    cmd.Parameters.Add(new SQLiteParameter("@movie_id", mvi.MovieId));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_name", mvi.MovieName.ToLower()));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_path", mvi.MoviePath.ToLower()));
                    cmd.Parameters.Add(new SQLiteParameter("@last_date", result));
                    cmd.Parameters.Add(new SQLiteParameter("@file_date", result));
                    cmd.Parameters.Add(new SQLiteParameter("@regist_date", result));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }

            // 例外が発生した場合
            catch (Exception e)
            {
                // 例外の内容を表示します。
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
                    cmd.CommandText = $"update bookmark set view_count = view_count + 1 where movie_id = @id";
                    cmd.Parameters.Add(new SQLiteParameter("@id", movieId));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }

            // 例外が発生した場合
            catch (Exception e)
            {
                // 例外の内容を表示します。
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
                        $"update bookmark set " +
                        $"movie_name = replace(movie_name,'{oldName}', '{newName}'), " +
                        $"movie_path = replace(movie_path,'{oldName}', '{newName}') " +
                        $"where lower(movie_name) like '%{oldName}%'";
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }

            // 例外が発生した場合
            catch (Exception e)
            {
                // 例外の内容を表示します。
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
                    cmd.CommandText =
                        $"DELETE from bookmark where movie_id = {movie_id}";
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }

            // 例外が発生した場合
            catch (Exception e)
            {
                // 例外の内容を表示します。
                var title = $"{Assembly.GetExecutingAssembly().GetName().Name} - {MethodBase.GetCurrentMethod().Name}";
                MessageBox.Show(e.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
