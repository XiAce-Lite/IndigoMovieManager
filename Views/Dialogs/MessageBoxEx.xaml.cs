using MaterialDesignThemes.Wpf;
using System.Windows.Input;
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
        public string DlogHeadline = "";
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
        public bool Radio1IsEnabled = true;
        public bool Radio2IsEnabled = true;
        public bool AllowOwnerMouseWheelPassthrough = false;
        public Brush DialogAccentBrush;
        public Brush DialogAccentForegroundBrush;
        public PackIconKind? Radio1PackIconKind;
        public PackIconKind? Radio2PackIconKind;
        public Brush Radio1AccentBrush;
        public Brush Radio2AccentBrush;
        public Brush Radio1AccentForegroundBrush;
        public Brush Radio2AccentForegroundBrush;

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
            string headline = string.IsNullOrWhiteSpace(DlogHeadline) ? DlogMessage : DlogHeadline;
            string detailMessage = string.IsNullOrWhiteSpace(DlogHeadline) ? "" : DlogMessage;
            dlogHeadline.Text = headline;
            dlogMessage.Text = detailMessage;
            dlogMessage.Visibility = string.IsNullOrWhiteSpace(detailMessage)
                ? Visibility.Collapsed
                : Visibility.Visible;
            checkBox.Content = CheckBoxContent;
            checkBox.IsChecked = CheckBoxIsChecked;
            radioButton1.IsChecked = Radio1IsChecked;
            radioButton2.IsChecked = Radio2IsChecked;
            radioButton1.Content = Radio1Content;
            radioButton2.Content = Radio2Content;
            radioButton1.IsEnabled = Radio1IsEnabled;
            radioButton2.IsEnabled = Radio2IsEnabled;
            ApplyDialogVisuals();

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

        // 基本の色とアイコンを適用しつつ、ラジオ連動の上書きがあれば優先する。
        private void ApplyDialogVisuals()
        {
            PackIconKind iconKind = PackIconKind;
            Brush accentBrush = DialogAccentBrush;
            Brush foregroundBrush = DialogAccentForegroundBrush ?? Brushes.White;

            if (UseRadioButton)
            {
                if (radioButton2.IsChecked == true)
                {
                    iconKind = Radio2PackIconKind ?? iconKind;
                    accentBrush = Radio2AccentBrush ?? accentBrush;
                    foregroundBrush = Radio2AccentForegroundBrush ?? foregroundBrush;
                }
                else
                {
                    iconKind = Radio1PackIconKind ?? iconKind;
                    accentBrush = Radio1AccentBrush ?? accentBrush;
                    foregroundBrush = Radio1AccentForegroundBrush ?? foregroundBrush;
                }
            }

            dlogIcon.Kind = iconKind;
            if (accentBrush != null)
            {
                dialogColorZone.Background = accentBrush;
            }

            dlogIcon.Foreground = foregroundBrush;
            dlogHeadline.Foreground = foregroundBrush;
            dlogMessage.Foreground = foregroundBrush;
            radioButton1.Foreground = foregroundBrush;
            radioButton2.Foreground = foregroundBrush;
            checkBox.Foreground = foregroundBrush;
        }

        // ファイル削除のラジオ切替時に、危険度に応じたアイコンと色へ即時追従させる。
        private void RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            ApplyDialogVisuals();
        }

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

        // 確認ダイアログを閉じずに一覧確認したい時だけ、ホイールを owner 側のスクロールへ渡す。
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!AllowOwnerMouseWheelPassthrough)
            {
                return;
            }

            if (Owner is not MainWindow ownerWindow)
            {
                return;
            }

            ownerWindow.ScrollCurrentUpperTabByMouseWheel(e.Delta);
            e.Handled = true;
        }
    }
}
