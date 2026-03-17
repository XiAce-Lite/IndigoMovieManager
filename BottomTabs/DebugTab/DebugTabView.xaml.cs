using System;
using System.Windows;
using System.Windows.Controls;

namespace IndigoMovieManager.BottomTabs.Debug
{
    public partial class DebugTabView : UserControl
    {
        public DebugTabView()
        {
            InitializeComponent();
        }

        public event RoutedEventHandler OpenCurrentDbDirRequested;
        public event RoutedEventHandler ClearCurrentDbRecordsRequested;
        public event RoutedEventHandler DeleteCurrentDbRequested;
        public event RoutedEventHandler RefreshCurrentDbRecordCountRequested;
        public event RoutedEventHandler OpenQueueDbDirRequested;
        public event RoutedEventHandler ClearQueueDbRecordsRequested;
        public event RoutedEventHandler DeleteQueueDbRequested;
        public event RoutedEventHandler RefreshQueueDbRecordCountRequested;
        public event RoutedEventHandler OpenThumbnailDirRequested;
        public event RoutedEventHandler RecreateAllThumbnailsRequested;
        public event RoutedEventHandler DeleteThumbnailDirRequested;

        public TextBox CurrentDbPathTextBox => DebugCurrentDbPathText;
        public TextBlock CurrentDbRecordCountTextBlock => DebugCurrentDbRecordCountText;
        public TextBox CurrentQueueDbPathTextBox => DebugCurrentQueueDbPathText;
        public TextBlock CurrentQueueDbRecordCountTextBlock => DebugCurrentQueueDbRecordCountText;
        public TextBox CurrentThumbnailPathTextBox => DebugCurrentThumbnailPathText;
        public TextBlock LogPathTextBlock => DebugLogPathText;
        public TextBlock LogInfoTextBlock => DebugLogInfoText;
        public TextBox LogTextBox => DebugLogTextBox;

        public void ScrollLogToEnd()
        {
            DebugLogTextBox?.ScrollToEnd();
        }

        private void DebugOpenCurrentDbDirButton_Click(object sender, RoutedEventArgs e)
        {
            OpenCurrentDbDirRequested?.Invoke(sender, e);
        }

        private void DebugClearCurrentDbRecordsButton_Click(object sender, RoutedEventArgs e)
        {
            ClearCurrentDbRecordsRequested?.Invoke(sender, e);
        }

        private void DebugDeleteCurrentDbButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteCurrentDbRequested?.Invoke(sender, e);
        }

        private void DebugRefreshCurrentDbRecordCountButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshCurrentDbRecordCountRequested?.Invoke(sender, e);
        }

        private void DebugOpenQueueDbDirButton_Click(object sender, RoutedEventArgs e)
        {
            OpenQueueDbDirRequested?.Invoke(sender, e);
        }

        private void DebugClearQueueDbRecordsButton_Click(object sender, RoutedEventArgs e)
        {
            ClearQueueDbRecordsRequested?.Invoke(sender, e);
        }

        private void DebugDeleteQueueDbButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteQueueDbRequested?.Invoke(sender, e);
        }

        private void DebugRefreshQueueDbRecordCountButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshQueueDbRecordCountRequested?.Invoke(sender, e);
        }

        private void DebugOpenThumbnailDirButton_Click(object sender, RoutedEventArgs e)
        {
            OpenThumbnailDirRequested?.Invoke(sender, e);
        }

        private void DebugRecreateAllThumbnailsButton_Click(object sender, RoutedEventArgs e)
        {
            RecreateAllThumbnailsRequested?.Invoke(sender, e);
        }

        private void DebugDeleteThumbnailDirButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteThumbnailDirRequested?.Invoke(sender, e);
        }
    }
}
