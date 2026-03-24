using System;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.UserControls
{
    /// <summary>
    /// ExtDetail.xaml の相互作用ロジック
    /// </summary>
    public partial class ExtDetail : UserControl
    {
        private bool _isSyncingDetailThumbnailModeUi;
        private string _appliedDetailThumbnailMode = "";
        private MovieRecords _subscribedRecord;

        // 詳細ペインの初期化。
        // 選択切替時にMainWindowからDataContextを差し替えて使う。
        public ExtDetail()
        {
            InitializeComponent();
            DataContext = new MovieRecords();
            DataContextChanged += ExtDetail_DataContextChanged;
            UpdateSubscribedRecord(DataContext as MovieRecords);
            ApplyConfiguredDetailThumbnailMode();
        }

        private void LabelExtDetail_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            MainWindow ownerWindow = Window.GetWindow(this) as MainWindow;
            if (ownerWindow != null)
            {
                // 画像クリック時も、前面表示中なら現在の詳細サイズで再評価する。
                ownerWindow.ReevaluateActiveExtensionDetailThumbnail();
            }

            // サムネイルのダブルクリックは、親Windowの再生処理へ委譲する。
            if (e.ClickCount >= 2 && e.LeftButton == MouseButtonState.Pressed)
            {
                ownerWindow.PlayMovie_Click(sender, e);
            }
        }

        public void Refresh()
        {
            // タグ更新時にItemsControlの再描画を強制する。
            ExtDetailTags.Items.Refresh();
        }

        public void ApplyThumbnailDisplaySize(int width, int height)
        {
            // 表示サイズは固定値で持たず、残り領域へ Uniform でフィットさせる。
            DetailThumbnailImage.ClearValue(WidthProperty);
            DetailThumbnailImage.ClearValue(HeightProperty);
        }

        public void ApplyConfiguredDetailThumbnailMode()
        {
            string currentMode = ThumbnailDetailModeRuntime.Normalize(
                IndigoMovieManager.Properties.Settings.Default.DetailThumbnailMode
            );
            ApplyThumbnailDisplaySizeForCurrentContext(currentMode);
            if (
                string.Equals(
                    _appliedDetailThumbnailMode,
                    currentMode,
                    StringComparison.Ordinal
                )
            )
            {
                // 選択切替ごとに同じモードを再適用すると、無駄なレイアウト更新が走るので止める。
                return;
            }

            _isSyncingDetailThumbnailModeUi = true;
            try
            {
                foreach (object item in DetailThumbnailModeComboBox.Items)
                {
                    if (item is ComboBoxItem comboBoxItem)
                    {
                        comboBoxItem.IsSelected = string.Equals(
                            comboBoxItem.Tag?.ToString(),
                            currentMode,
                            StringComparison.Ordinal
                        );
                    }
                }

                _appliedDetailThumbnailMode = currentMode;
            }
            finally
            {
                _isSyncingDetailThumbnailModeUi = false;
            }
        }

        private void DetailThumbnailModeComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e
        )
        {
            if (_isSyncingDetailThumbnailModeUi)
            {
                return;
            }

            if (DetailThumbnailModeComboBox.SelectedItem is not ComboBoxItem selectedItem)
            {
                return;
            }

            string selectedMode = selectedItem.Tag?.ToString() ?? "";
            ApplyThumbnailDisplaySizeForCurrentContext(selectedMode);
            _appliedDetailThumbnailMode = ThumbnailDetailModeRuntime.Normalize(selectedMode);

            if (Window.GetWindow(this) is MainWindow ownerWindow)
            {
                ownerWindow.ChangeExtensionDetailThumbnailMode(selectedMode);
            }
        }

        private void ExtDetail_DataContextChanged(
            object sender,
            DependencyPropertyChangedEventArgs e
        )
        {
            UpdateSubscribedRecord(e.NewValue as MovieRecords);
            ApplyConfiguredDetailThumbnailMode();
        }

        private void UpdateSubscribedRecord(MovieRecords record)
        {
            if (ReferenceEquals(_subscribedRecord, record))
            {
                return;
            }

            if (_subscribedRecord != null)
            {
                _subscribedRecord.PropertyChanged -= SubscribedRecord_PropertyChanged;
            }

            _subscribedRecord = record;

            if (_subscribedRecord != null)
            {
                _subscribedRecord.PropertyChanged += SubscribedRecord_PropertyChanged;
            }
        }

        private void SubscribedRecord_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e?.PropertyName, nameof(MovieRecords.ThumbDetail), StringComparison.Ordinal))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(ApplyConfiguredDetailThumbnailMode));
        }

        private void ApplyThumbnailDisplaySizeForCurrentContext(string mode)
        {
            ApplyThumbnailDisplaySize(0, 0);
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            // 親フォルダ上で対象ファイルを選択状態で開く。
            var item = sender as Hyperlink;
            if (item != null)
            {
                MovieRecords mv = item.DataContext as MovieRecords;
                if (mv != null && Path.Exists(mv.Movie_Path))
                {
                    Process.Start("explorer.exe", $"/select,{mv.Movie_Path}");
                }
            }
        }

        private void FileNameLink_Click(object sender, RoutedEventArgs e)
        {
            // ファイル名リンクは完全一致検索（"..."）としてSearchBoxへ投入する。
            // DataContext からファイル名を取得
            MainWindow ownerWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (ownerWindow == null)
            {
                return;
            }

            if (DataContext is not MovieRecords record)
            {
                return;
            }

            // ダブルクォーテーションで括ってSearchBoxとViewModelにセット
            var quoted = $"\"{record.Movie_Body}\"";
            ownerWindow.SearchBox.Text = quoted;
            ownerWindow.MainVM.DbInfo.SearchKeyword = quoted;

            // 検索処理を実行
            ownerWindow.FilterAndSort(ownerWindow.MainVM.DbInfo.Sort, true);
            ownerWindow.SelectFirstItem();

            // SearchBoxにフォーカスを当てる
            ownerWindow.SearchBox.Focus();
        }

        private void Ext_Click(object sender, RoutedEventArgs e)
        {
            // 拡張子リンクは拡張子検索としてSearchBoxへ投入する。
            MainWindow ownerWindow = Window.GetWindow(this) as MainWindow;
            if (ownerWindow == null)
            {
                return;
            }

            var item = sender as Hyperlink;
            if (item == null)
            {
                return;
            }

            MovieRecords mv = item.DataContext as MovieRecords;
            if (mv == null)
            {
                return;
            }

            // 検索キーワードもViewModelに反映
            ownerWindow.SearchBox.Text = mv.Ext;
            ownerWindow.MainVM.DbInfo.SearchKeyword = mv.Ext;

            // 検索処理を実行
            ownerWindow.FilterAndSort(ownerWindow.MainVM.DbInfo.Sort, true);
            ownerWindow.SelectFirstItem();

            // SearchBoxにフォーカスを当てる
            ownerWindow.SearchBox.Focus();
        }
    }
}
