﻿using System.Data;
using System.Data.SQLite;
using System.Reflection;
using System.Windows;

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
                MessageBox.Show(e.Message, Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(e.Message, Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Error);
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
                    cmd.CommandText ="insert into watch (dir,auto,watch,sub) values (@dir,@auto,@watch,@sub)";
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
                MessageBox.Show(e.Message, Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(e.Message, Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void DeleteMovieRecord(string dbFullPath, long movieId)
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
                MessageBox.Show(e.Message, Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static void InsertMovieTable(string dbFullPath, MovieInfo mvi)
        {
            try
            {
                using SQLiteConnection connection = new($"Data Source={dbFullPath}");
                connection.Open();

                string sql = "select max(movie_id) from movie";
                using SQLiteCommand selectCmd = connection.CreateCommand();
                selectCmd.CommandText = sql;

                // DataAdapterの生成
                SQLiteDataAdapter da = new(selectCmd);

                // データベースからデータを取得
                DataTable dt = new();
                da.Fill(dt);
                mvi.MovieId = (long)dt.Rows[0][0] + 1;

                //ここにホントはコーデックの情報とか入れるべきなんだろうなぁ。
                //todo : Sinku.dll使い方分からないのよねぇ。
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
                        "   hash) " +
                        "   values (" +
                        "   @movie_id," +
                        "   @movie_name," +
                        "   @movie_path," +
                        "   @movie_length," +
                        "   @movie_size," +
                        "   @last_date," +
                        "   @file_date," +
                        "   @regist_date," +
                        "   @hash" +
                        ")";

                    cmd.Parameters.Add(new SQLiteParameter("@movie_id", mvi.MovieId));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_name", mvi.MovieName));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_path", mvi.MoviePath));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_length", mvi.MovieLength));
                    cmd.Parameters.Add(new SQLiteParameter("@movie_size", mvi.MovieSize));
                    cmd.Parameters.Add(new SQLiteParameter("@last_date", mvi.LastDate));
                    cmd.Parameters.Add(new SQLiteParameter("@file_date", mvi.FileDate));
                    cmd.Parameters.Add(new SQLiteParameter("@regist_date", mvi.RegistDate));
                    cmd.Parameters.Add(new SQLiteParameter("@hash", mvi.Hash));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }

            // 例外が発生した場合
            catch (Exception e)
            {
                // 例外の内容を表示します。
                MessageBox.Show(e.Message, Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}