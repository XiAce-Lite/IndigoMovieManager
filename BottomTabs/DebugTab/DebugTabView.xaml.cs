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

        public event RoutedEventHandler OpenAppDataDirRequested;
        public event RoutedEventHandler OpenCurrentDirRequested;
        public event RoutedEventHandler OpenThumbDirRequested;
        public event RoutedEventHandler OpenDbDirRequested;
        public event RoutedEventHandler OpenLogDirRequested;
        public event RoutedEventHandler OpenFailureDbDirRequested;
        public event RoutedEventHandler ClearFailureDbRecordsRequested;
        public event RoutedEventHandler DeleteFailureDbRequested;
        public event RoutedEventHandler RefreshFailureDbRecordCountRequested;
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
        public TextBox CurrentFailureDbPathTextBox => DebugCurrentFailureDbPathText;
        public TextBlock CurrentFailureDbRecordCountTextBlock => DebugCurrentFailureDbRecordCountText;
        public TextBox CurrentThumbnailPathTextBox => DebugCurrentThumbnailPathText;

        private void DebugOpenAppDataDirButton_Click(object sender, RoutedEventArgs e)
        {
            OpenAppDataDirRequested?.Invoke(sender, e);
        }

        private void DebugOpenCurrentDirButton_Click(object sender, RoutedEventArgs e)
        {
            OpenCurrentDirRequested?.Invoke(sender, e);
        }

        private void DebugOpenThumbDirButton_Click(object sender, RoutedEventArgs e)
        {
            OpenThumbDirRequested?.Invoke(sender, e);
        }

        private void DebugOpenDbDirButton_Click(object sender, RoutedEventArgs e)
        {
            OpenDbDirRequested?.Invoke(sender, e);
        }

        private void DebugOpenLogDirButton_Click(object sender, RoutedEventArgs e)
        {
            OpenLogDirRequested?.Invoke(sender, e);
        }

        private void DebugOpenFailureDbDirButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFailureDbDirRequested?.Invoke(sender, e);
        }

        private void DebugClearFailureDbRecordsButton_Click(object sender, RoutedEventArgs e)
        {
            ClearFailureDbRecordsRequested?.Invoke(sender, e);
        }

        private void DebugDeleteFailureDbButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteFailureDbRequested?.Invoke(sender, e);
        }

        private void DebugRefreshFailureDbRecordCountButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshFailureDbRecordCountRequested?.Invoke(sender, e);
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
