using System.Diagnostics;
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

        // 詳細ペインの初期化。
        // 選択切替時にMainWindowからDataContextを差し替えて使う。
        public ExtDetail()
        {
            InitializeComponent();
            DataContext = new MovieRecords();
            ApplyConfiguredDetailThumbnailMode();
        }

        private void Label_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // サムネイルのダブルクリックは、親Windowの再生処理へ委譲する。
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                MainWindow ownerWindow = (MainWindow)Window.GetWindow(this);
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
            // 詳細タブの表示モード切替に合わせ、表示枠だけを素直に差し替える。
            if (width > 0)
            {
                LabelExtDetail.Width = width;
                DetailThumbnailModeComboBox.Width = width;
            }

            if (height > 0)
            {
                LabelExtDetail.Height = height;
            }
        }

        public void ApplyConfiguredDetailThumbnailMode()
        {
            string currentMode = ThumbnailDetailModeRuntime.Normalize(
                IndigoMovieManager.Properties.Settings.Default.DetailThumbnailMode
            );
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
                ApplyThumbnailDisplaySize(
                    ThumbnailDetailModeRuntime.GetDisplayWidth(currentMode),
                    ThumbnailDetailModeRuntime.GetDisplayHeight(currentMode)
                );

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
            ApplyThumbnailDisplaySize(
                ThumbnailDetailModeRuntime.GetDisplayWidth(selectedMode),
                ThumbnailDetailModeRuntime.GetDisplayHeight(selectedMode)
            );
            _appliedDetailThumbnailMode = ThumbnailDetailModeRuntime.Normalize(selectedMode);

            if (Window.GetWindow(this) is MainWindow ownerWindow)
            {
                ownerWindow.ChangeExtensionDetailThumbnailMode(selectedMode);
            }
        }

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            // 親フォルダ上で対象ファイルを選択状態で開く。
            var item = (Hyperlink)sender;
            if (item != null)
            {
                MovieRecords mv = item.DataContext as MovieRecords;
                if (Path.Exists(mv.Movie_Path))
                {
                    Process.Start("explorer.exe", $"/select,{mv.Movie_Path}");
                }
            }
        }

        private void FileNameLink_Click(object sender, RoutedEventArgs e)
        {
            // ファイル名リンクは完全一致検索（"..."）としてSearchBoxへ投入する。
            // DataContext からファイル名を取得
            if (DataContext is MovieRecords record)
            {
                // MainWindow のインスタンスを取得
                var mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                if (mainWindow != null)
                {
                    // ダブルクォーテーションで括ってSearchBoxとViewModelにセット
                    var quoted = $"\"{record.Movie_Body}\"";
                    mainWindow.SearchBox.Text = quoted;
                    mainWindow.MainVM.DbInfo.SearchKeyword = quoted;

                    // 検索処理を実行
                    mainWindow.FilterAndSort(mainWindow.MainVM.DbInfo.Sort, true);
                    mainWindow.SelectFirstItem();

                    // SearchBoxにフォーカスを当てる
                    mainWindow.SearchBox.Focus();
                }
            }
        }

        private void Ext_Click(object sender, RoutedEventArgs e)
        {
            // 拡張子リンクは拡張子検索としてSearchBoxへ投入する。
            MainWindow ownerWindow = (MainWindow)Window.GetWindow(this);
            var item = (Hyperlink)sender;
            if (item != null)
            {
                MovieRecords mv = item.DataContext as MovieRecords;
                ownerWindow.SearchBox.Text = mv.Ext;

                // 検索キーワードもViewModelに反映
                ownerWindow.MainVM.DbInfo.SearchKeyword = mv.Ext;

                // 検索処理を実行
                ownerWindow.FilterAndSort(ownerWindow.MainVM.DbInfo.Sort, true);
                ownerWindow.SelectFirstItem();

                // SearchBoxにフォーカスを当てる
                ownerWindow.SearchBox.Focus();
            }
        }
    }
}
