using MaterialDesignThemes.Wpf;
using System.Windows;
using System.Windows.Controls;

namespace IndigoMovieManager
{
    /// <summary>
    /// DialogWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class DialogWindowEx : Window
    {
        private MessageBoxResult _closeStatus = MessageBoxResult.Cancel;

        public string DlogTitle = "";
        public string DlogMessage = "";
        public PackIconKind PackIconKind = PackIconKind.InfoBox;
        public bool UseCheckBox = false;
        public bool IsChecked = false;
        public string CheckBoxContent = "";
        public bool UseRadioButton = false;

        public DialogWindowEx(Window owner)
        {
            InitializeComponent();
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ContentRendered += DialogWindowEx_ContentRendered;
        }

        private void DialogWindowEx_ContentRendered(object sender, EventArgs e)
        {
            Title = DlogTitle;
            dlogMessage.Text = DlogMessage;
            dlogIcon.Kind = PackIconKind;
            checkBox.Content = CheckBoxContent;
            checkBox.IsChecked = IsChecked;

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
