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

        // =================================================================================
        // リネーム用ダイアログ画面のUI処理(View層)
        // 表示時のフォーカス調整から、確定前の簡単な入力値検証(未入力ブロック等)までを管理する。
        // =================================================================================

        public RenameFile()
        {
            InitializeComponent();
            SourceInitialized += (_, _) => App.ApplyWindowTitleBarTheme(this);
            // 閉じる(×ボタン等を含む)直前に発生するイベントの接続
            Closing += RenameFile_Closing;
            // 画面が描画されユーザに見えたタイミングに発生するイベントの接続
            ContentRendered += RenameFile_ContentRendered;
        }

        /// <summary>
        /// 画面描画後の処理。ユーザが即座にタイピング開始できるようファイル名欄へフォーカス枠を当てる。
        /// </summary>
        private void RenameFile_ContentRendered(object sender, EventArgs e)
        {
            FileNameEditBox.Focus();
        }

        /// <summary>
        /// 画面が閉じられる寸前の検査処理。
        /// OKボタンや右上の「×」等、すべての「閉じる」経路で呼び出される。
        /// </summary>
        private void RenameFile_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // キャンセルボタンなどで閉じる場合はスキップさせても良いが、
            // 現状は無条件で最低限の「空文字チェック」が掛かるため、空だとそもそも閉じられない可能性がある。
            if (string.IsNullOrEmpty(FileNameEditBox.Text))
            {
                MessageBox.Show(
                    "ファイル名が入力されていません。",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Exclamation
                );
                FileNameEditBox.Focus();
                e.Cancel = true; // 閉じる処理を中断
                return;
            }

            if (string.IsNullOrEmpty(ExtEditBox.Text))
            {
                MessageBox.Show(
                    "拡張子が入力されていません。",
                    Assembly.GetExecutingAssembly().GetName().Name,
                    MessageBoxButton.OK,
                    MessageBoxImage.Exclamation
                );
                ExtEditBox.Focus();
                e.Cancel = true; // 閉じる処理を中断
                return;
            }
        }

        /// <summary>
        /// 呼び出し元の画面(MainWindow等)が、ユーザーの最終判断(OKかCancelか)を確認するためのプロパティ代替メソッド。
        /// </summary>
        public MessageBoxResult CloseStatus()
        {
            return _closeStatus;
        }

        /// <summary>
        /// OKまたはCancelボタンが押下された時の処理。
        /// </summary>
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

            // 「OK」が意図された場合は追加で入力内容をチェック。
            // ※RenameFile_Closing と内容が二重チェックになっている部分がある。
            if (_closeStatus == MessageBoxResult.OK)
            {
                if (string.IsNullOrEmpty(FileNameEditBox.Text))
                {
                    MessageBox.Show(
                        "ファイル名が入力されていません。",
                        Assembly.GetExecutingAssembly().GetName().Name,
                        MessageBoxButton.OK,
                        MessageBoxImage.Exclamation
                    );
                    FileNameEditBox.Focus();
                    return; // 画面を閉じずに待機状態に戻す
                }

                if (string.IsNullOrEmpty(ExtEditBox.Text))
                {
                    MessageBox.Show(
                        "拡張子が入力されていません。",
                        Assembly.GetExecutingAssembly().GetName().Name,
                        MessageBoxButton.OK,
                        MessageBoxImage.Exclamation
                    );
                    ExtEditBox.Focus();
                    return; // 画面を閉じずに待機状態に戻す
                }
            }

            // チェックを潜り抜けた場合のみ表示を隠す(Hide)ことで、擬似的に終了させる。
            // 呼び出し元が CloseStatus() と編集値を取り出すため。
            Hide();
        }

        /// <summary>
        /// 拡張子の入力欄にフォーカスが移った際、キー入力で即上書きできるよう全テキストを選択状態にする。
        /// </summary>
        private void ExtEditBox_GotFocus(object sender, RoutedEventArgs e)
        {
            ExtEditBox.SelectAll();
        }

        /// <summary>
        /// 名前の入力欄にフォーカスが移った際、全テキストを選択状態にする。
        /// </summary>
        private void FileNameEditBox_GotFocus(object sender, RoutedEventArgs e)
        {
            FileNameEditBox.SelectAll();
        }
    }
}
