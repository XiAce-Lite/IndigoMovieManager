using System.Windows;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        // host chrome は skin の上に被せるのではなく、MainHeader の中で最小シェルへ切り替える。
        private void ApplyExternalSkinMinimalChromeVisibility(
            bool hostReady,
            IndigoMovieManager.Skin.WhiteBrowserSkinDefinition definition
        )
        {
            bool minimalVisible = hostReady && definition?.RequiresWebView2 == true;

            if (MainHeaderStandardChromePanel != null)
            {
                MainHeaderStandardChromePanel.Visibility = minimalVisible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }

            if (ExternalSkinMinimalChromePanel != null)
            {
                ExternalSkinMinimalChromePanel.Visibility = minimalVisible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (ExternalSkinMinimalSkinNameText != null)
            {
                string displaySkinName = minimalVisible ? ResolveRequestedSkinName(definition) : "";
                ExternalSkinMinimalSkinNameText.Text = displaySkinName;
                ExternalSkinMinimalSkinNameText.ToolTip = displaySkinName;
            }
        }

        private void ExternalSkinMinimalReloadButton_Click(object sender, RoutedEventArgs e)
        {
            // host 再読込は現在 skin を維持したまま、WebView 側だけを積み直す。
            _externalSkinHostControl?.Clear();
            QueueExternalSkinHostRefresh("minimal-chrome-reload");
        }

        private void ExternalSkinBackToGridButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ApplySkinByName("DefaultGrid"))
            {
                if (MainVM?.DbInfo != null)
                {
                    MainVM.DbInfo.Skin = "DefaultGrid";
                }

                SelectUpperTabGridAsDefaultView();
            }
        }

        private void ExternalSkinMinimalSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            MenuToggleButton.IsChecked = false;
            CommonSettingsWindow commonSettingsWindow = new()
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            commonSettingsWindow.ShowDialog();
            ApplyThumbnailGpuDecodeSetting();
        }
    }
}
