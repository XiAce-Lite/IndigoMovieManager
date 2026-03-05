using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using IndigoMovieManager.Watcher;

namespace IndigoMovieManager
{
    /// <summary>
    /// Settings.xaml の相互作用ロジック
    /// </summary>
    public partial class CommonSettingsWindow : Window
    {
        private bool _isThumbnailParallelismSyncing;

        // 共通設定画面の初期化。
        // 閉じるイベントで設定保存するため、ここでイベントを接続する。
        public CommonSettingsWindow()
        {
            InitializeComponent();
            Closing += OnClosing;
            Closed += CommonSettingsWindow_Closed;
            PreviewKeyDown += CommonSettingsWindow_PreviewKeyDown;
            sliderThumbnailParallelism.ValueChanged += SliderThumbnailParallelism_ValueChanged;
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
            SyncThumbnailParallelismSliderFromSettings();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            // 画面上の値を Properties.Settings へ反映して永続化する。
            Properties.Settings.Default.AutoOpen = (bool)AutoOpen.IsChecked;
            Properties.Settings.Default.ConfirmExit = (bool)ConfirmExit.IsChecked;
            Properties.Settings.Default.DefaultPlayerPath = DefaultPlayerPath.Text;
            Properties.Settings.Default.DefaultPlayerParam = DefaultPlayerParam.Text;
            Properties.Settings.Default.RecentFilesCount = (int)slider.Value;
            Properties.Settings.Default.ThumbnailGpuDecodeEnabled = (bool)ThumbnailGpuDecodeEnabled.IsChecked;
            // Delキー押下時の動作を保存する。範囲外は既存互換の「登録解除」に戻す。
            int deleteKeyActionMode = DeleteKeyActionMode.SelectedIndex;
            if (deleteKeyActionMode < 0 || deleteKeyActionMode > 1)
            {
                deleteKeyActionMode = 0;
            }
            Properties.Settings.Default.DeleteKeyActionMode = deleteKeyActionMode;
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
            // サムネイル作成の並列数を保存する（1〜24）。
            Properties.Settings.Default.ThumbnailParallelism = (int)sliderThumbnailParallelism.Value;
            Properties.Settings.Default.Save();
        }

        // 共通設定を閉じる時にイベント購読を解除する。
        private void CommonSettingsWindow_Closed(object sender, System.EventArgs e)
        {
            PreviewKeyDown -= CommonSettingsWindow_PreviewKeyDown;
            sliderThumbnailParallelism.ValueChanged -= SliderThumbnailParallelism_ValueChanged;
            Properties.Settings.Default.PropertyChanged -= SettingsDefault_PropertyChanged;
            Closed -= CommonSettingsWindow_Closed;
        }

        // スライダー変更は即座に設定へ反映し、実行中の並列数制御にも即時反映させる。
        private void SliderThumbnailParallelism_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e
        )
        {
            if (_isThumbnailParallelismSyncing)
            {
                return;
            }

            int next = ClampThumbnailParallelism((int)System.Math.Round(e.NewValue));
            if (Properties.Settings.Default.ThumbnailParallelism == next)
            {
                return;
            }

            Properties.Settings.Default.ThumbnailParallelism = next;
        }

        // 他経路（ショートカット等）で設定値が変わった時、スライダーを即時追従させる。
        private void SettingsDefault_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (
                !string.Equals(
                    e?.PropertyName,
                    nameof(Properties.Settings.Default.ThumbnailParallelism),
                    System.StringComparison.Ordinal
                )
            )
            {
                return;
            }

            SyncThumbnailParallelismSliderFromSettings();
        }

        // 共通設定画面でも Ctrl + / Ctrl - で並列数を即時変更する。
        private void CommonSettingsWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            ModifierKeys modifiers = Keyboard.Modifiers;
            if ((modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                return;
            }
            if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            int delta = key switch
            {
                Key.Add => 1,
                Key.OemPlus => 1,
                Key.Subtract => -1,
                Key.OemMinus => -1,
                _ => 0,
            };
            if (delta == 0)
            {
                return;
            }

            int current = ClampThumbnailParallelism(Properties.Settings.Default.ThumbnailParallelism);
            int next = ClampThumbnailParallelism(current + delta);
            if (current != next)
            {
                Properties.Settings.Default.ThumbnailParallelism = next;
            }

            e.Handled = true;
        }

        // 設定値をスライダーへ同期する。値が同じ場合は何もしない。
        private void SyncThumbnailParallelismSliderFromSettings()
        {
            int next = ClampThumbnailParallelism(Properties.Settings.Default.ThumbnailParallelism);
            if (Properties.Settings.Default.ThumbnailParallelism != next)
            {
                Properties.Settings.Default.ThumbnailParallelism = next;
            }

            if (System.Math.Abs(sliderThumbnailParallelism.Value - next) < 0.0001d)
            {
                return;
            }

            _isThumbnailParallelismSyncing = true;
            try
            {
                sliderThumbnailParallelism.Value = next;
            }
            finally
            {
                _isThumbnailParallelismSyncing = false;
            }
        }

        // サムネイル並列数は 1〜24 の範囲に制限する。
        private static int ClampThumbnailParallelism(int value)
        {
            if (value < 1)
            {
                return 1;
            }
            if (value > 24)
            {
                return 24;
            }
            return value;
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
    }
}
