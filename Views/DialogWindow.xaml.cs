using System.Windows;
using System.Windows.Controls;

namespace IndigoMovieManager.Views
{
    /// <summary>
    /// DialogWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class DialogWindow : Window
    {
        private MessageBoxResult _closeStatus = MessageBoxResult.Cancel;

        public DialogWindow()
        {
            InitializeComponent();
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
            Hide();
        }
    }
}
