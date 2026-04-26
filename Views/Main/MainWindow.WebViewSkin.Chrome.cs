using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using IndigoMovieManager.Skin;
using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private bool _isExternalSkinMinimalSkinSelectorSyncing;

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
                SyncExternalSkinMinimalSkinSelector(minimalVisible, displaySkinName);
            }
            else
            {
                SyncExternalSkinMinimalSkinSelector(minimalVisible, "");
            }
        }

        // 最小 chrome では外部 skin 一覧だけをその場で切り替えられるように保つ。
        private void SyncExternalSkinMinimalSkinSelector(
            bool minimalVisible,
            string displaySkinName
        )
        {
            if (ExternalSkinMinimalSkinSelector == null)
            {
                return;
            }

            List<WhiteBrowserSkinDefinition> selectableDefinitions = [];
            if (minimalVisible)
            {
                foreach (WhiteBrowserSkinDefinition candidate in GetAvailableSkinDefinitions())
                {
                    if (candidate?.RequiresWebView2 == true)
                    {
                        selectableDefinitions.Add(candidate);
                    }
                }
            }

            _isExternalSkinMinimalSkinSelectorSyncing = true;
            try
            {
                ExternalSkinMinimalSkinSelector.ItemsSource = selectableDefinitions;
                ExternalSkinMinimalSkinSelector.IsEnabled =
                    minimalVisible && selectableDefinitions.Count > 0;
                ExternalSkinMinimalSkinSelector.SelectedValue = minimalVisible ? displaySkinName : null;
                ExternalSkinMinimalSkinSelector.ToolTip = minimalVisible ? displaySkinName : null;
            }
            finally
            {
                _isExternalSkinMinimalSkinSelectorSyncing = false;
            }
        }

        private void ExternalSkinMinimalSkinSelector_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e
        )
        {
            if (_isExternalSkinMinimalSkinSelectorSyncing || sender is not ComboBox selector)
            {
                return;
            }

            if (selector.SelectedValue is not string skinName || string.IsNullOrWhiteSpace(skinName))
            {
                return;
            }

            // 同じ skin を再選択した時は無駄な host refresh を積まず、表示名だけ同期する。
            if (string.Equals(GetCurrentSkinName(), skinName, StringComparison.OrdinalIgnoreCase))
            {
                selector.ToolTip = skinName;
                return;
            }

            if (ApplySkinByName(skinName, persistToCurrentDb: true))
            {
                selector.ToolTip = skinName;
                return;
            }

            WhiteBrowserSkinDefinition currentDefinition = GetCurrentExternalSkinDefinition();
            SyncExternalSkinMinimalSkinSelector(
                currentDefinition != null,
                ResolveRequestedSkinName(currentDefinition)
            );
        }

        private void ApplyExternalSkinFallbackNotice(
            string noticeText,
            string toolTipText,
            bool showRuntimeDownloadAction
        )
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

            if (ExternalSkinFallbackOpenRuntimeDownloadButton != null)
            {
                ExternalSkinFallbackOpenRuntimeDownloadButton.Visibility =
                    hasNotice && showRuntimeDownloadAction
                        ? Visibility.Visible
                        : Visibility.Collapsed;
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
            string runtimeDownloadUrl = ResolveExternalSkinRuntimeDownloadUrl();
            string nextAction = !operationResult.RuntimeAvailable
                ? "next: WebView2 Runtime 導入後に再読込、またはスキン再選択 / 再起動で再試行してください。"
                : string.Equals(operationResult.ErrorType, "SkinHtmlMissing", StringComparison.Ordinal)
                    ? "next: skin フォルダと HTML 配置を確認後に再読込してください。"
                    : "next: debug-runtime.log を確認し、再読込またはスキン再選択で再試行してください。";
            List<string> lines =
                new()
                {
                    $"skin: {skinName}",
                    $"errorType: {operationResult.ErrorType}",
                    $"error: {operationResult.ErrorMessage}",
                    $"reason: {reason ?? ""}",
                    "fallback: 標準 Grid / List 系表示へ戻しています。",
                    nextAction,
                };
            if (!operationResult.RuntimeAvailable)
            {
                lines.Add($"download: {runtimeDownloadUrl}");
            }

            lines.Add($"log: {logPath}");
            return string.Join(Environment.NewLine, lines);
        }

        private async void ExternalSkinMinimalReloadButton_Click(object sender, RoutedEventArgs e)
        {
            // blank 遷移完了を待ってから積み直し、clear と再 navigate の競合を避ける。
            await ClearExternalSkinHostBeforeRefreshAsync("minimal-chrome-reload");
            QueueExternalSkinHostRefresh("minimal-chrome-reload");
        }

        private async void ExternalSkinFallbackRetryButton_Click(object sender, RoutedEventArgs e)
        {
            if (GetCurrentExternalSkinDefinition() == null)
            {
                return;
            }

            await ClearExternalSkinHostBeforeRefreshAsync("fallback-notice-retry");
            QueueExternalSkinHostRefresh("fallback-notice-retry");
        }

        private async Task ClearExternalSkinHostBeforeRefreshAsync(string reason)
        {
            if (_externalSkinHostControl == null)
            {
                return;
            }

            try
            {
                await _externalSkinHostControl.ClearAsync();
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "skin-webview",
                    $"host clear before refresh failed: err='{ex.GetType().Name}: {ex.Message}' reason={reason}"
                );
            }
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

        private void ExternalSkinFallbackOpenRuntimeDownloadButton_Click(
            object sender,
            RoutedEventArgs e
        )
        {
            string runtimeDownloadUrl = ResolveExternalSkinRuntimeDownloadUrl();

            try
            {
                if (ExternalSkinFallbackOpenRuntimeDownloadActionForTesting != null)
                {
                    ExternalSkinFallbackOpenRuntimeDownloadActionForTesting(runtimeDownloadUrl);
                    return;
                }

                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = runtimeDownloadUrl,
                        UseShellExecute = true,
                    }
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "skin-webview",
                    $"fallback runtime download open failed: err='{ex.GetType().Name}: {ex.Message}' url='{runtimeDownloadUrl}'"
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
