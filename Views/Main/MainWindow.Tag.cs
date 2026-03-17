using System.Windows;
using IndigoMovieManager.Thumbnail;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // =================================================================================
        // タグ操作に関する UI イベント処理 (View層のロジック)
        // タグのコピー、ペースト、追加、削除などの一括操作において、
        // メモリ（ViewModel）のレコード更新とDB更新を確実に対で実行するフローを担う。
        // =================================================================================

        /// <summary>
        /// タグのコピー機能。
        /// 選択されている動画が持つタグの文字列（改行区切り）をクリップボードにコピーする。
        /// </summary>
        private void TagCopy_Click(object sender, RoutedEventArgs e)
        {
            MovieRecords mv = GetSelectedItemByTabIndex();
            if (mv == null)
            {
                return;
            }

            if (mv.Tags == null)
            {
                return;
            }
            if (mv.Tags.Length == 0)
            {
                return;
            }

            // 文字列情報としてクリップボードへ保存
            Clipboard.SetData(DataFormats.Text, mv.Tags);
        }

        /// <summary>
        /// タグのペースト（上書き）機能。
        /// クリップボードにあるタグ文字列を、選択された「全て」の動画にそのまま上書く。
        /// </summary>
        private void TagPaste_Click(object sender, RoutedEventArgs e)
        {
            if (!Clipboard.ContainsText(TextDataFormat.Text))
            {
                return;
            }

            // 複数選択されているすべての対象動画を取得
            List<MovieRecords> mv;
            mv = GetSelectedItemsByTabIndex();
            if (mv == null)
            {
                return;
            }

            // 各レコードに対してペーストしDBも更新する
            foreach (var rec in mv)
            {
                // UI用文字列プロパティに直接設定
                rec.Tags = Clipboard.GetText(TextDataFormat.Text);

                List<string> tagArray = [];
                // 配列型の Tag プロパティへもパースして同期する
                // （重複するタグ名は Distinct() で排除する）
                var splitTags = rec.Tags.Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries
                );
                foreach (var tagItem in splitTags.Distinct())
                {
                    tagArray.Add(tagItem);
                }
                rec.Tag = tagArray;

                // 最後に単一カラムのみをDBへUPDATEする
                UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, rec.Movie_Id, "tag", rec.Tags);
            }

            // 万一画面表示がズレないよう一覧を再描画
            Refresh();
        }

        /// <summary>
        /// 既存のタグは残したまま、新しいタグを選択対象「全て」に追記する。
        /// </summary>
        private void TagAdd_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            // TagEditダイアログ用の一時コンテナを作りバインドする
            MovieRecords dt = new();
            var tagEditWindow = new TagEdit
            {
                Title = "選択全ファイルにタグを追加",
                Owner = this,
                DataContext = dt,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            tagEditWindow.ShowDialog();

            if (tagEditWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            List<MovieRecords> mv;
            mv = GetSelectedItemsByTabIndex();
            if (mv == null)
            {
                return;
            }

            var dataContext = tagEditWindow.DataContext as MovieRecords;

            // ダイアログで入力された追記用のタグ文字列
            var addedTags = dataContext.Tags;

            // 選択された各動画に対して合成と重複排除を行う
            foreach (var rec in mv)
            {
                // 既存タグの後ろに改行をつけて足す
                string tagsEditedWithNewLine = rec.Tags + Environment.NewLine + addedTags;
                string tagsWithNewLine = "";
                List<string> tagArray = [];

                if (!string.IsNullOrEmpty(tagsEditedWithNewLine))
                {
                    // 改行でバラし、空行を消し、重複(Distinct)を排除しつつ再結合・リスト化
                    var splitTags = tagsEditedWithNewLine.Split(
                        Environment.NewLine,
                        StringSplitOptions.RemoveEmptyEntries
                    );
                    tagsWithNewLine = ThumbnailTagFormatter.ConvertTagsWithNewLine([.. splitTags]);

                    foreach (var tagItem in splitTags.Distinct())
                    {
                        tagArray.Add(tagItem);
                    }
                }
                rec.Tag = tagArray;
                rec.Tags = tagsWithNewLine;

                // 変更された文字列タグ情報をDBへ保存
                UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, rec.Movie_Id, "tag", rec.Tags);
            }
            Refresh();
        }

        /// <summary>
        /// 指定した特定のタグを、選択対象「全て」から引き算（削除）する。
        /// </summary>
        private void TagDelete_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            // ダイアログ側の初期表示用として「先頭で選択中の動画」の内容を便宜上渡す
            MovieRecords mvSelected = GetSelectedItemByTabIndex();
            if (mvSelected == null)
            {
                return;
            }

            var tagEditWindow = new TagEdit
            {
                Title = "選択全ファイルからタグを削除",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                DataContext = mvSelected,
            };
            tagEditWindow.ShowDialog();

            if (tagEditWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            List<MovieRecords> mv;
            mv = GetSelectedItemsByTabIndex();
            if (mv == null)
            {
                return;
            }

            var dataContext = tagEditWindow.DataContext as MovieRecords;
            // 削除したい対象として入力された「タグ群」
            var tagsEditedWithNewLine = dataContext.Tags;

            foreach (var rec in mv)
            {
                List<string> tagArray = rec.Tag;
                if (!string.IsNullOrEmpty(tagsEditedWithNewLine))
                {
                    // 削除指定されたタグをひとつずつ調べ、配列から Remove() で引いていく
                    var splitTags = tagsEditedWithNewLine.Split(
                        Environment.NewLine,
                        StringSplitOptions.RemoveEmptyEntries
                    );
                    foreach (var tagItem in splitTags.Distinct())
                    {
                        tagArray.Remove(tagItem);
                    }

                    // 引き算が終わったリストをご破算にして改行文字列に再形成
                    var tagsWithNewLine = ThumbnailTagFormatter.ConvertTagsWithNewLine([.. tagArray]);
                    rec.Tag = tagArray;
                    rec.Tags = tagsWithNewLine;

                    // DB更新
                    UpdateMovieSingleColumn(
                        MainVM.DbInfo.DBFullPath,
                        rec.Movie_Id,
                        "tag",
                        rec.Tags
                    );
                }
            }

            Refresh();
        }

        /// <summary>
        /// (単一用) 選択中動画のタグ直接編集。追加ボタンや削除ボタンよりも細かい手動編集に使う。
        /// 処理フローとしては TagAdd等とほぼ同じだが、1件だけの操作となる。
        /// </summary>
        private void TagEdit_Click(object sender, RoutedEventArgs e)
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

            var tagEditWindow = new TagEdit
            {
                Title = "タグ編集",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                DataContext = mv,
            };
            tagEditWindow.ShowDialog();

            if (tagEditWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            var dc = tagEditWindow.DataContext as MovieRecords;

            var tagsEditedWithNewLine = dc.Tags;
            List<string> tagArray = [];

            // 編集後の文字列を整頓してViewModel内の配列・文字列プロパティを更新
            if (!string.IsNullOrEmpty(tagsEditedWithNewLine))
            {
                var splitTags = tagsEditedWithNewLine.Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries
                );
                string tagsWithNewLine = ThumbnailTagFormatter.ConvertTagsWithNewLine([.. splitTags]);

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

            // DB更新
            UpdateMovieSingleColumn(MainVM.DbInfo.DBFullPath, mv.Movie_Id, "tag", mv.Tags);

            Refresh();
        }
    }
}
