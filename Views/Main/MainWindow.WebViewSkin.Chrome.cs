using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using IndigoMovieManager.Skin;
using IndigoMovieManager.Skin.Runtime;

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

        private void ApplyExternalSkinFallbackNotice(string noticeText, string toolTipText)
        {
            bool hasNotice = !string.IsNullOrWhiteSpace(noticeText);

            if (ExternalSkinFallbackNoticeBorder != null)
            {
                ExternalSkinFallbackNoticeBorder.Visibility = hasNotice
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                ExternalSkinFallbackNoticeBorder.ToolTip = hasNotice ? toolTipText : null;
            }

            if (ExternalSkinFallbackNoticeText != null)
            {
                ExternalSkinFallbackNoticeText.Text = hasNotice ? noticeText : "";
                ExternalSkinFallbackNoticeText.ToolTip = hasNotice ? toolTipText : null;
            }
        }

        private static string BuildExternalSkinFallbackNoticeText(
            WhiteBrowserSkinDefinition definition,
            WhiteBrowserSkinHostOperationResult operationResult
        )
        {
            string skinName = operationResult?.RequestedSkinName ?? definition?.Name ?? "";
            if (operationResult == null)
            {
                return "";
            }

            if (!operationResult.RuntimeAvailable)
            {
                return $"外部スキン「{skinName}」は WebView2 Runtime が見つからないため表示できません。標準表示で継続できます。";
            }

            if (
                string.Equals(
                    operationResult.ErrorType,
                    "SkinHtmlMissing",
                    StringComparison.Ordinal
                )
            )
            {
                return $"外部スキン「{skinName}」の HTML が見つからないため表示できません。標準表示へ戻しています。";
            }

            return $"外部スキン「{skinName}」の初期化に失敗したため標準表示へ戻しています。";
        }

        private static string BuildExternalSkinFallbackNoticeToolTip(
            WhiteBrowserSkinDefinition definition,
            WhiteBrowserSkinHostOperationResult operationResult,
            string reason
        )
        {
            if (operationResult == null)
            {
                return "";
            }

            string skinName = operationResult.RequestedSkinName ?? definition?.Name ?? "";
            string logPath = ResolveExternalSkinFallbackLogPath();
            string nextAction = !operationResult.RuntimeAvailable
                ? "next: WebView2 Runtime 導入後に再読込、またはスキン再選択 / 再起動で再試行してください。"
                : string.Equals(operationResult.ErrorType, "SkinHtmlMissing", StringComparison.Ordinal)
                    ? "next: skin フォルダと HTML 配置を確認後に再読込してください。"
                    : "next: debug-runtime.log を確認し、再読込またはスキン再選択で再試行してください。";
            return string.Join(
                Environment.NewLine,
                new[]
                {
                    $"skin: {skinName}",
                    $"errorType: {operationResult.ErrorType}",
                    $"error: {operationResult.ErrorMessage}",
                    $"reason: {reason ?? ""}",
                    "fallback: 標準 Grid / List 系表示へ戻しています。",
                    nextAction,
                    $"log: {logPath}",
                }
            );
        }

        private void ExternalSkinMinimalReloadButton_Click(object sender, RoutedEventArgs e)
        {
            // host 再読込は現在 skin を維持したまま、WebView 側だけを積み直す。
            _externalSkinHostControl?.Clear();
            QueueExternalSkinHostRefresh("minimal-chrome-reload");
        }

        private void ExternalSkinFallbackRetryButton_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentExternalSkinDefinition() == null)
            {
                return;
            }

            _externalSkinHostControl?.Clear();
            QueueExternalSkinHostRefresh("fallback-notice-retry");
        }

        private void ExternalSkinFallbackOpenLogButton_Click(object sender, RoutedEventArgs e)
        {
            string logPath = ResolveExternalSkinFallbackLogPath();

            try
            {
                if (ExternalSkinFallbackOpenLogActionForTesting != null)
                {
                    ExternalSkinFallbackOpenLogActionForTesting(logPath);
                    return;
                }

                string targetDirectory = Path.GetDirectoryName(logPath) ?? "";
                if (File.Exists(logPath))
                {
                    Process.Start("explorer.exe", $"/select,{logPath}");
                    return;
                }

                if (Directory.Exists(targetDirectory))
                {
                    Process.Start("explorer.exe", targetDirectory);
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "skin-webview",
                    $"fallback log open failed: err='{ex.GetType().Name}: {ex.Message}' path='{logPath}'"
                );
            }
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
