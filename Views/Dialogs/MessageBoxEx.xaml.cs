using MaterialDesignThemes.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IndigoMovieManager
{
    /// <summary>
    /// DialogWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MessageBoxEx : Window
    {
        private MessageBoxResult _closeStatus = MessageBoxResult.Cancel;

        public string DlogTitle = "";
        public string DlogMessage = "";
        public PackIconKind PackIconKind = PackIconKind.InfoBox;
        public bool UseCheckBox = false;
        public bool CheckBoxIsChecked = false;
        public string CheckBoxContent = "";
        public string Radio1Content = "";
        public string Radio2Content = "";
        public bool UseRadioButton = false;
        public bool Radio1IsChecked = true;
        public bool Radio2IsChecked = false;
        public Brush DialogAccentBrush;
        public Brush DialogAccentForegroundBrush;

        // 呼び出し元ウィンドウをオーナーとして保持し、中央表示で初期化する。
        public MessageBoxEx(Window owner)
        {
            InitializeComponent();
            SourceInitialized += (_, _) => App.ApplyWindowTitleBarTheme(this);
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ContentRendered += DialogWindowEx_ContentRendered;
        }

        private void DialogWindowEx_ContentRendered(object sender, EventArgs e)
        {
            // プロパティで渡された表示内容を、実際のUI部品へ流し込む。
            Title = DlogTitle;
            dlogMessage.Text = DlogMessage;
            dlogIcon.Kind = PackIconKind;
            checkBox.Content = CheckBoxContent;
            checkBox.IsChecked = CheckBoxIsChecked;
            radioButton1.IsChecked = true;
            radioButton1.Content = Radio1Content;
            radioButton2.Content = Radio2Content;
            if (DialogAccentBrush != null)
            {
                // ショートカット種別ごとの危険度が一目で分かるよう、背景色だけ差し替える。
                dialogColorZone.Background = DialogAccentBrush;
                Brush foregroundBrush = DialogAccentForegroundBrush ?? Brushes.White;
                dlogIcon.Foreground = foregroundBrush;
                dlogMessage.Foreground = foregroundBrush;
                radioButton1.Foreground = foregroundBrush;
                radioButton2.Foreground = foregroundBrush;
                checkBox.Foreground = foregroundBrush;
            }

            if (!UseCheckBox)
            {
                checkArea.Visibility = Visibility.Collapsed;
            }

            if (!UseRadioButton)
            {
                radioArea.Visibility = Visibility.Collapsed;
            }
        }

        public MessageBoxResult CloseStatus() { return _closeStatus; }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            // OK/Cancelの結果と、チェック・ラジオの状態を回収して閉じる。
            if (sender is Button btn)
            {
                _closeStatus = btn.Name switch
                {
                    "OK" => MessageBoxResult.OK,
                    "Cancel" => MessageBoxResult.Cancel,
                    _ => MessageBoxResult.Cancel,
                };
                CheckBoxIsChecked = (bool)checkBox.IsChecked;
                Radio1IsChecked = (bool)radioButton1.IsChecked;
                Radio2IsChecked = (bool)radioButton2.IsChecked;
            }
            Hide();
        }
    }
}
