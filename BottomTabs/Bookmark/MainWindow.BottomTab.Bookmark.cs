using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IndigoMovieManager.Thumbnail;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // Bookmarkタブ側で使うフォルダ解決を1か所へ寄せる。
        private string ResolveBookmarkFolderPath()
        {
            string bookmarkFolder = MainVM?.DbInfo?.BookmarkFolder ?? "";
            string dbName = MainVM?.DbInfo?.DBName ?? "";
            if (string.IsNullOrWhiteSpace(dbName))
            {
                return bookmarkFolder;
            }

            string defaultBookmarkFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                "bookmark",
                dbName
            );
            return string.IsNullOrWhiteSpace(bookmarkFolder)
                ? defaultBookmarkFolder
                : bookmarkFolder;
        }

        // Bookmark一覧の見た目更新は、この窓口を通す。
        private void RefreshBookmarkTabView()
        {
            BookmarkList?.Items.Refresh();
        }

        // DB再読込と一覧更新をセットで呼びたい場所が多いため、ここへ集約する。
        private void ReloadBookmarkTabData()
        {
            GetBookmarkTable();
            RefreshBookmarkTabView();
        }

        /// <summary>
        /// bookmarkテーブルを読み込み、画面表示用のコレクションを爆速で再構築！お気に入りを蘇らせる！💖
        /// </summary>
        private void GetBookmarkTable()
        {
            if (string.IsNullOrWhiteSpace(MainVM?.DbInfo?.DBFullPath))
            {
                bookmarkData?.Clear();
                MainVM?.BookmarkRecs.Clear();
                return;
            }

            bookmarkData = GetData(MainVM.DbInfo.DBFullPath, "select * from bookmark");
            MainVM.BookmarkRecs.Clear();
            if (bookmarkData == null)
            {
                return;
            }

            string bookmarkFolder = ResolveBookmarkFolderPath();
            DataRow[] list = bookmarkData.AsEnumerable().ToArray();
            foreach (DataRow row in list)
            {
                string movieFullPath = row["movie_path"].ToString();
                string ext = Path.GetExtension(movieFullPath);
                string thumbFile = Path.Combine(bookmarkFolder, movieFullPath);
                string thumbBody = movieFullPath.Split('[')[0];
                string frameS = movieFullPath.Split('(')[1];
                frameS = frameS.Split(')')[0];
                long frame = 0;
                if (frameS != "")
                {
                    frame = Convert.ToInt64(frameS);
                }

                var item = new MovieRecords
                {
                    Movie_Id = (long)row["movie_id"],
                    Movie_Name = $"{row["movie_name"]}{ext}",
                    Movie_Body = thumbBody,
                    Last_Date = ((DateTime)row["last_date"]).ToString("yyyy-MM-dd HH:mm:ss"),
                    File_Date = ((DateTime)row["file_date"]).ToString("yyyy-MM-dd HH:mm:ss"),
                    Regist_Date = ((DateTime)row["regist_date"]).ToString(
                        "yyyy-MM-dd HH:mm:ss"
                    ),
                    View_Count = (long)row["view_count"],
                    Score = frame,
                    Kana = row["kana"].ToString(),
                    Roma = row["roma"].ToString(),
                    IsExists = true,
                    Ext = ext,
                    ThumbDetail = thumbFile,
                };
                MainVM.BookmarkRecs.Add(item);
            }
        }

        // Bookmarkタブ上の削除は、DBと一覧再構築をまとめて処理する。
        public void DeleteBookmark(object sender, RoutedEventArgs e)
        {
            if (sender is not Button deleteButton)
            {
                return;
            }

            if (deleteButton.DataContext is not MovieRecords item)
            {
                return;
            }

            DeleteBookmarkTable(MainVM.DbInfo.DBFullPath, item.Movie_Id);
            ReloadBookmarkTabData();
        }

        // 再生位置からBookmarkサムネを作り、一覧まで更新する。
        private async void AddBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                return;
            }

            timer.Stop();
            uxVideoPlayer.Pause();

            PlayerArea.Visibility = Visibility.Collapsed;
            PlayerController.Visibility = Visibility.Collapsed;
            uxVideoPlayer.Visibility = Visibility.Collapsed;

            MovieInfo mvi = new(mv.Movie_Path, true);

            int pos = (int)uxVideoPlayer.Position.TotalSeconds;
            int targetFrame = pos * (int)mvi.FPS;
            string timestamp = string.Format($"{DateTime.Now:HH-mm-ss}");
            string thumbBody = $"{mv.Movie_Body}[({targetFrame}){timestamp}]";
            string thumbFileName = Path.Combine(
                ResolveBookmarkFolderPath(),
                $"{thumbBody}.jpg"
            );
            string thumbFolder = Path.GetDirectoryName(thumbFileName) ?? "";
            if (!Path.Exists(thumbFolder))
            {
                Directory.CreateDirectory(thumbFolder);
            }

            await Task.Delay(10);
            _ = CreateBookmarkThumbAsync(mv.Movie_Path, thumbFileName, pos);

            uxVideoPlayer.Stop();
            IsPlaying = false;

            mvi.MovieName = thumbBody;
            mvi.MoviePath = $"{thumbBody}.jpg";
            InsertBookmarkTable(MainVM.DbInfo.DBFullPath, mvi);
            ReloadBookmarkTabData();
        }
    }
}
