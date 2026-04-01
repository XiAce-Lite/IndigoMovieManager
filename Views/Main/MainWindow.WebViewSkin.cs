using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using IndigoMovieManager.Skin;
using IndigoMovieManager.Skin.Host;
using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const string ExternalSkinCacheFolderName = "IndigoMovieManager_fork_workthree";

        private WhiteBrowserSkinHostControl _externalSkinHostControl;
        private bool _externalSkinHostRefreshRunning;
        private bool _externalSkinHostRefreshPending;
        private int _externalSkinHostRefreshGeneration;
        private string _externalSkinHostPendingReason = "";

        // MainWindow 側は表示モードの切替だけを持ち、skin の正本は Orchestrator と ViewModel に寄せる。
        private void InitializeWebViewSkinIntegration()
        {
            if (MainVM?.DbInfo != null)
            {
                MainVM.DbInfo.PropertyChanged += MainDbInfo_PropertyChangedForExternalSkin;
            }

            Loaded += (_, _) => QueueExternalSkinHostRefresh("window-loaded");
            Closing += (_, _) => DisposeExternalSkinHostIntegration();
            QueueExternalSkinHostRefresh("integration-initialized");
        }

        private void MainDbInfo_PropertyChangedForExternalSkin(
            object sender,
            PropertyChangedEventArgs e
        )
        {
            string propertyName = e?.PropertyName ?? "";
            if (
                string.Equals(propertyName, "Skin", StringComparison.Ordinal)
                || string.Equals(propertyName, "DBFullPath", StringComparison.Ordinal)
                || string.Equals(propertyName, "ThumbFolder", StringComparison.Ordinal)
            )
            {
                QueueExternalSkinHostRefresh($"dbinfo-{propertyName}");
            }
        }

        // 同一フレーム内の更新を 1 回へ畳み、DB 切替や skin 切替の揺れを吸収する。
        private void QueueExternalSkinHostRefresh(string reason)
        {
            if (Dispatcher == null)
            {
                return;
            }

            _externalSkinHostRefreshPending = true;
            _externalSkinHostPendingReason = reason ?? "";
            _externalSkinHostRefreshGeneration++;
            if (_externalSkinHostRefreshRunning)
            {
                return;
            }

            _externalSkinHostRefreshRunning = true;
            _ = Dispatcher.BeginInvoke(
                new Action(async () =>
                {
                    await DrainExternalSkinHostRefreshQueueAsync();
                }),
                DispatcherPriority.Background
            );
        }

        private async Task DrainExternalSkinHostRefreshQueueAsync()
        {
            try
            {
                while (_externalSkinHostRefreshPending)
                {
                    _externalSkinHostRefreshPending = false;

                    int generation = _externalSkinHostRefreshGeneration;
                    string reason = _externalSkinHostPendingReason;
                    await RefreshExternalSkinHostPresentationAsync(generation, reason);
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "skin-webview",
                    $"refresh drain failed: err='{ex.GetType().Name}: {ex.Message}'"
                );
                ApplyExternalSkinFallbackPresentation();
            }
            finally
            {
                _externalSkinHostRefreshRunning = false;
                if (_externalSkinHostRefreshPending)
                {
                    QueueExternalSkinHostRefresh(_externalSkinHostPendingReason);
                }
            }
        }

        private async Task RefreshExternalSkinHostPresentationAsync(int generation, string reason)
        {
            WhiteBrowserSkinDefinition externalSkinDefinition = null;
            bool externalSkinActive = false;
            bool hostReady = false;

            try
            {
                externalSkinDefinition = GetCurrentExternalSkinDefinition();
                externalSkinActive = externalSkinDefinition != null;
                hostReady = externalSkinActive
                    && await TryPrepareExternalSkinHostAsync(externalSkinDefinition, reason);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "skin-webview",
                    $"refresh failed: err='{ex.GetType().Name}: {ex.Message}' reason={reason}"
                );
                hostReady = false;
            }

            if (generation != _externalSkinHostRefreshGeneration)
            {
                return;
            }

            if (hostReady)
            {
                ApplyExternalSkinHostVisibility(true);
            }
            else
            {
                ApplyExternalSkinFallbackPresentation();
            }

            DebugRuntimeLog.Write(
                "skin-webview",
                $"host presentation: active={externalSkinActive} ready={hostReady} skinRaw='{MainVM?.DbInfo?.Skin ?? ""}' skinResolved='{externalSkinDefinition?.Name ?? ""}' db='{MainVM?.DbInfo?.DBFullPath ?? ""}' reason={reason}"
            );
        }

        private void ApplyExternalSkinFallbackPresentation()
        {
            try
            {
                ApplyExternalSkinHostVisibility(false);
                _externalSkinHostControl?.Clear();
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "skin-webview",
                    $"fallback apply failed: err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        // 外部 skin 判定は Orchestrator の解決結果を使い、MainWindow 直判定を増やさない。
        private WhiteBrowserSkinDefinition GetCurrentExternalSkinDefinition()
        {
            WhiteBrowserSkinDefinition currentDefinition =
                GetSkinOrchestrator().GetCurrentSkinDefinition();
            return currentDefinition?.RequiresWebView2 == true ? currentDefinition : null;
        }

        private void ApplyExternalSkinHostVisibility(bool hostReady)
        {
            if (Tabs != null)
            {
                Tabs.Visibility = hostReady ? Visibility.Collapsed : Visibility.Visible;
            }

            if (ExternalSkinHostPresenter == null)
            {
                return;
            }

            ExternalSkinHostPresenter.Visibility = hostReady
                ? Visibility.Visible
                : Visibility.Collapsed;
            ExternalSkinHostPresenter.Content = hostReady ? _externalSkinHostControl : null;
        }

        private WhiteBrowserSkinHostControl EnsureExternalSkinHostCreated()
        {
            if (_externalSkinHostControl != null)
            {
                return _externalSkinHostControl;
            }

            try
            {
                _externalSkinHostControl = new WhiteBrowserSkinHostControl();
                AttachExternalSkinHostApiBridge(_externalSkinHostControl);
                return _externalSkinHostControl;
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "skin-webview",
                    $"host create failed: err='{ex.GetType().Name}: {ex.Message}'"
                );
                _externalSkinHostControl = null;
                return null;
            }
        }

        // Runtime 未導入や skin 実体不足なら、表示だけ既存 WPF タブへ戻して raw skin 名は保持する。
        private async Task<bool> TryPrepareExternalSkinHostAsync(
            WhiteBrowserSkinDefinition definition,
            string reason
        )
        {
            try
            {
                WhiteBrowserSkinHostControl hostControl = EnsureExternalSkinHostCreated();
                if (definition == null || hostControl == null)
                {
                    return false;
                }

                string requestedSkinName = ResolveRequestedSkinName(definition);
                if (string.IsNullOrWhiteSpace(definition.HtmlPath) || !File.Exists(definition.HtmlPath))
                {
                    DebugRuntimeLog.Write(
                        "skin-webview",
                        $"host navigate skipped: html missing skin='{requestedSkinName}' path='{definition.HtmlPath ?? ""}' reason={reason}"
                    );
                    return false;
                }

                string skinRootPath = WhiteBrowserSkinCatalogService.ResolveSkinRootPath(
                    AppContext.BaseDirectory
                );
                string thumbRootPath = MainVM?.DbInfo?.ThumbFolder ?? "";
                string userDataFolder = ResolveExternalSkinUserDataFolder();
                Directory.CreateDirectory(userDataFolder);

                WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                    requestedSkinName,
                    userDataFolder,
                    skinRootPath,
                    definition.HtmlPath,
                    thumbRootPath
                );

                return EvaluateExternalSkinHostOperationResult(navigateResult, reason);
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "skin-webview",
                    $"host prepare failed: skin='{ResolveRequestedSkinName(definition)}' err='{ex.GetType().Name}: {ex.Message}' reason={reason}"
                );
                return false;
            }
        }

        private bool EvaluateExternalSkinHostOperationResult(
            WhiteBrowserSkinHostOperationResult operationResult,
            string reason
        )
        {
            if (operationResult == null || operationResult.Succeeded)
            {
                return true;
            }

            DebugRuntimeLog.Write(
                "skin-webview",
                $"host navigate failed: skin='{operationResult.RequestedSkinName}' runtimeAvailable={operationResult.RuntimeAvailable} errorType='{operationResult.ErrorType}' error='{operationResult.ErrorMessage}' reason={reason}"
            );
            return false;
        }

        private string ResolveRequestedSkinName(WhiteBrowserSkinDefinition definition)
        {
            string rawSkinName = MainVM?.DbInfo?.Skin ?? "";
            return string.IsNullOrWhiteSpace(rawSkinName) ? definition?.Name ?? "" : rawSkinName;
        }

        private static string ResolveExternalSkinUserDataFolder()
        {
            string localAppDataPath = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            );
            return Path.Combine(localAppDataPath, ExternalSkinCacheFolderName, "WebView2Cache");
        }

        private void DisposeExternalSkinHostIntegration()
        {
            if (MainVM?.DbInfo != null)
            {
                MainVM.DbInfo.PropertyChanged -= MainDbInfo_PropertyChangedForExternalSkin;
            }

            if (_externalSkinHostControl != null)
            {
                DetachExternalSkinHostApiBridge(_externalSkinHostControl);
                _externalSkinHostControl.Clear();
                _externalSkinHostControl.RuntimeBridge.Dispose();
            }

            _externalSkinApiService = null;
            _externalSkinHostControl = null;
        }
    }
}
