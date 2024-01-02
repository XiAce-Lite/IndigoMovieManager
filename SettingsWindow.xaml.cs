using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace IndigoMovieManager
{
    /// <summary>
    /// SettingsWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            PlayerParam.ItemsSource = new string[]
            {
                "/start <ms>",
                "<file> player -seek pos=<ms>"
            };
        }

        private void BtnReturn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenFolderDialog_Click(object sender, RoutedEventArgs e)
        {
            Button item = sender as Button;

            if (!(item.Name is "OpenThumbFolder" or "OpenBookmarkFolder"))
            {
                return;
            }

            var dlgTitle = item.Name == "OpenThumbFolder" ? "サムネイルの保存先" : "ブックマークの保存先";
            var dlg = new OpenFolderDialog
            {
                Title = dlgTitle,
                Multiselect = false,
                AddToRecent = true,
            };

            var ret = dlg.ShowDialog();

            TextBox textBox = item.Name == "OpenThumbFolder" ?  ThumbFolder : BookmarkFolder;
            if (ret == true)
            {
                textBox.Text = dlg.FolderName;
            }
        }

        private void OpenDialogPlayer_Click(object sender, RoutedEventArgs e)
        {
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
                PlayerPrg.Text = ofd.FileName;
            }
        }
    }
}
