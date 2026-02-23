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

        // タグ編集ダイアログの初期化。
        // 表示後に編集しやすいカーソル位置へ整える。
        public TagEdit()
        {
            InitializeComponent();
            ContentRendered += TagEdit_ContentRendered;
        }

        private void TagEdit_ContentRendered(object sender, EventArgs e)
        {
            // 既存テキスト末尾に追記しやすいよう、末尾へフォーカスを移動する。
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
            // OK/Cancelの押下結果を保持してダイアログを閉じる。
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
