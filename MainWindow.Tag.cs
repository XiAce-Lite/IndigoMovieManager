using System.Windows;
using static IndigoMovieManager.DB.SQLite;
using static IndigoMovieManager.Thumbnail.Tools;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // タグ編集は一覧更新とDB更新を必ず対にする。
        private void TagCopy_Click(object sender, RoutedEventArgs e)
        {
            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }

            if (mv.Tags == null) { return; }
            if (mv.Tags.Length == 0) { return; }

            Clipboard.SetData(DataFormats.Text, mv.Tags);
        }

        private void TagPaste_Click(object sender, RoutedEventArgs e)
        {
            if (!Clipboard.ContainsText(TextDataFormat.Text)) { return; }

            List<MovieRecords> mv;
            mv = GetSelectedItemsByTabIndex();
            if (mv == null) { return; }

            foreach (var rec in mv)
            {
                rec.Tags = Clipboard.GetText(TextDataFormat.Text);

                List<string> tagArray = [];
                var splitTags = rec.Tags.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                foreach (var tagItem in splitTags.Distinct())
                {
                    tagArray.Add(tagItem);
                }
                rec.Tag = tagArray;

                UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, rec.Movie_Id, "tag", rec.Tags);
            }

            Refresh();
        }

        private void TagAdd_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) { return; }

            MovieRecords dt = new();
            var tagEditWindow = new TagEdit
            {
                Title = "選択全ファイルにタグを追加",
                Owner = this,
                DataContext = dt,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            tagEditWindow.ShowDialog();

            if (tagEditWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            List<MovieRecords> mv;
            mv = GetSelectedItemsByTabIndex();
            if (mv == null) { return; }

            var dataContext = tagEditWindow.DataContext as MovieRecords;
            // リスト状態のタグと、改行付のタグを作る。
            var addedTags = dataContext.Tags;

            foreach (var rec in mv)
            {
                string tagsEditedWithNewLine = rec.Tags + Environment.NewLine + addedTags;
                string tagsWithNewLine = "";
                List<string> tagArray = [];
                if (!string.IsNullOrEmpty(tagsEditedWithNewLine))
                {
                    var splitTags = tagsEditedWithNewLine.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                    tagsWithNewLine = ConvertTagsWithNewLine([.. splitTags]);

                    foreach (var tagItem in splitTags.Distinct())
                    {
                        tagArray.Add(tagItem);
                    }
                }
                rec.Tag = tagArray;
                rec.Tags = tagsWithNewLine;

                UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, rec.Movie_Id, "tag", rec.Tags);
            }
            Refresh();
        }

        private void TagDelete_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) { return; }

            MovieRecords mvSelected = GetSelectedItemByTabIndex();
            if (mvSelected == null) { return; }

            var tagEditWindow = new TagEdit
            {
                Title = "選択全ファイルからタグを削除",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                DataContext = mvSelected
            };
            tagEditWindow.ShowDialog();

            if (tagEditWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            List<MovieRecords> mv;
            mv = GetSelectedItemsByTabIndex();
            if (mv == null) { return; }

            var dataContext = tagEditWindow.DataContext as MovieRecords;
            var tagsEditedWithNewLine = dataContext.Tags;

            foreach (var rec in mv)
            {
                List<string> tagArray = rec.Tag;
                if (!string.IsNullOrEmpty(tagsEditedWithNewLine))
                {
                    var splitTags = tagsEditedWithNewLine.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var tagItem in splitTags.Distinct())
                    {
                        tagArray.Remove(tagItem);
                    }
                    var tagsWithNewLine = ConvertTagsWithNewLine([.. tagArray]);
                    rec.Tag = tagArray;
                    rec.Tags = tagsWithNewLine;

                    UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, rec.Movie_Id, "tag", rec.Tags);
                }
            }

            Refresh();
        }

        private void TagEdit_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null) { return; }

            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null) { return; }

            var tagEditWindow = new TagEdit
            {
                Title = "タグ編集",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                DataContext = mv
            };
            tagEditWindow.ShowDialog();

            if (tagEditWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            var dc = tagEditWindow.DataContext as MovieRecords;

            var tagsEditedWithNewLine = dc.Tags;
            List<string> tagArray = [];
            if (!string.IsNullOrEmpty(tagsEditedWithNewLine))
            {
                var splitTags = tagsEditedWithNewLine.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                string tagsWithNewLine = ConvertTagsWithNewLine([.. splitTags]);

                foreach (var tagItem in splitTags.Distinct())
                {
                    tagArray.Add(tagItem);
                }
                mv.Tag = tagArray;
                mv.Tags = tagsWithNewLine;
            }
            else
            {
                mv.Tag = [];
                mv.Tags = "";
            }

            UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, mv.Movie_Id, "tag", mv.Tags);

            Refresh();
        }
    }
}
