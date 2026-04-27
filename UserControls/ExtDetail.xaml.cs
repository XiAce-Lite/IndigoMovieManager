using System;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using IndigoMovieManager.Converter;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.UserControls
{
    /// <summary>
    /// ExtDetail.xaml の相互作用ロジック
    /// </summary>
    public partial class ExtDetail : UserControl, INotifyPropertyChanged
    {
        private bool _isSyncingDetailThumbnailModeUi;
        private string _appliedDetailThumbnailMode = "";
        private MovieRecords _subscribedRecord;
        private int _detailThumbnailDecodePixelHeight;
        private FileSystemWatcher _detailThumbnailFileWatcher;
        private string _watchedDetailThumbnailPath = "";

        public event PropertyChangedEventHandler PropertyChanged;

        public int DetailThumbnailDecodePixelHeight
        {
            get => _detailThumbnailDecodePixelHeight;
            private set
            {
                if (_detailThumbnailDecodePixelHeight == value)
                {
                    return;
                }

                _detailThumbnailDecodePixelHeight = value;
                if (
                    _subscribedRecord != null
                    && !string.IsNullOrWhiteSpace(_subscribedRecord.ThumbDetail)
                )
                {
                    NoLockImageConverter.InvalidateFilePath(_subscribedRecord.ThumbDetail);
                }
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(DetailThumbnailDecodePixelHeight))
                );
                // PropertyChanged で MultiBinding の ConverterParameter が変化し、
                // WPF が自動で MultiBinding を再評価する。
                // 明示的な RefreshDetailThumbnailImage() は呼び出し元に集約。
            }
        }

        // MultiBinding（ConverterBindableParameter）を壊さず画像バインドを再評価する。
        // キャッシュ無効化は呼び出し元で NoLockImageConverter.InvalidateFilePath を先に実行済み。
        private void RefreshDetailThumbnailImage()
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    // GetBindingExpression は MultiBinding に null を返すため、
                    // Binding 種別を問わない GetBindingExpressionBase を使う。
                    BindingExpressionBase binding =
                        BindingOperations.GetBindingExpressionBase(
                            DetailThumbnailImage,
                            Image.SourceProperty
                        );
                    binding?.UpdateTarget();
                })
            );
        }

        // 詳細ペインの初期化。
        // 選択切替時にMainWindowからDataContextを差し替えて使う。
        public ExtDetail()
        {
            InitializeComponent();
            DataContext = new MovieRecords();
            DataContextChanged += ExtDetail_DataContextChanged;
            Unloaded += ExtDetail_Unloaded;
            UpdateSubscribedRecord(DataContext as MovieRecords);
            ApplyConfiguredDetailThumbnailMode();
        }

        private void ExtDetail_Unloaded(object sender, RoutedEventArgs e)
        {
            StopDetailThumbnailFileWatcher();
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

        private void DetailThumbnailImage_ContextMenuOpening(
            object sender,
            ContextMenuEventArgs e
        )
        {
            if (sender is not System.Windows.FrameworkElement imageElement)
            {
                return;
            }

            if (imageElement.DataContext is not MovieRecords record)
            {
                return;
            }

            // 右クリック時に「画像未作成」だけ先に通常経路で即時投入し、
            // 画像が存在する場合は余計な再投入をしない。
            if (record.IsExists && HasDetailThumbnailFile(record))
            {
                return;
            }

            if (Window.GetWindow(this) is not MainWindow ownerWindow)
            {
                return;
            }

            RefreshDetailThumbnailImage();
            ownerWindow.ReevaluateActiveExtensionDetailThumbnail();
        }

        private static bool HasDetailThumbnailFile(MovieRecords record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.ThumbDetail))
            {
                return false;
            }

            if (MainWindow.IsThumbnailErrorPlaceholderPath(record.ThumbDetail))
            {
                return false;
            }

            return Path.Exists(record.ThumbDetail);
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
                ConfigureDetailThumbnailFileWatch();
            }
            else
            {
                StopDetailThumbnailFileWatcher();
            }
        }

        private void SubscribedRecord_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e?.PropertyName, nameof(MovieRecords.ThumbDetail), StringComparison.Ordinal))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(ApplyConfiguredDetailThumbnailMode));
            Dispatcher.BeginInvoke(new Action(ConfigureDetailThumbnailFileWatch));
        }

        private static bool IsDetailThumbnailPlaceholder(string path)
        {
            return MainWindow.IsThumbnailErrorPlaceholderPath(path);
        }

        private string ResolveWatchedDetailThumbnailPath(string path)
        {
            try
            {
                return string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path);
            }
            catch
            {
                return string.IsNullOrWhiteSpace(path) ? "" : path;
            }
        }

        private void ConfigureDetailThumbnailFileWatch()
        {
            StopDetailThumbnailFileWatcher();

            if (_subscribedRecord == null)
            {
                return;
            }

            string targetPath = _subscribedRecord.ThumbDetail;
            if (string.IsNullOrWhiteSpace(targetPath) || IsDetailThumbnailPlaceholder(targetPath))
            {
                return;
            }

            string normalizedTargetPath = ResolveWatchedDetailThumbnailPath(targetPath);
            string directoryPath;
            string fileName;
            try
            {
                directoryPath = Path.GetDirectoryName(normalizedTargetPath) ?? "";
                fileName = Path.GetFileName(normalizedTargetPath);
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            if (Path.Exists(normalizedTargetPath))
            {
                NoLockImageConverter.InvalidateFilePath(normalizedTargetPath);
                RefreshDetailThumbnailImage();
                return;
            }

            if (!Directory.Exists(directoryPath))
            {
                return;
            }

            try
            {
                _detailThumbnailFileWatcher = new FileSystemWatcher(directoryPath, fileName)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = false,
                };
                _detailThumbnailFileWatcher.Created += DetailThumbnailFileWatcher_Changed;
                _detailThumbnailFileWatcher.Changed += DetailThumbnailFileWatcher_Changed;
                _detailThumbnailFileWatcher.Renamed += DetailThumbnailFileWatcher_Renamed;
                _detailThumbnailFileWatcher.Error += DetailThumbnailFileWatcher_Error;
                _watchedDetailThumbnailPath = normalizedTargetPath;
            }
            catch
            {
                StopDetailThumbnailFileWatcher();
            }
        }

        private void DetailThumbnailFileWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (!string.Equals(
                ResolveWatchedDetailThumbnailPath(e.FullPath),
                _watchedDetailThumbnailPath,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                return;
            }

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (_subscribedRecord == null || string.IsNullOrWhiteSpace(_watchedDetailThumbnailPath))
                    {
                        return;
                    }

                    if (!Path.Exists(_watchedDetailThumbnailPath))
                    {
                        return;
                    }

                    NoLockImageConverter.InvalidateFilePath(_watchedDetailThumbnailPath);
                    RefreshDetailThumbnailImage();
                    StopDetailThumbnailFileWatcher();
                }),
                System.Windows.Threading.DispatcherPriority.Background
            );
        }

        private void DetailThumbnailFileWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            DetailThumbnailFileWatcher_Changed(sender, e);
        }

        private void DetailThumbnailFileWatcher_Error(object sender, ErrorEventArgs e)
        {
            StopDetailThumbnailFileWatcher();
        }

        private void StopDetailThumbnailFileWatcher()
        {
            if (_detailThumbnailFileWatcher != null)
            {
                try
                {
                    _detailThumbnailFileWatcher.EnableRaisingEvents = false;
                    _detailThumbnailFileWatcher.Created -= DetailThumbnailFileWatcher_Changed;
                    _detailThumbnailFileWatcher.Changed -= DetailThumbnailFileWatcher_Changed;
                    _detailThumbnailFileWatcher.Renamed -= DetailThumbnailFileWatcher_Renamed;
                    _detailThumbnailFileWatcher.Error -= DetailThumbnailFileWatcher_Error;
                    _detailThumbnailFileWatcher.Dispose();
                }
                finally
                {
                    _detailThumbnailFileWatcher = null;
                    _watchedDetailThumbnailPath = "";
                }
            }
        }

        private void ApplyThumbnailDisplaySizeForCurrentContext(string mode)
        {
            ApplyThumbnailDisplaySize(0, 0);
            DetailThumbnailDecodePixelHeight = ResolveDetailThumbnailDecodePixelHeight(mode);
            RefreshDetailThumbnailImage();
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

        private async void FileNameLink_Click(object sender, RoutedEventArgs e)
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

            var quoted = $"\"{record.Movie_Body}\"";
            await ownerWindow.ApplySearchKeywordFromLinkAsync(quoted);
        }

        private async void Ext_Click(object sender, RoutedEventArgs e)
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

            await ownerWindow.ApplySearchKeywordFromLinkAsync(mv.Ext);
        }

        private static int ResolveDetailThumbnailDecodePixelHeight(string mode)
        {
            return ThumbnailDetailModeRuntime.GetDisplayHeight(mode);
        }
    }
}
