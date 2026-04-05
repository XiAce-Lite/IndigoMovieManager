using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using IndigoMovieManager.Converter;
using IndigoMovieManager.Skin;
using IndigoMovieManager.Thumbnail;
using IndigoMovieManager.Watcher;

namespace IndigoMovieManager
{
    /// <summary>
    /// 共通設定画面の View 層。アプリ全体に影響する設定（プレイヤー・テーマ・Everythingモード・
    /// 一覧画像キャッシュ等）の表示と永続化を管理する。
    ///
    /// 【全体の流れでの位置づけ】
    ///   メニュー → ★ここ★ CommonSettingsWindow
    ///     → 閉じる時（OnClosing）で Properties.Settings へ保存
    /// </summary>
    public partial class CommonSettingsWindow : Window
    {
        private bool _isUpperTabImageCacheMaxEntriesSyncing;
        private bool _isSkinSelectorInitializing;

        // 共通設定画面の初期化。
        // 閉じるイベントで設定保存するため、ここでイベントを接続する。
        public CommonSettingsWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => App.ApplyWindowTitleBarTheme(this);
            Closing += OnClosing;
            Closed += CommonSettingsWindow_Closed;
            Activated += (_, _) => RefreshSkinSelector();
            sliderUpperTabImageCacheMaxEntries.ValueChanged +=
                SliderUpperTabImageCacheMaxEntries_ValueChanged;
            Properties.Settings.Default.PropertyChanged += SettingsDefault_PropertyChanged;
            DefaultPlayerParam.ItemsSource = new string[]
            {
                "/start <ms>",
                "<file> player -seek pos=<ms>"
            };
            string normalizedProvider = FileIndexProviderFactory.NormalizeProviderKey(
                Properties.Settings.Default.FileIndexProvider
            );
            FileIndexProviderSelector.SelectedValue = normalizedProvider;
            SyncUpperTabImageCacheMaxEntriesSliderFromSettings();
            InitializeSkinSelector();

            // テーマ設定の初期値を反映する。
            string currentTheme = IndigoMovieManager.Properties.Settings.Default.ThemeMode;
            foreach (System.Windows.Controls.ComboBoxItem item in ThemeComboBox.Items)
            {
                if (item.Tag?.ToString() == currentTheme)
                {
                    ThemeComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        /// <summary>
        /// 共通設定画面のクローズ時に、画面上の全設定値を Properties.Settings へ書き戻して永続化する。
        /// </summary>
        private void OnClosing(object sender, CancelEventArgs e)
        {
            // 画面上の値を Properties.Settings へ反映して永続化する。
            Properties.Settings.Default.AutoOpen = (bool)AutoOpen.IsChecked;
            Properties.Settings.Default.ConfirmExit = (bool)ConfirmExit.IsChecked;
            Properties.Settings.Default.DefaultPlayerPath = DefaultPlayerPath.Text;
            Properties.Settings.Default.DefaultPlayerParam = DefaultPlayerParam.Text;
            Properties.Settings.Default.RecentFilesCount = (int)slider.Value;
            Properties.Settings.Default.ThumbnailGpuDecodeEnabled = (bool)ThumbnailGpuDecodeEnabled.IsChecked;
            // Delete系ショートカットの動作を保存する。範囲外は各既定値へ戻す。
            int deleteKeyActionMode = DeleteKeyActionMode.SelectedIndex;
            if (deleteKeyActionMode < 0 || deleteKeyActionMode > 3)
            {
                deleteKeyActionMode = 0;
            }
            Properties.Settings.Default.DeleteKeyActionMode = deleteKeyActionMode;
            int shiftDeleteKeyActionMode = ShiftDeleteKeyActionMode.SelectedIndex;
            if (shiftDeleteKeyActionMode < 0 || shiftDeleteKeyActionMode > 3)
            {
                shiftDeleteKeyActionMode = 2;
            }
            Properties.Settings.Default.ShiftDeleteKeyActionMode = shiftDeleteKeyActionMode;
            int ctrlDeleteKeyActionMode = CtrlDeleteKeyActionMode.SelectedIndex;
            if (ctrlDeleteKeyActionMode < 0 || ctrlDeleteKeyActionMode > 3)
            {
                ctrlDeleteKeyActionMode = 1;
            }
            Properties.Settings.Default.CtrlDeleteKeyActionMode = ctrlDeleteKeyActionMode;
            // OFF/AUTO/ONの3値設定を保存する。範囲外はAUTOへ丸める。
            int integrationMode = EverythingIntegrationMode.SelectedIndex;
            if (integrationMode < 0 || integrationMode > 2)
            {
                integrationMode = 1;
            }
            Properties.Settings.Default.EverythingIntegrationMode = integrationMode;
            // 旧設定との互換のため、OFF以外をtrueとして同期する。
            Properties.Settings.Default.EverythingIntegrationEnabled = integrationMode != 0;
            string selectedProvider = FileIndexProviderSelector.SelectedValue as string;
            Properties.Settings.Default.FileIndexProvider = FileIndexProviderFactory.NormalizeProviderKey(
                selectedProvider
            );
            // 一覧画像キャッシュ件数は converter 側の安全範囲へ丸めて保存する。
            Properties.Settings.Default.UpperTabImageCacheMaxEntries =
                ClampUpperTabImageCacheMaxEntries(
                    (int)System.Math.Round(sliderUpperTabImageCacheMaxEntries.Value)
                );
            Properties.Settings.Default.Save();
            ThumbnailEnvConfig.ApplyFfmpegOnePassExecutionHintsForCurrentSettings();
        }

        // 共通設定を閉じる時にイベント購読を解除する。
        private void CommonSettingsWindow_Closed(object sender, System.EventArgs e)
        {
            sliderUpperTabImageCacheMaxEntries.ValueChanged -=
                SliderUpperTabImageCacheMaxEntries_ValueChanged;
            Properties.Settings.Default.PropertyChanged -= SettingsDefault_PropertyChanged;
            Closed -= CommonSettingsWindow_Closed;
        }

        // 他経路（ショートカット等）で設定値が変わった時、スライダーを即時追従させる。
        private void SettingsDefault_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            string propertyName = e?.PropertyName ?? "";
            if (
                string.Equals(
                    propertyName,
                    nameof(Properties.Settings.Default.UpperTabImageCacheMaxEntries),
                    System.StringComparison.Ordinal
                )
            )
            {
                SyncUpperTabImageCacheMaxEntriesSliderFromSettings();
                return;
            }
        }

        // 一覧画像キャッシュ件数は設定変更を即時反映し、次回 decode から使う。
        private void SliderUpperTabImageCacheMaxEntries_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e
        )
        {
            if (_isUpperTabImageCacheMaxEntriesSyncing)
            {
                return;
            }

            int next = ClampUpperTabImageCacheMaxEntries((int)System.Math.Round(e.NewValue));
            if (Properties.Settings.Default.UpperTabImageCacheMaxEntries == next)
            {
                return;
            }

            Properties.Settings.Default.UpperTabImageCacheMaxEntries = next;
        }

        // 設定値をスライダーへ同期し、範囲外値もここで吸収する。
        private void SyncUpperTabImageCacheMaxEntriesSliderFromSettings()
        {
            int next = ClampUpperTabImageCacheMaxEntries(
                Properties.Settings.Default.UpperTabImageCacheMaxEntries
            );
            if (Properties.Settings.Default.UpperTabImageCacheMaxEntries != next)
            {
                Properties.Settings.Default.UpperTabImageCacheMaxEntries = next;
            }

            if (System.Math.Abs(sliderUpperTabImageCacheMaxEntries.Value - next) < 0.0001d)
            {
                return;
            }

            _isUpperTabImageCacheMaxEntriesSyncing = true;
            try
            {
                sliderUpperTabImageCacheMaxEntries.Value = next;
            }
            finally
            {
                _isUpperTabImageCacheMaxEntriesSyncing = false;
            }
        }

        // 一覧画像キャッシュ件数は 256〜4096 の範囲に制限する。
        private static int ClampUpperTabImageCacheMaxEntries(int value)
        {
            return NoLockImageConverter.ClampImageCacheEntryLimit(value);
        }

        private void InitializeSkinSelector()
        {
            RefreshSkinSelector();
        }

        private void RefreshSkinSelector()
        {
            WhiteBrowserSkinOrchestrator skinOrchestrator = GetMainWindowSkinOrchestrator();
            if (skinOrchestrator == null)
            {
                SkinComboBox.IsEnabled = false;
                SkinComboBox.ToolTip = "メインウィンドウ初期化後に利用できます。";
                return;
            }

            _isSkinSelectorInitializing = true;
            try
            {
                SkinComboBox.ItemsSource = skinOrchestrator.GetAvailableSkinDefinitions();
                SkinComboBox.SelectedValue = skinOrchestrator.GetCurrentSkinName();
            }
            finally
            {
                _isSkinSelectorInitializing = false;
            }

            MainWindow mainWindow = Application.Current?.MainWindow as MainWindow;
            bool hasCurrentDb = !string.IsNullOrWhiteSpace(mainWindow.MainVM?.DbInfo?.DBFullPath ?? "");
            SkinComboBox.IsEnabled = hasCurrentDb;
            UpdateSkinSelectorToolTip(
                skinOrchestrator.GetCurrentSkinDefinition(),
                hasCurrentDb
            );
        }

        private WhiteBrowserSkinOrchestrator GetMainWindowSkinOrchestrator()
        {
            return (Application.Current?.MainWindow as MainWindow)?.GetSkinOrchestrator();
        }

        private void UpdateSkinSelectorToolTip(
            WhiteBrowserSkinDefinition selectedDefinition,
            bool hasCurrentDb
        )
        {
            if (!hasCurrentDb)
            {
                SkinComboBox.ToolTip = "DB を開くと選択できます。";
                return;
            }

            if (selectedDefinition?.IsMissing == true)
            {
                SkinComboBox.ToolTip =
                    "現在 DB には未解決の外部スキン名が保存されています。名前は保持しつつ、表示はフォールバック前提です。";
                return;
            }

            SkinComboBox.ToolTip = selectedDefinition?.RequiresWebView2 == true
                ? "現在 DB の system.skin へ保存します。外部スキンは WebView2 経路で表示する前提です。"
                : "現在 DB の system.skin へ保存します。既定スキンは従来の高速表示を使います。";
        }

        private void BtnReturn_Click(object sender, RoutedEventArgs e)
        {
            // 共通設定画面を閉じてメインへ戻る。
            Close();
        }

        private void OpenDialogPlayer_Click(object sender, RoutedEventArgs e)
        {
            // 共通設定の既定プレイヤー実行ファイルを選択する。
            var ofd = new OpenFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                RestoreDirectory = true,
                Filter = "実行ファイル(*.exe)|*.exe|すべてのファイル(*.*)|*.*",
                FilterIndex = 1,
                Title = "既定のプレイヤー選択"
            };

            var result = ofd.ShowDialog();
            if (result == true)
            {
                DefaultPlayerPath.Text = ofd.FileName;
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (ThemeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag is string themeTag)
            {
                if (IndigoMovieManager.Properties.Settings.Default.ThemeMode != themeTag)
                {
                    IndigoMovieManager.Properties.Settings.Default.ThemeMode = themeTag;
                    IndigoMovieManager.Properties.Settings.Default.Save();
                    // アプリ全体に即時反映させる。
                    App.ApplyTheme(themeTag);
                }
            }
        }

        private void SkinComboBox_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e
        )
        {
            if (_isSkinSelectorInitializing)
            {
                return;
            }

            WhiteBrowserSkinOrchestrator skinOrchestrator = GetMainWindowSkinOrchestrator();
            if (skinOrchestrator == null)
            {
                return;
            }

            if (SkinComboBox.SelectedValue is string skinName && !string.IsNullOrWhiteSpace(skinName))
            {
                _ = skinOrchestrator.ApplySkinByName(skinName, persistToCurrentDb: true);
                UpdateSkinSelectorToolTip(
                    skinOrchestrator.GetCurrentSkinDefinition(),
                    hasCurrentDb: true
                );
            }
        }
    }
}
