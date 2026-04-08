using System.Windows;
using IndigoMovieManager.Thumbnail;
using System.Linq;
using Notification.Wpf;
using System.Windows.Controls;
using System.Windows.Media;

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
                _mainDbMovieMutationFacade.UpdateTag(
                    MainVM.DbInfo.DBFullPath,
                    rec.Movie_Id,
                    rec.Tags
                );
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

            List<MovieRecords> mv;
            mv = GetSelectedItemsByTabIndex();
            if (mv == null)
            {
                return;
            }

            PromptAndAddTagsToRecords(mv, "選択全ファイルにタグを追加");
        }

        /// <summary>
        /// メイン検索バー横の「タグ付...」から、対象範囲を選んで一括タグ付けする。
        /// </summary>
        private async void BulkTagAssignButton_Click(object sender, RoutedEventArgs e)
        {
            if (Tabs.SelectedItem == null)
            {
                return;
            }

            string searchKeyword = MainVM?.DbInfo?.SearchKeyword?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(searchKeyword))
            {
                ShowThumbnailUserActionPopup(
                    "タグ付け",
                    "検索キーワードが空のため、タグ付けできません。",
                    MessageBoxImage.Warning
                );
                return;
            }

            List<MovieRecords> visibleRecords = MainVM
                ?.FilteredMovieRecs?.Where(x => x != null).Distinct().ToList() ?? [];
            MovieRecords selectedRecord = GetSelectedItemByTabIndex();

            if (visibleRecords.Count == 0 && selectedRecord == null)
            {
                return;
            }

            var scopeDialog = new MessageBoxEx(this)
            {
                DlogTitle = "タグ付け対象を選択",
                DlogHeadline = $"タグ「{searchKeyword}」を追加します。",
                DlogMessage = "現在の検索キーワードをタグとして追加します。表示中の一覧すべてに付けるか、選択中の動画1件だけに付けるかを選んでください。",
                AllowOwnerMouseWheelPassthrough = true,
                UseRadioButton = true,
                Radio1Content = $"表示中のすべての動画（{visibleRecords.Count}件）",
                Radio2Content = "選択中の動画 1件",
                Radio1IsChecked = visibleRecords.Count > 0,
                Radio2IsChecked = visibleRecords.Count == 0 && selectedRecord != null,
                Radio1IsEnabled = visibleRecords.Count > 0,
                Radio2IsEnabled = selectedRecord != null,
            };
            await ShowModelessMessageBoxExAsync(scopeDialog);

            if (scopeDialog.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            if (scopeDialog.Radio2IsChecked && selectedRecord != null)
            {
                ApplyTagsToRecords([selectedRecord], searchKeyword);
                ShowBulkTagAssignedToast(searchKeyword, 1);
                return;
            }

            if (visibleRecords.Count > 0)
            {
                ApplyTagsToRecords(visibleRecords, searchKeyword);
                ShowBulkTagAssignedToast(searchKeyword, visibleRecords.Count);
            }
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
                    _mainDbMovieMutationFacade.UpdateTag(
                        MainVM.DbInfo.DBFullPath,
                        rec.Movie_Id,
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
            _mainDbMovieMutationFacade.UpdateTag(MainVM.DbInfo.DBFullPath, mv.Movie_Id, mv.Tags);

            Refresh();
        }

        // 追加用ダイアログを出し、入力されたタグを対象レコード群へまとめて反映する。
        private void PromptAndAddTagsToRecords(IReadOnlyList<MovieRecords> records, string dialogTitle)
        {
            if (records == null || records.Count == 0)
            {
                return;
            }

            MovieRecords dt = new();
            var tagEditWindow = new TagEdit
            {
                Title = dialogTitle,
                Owner = this,
                DataContext = dt,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            tagEditWindow.ShowDialog();

            if (tagEditWindow.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            var dataContext = tagEditWindow.DataContext as MovieRecords;
            var addedTags = dataContext?.Tags ?? "";
            if (string.IsNullOrWhiteSpace(addedTags))
            {
                return;
            }

            ApplyTagsToRecords(records, addedTags);
        }

        // 指定されたタグ文字列を、そのまま対象レコード群へ一括反映する。
        private void ApplyTagsToRecords(IReadOnlyList<MovieRecords> records, string addedTags)
        {
            if (records == null || records.Count == 0 || string.IsNullOrWhiteSpace(addedTags))
            {
                return;
            }

            foreach (var rec in records)
            {
                // 既存タグと追加タグを改行ベースで合成し、重複を除いて保存する。
                string tagsEditedWithNewLine = (rec.Tags ?? "") + Environment.NewLine + addedTags;
                string tagsWithNewLine = "";
                List<string> tagArray = [];

                var splitTags = tagsEditedWithNewLine.Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries
                );
                if (splitTags.Length > 0)
                {
                    tagsWithNewLine = ThumbnailTagFormatter.ConvertTagsWithNewLine([.. splitTags]);

                    foreach (var tagItem in splitTags.Distinct())
                    {
                        tagArray.Add(tagItem);
                    }
                }

                rec.Tag = tagArray;
                rec.Tags = tagsWithNewLine;

                _mainDbMovieMutationFacade.UpdateTag(
                    MainVM.DbInfo.DBFullPath,
                    rec.Movie_Id,
                    rec.Tags
                );
            }

            Refresh();
        }

        // 一括タグ付けの完了は軽いトーストで返し、一覧操作の流れを止めない。
        private void ShowBulkTagAssignedToast(string tagName, int recordCount)
        {
            try
            {
                string normalizedTagName = tagName?.Trim() ?? "";
                _watchFolderDropNotificationManager.Show(
                    "タグ付け",
                    $"タグ「{normalizedTagName}」を{recordCount}件に追加",
                    NotificationType.Success,
                    MainWindowDropToastAreaName,
                    TimeSpan.FromSeconds(4)
                );
            }
            catch
            {
                // トースト失敗でタグ付け結果は変えない。
            }
        }

        // 一覧確認のため owner を止めたくない確認ダイアログだけ、modeless 風に待ち合わせる。
        private static Task ShowModelessMessageBoxExAsync(MessageBoxEx dialog)
        {
            if (dialog == null)
            {
                return Task.CompletedTask;
            }

            TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            void complete()
            {
                dialog.IsVisibleChanged -= handleVisibilityChanged;
                dialog.Closed -= handleClosed;
                _ = tcs.TrySetResult(true);
            }

            void handleVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
            {
                if (e.NewValue is bool isVisible && !isVisible)
                {
                    complete();
                }
            }

            void handleClosed(object sender, EventArgs e)
            {
                complete();
            }

            dialog.IsVisibleChanged += handleVisibilityChanged;
            dialog.Closed += handleClosed;
            dialog.Show();
            dialog.Activate();
            return tcs.Task;
        }

        // この確認ダイアログ中だけ、ホイールで今見ている上側タブを確認できるようにする。
        internal void ScrollCurrentUpperTabByMouseWheel(int delta)
        {
            ItemsControl scrollHost = ResolveCurrentUpperTabScrollHost();
            if (scrollHost == null)
            {
                return;
            }

            ScrollViewer scrollViewer = FindFirstDescendant<ScrollViewer>(scrollHost);
            if (scrollViewer == null)
            {
                return;
            }

            double nextOffset =
                scrollViewer.VerticalOffset
                - (
                    Math.Sign(delta)
                    * Math.Max(1, SystemParameters.WheelScrollLines)
                    * 18
                );
            nextOffset = Math.Max(0, Math.Min(nextOffset, scrollViewer.ScrollableHeight));
            scrollViewer.ScrollToVerticalOffset(nextOffset);

            scrollHost.UpdateLayout();
        }

        // 現在見ている上側タブごとに、スクロール対象の本体コントロールを返す。
        private ItemsControl ResolveCurrentUpperTabScrollHost()
        {
            if (TabSmall?.IsSelected == true)
            {
                return SmallList;
            }

            if (TabBig?.IsSelected == true)
            {
                return BigList;
            }

            if (TabGrid?.IsSelected == true)
            {
                return GridList;
            }

            if (TabList?.IsSelected == true)
            {
                return ListDataGrid;
            }

            if (BigList10 != null && BigList10.IsVisible)
            {
                return BigList10;
            }

            return null;
        }

        private static T FindFirstDescendant<T>(DependencyObject root)
            where T : DependencyObject
        {
            if (root == null)
            {
                return null;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T matched)
                {
                    return matched;
                }

                T descendant = FindFirstDescendant<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }
    }
}
