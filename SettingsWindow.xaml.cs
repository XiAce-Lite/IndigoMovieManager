using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace IndigoMovieManager
{
    /// <summary>
    /// SettingsWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class SettingsWindow : Window
    {
        // 個別設定画面の初期化。
        // 画面表示時にプレイヤーパラメータ候補をセットして入力を補助する。
        public SettingsWindow()
        {
            InitializeComponent();
            PlayerParam.ItemsSource = new string[]
            {
                "/start <ms>",
                "<file> player -seek pos=<ms>",
            };

            // テーマの現在値を ComboBox に反映する。
            var currentTheme = Properties.Settings.Default.ThemeMode;
            ThemeComboBox.SelectedIndex = currentTheme == "Original" ? 1 : 0;
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // テーマが切り替えられたら即保存＆即反映する。
            if (ThemeComboBox.SelectedItem is ComboBoxItem item
                && item.Tag is string newTheme
                && !string.IsNullOrEmpty(newTheme))
            {
                Properties.Settings.Default.ThemeMode = newTheme;
                Properties.Settings.Default.Save();
                App.ApplyTheme(newTheme);
            }
        }

        private void BtnReturn_Click(object sender, RoutedEventArgs e)
        {
            // 個別設定画面を閉じてメインへ戻る。
            Close();
        }

        private void OpenFolderDialog_Click(object sender, RoutedEventArgs e)
        {
            // サムネイル/ブックマークの保存先フォルダを選択し、対応するTextBoxへ反映する。
            Button item = sender as Button;

            if (!(item.Name is "OpenThumbFolder" or "OpenBookmarkFolder"))
            {
                return;
            }

            var dlgTitle =
                item.Name == "OpenThumbFolder" ? "サムネイルの保存先" : "ブックマークの保存先";
            var dlg = new OpenFolderDialog
            {
                Title = dlgTitle,
                Multiselect = false,
                AddToRecent = true,
            };

            var ret = dlg.ShowDialog();

            TextBox textBox = item.Name == "OpenThumbFolder" ? ThumbFolder : BookmarkFolder;
            if (ret == true)
            {
                textBox.Text = dlg.FolderName;
            }
        }

        private void OpenDialogPlayer_Click(object sender, RoutedEventArgs e)
        {
            // 個別設定の再生プレイヤー実行ファイルを選択する。
            var ofd = new OpenFileDialog
            {
                InitialDirectory = Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles
                ),
                RestoreDirectory = true,
                Filter = "実行ファイル(*.exe)|*.exe|すべてのファイル(*.*)|*.*",
                FilterIndex = 1,
                Title = "既定のプレイヤー選択",
            };

            var result = ofd.ShowDialog();
            if (result == true)
            {
                PlayerPrg.Text = ofd.FileName;
            }
        }
    }
}
