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
        private ExternalSkinHostRefreshScheduler _externalSkinHostRefreshScheduler;
        private WhiteBrowserSkinHostOperationResult _lastExternalSkinHostOperationResult;
        private string _lastExternalSkinHostFailureReason = "";
        // 実 UI テストでは host 準備成否だけ差し替え、表示切替そのものは本物の MainWindow で確認する。
        internal Func<WhiteBrowserSkinDefinition, string, Task<bool>> ExternalSkinHostPrepareAsyncForTesting
        {
            get;
            set;
        }
        internal Func<WhiteBrowserSkinDefinition, string, Task<WhiteBrowserSkinHostOperationResult>> ExternalSkinHostPrepareResultAsyncForTesting
        {
            get;
            set;
        }
        internal Action<string> ExternalSkinFallbackOpenLogActionForTesting
        {
            get;
            set;
        }
        internal Action<string> ExternalSkinFallbackOpenRuntimeDownloadActionForTesting
        {
            get;
            set;
        }
        internal Action<int, bool, string> ExternalSkinHostPresentationAppliedForTesting
        {
            get;
            set;
        }

        // MainWindow 側は表示モードの切替だけを持ち、skin の正本は Orchestrator と ViewModel に寄せる。
        private void InitializeWebViewSkinIntegration()
        {
            _externalSkinHostRefreshScheduler ??= CreateExternalSkinHostRefreshScheduler();

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
                if (
                    string.Equals(propertyName, "Skin", StringComparison.Ordinal)
                    || string.Equals(propertyName, "DBFullPath", StringComparison.Ordinal)
                )
                {
                    ResetExternalSkinApiTransientState();
                }

                QueueExternalSkinHostRefresh($"dbinfo-{propertyName}");
            }
        }

        // 同一フレーム内の更新を 1 回へ畳み、DB 切替や skin 切替の揺れを吸収する。
        private void QueueExternalSkinHostRefresh(string reason)
        {
            DebugRuntimeLog.Write(
                "skin-webview",
                $"refresh queued: hasScheduler={_externalSkinHostRefreshScheduler != null} skinRaw='{MainVM?.DbInfo?.Skin ?? ""}' db='{MainVM?.DbInfo?.DBFullPath ?? ""}' reason={reason}"
            );
            _externalSkinHostRefreshScheduler?.Queue(reason);
        }

        private ExternalSkinHostRefreshScheduler CreateExternalSkinHostRefreshScheduler()
        {
            if (Dispatcher == null)
            {
                return null;
            }

            return new ExternalSkinHostRefreshScheduler(
                Dispatcher,
                RefreshExternalSkinHostPresentationAsync,
                ex =>
                {
                    DebugRuntimeLog.Write(
                        "skin-webview",
                        $"refresh drain failed: err='{ex.GetType().Name}: {ex.Message}'"
                    );
                    ApplyExternalSkinFallbackPresentation();
                }
            );
        }

        private async Task RefreshExternalSkinHostPresentationAsync(int generation, string reason)
        {
            WhiteBrowserSkinDefinition externalSkinDefinition = null;
            bool externalSkinActive = false;
            bool hostReady = false;
            WhiteBrowserSkinHostOperationResult operationResult = null;

            try
            {
                if (TrySkipStaleExternalSkinRefresh(generation, reason, "begin"))
                {
                    return;
                }

                externalSkinDefinition = GetCurrentExternalSkinDefinition();
                externalSkinActive = externalSkinDefinition != null;
                if (TrySkipStaleExternalSkinRefresh(generation, reason, "resolved-definition"))
                {
                    return;
                }

                if (externalSkinActive)
                {
                    operationResult = await PrepareExternalSkinHostPresentationAsync(
                        externalSkinDefinition,
                        generation,
                        reason
                    );
                    if (TrySkipStaleExternalSkinRefresh(generation, reason, "prepared-host"))
                    {
                        return;
                    }
                    hostReady = operationResult?.Succeeded == true;
                }
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "skin-webview",
                    $"refresh failed: err='{ex.GetType().Name}: {ex.Message}' reason={reason}"
                );
                operationResult = WhiteBrowserSkinHostOperationResult.CreateFailed(
                    ResolveRequestedSkinName(externalSkinDefinition),
                    ex
                );
                hostReady = false;
            }

            if (TrySkipStaleExternalSkinRefresh(generation, reason, "apply"))
            {
                return;
            }

            ApplyExternalSkinFallbackDiagnostics(
                externalSkinActive,
                externalSkinDefinition,
                operationResult,
                reason
            );

            if (hostReady)
            {
                ApplyExternalSkinHostVisibility(true, externalSkinDefinition);
            }
            else
            {
                await ApplyExternalSkinFallbackPresentationAsync();
            }

            ExternalSkinHostPresentationAppliedForTesting?.Invoke(generation, hostReady, reason);
            DebugRuntimeLog.Write(
                "skin-webview",
                $"host presentation: active={externalSkinActive} ready={hostReady} skinRaw='{MainVM?.DbInfo?.Skin ?? ""}' skinResolved='{externalSkinDefinition?.Name ?? ""}' db='{MainVM?.DbInfo?.DBFullPath ?? ""}' reason={reason}"
            );
        }

        private void ApplyExternalSkinFallbackPresentation()
        {
            _ = ApplyExternalSkinFallbackPresentationAsync();
        }

        private async Task ApplyExternalSkinFallbackPresentationAsync()
        {
            WhiteBrowserSkinHostControl hostControl = _externalSkinHostControl;
            try
            {
                ApplyExternalSkinHostVisibility(false, null);
                if (hostControl != null)
                {
                    await hostControl.ClearAsync();
                }
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

        private void ApplyExternalSkinHostVisibility(
            bool hostReady,
            WhiteBrowserSkinDefinition definition
        )
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
            ApplyExternalSkinMinimalChromeVisibility(hostReady, definition);
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
        private async Task<WhiteBrowserSkinHostOperationResult> PrepareExternalSkinHostPresentationAsync(
            WhiteBrowserSkinDefinition definition,
            int generation,
            string reason
        )
        {
            Func<WhiteBrowserSkinDefinition, string, Task<WhiteBrowserSkinHostOperationResult>> resultHook =
                ExternalSkinHostPrepareResultAsyncForTesting;
            if (resultHook != null)
            {
                WhiteBrowserSkinHostControl testHostControl = EnsureExternalSkinHostCreated();
                if (testHostControl == null)
                {
                    return WhiteBrowserSkinHostOperationResult.CreateFailed(
                        ResolveRequestedSkinName(definition),
                        "External skin host could not be created.",
                        "HostControlCreateFailed"
                    );
                }

                if (TrySkipStaleExternalSkinRefresh(generation, reason, "prepare-hook-before-mount"))
                {
                    return WhiteBrowserSkinHostOperationResult.CreateSkipped(
                        ResolveRequestedSkinName(definition),
                        "Refresh became stale before prepare result hook mount."
                    );
                }

                await EnsureExternalSkinHostMountedForPreparationAsync(testHostControl);
                if (TrySkipStaleExternalSkinRefresh(generation, reason, "prepare-hook-before-run"))
                {
                    return WhiteBrowserSkinHostOperationResult.CreateSkipped(
                        ResolveRequestedSkinName(definition),
                        "Refresh became stale before prepare result hook execution."
                    );
                }

                return await resultHook(definition, reason)
                    ?? WhiteBrowserSkinHostOperationResult.CreateFailed(
                        ResolveRequestedSkinName(definition),
                        "External skin host prepare result hook returned null.",
                        "HostPrepareResultHookReturnedNull"
                    );
            }

            Func<WhiteBrowserSkinDefinition, string, Task<bool>> testHook =
                ExternalSkinHostPrepareAsyncForTesting;
            if (testHook != null)
            {
                // テスト差し替えでも本物の host control を作り、Content 差し替えまで実 UI で確認する。
                WhiteBrowserSkinHostControl testHostControl = EnsureExternalSkinHostCreated();
                if (testHostControl == null)
                {
                    return WhiteBrowserSkinHostOperationResult.CreateFailed(
                        ResolveRequestedSkinName(definition),
                        "External skin host could not be created.",
                        "HostControlCreateFailed"
                    );
                }

                if (TrySkipStaleExternalSkinRefresh(generation, reason, "prepare-test-before-mount"))
                {
                    return WhiteBrowserSkinHostOperationResult.CreateSkipped(
                        ResolveRequestedSkinName(definition),
                        "Refresh became stale before prepare test hook mount."
                    );
                }

                await EnsureExternalSkinHostMountedForPreparationAsync(testHostControl);
                if (TrySkipStaleExternalSkinRefresh(generation, reason, "prepare-test-before-run"))
                {
                    return WhiteBrowserSkinHostOperationResult.CreateSkipped(
                        ResolveRequestedSkinName(definition),
                        "Refresh became stale before prepare test hook execution."
                    );
                }

                bool prepared = await testHook(definition, reason);
                return prepared
                    ? WhiteBrowserSkinHostOperationResult.CreateSuccess(ResolveRequestedSkinName(definition))
                    : WhiteBrowserSkinHostOperationResult.CreateFailed(
                        ResolveRequestedSkinName(definition),
                        "External skin host prepare test hook returned false.",
                        "HostPrepareTestHookReturnedFalse"
                    );
            }

            return await TryPrepareExternalSkinHostAsync(definition, generation, reason);
        }

        // Runtime 未導入や skin 実体不足なら、表示だけ既存 WPF タブへ戻して raw skin 名は保持する。
        private async Task<WhiteBrowserSkinHostOperationResult> TryPrepareExternalSkinHostAsync(
            WhiteBrowserSkinDefinition definition,
            int generation,
            string reason
        )
        {
            try
            {
                WhiteBrowserSkinHostControl hostControl = EnsureExternalSkinHostCreated();
                if (definition == null || hostControl == null)
                {
                    return WhiteBrowserSkinHostOperationResult.CreateFailed(
                        ResolveRequestedSkinName(definition),
                        "External skin host could not be created.",
                        "HostControlCreateFailed"
                    );
                }

                if (TrySkipStaleExternalSkinRefresh(generation, reason, "prepare-before-mount"))
                {
                    return WhiteBrowserSkinHostOperationResult.CreateSkipped(
                        ResolveRequestedSkinName(definition),
                        "Refresh became stale before host mount."
                    );
                }

                // 実アプリでは host を visual tree へ入れる前に Navigate すると、
                // WebView2 初期化待ちが完了せず skin 切替が無音で止まることがある。
                // 準備中だけ Hidden で先に載せ、実体をぶら下げてから初期化へ進める。
                await EnsureExternalSkinHostMountedForPreparationAsync(hostControl);
                if (TrySkipStaleExternalSkinRefresh(generation, reason, "prepare-before-navigate"))
                {
                    return WhiteBrowserSkinHostOperationResult.CreateSkipped(
                        ResolveRequestedSkinName(definition),
                        "Refresh became stale before navigate."
                    );
                }

                string requestedSkinName = ResolveRequestedSkinName(definition);
                DebugRuntimeLog.Write(
                    "skin-webview",
                    $"host prepare begin: skin='{requestedSkinName}' reason={reason}"
                );
                if (string.IsNullOrWhiteSpace(definition.HtmlPath) || !File.Exists(definition.HtmlPath))
                {
                    DebugRuntimeLog.Write(
                        "skin-webview",
                        $"host navigate skipped: html missing skin='{requestedSkinName}' path='{definition.HtmlPath ?? ""}' reason={reason}"
                    );
                    return WhiteBrowserSkinHostOperationResult.CreateMissingHtml(
                        requestedSkinName,
                        definition.HtmlPath
                    );
                }

                string skinRootPath = ResolveExternalSkinRootPath();
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
                return WhiteBrowserSkinHostOperationResult.CreateFailed(
                    ResolveRequestedSkinName(definition),
                    ex
                );
            }
        }

        private WhiteBrowserSkinHostOperationResult EvaluateExternalSkinHostOperationResult(
            WhiteBrowserSkinHostOperationResult operationResult,
            string reason
        )
        {
            if (operationResult == null || operationResult.Succeeded)
            {
                return operationResult
                    ?? WhiteBrowserSkinHostOperationResult.CreateSuccess(
                        MainVM?.DbInfo?.Skin ?? ""
                    );
            }

            DebugRuntimeLog.Write(
                "skin-webview",
                $"host navigate failed: skin='{operationResult.RequestedSkinName}' runtimeAvailable={operationResult.RuntimeAvailable} errorType='{operationResult.ErrorType}' error='{operationResult.ErrorMessage}' reason={reason}"
            );
            return operationResult;
        }

        private string ResolveRequestedSkinName(WhiteBrowserSkinDefinition definition)
        {
            string rawSkinName = MainVM?.DbInfo?.Skin ?? "";
            return string.IsNullOrWhiteSpace(rawSkinName) ? definition?.Name ?? "" : rawSkinName;
        }

        // 最新 generation だけを host 準備と表示適用の対象にし、古い refresh の無駄仕事を前段で止める。
        private bool TrySkipStaleExternalSkinRefresh(int generation, string reason, string stage)
        {
            if (
                _externalSkinHostRefreshScheduler == null
                || generation == _externalSkinHostRefreshScheduler.CurrentGeneration
            )
            {
                return false;
            }

            DebugRuntimeLog.Write(
                "skin-webview",
                $"refresh skipped stale: generation={generation} current_generation={_externalSkinHostRefreshScheduler.CurrentGeneration} stage={stage} reason={reason}"
            );
            return true;
        }

        private Task EnsureExternalSkinHostMountedForPreparationAsync(
            WhiteBrowserSkinHostControl hostControl
        )
        {
            if (hostControl == null || ExternalSkinHostPresenter == null || Dispatcher == null)
            {
                return Task.CompletedTask;
            }

            return Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (!ReferenceEquals(ExternalSkinHostPresenter.Content, hostControl))
                        {
                            ExternalSkinHostPresenter.Content = hostControl;
                        }

                        // Hidden の間は既存 WPF タブを見せたまま host の初期化だけ先に進める。
                        if (ExternalSkinHostPresenter.Visibility == Visibility.Collapsed)
                        {
                            ExternalSkinHostPresenter.Visibility = Visibility.Hidden;
                        }
                    },
                    DispatcherPriority.Loaded
                )
                .Task;
        }

        private static string ResolveExternalSkinUserDataFolder()
        {
            string localAppDataPath = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            );
            return Path.Combine(localAppDataPath, ExternalSkinCacheFolderName, "WebView2Cache");
        }

        private static string ResolveExternalSkinFallbackLogPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndigoMovieManager",
                "logs",
                "debug-runtime.log"
            );
        }

        private static string ResolveExternalSkinRuntimeDownloadUrl()
        {
            return "https://developer.microsoft.com/microsoft-edge/webview2/";
        }

        private void ApplyExternalSkinFallbackDiagnostics(
            bool externalSkinActive,
            WhiteBrowserSkinDefinition definition,
            WhiteBrowserSkinHostOperationResult operationResult,
            string reason
        )
        {
            if (!externalSkinActive || operationResult?.Succeeded != false)
            {
                _lastExternalSkinHostOperationResult = null;
                _lastExternalSkinHostFailureReason = "";
                ApplyExternalSkinFallbackNotice("", "", false);
                return;
            }

            _lastExternalSkinHostOperationResult = operationResult;
            _lastExternalSkinHostFailureReason = reason ?? "";
            ApplyExternalSkinFallbackNotice(
                BuildExternalSkinFallbackNoticeText(definition, operationResult),
                BuildExternalSkinFallbackNoticeToolTip(definition, operationResult, reason),
                !operationResult.RuntimeAvailable
            );
        }

        private void DisposeExternalSkinHostIntegration()
        {
            if (MainVM?.DbInfo != null)
            {
                MainVM.DbInfo.PropertyChanged -= MainDbInfo_PropertyChangedForExternalSkin;
            }

            if (ExternalSkinHostPresenter != null)
            {
                // 先に visual tree から host を外し、closing 中の HWND 競合を減らす。
                ExternalSkinHostPresenter.Content = null;
                ExternalSkinHostPresenter.Visibility = Visibility.Collapsed;
            }

            if (Tabs != null)
            {
                Tabs.Visibility = Visibility.Visible;
            }

            ApplyExternalSkinMinimalChromeVisibility(false, null);
            ApplyExternalSkinFallbackNotice("", "", false);

            if (_externalSkinHostControl != null)
            {
                DetachExternalSkinHostApiBridge(_externalSkinHostControl);
                // 終了時は blank 遷移を fire-and-forget で投げると、
                // WebView2 host 破棄と競合して close race を起こしやすい。
                // visual tree を外したあとに WebView2 実体まで明示破棄して、HWND の後始末を先に進める。
                _externalSkinHostControl.Dispose();
            }

            _externalSkinApiService = null;
            _externalSkinHostControl = null;
            _externalSkinHostRefreshScheduler = null;
            _lastExternalSkinHostOperationResult = null;
            _lastExternalSkinHostFailureReason = "";
        }
    }
}
