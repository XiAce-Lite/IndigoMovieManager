using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace IndigoMovieManager
{
    /// <summary>
    /// RenameFile.xaml の相互作用ロジック
    /// </summary>
    public partial class RenameFile : Window
    {
        private MessageBoxResult _closeStatus = MessageBoxResult.Cancel;

        public RenameFile()
        {
            InitializeComponent();
            Closing += RenameFile_Closing;
            ContentRendered += RenameFile_ContentRendered;
        }

        private void RenameFile_ContentRendered(object sender, EventArgs e)
        {
            FileNameEditBox.Focus();
        }

        private void RenameFile_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (string.IsNullOrEmpty(FileNameEditBox.Text))
            {
                MessageBox.Show("ファイル名が入力されていません。", Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                FileNameEditBox.Focus();
                e.Cancel = true;
                return;
            }

            if (string.IsNullOrEmpty(ExtEditBox.Text)) {
                MessageBox.Show("拡張子が入力されていません。", Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                ExtEditBox.Focus();
                e.Cancel = true;
                return;
            }
        }

        public MessageBoxResult CloseStatus() { return _closeStatus; }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                _closeStatus = btn.Name switch
                {
                    "OK" => MessageBoxResult.OK,
                    "Cancel" => MessageBoxResult.Cancel,
                    _ => MessageBoxResult.Cancel,
                };
            }

            if (_closeStatus == MessageBoxResult.OK)
            {
                if (string.IsNullOrEmpty(FileNameEditBox.Text))
                {
                    MessageBox.Show("ファイル名が入力されていません。", Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    FileNameEditBox.Focus();
                    return;
                }

                if (string.IsNullOrEmpty(ExtEditBox.Text))
                {
                    MessageBox.Show("拡張子が入力されていません。", Assembly.GetExecutingAssembly().GetName().Name, MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    ExtEditBox.Focus();
                    return;
                }
            }

            Hide();
        }

        private void ExtEditBox_GotFocus(object sender, RoutedEventArgs e)
        {
            ExtEditBox.SelectAll();
        }

        private void FileNameEditBox_GotFocus(object sender, RoutedEventArgs e)
        {
            FileNameEditBox.SelectAll();
        }
    }
}
