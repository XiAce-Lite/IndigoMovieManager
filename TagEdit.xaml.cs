using System.Windows;
using System.Windows.Controls;

namespace IndigoMovieManager
{
    /// <summary>
    /// TagEdit.xaml の相互作用ロジック
    /// </summary>
    public partial class TagEdit : Window
    {
        private MessageBoxResult _closeStatus = MessageBoxResult.Cancel;

        public TagEdit()
        {
            InitializeComponent();
            ContentRendered += TagEdit_ContentRendered;
        }

        private void TagEdit_ContentRendered(object sender, EventArgs e)
        {
            _ = TagEditBox.Focus();
            if (!string.IsNullOrEmpty(TagEditBox.Text))
            {
                TagEditBox.Text += Environment.NewLine;
                TagEditBox.Select(TagEditBox.Text.Length, 0);
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
            Hide();
        }
    }
}
