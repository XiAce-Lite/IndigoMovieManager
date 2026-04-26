using System.Windows;
using System.Windows.Controls;

namespace IndigoMovieManager.BottomTabs.Log
{
    public partial class LogTabView : UserControl
    {
        public LogTabView()
        {
            InitializeComponent();
        }

        public event RoutedEventHandler OpenLogDirRequested;
        public event RoutedEventHandler RefreshPreviewRequested;
        public event RoutedEventHandler SwitchChangedRequested;

        public TextBlock LogPathTextBlock => DebugLogPathText;
        public TextBlock LogInfoTextBlock => DebugLogInfoText;
        public TextBlock LogSwitchInfoTextBlock => LogSwitchInfoText;
        public TextBox LogTextBox => DebugLogTextBox;

        public void ScrollLogToEnd()
        {
            DebugLogTextBox?.ScrollToEnd();
        }

        private void OpenLogDirButton_Click(object sender, RoutedEventArgs e)
        {
            OpenLogDirRequested?.Invoke(sender, e);
        }

        private void RefreshPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshPreviewRequested?.Invoke(sender, e);
        }

        private void LogSwitchCheckBox_Click(object sender, RoutedEventArgs e)
        {
            SwitchChangedRequested?.Invoke(sender, e);
        }
    }
}
