using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace IndigoMovieManager.UserControls
{
    /// <summary>
    /// ImageControl.xaml の相互作用ロジック
    /// </summary>
    public partial class ImageControl : UserControl
    {
        public ImageControl()
        {
            InitializeComponent();
        }

        private async void PlayMovie(object sender, RoutedEventArgs e)
        {
            MainWindow ownerWindow = (MainWindow)Window.GetWindow(this);

            if (ownerWindow.Tabs.SelectedItem == null) return;

            MovieRecords mv;
            mv = ownerWindow.GetSelectedItemByTabIndex();
            if (mv == null) return;

            var playerPrg = ownerWindow.SelectSystemTable("playerPrg");
            var playerParam = ownerWindow.SelectSystemTable("playerParam");

            //設定DBごとのプレイヤーが空
            if (string.IsNullOrEmpty(playerPrg))
            {
                //全体設定のプレイヤーを設定
                playerPrg = Properties.Settings.Default.DefaultPlayerPath;
            }

            //設定DBごとのプレイヤーパラメータが空
            if (string.IsNullOrEmpty(playerParam))
            {
                //全体設定のプレイヤーパラメータを設定
                playerParam = Properties.Settings.Default.DefaultPlayerParam;
            }

            int msec = 0;
            int secPos = 0; //ここでは渡す為だけに使ってる。
            if (sender is MenuItem senderObj)
            {
                if (senderObj.Name == "PlayFromThumb")
                {
                    msec = ownerWindow.GetPlayPosition(ownerWindow.Tabs.SelectedIndex, mv, ref secPos);
                }
            }

            if (!string.IsNullOrEmpty(playerParam))
            {
                playerParam = playerParam.Replace("<file>", $"{mv.Movie_Path}");
                playerParam = playerParam.Replace("<ms>", $"{msec}");
            }

            var moviePath = $"\"{mv.Movie_Path}\"";
            var arg = $"{moviePath} {playerParam}";

            try
            {
                using Process ps1 = new();
                //設定ファイルのプログラムも既定のプログラムも空だった場合にはここのはず。
                if (string.IsNullOrEmpty(playerPrg))
                {
                    ps1.StartInfo.UseShellExecute = true;
                    ps1.StartInfo.FileName = moviePath;
                }
                else
                {
                    ps1.StartInfo.Arguments = arg;
                    ps1.StartInfo.FileName = playerPrg;
                }
                ps1.Start();

                var psName = ps1.ProcessName;
                Process ps2 = Process.GetProcessById(ps1.Id);
                foreach (Process p in Process.GetProcessesByName(psName))
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        if (p.MainWindowTitle.Contains(mv.Movie_Name, StringComparison.CurrentCultureIgnoreCase))
                        {
                            p.Kill();
                            await p.WaitForExitAsync();
                        }
                    }
                }
            }
            catch (Exception err)
            {
                MessageBox.Show(err.Message, Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        private void Label_MouseDown(object sender, MouseButtonEventArgs e)
        {
            MainWindow ownerWindow = (MainWindow)Window.GetWindow(this);

            ownerWindow.lbClickPoint = e.GetPosition(sender as Label);
        }
    }
}
