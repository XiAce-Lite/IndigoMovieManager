using System.Windows;
using IndigoMovieManager.Infrastructure;
using IndigoMovieManager.Thumbnail;
using System.Linq;
using Notification.Wpf;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

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
                rec.Tag = TagTextParser
                    .SplitDistinct(rec.Tags, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                // 最後に単一カラムのみをDBへUPDATEする
                _mainDbMovieMutationFacade.UpdateTag(
                    MainVM.DbInfo.DBFullPath,
                    rec.Movie_Id,
                    rec.Tags
                );
                NotifyTagEditorTagIndexChanged(rec);
            }

            // 万一画面表示がズレないよう一覧を再描画
            Refresh();
            RefreshTagEditorView();
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

            if (
                !TagSearchKeywordCodec.TryResolveTagAssignmentCandidate(
                    searchKeyword,
                    out string tagKeyword
                )
            )
            {
                ShowThumbnailUserActionPopup(
                    "タグ付け",
                    "複数タグや曖昧な検索条件は、そのままタグ付け対象にできません。",
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
                DlogHeadline = $"タグ「{tagKeyword}」を操作します。",
                DlogMessage = "現在の検索キーワードをタグとして、追加または削除します。対象範囲を選んでください。",
                AllowOwnerMouseWheelPassthrough = true,
                UseRadioButton = true,
                Radio1Content = $"表示中のすべての動画（{visibleRecords.Count}件）",
                Radio2Content = "選択中の動画 1件",
                Radio3Content = "表示中のすべての動画からこのタグを削除",
                Radio1IsChecked = visibleRecords.Count > 0,
                Radio2IsChecked = visibleRecords.Count == 0 && selectedRecord != null,
                Radio3IsChecked = false,
                Radio1IsEnabled = visibleRecords.Count > 0,
                Radio2IsEnabled = selectedRecord != null,
                Radio3IsEnabled = visibleRecords.Count > 0,
                Radio3PackIconKind = PackIconKind.TrashCanOutline,
                Radio3AccentForegroundBrush = Brushes.DeepPink,
            };
            await ShowModelessMessageBoxExAsync(scopeDialog);

            if (scopeDialog.CloseStatus() == MessageBoxResult.Cancel)
            {
                return;
            }

            if (scopeDialog.Radio3IsChecked && visibleRecords.Count > 0)
            {
                RemoveTagFromRecords(visibleRecords, tagKeyword);
                ShowBulkTagRemovedToast(tagKeyword, visibleRecords.Count);
                return;
            }

            if (scopeDialog.Radio2IsChecked && selectedRecord != null)
            {
                ApplyTagsToRecords([selectedRecord], tagKeyword);
                ShowBulkTagAssignedToast(tagKeyword, 1);
                return;
            }

            if (visibleRecords.Count > 0)
            {
                ApplyTagsToRecords(visibleRecords, tagKeyword);
                ShowBulkTagAssignedToast(tagKeyword, visibleRecords.Count);
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
                    string[] splitTags = TagTextParser.SplitDistinct(
                        tagsEditedWithNewLine,
                        StringComparer.CurrentCultureIgnoreCase
                    );
                    foreach (string tagItem in splitTags)
                    {
                        tagArray.RemoveAll(x =>
                            string.Equals(x, tagItem, StringComparison.CurrentCultureIgnoreCase)
                        );
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
                    NotifyTagEditorTagIndexChanged(rec);
                }
            }

            Refresh();
            RefreshTagEditorView();
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
                string[] splitTags = TagTextParser.SplitDistinct(
                    tagsEditedWithNewLine,
                    StringComparer.CurrentCultureIgnoreCase
                );
                string tagsWithNewLine = ThumbnailTagFormatter.ConvertTagsWithNewLine([.. splitTags]);
                tagArray.AddRange(splitTags);
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
            NotifyTagEditorTagIndexChanged(mv);

            Refresh();
            RefreshTagEditorView();
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

                string[] splitTags = TagTextParser.SplitDistinct(
                    tagsEditedWithNewLine,
                    StringComparer.CurrentCultureIgnoreCase
                );
                if (splitTags.Length > 0)
                {
                    tagsWithNewLine = ThumbnailTagFormatter.ConvertTagsWithNewLine([.. splitTags]);
                    tagArray.AddRange(splitTags);
                }

                rec.Tag = tagArray;
                rec.Tags = tagsWithNewLine;

                _mainDbMovieMutationFacade.UpdateTag(
                    MainVM.DbInfo.DBFullPath,
                    rec.Movie_Id,
                    rec.Tags
                );
                NotifyTagEditorTagIndexChanged(rec);
            }

            Refresh();
            RefreshTagEditorView();
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

        // 一括タグ削除も軽いトーストで返し、一覧操作のテンポを止めない。
        private void ShowBulkTagRemovedToast(string tagName, int recordCount)
        {
            try
            {
                string normalizedTagName = tagName?.Trim() ?? "";
                _watchFolderDropNotificationManager.Show(
                    "タグ削除",
                    $"タグ「{normalizedTagName}」を{recordCount}件から削除",
                    NotificationType.Information,
                    MainWindowDropToastAreaName,
                    TimeSpan.FromSeconds(4)
                );
            }
            catch
            {
                // トースト失敗でタグ削除結果は変えない。
            }
        }

        // 指定タグだけを対象レコード群から引き算して、DBへ反映する。
        private void RemoveTagFromRecords(IReadOnlyList<MovieRecords> records, string targetTag)
        {
            if (records == null || records.Count == 0 || string.IsNullOrWhiteSpace(targetTag))
            {
                return;
            }

            foreach (var rec in records)
            {
                List<string> currentTags = rec.Tag ?? [];
                currentTags = currentTags
                    .Where(x => !string.Equals(x, targetTag, StringComparison.CurrentCultureIgnoreCase))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                rec.Tag = currentTags;
                rec.Tags = ThumbnailTagFormatter.ConvertTagsWithNewLine([.. currentTags]);

                _mainDbMovieMutationFacade.UpdateTag(
                    MainVM.DbInfo.DBFullPath,
                    rec.Movie_Id,
                    rec.Tags
                );
                NotifyTagEditorTagIndexChanged(rec);
            }

            Refresh();
            RefreshTagEditorView();
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

            if (TabPlayer?.IsSelected == true)
            {
                return PlayerThumbnailList;
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
