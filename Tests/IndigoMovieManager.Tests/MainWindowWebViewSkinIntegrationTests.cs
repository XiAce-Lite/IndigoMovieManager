using System.Collections.Specialized;
using System.IO;
using System.Data.SQLite;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IndigoMovieManager.DB;
using IndigoMovieManager.Skin.Host;
using IndigoMovieManager.Skin;
using IndigoMovieManager.Skin.Runtime;
using IndigoMovieManager.ViewModels;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using Microsoft.Web.WebView2.Wpf;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class MainWindowWebViewSkinIntegrationTests
{
    private static readonly object UiThreadSync = new();
    private static Thread? uiThread;
    private static Dispatcher? uiDispatcher;
    private static TaskCompletionSource<bool>? uiThreadReady;

    [Test]
    public async Task 外部skin有効かつhost_readyならTabsを畳んでhostを表示する()
    {
        HostPresentationSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> applied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            window.ExternalSkinHostPrepareAsyncForTesting = (_, _) => Task.FromResult(true);
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal) && hostReady)
                {
                    applied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.Skin = $"MainWindowWebViewSkinHostReady_{Guid.NewGuid():N}";

                HostPresentationEvent appliedEvent = await WaitAsync(
                    applied.Task,
                    TimeSpan.FromSeconds(10),
                    "外部 skin 表示への切替完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return CaptureSnapshot(window, appliedEvent);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied.HostReady, Is.True);
            Assert.That(result.TabsVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.PresenterVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.PresenterContent, Is.InstanceOf<WhiteBrowserSkinHostControl>());
            Assert.That(result.StandardChromeVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.MinimalChromeVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.MinimalSkinName, Is.Not.Empty);
        });
    }

    [Test]
    public async Task ApplySkinByName経由でも外部skin_host表示へ切替できる()
    {
        HostPresentationSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> applied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            window.ExternalSkinHostPrepareAsyncForTesting = (_, _) => Task.FromResult(true);
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (hostReady)
                {
                    applied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                WhiteBrowserSkinDefinition externalSkin = window.GetAvailableSkinDefinitions()
                    .FirstOrDefault(x => x?.RequiresWebView2 == true);
                Assert.That(externalSkin, Is.Not.Null, "外部 skin fixture が見つかりませんでした。");
                if (externalSkin == null)
                {
                    throw new AssertionException("外部 skin fixture が見つかりませんでした。");
                }

                bool appliedByName = window.ApplySkinByName(
                    externalSkin.Name,
                    persistToCurrentDb: false
                );
                Assert.That(appliedByName, Is.True, "ApplySkinByName が外部 skin を解決できませんでした。");

                HostPresentationEvent appliedEvent = await WaitAsync(
                    applied.Task,
                    TimeSpan.FromSeconds(10),
                    "ApplySkinByName 経由の外部 skin 表示完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return CaptureSnapshot(window, appliedEvent);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied.HostReady, Is.True);
            Assert.That(result.TabsVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.PresenterVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.PresenterContent, Is.InstanceOf<WhiteBrowserSkinHostControl>());
            Assert.That(result.StandardChromeVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.MinimalChromeVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.MinimalSkinName, Is.Not.Empty);
        });
    }

    [Test]
    public async Task 外部skin準備前にhostをHiddenで仮マウントする()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> applied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<(Visibility PresenterVisibility, object PresenterContent)> mounted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            window.ExternalSkinHostPrepareAsyncForTesting = (_, _) =>
            {
                mounted.TrySetResult(
                    (
                        window.ExternalSkinHostPresenter.Visibility,
                        window.ExternalSkinHostPresenter.Content
                    )
                );
                return Task.FromResult(true);
            };
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (hostReady)
                {
                    applied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                WhiteBrowserSkinDefinition externalSkin = window.GetAvailableSkinDefinitions()
                    .FirstOrDefault(x => x?.RequiresWebView2 == true);
                Assert.That(externalSkin, Is.Not.Null, "外部 skin fixture が見つかりませんでした。");
                if (externalSkin == null)
                {
                    throw new AssertionException("外部 skin fixture が見つかりませんでした。");
                }

                window.MainVM.DbInfo.Skin = externalSkin.Name;

                (Visibility presenterVisibility, object presenterContent) mountedState =
                    await WaitAsync(
                        mounted.Task,
                        TimeSpan.FromSeconds(10),
                        "外部 skin host の仮マウント完了を待てませんでした。"
                    );
                await WaitAsync(
                    applied.Task,
                    TimeSpan.FromSeconds(10),
                    "外部 skin 表示完了を待てませんでした。"
                );

                Assert.Multiple(() =>
                {
                    Assert.That(
                        mountedState.presenterVisibility,
                        Is.EqualTo(Visibility.Hidden),
                        "準備中は host を Hidden で先に visual tree へ載せる。"
                    );
                    Assert.That(
                        mountedState.presenterContent,
                        Is.InstanceOf<WhiteBrowserSkinHostControl>()
                    );
                });
            }
            finally
            {
                await CloseWindowAsync(window);
            }

            return null;
        });
    }

    [Test]
    public async Task 外部skinのhtml欠落ならWpfFallbackへ戻る()
    {
        HostPresentationSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> applied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal) && !hostReady)
                {
                    applied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                // 実在しない外部 skin 名を流し、html missing 分岐を本物の MainWindow で踏む。
                window.MainVM.DbInfo.Skin = $"MissingExternalSkin_{Guid.NewGuid():N}";

                HostPresentationEvent appliedEvent = await WaitAsync(
                    applied.Task,
                    TimeSpan.FromSeconds(10),
                    "html missing fallback の適用完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return CaptureSnapshot(window, appliedEvent);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied.HostReady, Is.False);
            Assert.That(result.TabsVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.PresenterVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.PresenterContent, Is.Null);
            Assert.That(result.StandardChromeVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.MinimalChromeVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.FallbackNoticeVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.FallbackNoticeText, Does.Contain("HTML"));
            Assert.That(result.FallbackNoticeText, Does.Contain("標準表示"));
            Assert.That(
                result.FallbackRuntimeDownloadButtonVisibility,
                Is.EqualTo(Visibility.Collapsed)
            );
        });
    }

    [Test]
    public async Task WebView2_Runtime未導入なら診断案内を出してWpfFallbackへ戻る()
    {
        HostPresentationSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> applied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            window.ExternalSkinHostPrepareResultAsyncForTesting = (definition, _) =>
                Task.FromResult(
                    WhiteBrowserSkinHostOperationResult.CreateRuntimeUnavailable(
                        definition?.Name ?? "",
                        "WebView2 Runtime not found for testing."
                    )
                );
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal) && !hostReady)
                {
                    applied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                WhiteBrowserSkinDefinition externalSkin = GetExternalSkinDefinitions(window).First();
                window.MainVM.DbInfo.Skin = externalSkin.Name;

                HostPresentationEvent appliedEvent = await WaitAsync(
                    applied.Task,
                    TimeSpan.FromSeconds(10),
                    "runtime unavailable fallback の適用完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return CaptureSnapshot(window, appliedEvent);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied.HostReady, Is.False);
            Assert.That(result.TabsVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.PresenterVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.PresenterContent, Is.Null);
            Assert.That(result.StandardChromeVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.MinimalChromeVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.FallbackNoticeVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.FallbackNoticeText, Does.Contain("WebView2 Runtime"));
            Assert.That(result.FallbackNoticeToolTip, Does.Contain("WebView2RuntimeNotFound"));
            Assert.That(result.FallbackNoticeToolTip, Does.Contain("再試行"));
            Assert.That(
                result.FallbackNoticeToolTip,
                Does.Contain("developer.microsoft.com/microsoft-edge/webview2/")
            );
            Assert.That(
                result.FallbackRuntimeDownloadButtonVisibility,
                Is.EqualTo(Visibility.Visible)
            );
        });
    }

    [Test]
    public async Task fallback通知の再試行からhost表示へ復帰できる()
    {
        HostPresentationSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> fallbackApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<HostPresentationEvent> retriedApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            int prepareCallCount = 0;

            window.ExternalSkinHostPrepareResultAsyncForTesting = (definition, reason) =>
            {
                int callCount = Interlocked.Increment(ref prepareCallCount);
                return Task.FromResult(
                    callCount == 1
                        ? WhiteBrowserSkinHostOperationResult.CreateRuntimeUnavailable(
                            definition?.Name ?? "",
                            "WebView2 Runtime not found for testing."
                        )
                        : WhiteBrowserSkinHostOperationResult.CreateSuccess(definition?.Name ?? "")
                );
            };
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                HostPresentationEvent appliedEvent = new(generation, reason, hostReady);
                if (!hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                {
                    fallbackApplied.TrySetResult(appliedEvent);
                }

                if (hostReady && string.Equals(reason, "fallback-notice-retry", StringComparison.Ordinal))
                {
                    retriedApplied.TrySetResult(appliedEvent);
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                WhiteBrowserSkinDefinition externalSkin = GetExternalSkinDefinitions(window).First();
                window.MainVM.DbInfo.Skin = externalSkin.Name;
                await WaitAsync(
                    fallbackApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "初回 fallback 適用完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                window.ExternalSkinFallbackRetryButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent)
                );

                HostPresentationEvent retriedEvent = await WaitAsync(
                    retriedApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "retry 後の host 復帰完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return CaptureSnapshot(window, retriedEvent);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied.HostReady, Is.True);
            Assert.That(result.Applied.Reason, Is.EqualTo("fallback-notice-retry"));
            Assert.That(result.TabsVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.PresenterVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.PresenterContent, Is.InstanceOf<WhiteBrowserSkinHostControl>());
            Assert.That(result.MinimalChromeVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.FallbackNoticeVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.FallbackNoticeText, Is.Empty);
        });
    }

    [Test]
    public async Task fallback通知のログを開くからdebug_runtime_logのパスを辿れる()
    {
        string openedLogPath = "";
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> fallbackApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            window.ExternalSkinHostPrepareResultAsyncForTesting = (definition, _) =>
                Task.FromResult(
                    WhiteBrowserSkinHostOperationResult.CreateRuntimeUnavailable(
                        definition?.Name ?? "",
                        "WebView2 Runtime not found for testing."
                    )
                );
            window.ExternalSkinFallbackOpenLogActionForTesting = path => openedLogPath = path ?? "";
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (!hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                {
                    fallbackApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                WhiteBrowserSkinDefinition externalSkin = GetExternalSkinDefinitions(window).First();
                window.MainVM.DbInfo.Skin = externalSkin.Name;
                await WaitAsync(
                    fallbackApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "fallback 通知の表示完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                window.ExternalSkinFallbackOpenLogButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent)
                );

                Assert.That(
                    openedLogPath,
                    Does.EndWith(Path.Combine("logs", "debug-runtime.log"))
                );
            }
            finally
            {
                await CloseWindowAsync(window);
            }

            return null;
        });
    }

    [Test]
    public async Task fallback通知のRuntime入手から公式導線URLを辿れる()
    {
        string openedRuntimeDownloadUrl = "";
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> fallbackApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            window.ExternalSkinHostPrepareResultAsyncForTesting = (definition, _) =>
                Task.FromResult(
                    WhiteBrowserSkinHostOperationResult.CreateRuntimeUnavailable(
                        definition?.Name ?? "",
                        "WebView2 Runtime not found for testing."
                    )
                );
            window.ExternalSkinFallbackOpenRuntimeDownloadActionForTesting = url =>
                openedRuntimeDownloadUrl = url ?? "";
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (!hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                {
                    fallbackApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                WhiteBrowserSkinDefinition externalSkin = GetExternalSkinDefinitions(window).First();
                window.MainVM.DbInfo.Skin = externalSkin.Name;
                await WaitAsync(
                    fallbackApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "fallback 通知の表示完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                window.ExternalSkinFallbackOpenRuntimeDownloadButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent)
                );

                Assert.That(
                    openedRuntimeDownloadUrl,
                    Is.EqualTo("https://developer.microsoft.com/microsoft-edge/webview2/")
                );
            }
            finally
            {
                await CloseWindowAsync(window);
            }

            return null;
        });
    }

    [Test]
    public async Task host準備失敗でもWpfFallbackへ戻る()
    {
        HostPresentationSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> applied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            window.ExternalSkinHostPrepareAsyncForTesting = (_, _) => Task.FromResult(false);
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal) && !hostReady)
                {
                    applied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.Skin = $"NavigateFailureSkin_{Guid.NewGuid():N}";

                HostPresentationEvent appliedEvent = await WaitAsync(
                    applied.Task,
                    TimeSpan.FromSeconds(10),
                    "host 準備失敗 fallback の適用完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return CaptureSnapshot(window, appliedEvent);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied.HostReady, Is.False);
            Assert.That(result.TabsVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.PresenterVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.PresenterContent, Is.Null);
            Assert.That(result.StandardChromeVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.MinimalChromeVisibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [Test]
    public async Task skin切替競合でも古いrefresh完了で表示が巻き戻らない()
    {
        RacePresentationSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            List<HostPresentationEvent> appliedEvents = [];
            TaskCompletionSource<bool> firstPrepareStarted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<bool> releaseFirstPrepare = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<HostPresentationEvent> latestApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            int prepareCallCount = 0;

            window.ExternalSkinHostPrepareAsyncForTesting = async (_, _) =>
            {
                int callCount = Interlocked.Increment(ref prepareCallCount);
                if (callCount == 1)
                {
                    firstPrepareStarted.TrySetResult(true);
                    await releaseFirstPrepare.Task;
                }

                return true;
            };
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (!string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                {
                    return;
                }

                HostPresentationEvent appliedEvent = new(generation, reason, hostReady);
                appliedEvents.Add(appliedEvent);
                if (!hostReady)
                {
                    latestApplied.TrySetResult(appliedEvent);
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();
                appliedEvents.Clear();

                window.MainVM.DbInfo.Skin = $"RaceExternalSkin_{Guid.NewGuid():N}";
                await WaitAsync(
                    firstPrepareStarted.Task,
                    TimeSpan.FromSeconds(10),
                    "最初の host 準備開始を待てませんでした。"
                );

                // 新しい refresh を built-in 側へ切り替えて積み、古い完了で巻き戻らないことを見る。
                window.MainVM.DbInfo.Skin = "DefaultGrid";
                releaseFirstPrepare.TrySetResult(true);

                HostPresentationEvent latestEvent = await WaitAsync(
                    latestApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "最新 refresh の fallback 適用完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return new RacePresentationSnapshot(
                    prepareCallCount,
                    appliedEvents.ToArray(),
                    latestEvent,
                    window.Tabs.Visibility,
                    window.ExternalSkinHostPresenter.Visibility,
                    window.ExternalSkinHostPresenter.Content,
                    window.MainHeaderStandardChromePanel.Visibility,
                    window.ExternalSkinMinimalChromePanel.Visibility
                );
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.PrepareCallCount, Is.EqualTo(1));
            Assert.That(result.AppliedEvents, Has.Length.EqualTo(1));
            Assert.That(result.AppliedEvents[0].HostReady, Is.False);
            Assert.That(result.TabsVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.PresenterVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.PresenterContent, Is.Null);
            Assert.That(result.StandardChromeVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.MinimalChromeVisibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [Test]
    public async Task MinimalChromeのGridへ戻るで標準Gridへ復帰できる()
    {
        GridReturnSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> externalApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<HostPresentationEvent> gridApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            window.ExternalSkinHostPrepareAsyncForTesting = (_, _) => Task.FromResult(true);
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                bool isSkinApplyReason =
                    string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal)
                    || string.Equals(reason, "apply-skin", StringComparison.Ordinal);
                if (!isSkinApplyReason)
                {
                    return;
                }

                HostPresentationEvent appliedEvent = new(generation, reason, hostReady);
                if (hostReady)
                {
                    externalApplied.TrySetResult(appliedEvent);
                }
                else
                {
                    gridApplied.TrySetResult(appliedEvent);
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.Skin = $"MinimalChromeGridReturn_{Guid.NewGuid():N}";
                await WaitAsync(
                    externalApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "外部スキン表示の完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                window.ExternalSkinBackToGridButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent)
                );

                HostPresentationEvent gridEvent = await WaitAsync(
                    gridApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "Grid への復帰完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return new GridReturnSnapshot(
                    gridEvent,
                    window.MainVM.DbInfo.Skin ?? "",
                    window.Tabs.Visibility,
                    window.ExternalSkinHostPresenter.Visibility,
                    window.MainHeaderStandardChromePanel.Visibility,
                    window.ExternalSkinMinimalChromePanel.Visibility
                );
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied.HostReady, Is.False);
            Assert.That(result.SkinName, Is.EqualTo("DefaultGrid"));
            Assert.That(result.TabsVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.PresenterVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.StandardChromeVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.MinimalChromeVisibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [Test]
    public async Task MinimalChromeのskin選択ドロップダウンで別外部skinへ切替できる()
    {
        (
            string initialSkinName,
            string switchedSkinName,
            int selectorItemCount,
            string selectorSelectedValue,
            string minimalSkinName
        ) result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<HostPresentationEvent> switchedApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            try
            {
                window.ExternalSkinHostPrepareAsyncForTesting = (_, _) => Task.FromResult(true);
                window.Show();
                await WaitForDispatcherIdleAsync();

                WhiteBrowserSkinDefinition[] externalSkins = GetExternalSkinDefinitions(window);
                Assert.That(
                    externalSkins.Length,
                    Is.GreaterThanOrEqualTo(2),
                    "ドロップダウン切替検証には外部 skin fixture が 2 件以上必要です。"
                );

                WhiteBrowserSkinDefinition initialSkin = externalSkins[0];
                WhiteBrowserSkinDefinition switchedSkin = externalSkins[1];
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (!hostReady)
                    {
                        return;
                    }

                    string currentSkinName = window.MainVM.DbInfo.Skin ?? "";
                    HostPresentationEvent appliedEvent = new(generation, reason, hostReady);
                    if (string.Equals(currentSkinName, initialSkin.Name, StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(appliedEvent);
                    }

                    if (string.Equals(currentSkinName, switchedSkin.Name, StringComparison.Ordinal))
                    {
                        switchedApplied.TrySetResult(appliedEvent);
                    }
                };

                Assert.That(
                    window.ApplySkinByName(initialSkin.Name, persistToCurrentDb: false),
                    Is.True,
                    "初回の外部 skin 適用に失敗しました。"
                );
                await WaitAsync(
                    initialApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "初回の外部 skin 表示完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                // UI 上のドロップダウン選択から skin 切替の流れをそのまま通す。
                window.ExternalSkinMinimalSkinSelector.SelectedValue = switchedSkin.Name;

                await WaitAsync(
                    switchedApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "ドロップダウンからの外部 skin 切替完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return (
                    initialSkin.Name,
                    switchedSkin.Name,
                    window.ExternalSkinMinimalSkinSelector.Items.Count,
                    window.ExternalSkinMinimalSkinSelector.SelectedValue?.ToString() ?? "",
                    window.ExternalSkinMinimalSkinNameText.Text ?? ""
                );
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.initialSkinName, Is.Not.EqualTo(result.switchedSkinName));
            Assert.That(result.selectorItemCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(result.selectorSelectedValue, Is.EqualTo(result.switchedSkinName));
            Assert.That(result.minimalSkinName, Is.EqualTo(result.switchedSkinName));
        });
    }

    [Test]
    public async Task MinimalChromeのReloadでもhost表示を維持して再準備できる()
    {
        string skinName = "";
        ReloadPresentationSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<HostPresentationEvent> reloadedApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            int prepareCallCount = 0;

            window.ExternalSkinHostPrepareAsyncForTesting = (_, _) =>
            {
                Interlocked.Increment(ref prepareCallCount);
                return Task.FromResult(true);
            };
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (!hostReady)
                {
                    return;
                }

                HostPresentationEvent appliedEvent = new(generation, reason, hostReady);
                if (string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                {
                    initialApplied.TrySetResult(appliedEvent);
                }

                if (string.Equals(reason, "minimal-chrome-reload", StringComparison.Ordinal))
                {
                    reloadedApplied.TrySetResult(appliedEvent);
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                WhiteBrowserSkinDefinition externalSkin = GetExternalSkinDefinitions(window).First();
                skinName = externalSkin.Name;
                window.MainVM.DbInfo.Skin = skinName;
                await WaitAsync(
                    initialApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "初回の外部 skin 表示完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                window.ExternalSkinMinimalReloadButton.RaiseEvent(
                    new RoutedEventArgs(Button.ClickEvent)
                );

                HostPresentationEvent reloadedEvent = await WaitAsync(
                    reloadedApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "Minimal reload 後の host 再準備完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return new ReloadPresentationSnapshot(
                    prepareCallCount,
                    CaptureSnapshot(window, reloadedEvent)
                );
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.PrepareCallCount, Is.EqualTo(2));
            Assert.That(result.Snapshot.Applied.HostReady, Is.True);
            Assert.That(
                result.Snapshot.Applied.Reason,
                Is.EqualTo("minimal-chrome-reload")
            );
            Assert.That(result.Snapshot.TabsVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.Snapshot.PresenterVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(
                result.Snapshot.PresenterContent,
                Is.InstanceOf<WhiteBrowserSkinHostControl>()
            );
            Assert.That(
                result.Snapshot.StandardChromeVisibility,
                Is.EqualTo(Visibility.Collapsed)
            );
            Assert.That(
                result.Snapshot.MinimalChromeVisibility,
                Is.EqualTo(Visibility.Visible)
            );
            Assert.That(result.Snapshot.MinimalSkinName, Is.EqualTo(skinName));
        });
    }

    [Test]
    public async Task DB切替で一時overlayを落とし_skin切替では検索連動filterを維持できる()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                WhiteBrowserSkinApiService service = GetExternalSkinApiService(window);
                await HandleApiAsync(service, "addFilter", """{"filter":"idol"}""");
                await HandleApiAsync(service, "addWhere", """{"where":"score >= 80"}""");
                await HandleApiAsync(
                    service,
                    "addOrder",
                    """{"order":"ファイル名(昇順)","override":1}"""
                );

                window.MainVM.DbInfo.DBFullPath = $"db-reset-{Guid.NewGuid():N}.wb";
                await WaitForDispatcherIdleAsync();
                using JsonDocument afterDbReset = await GetFindInfoPayloadAsync(service);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        afterDbReset.RootElement.GetProperty("filter").GetArrayLength(),
                        Is.EqualTo(0)
                    );
                    Assert.That(
                        afterDbReset.RootElement.GetProperty("where").GetString(),
                        Is.EqualTo("")
                    );
                    Assert.That(
                        afterDbReset.RootElement.GetProperty("sort")[1].GetString(),
                        Is.EqualTo("")
                    );
                });

                await HandleApiAsync(service, "addFilter", """{"filter":"beta"}""");
                await HandleApiAsync(service, "addWhere", """{"where":"score >= 50"}""");
                await HandleApiAsync(
                    service,
                    "addOrder",
                    """{"order":"ファイル名(昇順)","override":1}"""
                );

                window.MainVM.DbInfo.Skin = $"skin-reset-{Guid.NewGuid():N}";
                await WaitForDispatcherIdleAsync();
                using JsonDocument afterSkinReset = await GetFindInfoPayloadAsync(service);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        afterSkinReset.RootElement.GetProperty("filter").GetArrayLength(),
                        Is.EqualTo(1)
                    );
                    Assert.That(
                        afterSkinReset.RootElement.GetProperty("filter")[0].GetString(),
                        Is.EqualTo("beta")
                    );
                    Assert.That(
                        afterSkinReset.RootElement.GetProperty("where").GetString(),
                        Is.EqualTo("")
                    );
                    Assert.That(
                        afterSkinReset.RootElement.GetProperty("sort")[1].GetString(),
                        Is.EqualTo("")
                    );
                });
            }
            finally
            {
                await CloseWindowAsync(window);
            }

            return null;
        });
    }

    [Test]
    public async Task addFilter_空白を含むタグでもexact_tag構文で同期できる()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.DBFullPath = $"filter-space-{Guid.NewGuid():N}.wb";
                window.MainVM.DbInfo.Sort = "12";
                window.MainVM.MovieRecs.Add(
                    new MovieRecords
                    {
                        Movie_Id = 1,
                        Movie_Name = "series-a.mp4",
                        Movie_Path = "series-a.mp4",
                        Tags = "シリーズ A",
                    }
                );
                window.MainVM.MovieRecs.Add(
                    new MovieRecords
                    {
                        Movie_Id = 2,
                        Movie_Name = "split.mp4",
                        Movie_Path = "split.mp4",
                        Tags = "シリーズ\nA",
                    }
                );
                window.MainVM.MovieRecs.Add(
                    new MovieRecords
                    {
                        Movie_Id = 3,
                        Movie_Name = "both.mp4",
                        Movie_Path = "both.mp4",
                        Tags = "シリーズ A\n主演",
                    }
                );

                WhiteBrowserSkinApiService service = GetExternalSkinApiService(window);

                await HandleApiAsync(service, "addFilter", """{"filter":"シリーズ A"}""");
                await WaitForDispatcherIdleAsync();
                using JsonDocument afterAdd = await GetFindInfoPayloadAsync(service);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        window.MainVM.DbInfo.SearchKeyword,
                        Is.EqualTo("!tag:\"シリーズ A\"")
                    );
                    Assert.That(afterAdd.RootElement.GetProperty("filter").GetArrayLength(), Is.EqualTo(1));
                    Assert.That(
                        afterAdd.RootElement.GetProperty("filter")[0].GetString(),
                        Is.EqualTo("シリーズ A")
                    );
                });
            }
            finally
            {
                await CloseWindowAsync(window);
            }

            return null;
        });
    }

    [Test]
    public async Task addFilter_自由入力検索を保持したままexact_tagを重ねられる()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.DBFullPath = $"filter-mixed-{Guid.NewGuid():N}.wb";
                window.MainVM.DbInfo.Sort = "12";
                window.MainVM.DbInfo.SearchKeyword = "idol";

                WhiteBrowserSkinApiService service = GetExternalSkinApiService(window);

                await HandleApiAsync(service, "addFilter", """{"filter":"シリーズ A"}""");
                await WaitForDispatcherIdleAsync();
                using JsonDocument afterAdd = await GetFindInfoPayloadAsync(service);

                await HandleApiAsync(service, "clearFilter", """{}""");
                await WaitForDispatcherIdleAsync();
                using JsonDocument afterClear = await GetFindInfoPayloadAsync(service);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        afterAdd.RootElement.GetProperty("find").GetString(),
                        Is.EqualTo("idol !tag:\"シリーズ A\"")
                    );
                    Assert.That(
                        afterAdd.RootElement.GetProperty("filter")[0].GetString(),
                        Is.EqualTo("シリーズ A")
                    );
                    Assert.That(window.MainVM.DbInfo.SearchKeyword, Is.EqualTo("idol"));
                    Assert.That(afterClear.RootElement.GetProperty("find").GetString(), Is.EqualTo("idol"));
                    Assert.That(afterClear.RootElement.GetProperty("filter").GetArrayLength(), Is.EqualTo(0));
                });
            }
            finally
            {
                await CloseWindowAsync(window);
            }

            return null;
        });
    }

    [Test]
    public async Task addFilter_quoted_phrase検索を保持したままexact_tagを重ねられる()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.DBFullPath = $"filter-quoted-{Guid.NewGuid():N}.wb";
                window.MainVM.DbInfo.Sort = "12";
                window.MainVM.DbInfo.SearchKeyword = "\"青い 空\"";

                WhiteBrowserSkinApiService service = GetExternalSkinApiService(window);

                await HandleApiAsync(service, "addFilter", """{"filter":"シリーズ A"}""");
                await WaitForDispatcherIdleAsync();
                using JsonDocument afterAdd = await GetFindInfoPayloadAsync(service);

                await HandleApiAsync(service, "clearFilter", """{}""");
                await WaitForDispatcherIdleAsync();
                using JsonDocument afterClear = await GetFindInfoPayloadAsync(service);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        afterAdd.RootElement.GetProperty("find").GetString(),
                        Is.EqualTo("\"青い 空\" !tag:\"シリーズ A\"")
                    );
                    Assert.That(
                        afterAdd.RootElement.GetProperty("filter")[0].GetString(),
                        Is.EqualTo("シリーズ A")
                    );
                    Assert.That(
                        window.MainVM.DbInfo.SearchKeyword,
                        Is.EqualTo("\"青い 空\"")
                    );
                    Assert.That(
                        afterClear.RootElement.GetProperty("find").GetString(),
                        Is.EqualTo("\"青い 空\"")
                    );
                    Assert.That(afterClear.RootElement.GetProperty("filter").GetArrayLength(), Is.EqualTo(0));
                });
            }
            finally
            {
                await CloseWindowAsync(window);
            }

            return null;
        });
    }

    [Test]
    public async Task 外部skin表示中のDB切替でもhost表示を維持して再準備できる()
    {
        HostPresentationSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> firstApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<HostPresentationEvent> dbSwitchApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            window.ExternalSkinHostPrepareAsyncForTesting = (_, _) => Task.FromResult(true);
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                HostPresentationEvent appliedEvent = new(generation, reason, hostReady);
                if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                {
                    firstApplied.TrySetResult(appliedEvent);
                }

                if (hostReady && string.Equals(reason, "dbinfo-DBFullPath", StringComparison.Ordinal))
                {
                    dbSwitchApplied.TrySetResult(appliedEvent);
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                WhiteBrowserSkinDefinition externalSkin = GetExternalSkinDefinitions(window).First();
                window.MainVM.DbInfo.Skin = externalSkin.Name;
                await WaitAsync(
                    firstApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "初回の外部 skin 表示完了を待てませんでした。"
                );

                window.MainVM.DbInfo.DBFullPath = $"host-db-switch-{Guid.NewGuid():N}.wb";
                HostPresentationEvent appliedEvent = await WaitAsync(
                    dbSwitchApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "DB切替後の host 再準備完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return CaptureSnapshot(window, appliedEvent);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied.HostReady, Is.True);
            Assert.That(result.Applied.Reason, Is.EqualTo("dbinfo-DBFullPath"));
            Assert.That(result.TabsVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.PresenterVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.PresenterContent, Is.InstanceOf<WhiteBrowserSkinHostControl>());
            Assert.That(result.StandardChromeVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.MinimalChromeVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.MinimalSkinName, Is.Not.Empty);
        });
    }

    [Test]
    public async Task external_skin同士の切替でもhost表示を維持しskin名が追従する()
    {
        string secondSkinName = "";
        HostPresentationSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> firstApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<HostPresentationEvent> secondApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            int hostReadyCount = 0;

            window.ExternalSkinHostPrepareAsyncForTesting = (_, _) => Task.FromResult(true);
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (!hostReady || !string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                {
                    return;
                }

                HostPresentationEvent appliedEvent = new(generation, reason, hostReady);
                int readyCount = Interlocked.Increment(ref hostReadyCount);
                if (readyCount == 1)
                {
                    firstApplied.TrySetResult(appliedEvent);
                }
                else if (readyCount == 2)
                {
                    secondApplied.TrySetResult(appliedEvent);
                }
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                WhiteBrowserSkinDefinition[] externalSkins = GetExternalSkinDefinitions(window)
                    .Take(2)
                    .ToArray();
                Assert.That(
                    externalSkins,
                    Has.Length.GreaterThanOrEqualTo(2),
                    "external skin fixture が 2 つ必要です。"
                );

                window.MainVM.DbInfo.Skin = externalSkins[0].Name;
                await WaitAsync(
                    firstApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "最初の外部 skin 表示完了を待てませんでした。"
                );

                secondSkinName = externalSkins[1].Name;
                window.MainVM.DbInfo.Skin = secondSkinName;
                HostPresentationEvent appliedEvent = await WaitAsync(
                    secondApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "2つ目の外部 skin 表示完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return CaptureSnapshot(window, appliedEvent);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied.HostReady, Is.True);
            Assert.That(result.TabsVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.PresenterVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.PresenterContent, Is.InstanceOf<WhiteBrowserSkinHostControl>());
            Assert.That(result.StandardChromeVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.MinimalChromeVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.MinimalSkinName, Is.EqualTo(secondSkinName));
        });
    }

    [Test]
    public async Task external_skinからbuilt_in_skinへ切替してもhost残骸を残さず標準表示へ戻る()
    {
        string currentSkinName = "";
        HostPresentationSnapshot result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();
            TaskCompletionSource<HostPresentationEvent> externalApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            TaskCompletionSource<HostPresentationEvent> builtInApplied = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            int hostReadyCount = 0;

            window.ExternalSkinHostPrepareAsyncForTesting = (_, _) => Task.FromResult(true);
            window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
            {
                if (!string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                {
                    return;
                }

                HostPresentationEvent appliedEvent = new(generation, reason, hostReady);
                if (hostReady)
                {
                    int readyCount = Interlocked.Increment(ref hostReadyCount);
                    if (readyCount == 1)
                    {
                        externalApplied.TrySetResult(appliedEvent);
                    }

                    return;
                }

                builtInApplied.TrySetResult(appliedEvent);
            };

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                WhiteBrowserSkinDefinition externalSkin = GetExternalSkinDefinitions(window).First();
                window.MainVM.DbInfo.Skin = externalSkin.Name;
                await WaitAsync(
                    externalApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "外部 skin 表示完了を待てませんでした。"
                );

                window.MainVM.DbInfo.Skin = "DefaultGrid";
                HostPresentationEvent builtInEvent = await WaitAsync(
                    builtInApplied.Task,
                    TimeSpan.FromSeconds(10),
                    "built-in への復帰完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                currentSkinName = window.MainVM.DbInfo.Skin ?? "";
                return CaptureSnapshot(window, builtInEvent);
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Applied.HostReady, Is.False);
            Assert.That(currentSkinName, Is.EqualTo("DefaultGrid"));
            Assert.That(result.TabsVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.PresenterVisibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(result.PresenterContent, Is.Null);
            Assert.That(result.StandardChromeVisibility, Is.EqualTo(Visibility.Visible));
            Assert.That(result.MinimalChromeVisibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [Test]
    public async Task TutorialCallbackGrid_fixtureをMainWindow経由でDB切替しても旧DOM残骸を残さず再描画できる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            ["TutorialCallbackGrid"],
            rewriteHtmlAsShiftJis: true
        );
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                TaskCompletionSource<HostPresentationEvent> dbSwitchApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    HostPresentationEvent appliedEvent = new(generation, reason, hostReady);
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(appliedEvent);
                    }

                    if (
                        hostReady
                        && string.Equals(reason, "dbinfo-DBFullPath", StringComparison.Ordinal)
                    )
                    {
                        dbSwitchApplied.TrySetResult(appliedEvent);
                    }
                };

                try
                {
                    ReplaceVisibleMovies(
                        window,
                        CreateMovieRecord(42, "Alpha.mp4", "alpha.mp4", "00:01:23", 2048, 12),
                        CreateMovieRecord(43, "Beta.mp4", "beta.mp4", "00:02:34", 4096, 8)
                    );
                    window.MainVM.DbInfo.DBFullPath = $"fixture-mainwindow-{Guid.NewGuid():N}.wb";

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "TutorialCallbackGrid";
                    HostPresentationEvent initialEvent = await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 2 && !!document.getElementById('title42')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 DOM 描画完了を待てませんでした。"
                    );
                    TutorialCallbackGridDomSnapshot firstSnapshot =
                        await ReadTutorialCallbackGridSnapshotAsync(webView, 42);

                    ReplaceVisibleMovies(
                        window,
                        CreateMovieRecord(84, "Gamma.mkv", "gamma.mkv", "00:03:45", 8192, 18)
                    );
                    window.MainVM.DbInfo.DBFullPath = $"fixture-mainwindow-switch-{Guid.NewGuid():N}.wb";

                    HostPresentationEvent dbSwitchEvent = await WaitAsync(
                        dbSwitchApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の DB 切替後 host 再表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    hostControl = GetPresentedHostControl(window);
                    webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 1 && !!document.getElementById('title84') && !document.getElementById('title42')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の DB 切替後 DOM 再描画完了を待てませんでした。"
                    );
                    TutorialCallbackGridDomSnapshot secondSnapshot =
                        await ReadTutorialCallbackGridSnapshotAsync(webView, 84);
                    bool hasLegacyTitle = await ReadJsonBoolAsync(
                        webView,
                        "Boolean(document.getElementById('title42'))"
                    );

                    Assert.Multiple(() =>
                    {
                        Assert.That(initialEvent.HostReady, Is.True);
                        Assert.That(dbSwitchEvent.HostReady, Is.True);
                        Assert.That(dbSwitchEvent.Reason, Is.EqualTo("dbinfo-DBFullPath"));
                        Assert.That(firstSnapshot.ItemCount, Is.EqualTo(2));
                        Assert.That(firstSnapshot.TitleText, Is.EqualTo("Alpha.mp4"));
                        Assert.That(secondSnapshot.ItemCount, Is.EqualTo(1));
                        Assert.That(secondSnapshot.TitleText, Is.EqualTo("Gamma.mkv"));
                        Assert.That(hasLegacyTitle, Is.False);
                        Assert.That(window.Tabs.Visibility, Is.EqualTo(Visibility.Collapsed));
                        Assert.That(
                            window.ExternalSkinHostPresenter.Content,
                            Is.InstanceOf<WhiteBrowserSkinHostControl>()
                        );
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task 実fixtureのexternal_skin同士切替でも旧DOM残骸を残さず描画を切り替えられる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            ["TutorialCallbackGrid", "WhiteBrowserDefaultList"],
            rewriteHtmlAsShiftJis: true
        );
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> firstApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                TaskCompletionSource<HostPresentationEvent> secondApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                int hostReadyCount = 0;

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (!hostReady || !string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        return;
                    }

                    HostPresentationEvent appliedEvent = new(generation, reason, hostReady);
                    int readyCount = Interlocked.Increment(ref hostReadyCount);
                    if (readyCount == 1)
                    {
                        firstApplied.TrySetResult(appliedEvent);
                    }
                    else if (readyCount == 2)
                    {
                        secondApplied.TrySetResult(appliedEvent);
                    }
                };

                try
                {
                    ReplaceVisibleMovies(
                        window,
                        CreateMovieRecord(42, "Alpha.mp4", "alpha.mp4", "00:01:23", 2048, 12),
                        CreateMovieRecord(43, "Beta.mp4", "beta.mp4", "00:02:34", 4096, 8)
                    );
                    window.MainVM.DbInfo.DBFullPath = $"fixture-external-switch-{Guid.NewGuid():N}.wb";

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "TutorialCallbackGrid";
                    await WaitAsync(
                        firstApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 2 && !!document.getElementById('title42')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回描画完了を待てませんでした。"
                    );

                    ReplaceVisibleMovies(
                        window,
                        CreateMovieRecord(77, "ListOnly.mp4", "list-only.mp4", "00:05:06", 16384, 21)
                    );
                    window.MainVM.DbInfo.Skin = "WhiteBrowserDefaultList";

                    HostPresentationEvent secondEvent = await WaitAsync(
                        secondApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList への切替完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    hostControl = GetPresentedHostControl(window);
                    webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view tr').length === 1 && !!document.getElementById('title77') && !!document.getElementById('scroll') && !document.getElementById('title42')",
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList の描画完了を待てませんでした。"
                    );
                    WhiteBrowserDefaultListDomSnapshot listSnapshot =
                        await ReadWhiteBrowserDefaultListSnapshotAsync(webView);
                    bool hasLegacyTutorialTitle = await ReadJsonBoolAsync(
                        webView,
                        "Boolean(document.getElementById('title42'))"
                    );

                    Assert.Multiple(() =>
                    {
                        Assert.That(secondEvent.HostReady, Is.True);
                        Assert.That(listSnapshot.ItemCount, Is.EqualTo(1));
                        Assert.That(listSnapshot.TitleText, Is.EqualTo("ListOnly.mp4"));
                        Assert.That(listSnapshot.ScrollElementId, Is.EqualTo("scroll"));
                        Assert.That(hasLegacyTutorialTitle, Is.False);
                        Assert.That(
                            window.ExternalSkinMinimalSkinNameText.Text,
                            Is.EqualTo("WhiteBrowserDefaultList")
                        );
                        Assert.That(window.Tabs.Visibility, Is.EqualTo(Visibility.Collapsed));
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task WhiteBrowserDefaultListをMainWindow経由でstartIndex付きupdate追記できる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            ["WhiteBrowserDefaultList"],
            rewriteHtmlAsShiftJis: true
        );
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-defaultlist-append-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        MovieRecords[] pagedMovies = Enumerable
            .Range(1, 201)
            .Select(index =>
                CreateMovieRecord(
                    index,
                    $"Movie{index:D3}.mp4",
                    $"movie-{index:D3}.mp4",
                    "00:01:23",
                    1024 + index,
                    index % 100
                )
            )
            .ToArray();
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, pagedMovies);
                    window.MainVM.DbInfo.DBFullPath = $"fixture-defaultlist-append-{Guid.NewGuid():N}.wb";
                    window.MainVM.DbInfo.DBName = "fixture-defaultlist-append";
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "WhiteBrowserDefaultList";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view tr').length === 200 && !!document.getElementById('title200') && !document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList の初回 200 件描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """(async () => { await wb.update(200, 1); return true; })();"""
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view tr').length === 201 && !!document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList の 201 件目追記完了を待てませんでした。"
                    );

                    string[] titles = await ReadJsonStringArrayValueAsync(
                        webView,
                        "Array.from(document.querySelectorAll('#view tr h3')).map(x => x.textContent || '')"
                    );

                    Assert.Multiple(() =>
                    {
                        Assert.That(titles.Length, Is.EqualTo(201));
                        Assert.That(titles[0], Is.EqualTo("Movie001.mp4"));
                        Assert.That(titles[199], Is.EqualTo("Movie200.mp4"));
                        Assert.That(titles[200], Is.EqualTo("Movie201.mp4"));
                        Assert.That(
                            titles.Count(title => string.Equals(title, "Movie200.mp4", StringComparison.Ordinal)),
                            Is.EqualTo(1)
                        );
                        Assert.That(
                            window.ExternalSkinMinimalSkinNameText.Text,
                            Is.EqualTo("WhiteBrowserDefaultList")
                        );
                        Assert.That(window.Tabs.Visibility, Is.EqualTo(Visibility.Collapsed));
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task WhiteBrowserDefaultListをMainWindow経由でconfig_seamless_scroll追記できる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            ["WhiteBrowserDefaultList"],
            rewriteHtmlAsShiftJis: true
        );
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-defaultlist-seamless-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        MovieRecords[] pagedMovies = Enumerable
            .Range(1, 201)
            .Select(index =>
                CreateMovieRecord(
                    index,
                    $"Movie{index:D3}.mp4",
                    $"movie-{index:D3}.mp4",
                    "00:01:23",
                    1024 + index,
                    index % 100
                )
            )
            .ToArray();
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, pagedMovies);
                    window.MainVM.DbInfo.DBFullPath = $"fixture-defaultlist-seamless-{Guid.NewGuid():N}.wb";
                    window.MainVM.DbInfo.DBName = "fixture-defaultlist-seamless";
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "WhiteBrowserDefaultList";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view tr').length === 200 && !!document.getElementById('title200') && !document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList の初回 200 件描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """
                        (() => {
                          const scroll = document.getElementById('scroll');
                          if (!scroll) {
                            return false;
                          }

                          scroll.style.maxHeight = '120px';
                          scroll.style.overflowY = 'auto';
                          scroll.scrollTop = scroll.scrollHeight;
                          scroll.dispatchEvent(new Event('scroll'));
                          return true;
                        })()
                        """
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view tr').length === 201 && !!document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList の seamless scroll 追記完了を待てませんでした。"
                    );

                    string[] titles = await ReadJsonStringArrayValueAsync(
                        webView,
                        "Array.from(document.querySelectorAll('#view tr h3')).map(x => x.textContent || '')"
                    );

                    Assert.Multiple(() =>
                    {
                        Assert.That(titles.Length, Is.EqualTo(201));
                        Assert.That(titles[0], Is.EqualTo("Movie001.mp4"));
                        Assert.That(titles[199], Is.EqualTo("Movie200.mp4"));
                        Assert.That(titles[200], Is.EqualTo("Movie201.mp4"));
                        Assert.That(
                            titles.Count(title => string.Equals(title, "Movie200.mp4", StringComparison.Ordinal)),
                            Is.EqualTo(1)
                        );
                        Assert.That(window.ExternalSkinMinimalSkinNameText.Text, Is.EqualTo("WhiteBrowserDefaultList"));
                        Assert.That(window.Tabs.Visibility, Is.EqualTo(Visibility.Collapsed));
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task WhiteBrowserDefaultListをMainWindow経由でseamless_scroll追記後にfindしても旧row残骸を残さず戻せる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            ["WhiteBrowserDefaultList"],
            rewriteHtmlAsShiftJis: true
        );
        string dbPath = CreateTempMainDbWithMovies(
            Enumerable
                .Range(1, 201)
                .Select(index =>
                    CreateMovieRecord(
                        index,
                        $"Movie{index:D3}.mp4",
                        $"movie-{index:D3}.mp4",
                        "00:01:23",
                        1024 + index,
                        index % 100
                    )
                )
                .ToArray()
        );
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-defaultlist-seamless-find-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(
                        window,
                        Enumerable
                            .Range(1, 201)
                            .Select(index =>
                                CreateMovieRecord(
                                    index,
                                    $"Movie{index:D3}.mp4",
                                    $"movie-{index:D3}.mp4",
                                    "00:01:23",
                                    1024 + index,
                                    index % 100
                                )
                            )
                            .ToArray()
                    );
                    window.MainVM.DbInfo.DBFullPath = dbPath;
                    window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "WhiteBrowserDefaultList";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view tr').length === 200 && !!document.getElementById('title200') && !document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList の初回 200 件描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """
                        (() => {
                          const scroll = document.getElementById('scroll');
                          if (!scroll) {
                            return false;
                          }

                          scroll.style.maxHeight = '120px';
                          scroll.style.overflowY = 'auto';
                          scroll.scrollTop = scroll.scrollHeight;
                          scroll.dispatchEvent(new Event('scroll'));
                          return true;
                        })()
                        """
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view tr').length === 201 && !!document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList の seamless scroll 追記完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """(async () => { await wb.find("Movie201", 0); return true; })();"""
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view tr').length === 1 && !!document.getElementById('title201') && !document.getElementById('title200')",
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList の find 再描画完了を待てませんでした。"
                    );

                    string[] titles = await ReadJsonStringArrayValueAsync(
                        webView,
                        "Array.from(document.querySelectorAll('#view tr h3')).map(x => x.textContent || '')"
                    );

                    Assert.Multiple(() =>
                    {
                        Assert.That(window.MainVM.DbInfo.SearchKeyword, Is.EqualTo("Movie201"));
                        Assert.That(window.MainVM.FilteredMovieRecs.Select(x => x.Movie_Id), Is.EqualTo(new[] { 201L }));
                        Assert.That(titles, Is.EqualTo(new[] { "Movie201.mp4" }));
                        Assert.That(window.ExternalSkinMinimalSkinNameText.Text, Is.EqualTo("WhiteBrowserDefaultList"));
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(dbPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task WhiteBrowserDefaultListをMainWindow経由で検索後にseamless_scroll追記できる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            ["WhiteBrowserDefaultList"],
            rewriteHtmlAsShiftJis: true
        );
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-defaultlist-search-seamless-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        MovieRecords[] pagedMovies = Enumerable
            .Range(1, 260)
            .Select(index =>
                CreateMovieRecord(
                    index,
                    $"Movie{index:D3}.mp4",
                    $"movie-{index:D3}.mp4",
                    "00:01:23",
                    1024 + index,
                    index % 100
                )
            )
            .ToArray();
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, pagedMovies);
                    window.MainVM.DbInfo.DBFullPath =
                        $"fixture-defaultlist-search-seamless-{Guid.NewGuid():N}.wb";
                    window.MainVM.DbInfo.DBName = "fixture-defaultlist-search-seamless";
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "WhiteBrowserDefaultList";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view tr').length === 200 && !!document.getElementById('title200') && !document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList の初回 200 件描画完了を待てませんでした。"
                    );

                    // find 経由の絞り込み状態を作ってから scroll 追記へ入れる
                    await ExecuteHostScriptAsync(
                        webView,
                        """(async () => { await wb.find("Movie", 0); return true; })();"""
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view tr').length === 200 && !!document.getElementById('title200') && !document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList の検索結果初回描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """
                        (() => {
                          const scroll = document.getElementById('scroll');
                          if (!scroll) {
                            return false;
                          }

                          scroll.style.maxHeight = '120px';
                          scroll.style.overflowY = 'auto';
                          scroll.scrollTop = scroll.scrollHeight;
                          scroll.dispatchEvent(new Event('scroll'));
                          return true;
                        })()
                        """
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view tr').length === 260 && !!document.getElementById('title260')",
                        TimeSpan.FromSeconds(15),
                        "WhiteBrowserDefaultList の検索後 seamless scroll 追記完了を待てませんでした。"
                    );

                    string[] titles = await ReadJsonStringArrayValueAsync(
                        webView,
                        "Array.from(document.querySelectorAll('#view tr h3')).map(x => x.textContent || '')"
                    );

                    Assert.Multiple(() =>
                    {
                        Assert.That(window.MainVM.DbInfo.SearchKeyword, Is.EqualTo("Movie"));
                        Assert.That(titles.Length, Is.EqualTo(260));
                        Assert.That(titles[0], Is.EqualTo("Movie001.mp4"));
                        Assert.That(titles[199], Is.EqualTo("Movie200.mp4"));
                        Assert.That(titles[259], Is.EqualTo("Movie260.mp4"));
                        Assert.That(
                            titles.Count(title => string.Equals(title, "Movie200.mp4", StringComparison.Ordinal)),
                            Is.EqualTo(1)
                        );
                        Assert.That(window.ExternalSkinMinimalSkinNameText.Text, Is.EqualTo("WhiteBrowserDefaultList"));
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task TutorialCallbackGridをMainWindow経由でseamless_scroll追記しても先頭focusを保てる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            ["TutorialCallbackGrid"],
            rewriteHtmlAsShiftJis: true
        );
        MovieRecords[] pagedMovies = Enumerable
            .Range(1, 201)
            .Select(index =>
                CreateMovieRecord(
                    index,
                    $"Movie{index:D3}.mp4",
                    $"movie-{index:D3}.mp4",
                    "00:01:23",
                    1024 + index,
                    index % 100
                )
            )
            .ToArray();
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, pagedMovies);
                    window.MainVM.DbInfo.DBFullPath = $"fixture-tutorial-seamless-{Guid.NewGuid():N}.wb";

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "TutorialCallbackGrid";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 200 && !!document.getElementById('title200') && !document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 200 件描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """
                        (() => {
                          const view = document.getElementById('view');
                          if (!view) {
                            return false;
                          }

                          view.style.maxHeight = '120px';
                          view.style.overflowY = 'auto';
                          view.scrollTop = view.scrollHeight;
                          view.dispatchEvent(new Event('scroll'));
                          return true;
                        })()
                        """
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 201 && !!document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の seamless scroll 追記完了を待てませんでした。"
                    );

                    string[] titles = await ReadJsonStringArrayValueAsync(
                        webView,
                        "Array.from(document.querySelectorAll('#view .thum_base h1')).map(x => x.textContent || '')"
                    );
                    string focusedTitle = await ReadJsonStringAsync(
                        webView,
                        """
                        (() => {
                          const focused = document.querySelector('#view .img_base.img_f');
                          if (!focused || !focused.id) {
                            return '';
                          }

                          const title = document.getElementById(focused.id.replace('img', 'title'));
                          return title ? title.textContent || '' : '';
                        })()
                        """
                    );

                    Assert.Multiple(() =>
                    {
                        Assert.That(titles.Length, Is.EqualTo(201));
                        Assert.That(titles[0], Is.EqualTo("Movie001.mp4"));
                        Assert.That(titles[199], Is.EqualTo("Movie200.mp4"));
                        Assert.That(titles[200], Is.EqualTo("Movie201.mp4"));
                        Assert.That(focusedTitle, Is.EqualTo("Movie001.mp4"));
                        Assert.That(window.Tabs.Visibility, Is.EqualTo(Visibility.Collapsed));
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task TutorialCallbackGridをMainWindow経由でstartIndex付きupdate追記できる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            ["TutorialCallbackGrid"],
            rewriteHtmlAsShiftJis: true
        );
        MovieRecords[] pagedMovies = Enumerable
            .Range(1, 201)
            .Select(index =>
                CreateMovieRecord(
                    index,
                    $"Movie{index:D3}.mp4",
                    $"movie-{index:D3}.mp4",
                    "00:01:23",
                    1024 + index,
                    index % 100
                )
            )
            .ToArray();
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, pagedMovies);
                    window.MainVM.DbInfo.DBFullPath = $"fixture-tutorial-append-{Guid.NewGuid():N}.wb";

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "TutorialCallbackGrid";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 200 && !!document.getElementById('title200') && !document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 200 件描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """(async () => { await wb.update(200, 1); return true; })();"""
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 201 && !!document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の 201 件目追記完了を待てませんでした。"
                    );

                    string[] titles = await ReadJsonStringArrayValueAsync(
                        webView,
                        "Array.from(document.querySelectorAll('#view .thum_base h1')).map(x => x.textContent || '')"
                    );
                    string focusedTitle = await ReadJsonStringAsync(
                        webView,
                        """
                        (() => {
                          const focused = document.querySelector('#view .img_base.img_f');
                          if (!focused || !focused.id) {
                            return '';
                          }

                          const title = document.getElementById(focused.id.replace('img', 'title'));
                          return title ? title.textContent || '' : '';
                        })()
                        """
                    );

                    Assert.Multiple(() =>
                    {
                        Assert.That(titles.Length, Is.EqualTo(201));
                        Assert.That(titles[0], Is.EqualTo("Movie001.mp4"));
                        Assert.That(titles[199], Is.EqualTo("Movie200.mp4"));
                        Assert.That(titles[200], Is.EqualTo("Movie201.mp4"));
                        Assert.That(
                            titles.Count(title => string.Equals(title, "Movie200.mp4", StringComparison.Ordinal)),
                            Is.EqualTo(1)
                        );
                        Assert.That(focusedTitle, Is.EqualTo("Movie201.mp4"));
                        Assert.That(window.Tabs.Visibility, Is.EqualTo(Visibility.Collapsed));
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task TutorialCallbackGridをMainWindow経由で追加ページ後にfindしても旧thum残骸を残さず先頭結果へ戻せる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            ["TutorialCallbackGrid"],
            rewriteHtmlAsShiftJis: true
        );
        MovieRecords[] pagedMovies = Enumerable
            .Range(1, 201)
            .Select(index =>
                CreateMovieRecord(
                    index,
                    $"Movie{index:D3}.mp4",
                    $"movie-{index:D3}.mp4",
                    "00:01:23",
                    2048 + index,
                    index % 100
                )
            )
            .ToArray();
        string dbPath = CreateTempMainDbWithMovies(pagedMovies);
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, pagedMovies);
                    window.MainVM.DbInfo.DBFullPath = dbPath;
                    window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "TutorialCallbackGrid";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 200 && !!document.getElementById('title200') && !document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 200 件描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """(async () => { await wb.update(200, 1); return true; })();"""
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 201 && !!document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の追加ページ描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """(async () => { await wb.find("Movie201", 0); return true; })();"""
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 1 && !!document.getElementById('title201') && !document.getElementById('title200') && document.querySelector('#view .img_base.img_f') && document.querySelector('#view .img_base.img_f').id === 'img201'",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の find 再描画完了を待てませんでした。"
                    );

                    TutorialCallbackGridDomSnapshot snapshot =
                        await ReadTutorialCallbackGridSnapshotAsync(webView, 201);
                    bool hasLegacyTitle200 = await ReadJsonBoolAsync(
                        webView,
                        "Boolean(document.getElementById('title200'))"
                    );
                    string[] titles = await ReadJsonStringArrayValueAsync(
                        webView,
                        "Array.from(document.querySelectorAll('#view .thum_base h1')).map(x => x.textContent || '')"
                    );

                    Assert.Multiple(() =>
                    {
                        Assert.That(window.MainVM.DbInfo.SearchKeyword, Is.EqualTo("Movie201"));
                        Assert.That(window.MainVM.FilteredMovieRecs.Select(x => x.Movie_Id), Is.EqualTo(new[] { 201L }));
                        Assert.That(snapshot.ItemCount, Is.EqualTo(1));
                        Assert.That(snapshot.TitleText, Is.EqualTo("Movie201.mp4"));
                        Assert.That(snapshot.FocusedImageClass, Does.Contain("img_f"));
                        Assert.That(hasLegacyTitle200, Is.False);
                        Assert.That(titles, Is.EqualTo(new[] { "Movie201.mp4" }));
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(dbPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task SimpleGridWBをMainWindow経由で段階読み込みしても追加ページを重複なく描画できる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateRepositorySkinRootCopyWithCompat(
            ["SimpleGridWB"]
        );
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-simplegrid-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        MovieRecords[] pagedMovies = Enumerable
            .Range(1, 260)
            .Select(index =>
                CreateMovieRecord(
                    index,
                    $"Movie{index:D3}.mp4",
                    $"movie-{index:D3}.mp4",
                    "00:01:23",
                    1024 + index,
                    index % 100,
                    index % 2 == 0 ? "idol" : "beta"
                )
            )
            .ToArray();
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, pagedMovies);
                    window.MainVM.DbInfo.DBFullPath = $"fixture-simplegrid-{Guid.NewGuid():N}.wb";
                    window.MainVM.DbInfo.DBName = "fixture-simplegrid";
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "SimpleGridWB";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card').length === 200 && document.getElementById('loadMoreButton') && getComputedStyle(document.getElementById('loadMoreButton')).display !== 'none'",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の初回ページ描画完了を待てませんでした。"
                    );

                    SimpleGridDomSnapshot firstSnapshot =
                        await ReadSimpleGridSnapshotAsync(webView);
                    await ExecuteHostScriptAsync(
                        webView,
                        """document.getElementById('loadMoreButton').click();"""
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card').length === 260 && document.querySelectorAll('#view .card .card__title').length === 260 && document.querySelectorAll('#view .card .card__title')[259].textContent === 'Movie260.mp4' && getComputedStyle(document.getElementById('loadMoreButton')).display === 'none'",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の追加ページ描画完了を待てませんでした。"
                    );
                    SimpleGridDomSnapshot secondSnapshot =
                        await ReadSimpleGridSnapshotAsync(webView);

                    Assert.Multiple(() =>
                    {
                        Assert.That(firstSnapshot.ItemCount, Is.EqualTo(200));
                        Assert.That(firstSnapshot.FirstTitle, Is.EqualTo("Movie001.mp4"));
                        Assert.That(firstSnapshot.LastTitle, Is.EqualTo("Movie200.mp4"));
                        Assert.That(firstSnapshot.ResultCountText, Is.EqualTo("200 / 260 items"));
                        Assert.That(firstSnapshot.LoadMoreVisible, Is.True);
                        Assert.That(secondSnapshot.ItemCount, Is.EqualTo(260));
                        Assert.That(secondSnapshot.FirstTitle, Is.EqualTo("Movie001.mp4"));
                        Assert.That(secondSnapshot.LastTitle, Is.EqualTo("Movie260.mp4"));
                        Assert.That(secondSnapshot.ResultCountText, Is.EqualTo("260 items"));
                        Assert.That(secondSnapshot.LoadMoreVisible, Is.False);
                        Assert.That(secondSnapshot.StatusText, Is.EqualTo("全件表示"));
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task SimpleGridWBをMainWindow経由でスクロールしても追加ページを重複なく描画できる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateRepositorySkinRootCopyWithCompat(
            ["SimpleGridWB"]
        );
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-simplegrid-scroll-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        MovieRecords[] pagedMovies = Enumerable
            .Range(1, 260)
            .Select(index =>
                CreateMovieRecord(
                    index,
                    $"Movie{index:D3}.mp4",
                    $"movie-{index:D3}.mp4",
                    "00:01:23",
                    1024 + index,
                    index % 100,
                    index % 2 == 0 ? "idol" : "beta"
                )
            )
            .ToArray();
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, pagedMovies);
                    window.MainVM.DbInfo.DBFullPath = $"fixture-simplegrid-scroll-{Guid.NewGuid():N}.wb";
                    window.MainVM.DbInfo.DBName = "fixture-simplegrid-scroll";
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "SimpleGridWB";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card').length === 200 && document.getElementById('loadMoreButton') && getComputedStyle(document.getElementById('loadMoreButton')).display !== 'none'",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の初回ページ描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """
                        (() => {
                          const view = document.getElementById('view');
                          if (!view) {
                            return false;
                          }

                          view.scrollTop = view.scrollHeight;
                          view.dispatchEvent(new Event('scroll'));
                          return true;
                        })()
                        """
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card').length === 260 && document.querySelectorAll('#view .card .card__title').length === 260 && document.querySelectorAll('#view .card .card__title')[259].textContent === 'Movie260.mp4' && getComputedStyle(document.getElementById('loadMoreButton')).display === 'none'",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB のスクロール追加ページ描画完了を待てませんでした。"
                    );

                    SimpleGridDomSnapshot snapshot = await ReadSimpleGridSnapshotAsync(webView);

                    Assert.Multiple(() =>
                    {
                        Assert.That(snapshot.ItemCount, Is.EqualTo(260));
                        Assert.That(snapshot.FirstTitle, Is.EqualTo("Movie001.mp4"));
                        Assert.That(snapshot.LastTitle, Is.EqualTo("Movie260.mp4"));
                        Assert.That(snapshot.ResultCountText, Is.EqualTo("260 items"));
                        Assert.That(snapshot.LoadMoreVisible, Is.False);
                        Assert.That(snapshot.StatusText, Is.EqualTo("全件表示"));
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task SimpleGridWBをMainWindow経由で残り件数だけ要求し既存cardを保持したまま追記できる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateRepositorySkinRootCopyWithCompat(
            ["SimpleGridWB"]
        );
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-simplegrid-append-diff-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        MovieRecords[] pagedMovies = Enumerable
            .Range(1, 260)
            .Select(index =>
                CreateMovieRecord(
                    index,
                    $"Movie{index:D3}.mp4",
                    $"movie-{index:D3}.mp4",
                    "00:01:23",
                    1024 + index,
                    index % 100,
                    index % 2 == 0 ? "idol" : "beta"
                )
            )
            .ToArray();
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, pagedMovies);
                    window.MainVM.DbInfo.DBFullPath = $"fixture-simplegrid-append-diff-{Guid.NewGuid():N}.wb";
                    window.MainVM.DbInfo.DBName = "fixture-simplegrid-append-diff";
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "SimpleGridWB";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card').length === 200",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の初回ページ描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """
                        (() => {
                          const originalGetInfos = wb.getInfos;
                          const firstCard = document.querySelector('#view .card');
                          const view = document.getElementById('view');
                          window.__simpleGridAppendProbe = { calls: [] };
                          if (firstCard) {
                            firstCard.dataset.probeKeep = 'true';
                            window.__simpleGridAppendProbe.firstCard = firstCard;
                          }

                          wb.getInfos = function(startIndex, count) {
                            window.__simpleGridAppendProbe.calls.push(String(startIndex || 0) + ':' + String(count || 0));
                            return originalGetInfos(startIndex, count);
                          };

                          view.scrollTop = view.scrollHeight;
                          view.dispatchEvent(new Event('scroll'));
                          return true;
                        })()
                        """
                    );

                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card').length === 260 && document.querySelectorAll('#view .card .card__title').length === 260 && document.querySelectorAll('#view .card .card__title')[259].textContent === 'Movie260.mp4'",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の差分追記完了を待てませんでした。"
                    );

                    string probeJson = await ReadJsonStringAsync(
                        webView,
                        """
                        JSON.stringify({
                          calls: window.__simpleGridAppendProbe ? window.__simpleGridAppendProbe.calls : [],
                          sameFirstCard: !!(window.__simpleGridAppendProbe && window.__simpleGridAppendProbe.firstCard) &&
                            window.__simpleGridAppendProbe.firstCard === document.querySelector('#view .card'),
                          firstCardKept: !!document.querySelector('#view .card') &&
                            document.querySelector('#view .card').dataset.probeKeep === 'true'
                        })
                        """
                    );
                    using JsonDocument probeDocument = JsonDocument.Parse(probeJson);
                    string[] calls = JsonSerializer.Deserialize<string[]>(
                        probeDocument.RootElement.GetProperty("calls").GetRawText()
                    ) ?? [];

                    Assert.Multiple(() =>
                    {
                        Assert.That(calls, Is.EqualTo(["200:60"]));
                        Assert.That(probeDocument.RootElement.GetProperty("sameFirstCard").GetBoolean(), Is.True);
                        Assert.That(probeDocument.RootElement.GetProperty("firstCardKept").GetBoolean(), Is.True);
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task SimpleGridWBをMainWindow経由で可視範囲優先にthumbを読み込める()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateRepositorySkinRootCopyWithCompat(
            ["SimpleGridWB"]
        );
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-simplegrid-visible-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        MovieRecords[] pagedMovies = Enumerable
            .Range(1, 200)
            .Select(index =>
                CreateMovieRecord(
                    index,
                    $"Movie{index:D3}.mp4",
                    $"movie-{index:D3}.mp4",
                    "00:01:23",
                    1024 + index,
                    index % 100,
                    index % 2 == 0 ? "idol" : "beta"
                )
            )
            .ToArray();
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, pagedMovies);
                    window.MainVM.DbInfo.DBFullPath = $"fixture-simplegrid-visible-thumb-{Guid.NewGuid():N}.wb";
                    window.MainVM.DbInfo.DBName = "fixture-simplegrid-visible-thumb";
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "SimpleGridWB";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card').length === 200 && document.querySelectorAll('#view .card__thumb[src]').length > 0 && document.querySelectorAll('#view .card__thumb[data-thumb-url]').length > 0",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の可視範囲 thumb 初期化完了を待てませんでした。"
                    );

                    string beforeJson = await ReadJsonStringAsync(
                        webView,
                        """
                        JSON.stringify({
                          loadedCount: document.querySelectorAll('#view .card__thumb[src]').length,
                          deferredCount: document.querySelectorAll('#view .card__thumb[data-thumb-url]').length,
                          firstLoaded: !!document.querySelectorAll('#view .card__thumb')[0] && document.querySelectorAll('#view .card__thumb')[0].hasAttribute('src'),
                          lastLoaded: !!document.querySelectorAll('#view .card__thumb')[199] && document.querySelectorAll('#view .card__thumb')[199].hasAttribute('src'),
                          firstDistant: !!document.querySelectorAll('#view .card')[0] && document.querySelectorAll('#view .card')[0].classList.contains('is-distant'),
                          lastDistant: !!document.querySelectorAll('#view .card')[199] && document.querySelectorAll('#view .card')[199].classList.contains('is-distant')
                        })
                        """
                    );
                    using JsonDocument beforeDocument = JsonDocument.Parse(beforeJson);

                    await ExecuteHostScriptAsync(
                        webView,
                        """
                        (() => {
                          const view = document.getElementById('view');
                          if (!view) {
                            return false;
                          }

                          view.scrollTop = view.scrollHeight;
                          view.dispatchEvent(new Event('scroll'));
                          return true;
                        })()
                        """
                    );

                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card__thumb')[199] && document.querySelectorAll('#view .card__thumb')[199].hasAttribute('src')",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の末尾 thumb 可視範囲読込完了を待てませんでした。"
                    );

                    string afterJson = await ReadJsonStringAsync(
                        webView,
                        """
                        JSON.stringify({
                          loadedCount: document.querySelectorAll('#view .card__thumb[src]').length,
                          deferredCount: document.querySelectorAll('#view .card__thumb[data-thumb-url]').length,
                          firstLoaded: !!document.querySelectorAll('#view .card__thumb')[0] && document.querySelectorAll('#view .card__thumb')[0].hasAttribute('src'),
                          lastLoaded: !!document.querySelectorAll('#view .card__thumb')[199] && document.querySelectorAll('#view .card__thumb')[199].hasAttribute('src'),
                          firstDistant: !!document.querySelectorAll('#view .card')[0] && document.querySelectorAll('#view .card')[0].classList.contains('is-distant'),
                          lastDistant: !!document.querySelectorAll('#view .card')[199] && document.querySelectorAll('#view .card')[199].classList.contains('is-distant')
                        })
                        """
                    );
                    using JsonDocument afterDocument = JsonDocument.Parse(afterJson);

                    Assert.Multiple(() =>
                    {
                        Assert.That(beforeDocument.RootElement.GetProperty("loadedCount").GetInt32(), Is.GreaterThan(0));
                        Assert.That(beforeDocument.RootElement.GetProperty("deferredCount").GetInt32(), Is.GreaterThan(0));
                        Assert.That(beforeDocument.RootElement.GetProperty("firstLoaded").GetBoolean(), Is.True);
                        Assert.That(beforeDocument.RootElement.GetProperty("lastLoaded").GetBoolean(), Is.False);
                        Assert.That(beforeDocument.RootElement.GetProperty("firstDistant").GetBoolean(), Is.False);
                        Assert.That(beforeDocument.RootElement.GetProperty("lastDistant").GetBoolean(), Is.True);
                        Assert.That(afterDocument.RootElement.GetProperty("lastLoaded").GetBoolean(), Is.True);
                        Assert.That(afterDocument.RootElement.GetProperty("firstLoaded").GetBoolean(), Is.False);
                        Assert.That(afterDocument.RootElement.GetProperty("firstDistant").GetBoolean(), Is.True);
                        Assert.That(afterDocument.RootElement.GetProperty("lastDistant").GetBoolean(), Is.False);
                        Assert.That(afterDocument.RootElement.GetProperty("loadedCount").GetInt32(), Is.GreaterThan(0));
                        Assert.That(afterDocument.RootElement.GetProperty("deferredCount").GetInt32(), Is.GreaterThan(0));
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task SimpleGridWBをMainWindow経由でoffscreen_thumbへonUpdateThum差分更新できる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateRepositorySkinRootCopyWithCompat(
            ["SimpleGridWB"]
        );
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-simplegrid-thumb-diff-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        MovieRecords[] pagedMovies = Enumerable
            .Range(1, 200)
            .Select(index =>
                CreateMovieRecord(
                    index,
                    $"Movie{index:D3}.mp4",
                    $"movie-{index:D3}.mp4",
                    "00:01:23",
                    1024 + index,
                    index % 100,
                    index % 2 == 0 ? "idol" : "beta"
                )
            )
            .ToArray();
        const string UpdatedThumbUrl = "about:blank#updated-thumb-200";
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, pagedMovies);
                    window.MainVM.DbInfo.DBFullPath = $"fixture-simplegrid-thumb-diff-{Guid.NewGuid():N}.wb";
                    window.MainVM.DbInfo.DBName = "fixture-simplegrid-thumb-diff";
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "SimpleGridWB";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card').length === 200 && document.querySelectorAll('#view .card__thumb[data-thumb-url]').length > 0 && document.querySelectorAll('#view .card__thumb')[199] && !document.querySelectorAll('#view .card__thumb')[199].hasAttribute('src')",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の offscreen thumb 初期状態を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        $$"""
                        (() => {
                          const cards = document.querySelectorAll('#view .card');
                          const thumbs = document.querySelectorAll('#view .card__thumb');
                          const lastCard = cards[199];
                          const lastThumb = thumbs[199];
                          if (!lastCard || !lastThumb) {
                            return false;
                          }

                          const recordKey = lastCard.getAttribute('data-record-key') || '';
                          window.__simpleGridThumbDiffProbe = {
                            lastCard,
                            recordKey,
                            updatedUrl: '{{UpdatedThumbUrl}}'
                          };
                          lastCard.dataset.probeKeep = 'true';
                          window.onUpdateThum(recordKey, '{{UpdatedThumbUrl}}');
                          return true;
                        })()
                        """
                    );

                    await WaitForWebConditionAsync(
                        webView,
                        $$"""document.querySelectorAll('#view .card__thumb')[199] && document.querySelectorAll('#view .card__thumb')[199].getAttribute('data-thumb-url') === '{{UpdatedThumbUrl}}' && !document.querySelectorAll('#view .card__thumb')[199].hasAttribute('src')""",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の offscreen thumb 差分更新完了を待てませんでした。"
                    );

                    string beforeJson = await ReadJsonStringAsync(
                        webView,
                        """
                        JSON.stringify({
                          sameLastCard: !!(window.__simpleGridThumbDiffProbe && window.__simpleGridThumbDiffProbe.lastCard) &&
                            window.__simpleGridThumbDiffProbe.lastCard === document.querySelectorAll('#view .card')[199],
                          probeKept: !!document.querySelectorAll('#view .card')[199] &&
                            document.querySelectorAll('#view .card')[199].dataset.probeKeep === 'true',
                          deferredThumbUrl: document.querySelectorAll('#view .card__thumb')[199] ?
                            (document.querySelectorAll('#view .card__thumb')[199].getAttribute('data-thumb-url') || '') : '',
                          hasSrc: !!document.querySelectorAll('#view .card__thumb')[199] &&
                            document.querySelectorAll('#view .card__thumb')[199].hasAttribute('src'),
                          stateThumbUrl: (() => {
                            const probe = window.__simpleGridThumbDiffProbe;
                            const items = (window.simpleGridState && window.simpleGridState.items) || [];
                            const key = probe ? probe.recordKey : '';
                            for (let i = 0; i < items.length; i += 1) {
                              const item = items[i] || {};
                              const itemKey = String(item.RecordKey || item.recordKey || item.MovieId || item.movieId || item.Id || item.id || '');
                              if (itemKey === key) {
                                return String(item.ThumbUrl || item.thumbUrl || '');
                              }
                            }

                            return '';
                          })()
                        })
                        """
                    );
                    using JsonDocument beforeDocument = JsonDocument.Parse(beforeJson);

                    await ExecuteHostScriptAsync(
                        webView,
                        """
                        (() => {
                          const view = document.getElementById('view');
                          if (!view) {
                            return false;
                          }

                          view.scrollTop = view.scrollHeight;
                          view.dispatchEvent(new Event('scroll'));
                          return true;
                        })()
                        """
                    );

                    await WaitForWebConditionAsync(
                        webView,
                        $$"""document.querySelectorAll('#view .card__thumb')[199] && document.querySelectorAll('#view .card__thumb')[199].getAttribute('src') === '{{UpdatedThumbUrl}}' && !document.querySelectorAll('#view .card__thumb')[199].hasAttribute('data-thumb-url')""",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の差分更新 thumb 昇格完了を待てませんでした。"
                    );

                    string afterJson = await ReadJsonStringAsync(
                        webView,
                        """
                        JSON.stringify({
                          sameLastCard: !!(window.__simpleGridThumbDiffProbe && window.__simpleGridThumbDiffProbe.lastCard) &&
                            window.__simpleGridThumbDiffProbe.lastCard === document.querySelectorAll('#view .card')[199],
                          probeKept: !!document.querySelectorAll('#view .card')[199] &&
                            document.querySelectorAll('#view .card')[199].dataset.probeKeep === 'true',
                          srcValue: document.querySelectorAll('#view .card__thumb')[199] ?
                            (document.querySelectorAll('#view .card__thumb')[199].getAttribute('src') || '') : '',
                          hasDeferredThumbUrl: !!document.querySelectorAll('#view .card__thumb')[199] &&
                            document.querySelectorAll('#view .card__thumb')[199].hasAttribute('data-thumb-url')
                        })
                        """
                    );
                    using JsonDocument afterDocument = JsonDocument.Parse(afterJson);

                    Assert.Multiple(() =>
                    {
                        Assert.That(beforeDocument.RootElement.GetProperty("sameLastCard").GetBoolean(), Is.True);
                        Assert.That(beforeDocument.RootElement.GetProperty("probeKept").GetBoolean(), Is.True);
                        Assert.That(beforeDocument.RootElement.GetProperty("deferredThumbUrl").GetString(), Is.EqualTo(UpdatedThumbUrl));
                        Assert.That(beforeDocument.RootElement.GetProperty("hasSrc").GetBoolean(), Is.False);
                        Assert.That(beforeDocument.RootElement.GetProperty("stateThumbUrl").GetString(), Is.EqualTo(UpdatedThumbUrl));
                        Assert.That(afterDocument.RootElement.GetProperty("sameLastCard").GetBoolean(), Is.True);
                        Assert.That(afterDocument.RootElement.GetProperty("probeKept").GetBoolean(), Is.True);
                        Assert.That(afterDocument.RootElement.GetProperty("srcValue").GetString(), Is.EqualTo(UpdatedThumbUrl));
                        Assert.That(afterDocument.RootElement.GetProperty("hasDeferredThumbUrl").GetBoolean(), Is.False);
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task SimpleGridWBをMainWindow経由で空振り追記後は再要求せず停止できる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateRepositorySkinRootCopyWithCompat(
            ["SimpleGridWB"]
        );
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-simplegrid-stop-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        MovieRecords[] pagedMovies = Enumerable
            .Range(1, 200)
            .Select(index =>
                CreateMovieRecord(
                    index,
                    $"Movie{index:D3}.mp4",
                    $"movie-{index:D3}.mp4",
                    "00:01:23",
                    1024 + index,
                    index % 100,
                    index % 2 == 0 ? "idol" : "beta"
                )
            )
            .ToArray();
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, pagedMovies);
                    window.MainVM.DbInfo.DBFullPath = $"fixture-simplegrid-stop-{Guid.NewGuid():N}.wb";
                    window.MainVM.DbInfo.DBName = "fixture-simplegrid-stop";
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "SimpleGridWB";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card').length === 200 && document.getElementById('loadMoreButton') && getComputedStyle(document.getElementById('loadMoreButton')).display === 'none'",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の初回終端描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """
                        (async () => {
                          const originalGetInfos = wb.getInfos;
                          const view = document.getElementById('view');
                          window.__simpleGridStopProbe = { calls: 0 };
                          simpleGridState.totalCount = simpleGridState.items.length + 5;
                          simpleGridState.appendExhausted = false;
                          updateLoadMoreButton();

                          wb.getInfos = function(startIndex, count) {
                            window.__simpleGridStopProbe.calls += 1;
                            return Promise.resolve({
                              startIndex: Number(startIndex || 0),
                              count: Number(count || 0),
                              totalCount: simpleGridState.items.length + 5,
                              items: []
                            });
                          };

                          view.scrollTop = view.scrollHeight;
                          view.dispatchEvent(new Event('scroll'));
                          await new Promise(resolve => setTimeout(resolve, 120));
                          view.dispatchEvent(new Event('scroll'));
                          await new Promise(resolve => setTimeout(resolve, 120));

                          wb.getInfos = originalGetInfos;
                          return true;
                        })()
                        """
                    );

                    await WaitForWebConditionAsync(
                        webView,
                        "window.__simpleGridStopProbe && window.__simpleGridStopProbe.calls === 1 && simpleGridState.appendExhausted === true && document.getElementById('loadMoreButton') && getComputedStyle(document.getElementById('loadMoreButton')).display === 'none'",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の空振り停止完了を待てませんでした。"
                    );

                    string probeJson = await ReadJsonStringAsync(
                        webView,
                        """
                        JSON.stringify({
                          calls: window.__simpleGridStopProbe ? window.__simpleGridStopProbe.calls : -1,
                          itemCount: document.querySelectorAll('#view .card').length,
                          appendExhausted: !!simpleGridState.appendExhausted,
                          statusText: document.getElementById('status') ? document.getElementById('status').textContent : '',
                          loadMoreVisible: document.getElementById('loadMoreButton') ? getComputedStyle(document.getElementById('loadMoreButton')).display !== 'none' : false
                        })
                        """
                    );
                    using JsonDocument probeDocument = JsonDocument.Parse(probeJson);

                    Assert.Multiple(() =>
                    {
                        Assert.That(probeDocument.RootElement.GetProperty("calls").GetInt32(), Is.EqualTo(1));
                        Assert.That(probeDocument.RootElement.GetProperty("itemCount").GetInt32(), Is.EqualTo(200));
                        Assert.That(probeDocument.RootElement.GetProperty("appendExhausted").GetBoolean(), Is.True);
                        Assert.That(probeDocument.RootElement.GetProperty("statusText").GetString(), Is.EqualTo("全件表示"));
                        Assert.That(probeDocument.RootElement.GetProperty("loadMoreVisible").GetBoolean(), Is.False);
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task SimpleGridWBをMainWindow経由で検索後にスクロールしても追加ページを重複なく描画できる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateRepositorySkinRootCopyWithCompat(
            ["SimpleGridWB"]
        );
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-simplegrid-search-scroll-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        MovieRecords[] pagedMovies = Enumerable
            .Range(1, 260)
            .Select(index =>
                CreateMovieRecord(
                    index,
                    $"Movie{index:D3}.mp4",
                    $"movie-{index:D3}.mp4",
                    "00:01:23",
                    1024 + index,
                    index % 100,
                    index % 2 == 0 ? "idol" : "beta"
                )
            )
            .ToArray();
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, pagedMovies);
                    window.MainVM.DbInfo.DBFullPath = $"fixture-simplegrid-search-scroll-{Guid.NewGuid():N}.wb";
                    window.MainVM.DbInfo.DBName = "fixture-simplegrid-search-scroll";
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "SimpleGridWB";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card').length === 200",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の初回ページ描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """
                        (() => {
                          const input = document.getElementById('searchInput');
                          const button = document.getElementById('searchButton');
                          if (!input || !button) {
                            return false;
                          }

                          input.value = 'Movie';
                          button.click();
                          return true;
                        })()
                        """
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card').length === 200 && document.getElementById('resultCount') && document.getElementById('resultCount').textContent === '200 / 260 items' && document.getElementById('status') && document.getElementById('status').textContent.indexOf('検索: \"Movie\"') >= 0",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の検索結果初回描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """
                        (() => {
                          const view = document.getElementById('view');
                          if (!view) {
                            return false;
                          }

                          view.scrollTop = view.scrollHeight;
                          view.dispatchEvent(new Event('scroll'));
                          return true;
                        })()
                        """
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card').length === 260 && document.querySelectorAll('#view .card .card__title').length === 260 && document.querySelectorAll('#view .card .card__title')[259].textContent === 'Movie260.mp4' && document.getElementById('resultCount').textContent === '260 items'",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の検索後スクロール追加ページ描画完了を待てませんでした。"
                    );

                    SimpleGridDomSnapshot snapshot = await ReadSimpleGridSnapshotAsync(webView);

                    Assert.Multiple(() =>
                    {
                        Assert.That(snapshot.ItemCount, Is.EqualTo(260));
                        Assert.That(snapshot.FirstTitle, Is.EqualTo("Movie001.mp4"));
                        Assert.That(snapshot.LastTitle, Is.EqualTo("Movie260.mp4"));
                        Assert.That(snapshot.ResultCountText, Is.EqualTo("260 items"));
                        Assert.That(snapshot.LoadMoreVisible, Is.False);
                        Assert.That(snapshot.StatusText, Is.EqualTo("検索: \"Movie\""));
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task SimpleGridWBをMainWindow経由で追加ページ後にfindしても旧card残骸を残さず先頭結果へ戻せる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateRepositorySkinRootCopyWithCompat(
            ["SimpleGridWB"]
        );
        MovieRecords[] pagedMovies = Enumerable
            .Range(1, 260)
            .Select(index =>
                CreateMovieRecord(
                    index,
                    $"Movie{index:D3}.mp4",
                    $"movie-{index:D3}.mp4",
                    "00:01:23",
                    2048 + index,
                    index % 100,
                    index % 2 == 0 ? "idol" : "beta"
                )
            )
            .ToArray();
        string dbPath = CreateTempMainDbWithMovies(pagedMovies);
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-simplegrid-find-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, pagedMovies);
                    window.MainVM.DbInfo.DBFullPath = dbPath;
                    window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;
                    window.MainVM.DbInfo.Sort = "12";

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "SimpleGridWB";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card').length === 200 && document.getElementById('loadMoreButton') && getComputedStyle(document.getElementById('loadMoreButton')).display !== 'none'",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の初回ページ描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """document.getElementById('loadMoreButton').click();"""
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card').length === 260 && getComputedStyle(document.getElementById('loadMoreButton')).display === 'none'",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の追加ページ描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """
                        document.getElementById('searchInput').value = 'Movie260';
                        document.getElementById('searchButton').click();
                        """
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .card').length === 1 && document.querySelector('#view .card .card__title') && document.querySelector('#view .card .card__title').textContent === 'Movie260.mp4' && getComputedStyle(document.getElementById('loadMoreButton')).display === 'none'",
                        TimeSpan.FromSeconds(15),
                        "SimpleGridWB の find 再描画完了を待てませんでした。"
                    );

                    SimpleGridDomSnapshot snapshot = await ReadSimpleGridSnapshotAsync(webView);
                    string[] titles = await ReadJsonStringArrayValueAsync(
                        webView,
                        "Array.from(document.querySelectorAll('#view .card .card__title')).map(x => x.textContent || '')"
                    );

                    Assert.Multiple(() =>
                    {
                        Assert.That(window.MainVM.DbInfo.SearchKeyword, Is.EqualTo("Movie260"));
                        Assert.That(window.MainVM.FilteredMovieRecs.Select(x => x.Movie_Id), Is.EqualTo(new[] { 260L }));
                        Assert.That(snapshot.ItemCount, Is.EqualTo(1));
                        Assert.That(snapshot.FirstTitle, Is.EqualTo("Movie260.mp4"));
                        Assert.That(snapshot.LastTitle, Is.EqualTo("Movie260.mp4"));
                        Assert.That(snapshot.ResultCountText, Is.EqualTo("1 items"));
                        Assert.That(snapshot.StatusText, Is.EqualTo("検索: \"Movie260\""));
                        Assert.That(snapshot.LoadMoreVisible, Is.False);
                        Assert.That(titles, Is.EqualTo(new[] { "Movie260.mp4" }));
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            TryDeleteFile(dbPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task TutorialCallbackGrid_fixtureをMainWindow経由でfind再更新しても旧DOM残骸を残さず絞り込める()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            ["TutorialCallbackGrid"],
            rewriteHtmlAsShiftJis: true
        );
        MovieRecords[] seededMovies =
        [
            CreateMovieRecord(42, "Alpha.mp4", "alpha.mp4", "00:01:23", 2048, 12, "idol"),
            CreateMovieRecord(43, "Beta.mp4", "beta.mp4", "00:02:34", 4096, 8, "beta"),
            CreateMovieRecord(84, "Gamma.mkv", "gamma.mkv", "00:03:45", 8192, 18, "idol"),
        ];
        string dbPath = CreateTempMainDbWithMovies(seededMovies);
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-find-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, seededMovies);
                    window.MainVM.DbInfo.DBFullPath = dbPath;
                    window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;
                    window.MainVM.DbInfo.Sort = "12";

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "TutorialCallbackGrid";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 3 && !!document.getElementById('title42') && !!document.getElementById('title84')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 DOM 描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """(async () => { await wb.find("Gamma"); return true; })();"""
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 1 && !!document.getElementById('title84') && !document.getElementById('title42') && !document.getElementById('title43')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の find 再描画完了を待てませんでした。"
                    );

                    TutorialCallbackGridDomSnapshot filteredSnapshot =
                        await ReadTutorialCallbackGridSnapshotAsync(webView, 84);
                    bool hasLegacyAlpha = await ReadJsonBoolAsync(
                        webView,
                        "Boolean(document.getElementById('title42'))"
                    );
                    bool hasLegacyBeta = await ReadJsonBoolAsync(
                        webView,
                        "Boolean(document.getElementById('title43'))"
                    );

                    Assert.Multiple(() =>
                    {
                        Assert.That(window.MainVM.DbInfo.SearchKeyword, Is.EqualTo("Gamma"));
                        Assert.That(window.MainVM.FilteredMovieRecs.Select(x => x.Movie_Id), Is.EqualTo(new[] { 84L }));
                        Assert.That(filteredSnapshot.ItemCount, Is.EqualTo(1));
                        Assert.That(filteredSnapshot.TitleText, Is.EqualTo("Gamma.mkv"));
                        Assert.That(hasLegacyAlpha, Is.False);
                        Assert.That(hasLegacyBeta, Is.False);
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            TryDeleteFile(dbPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task TutorialCallbackGrid_fixtureをMainWindow経由でsort再更新しても重複せず並び順を更新できる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            ["TutorialCallbackGrid"],
            rewriteHtmlAsShiftJis: true
        );
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(
                        window,
                        CreateMovieRecord(42, "Alpha.mp4", "alpha.mp4", "00:01:23", 2048, 12, "idol"),
                        CreateMovieRecord(84, "Gamma.mkv", "gamma.mkv", "00:03:45", 8192, 18, "idol"),
                        CreateMovieRecord(43, "Beta.mp4", "beta.mp4", "00:02:34", 4096, 8, "beta")
                    );
                    window.MainVM.DbInfo.DBFullPath = $"fixture-mainwindow-sort-{Guid.NewGuid():N}.wb";

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "TutorialCallbackGrid";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 3 && !!document.getElementById('title42') && !!document.getElementById('title43') && !!document.getElementById('title84')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 DOM 描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """(async () => { await wb.sort("ファイル名(降順)"); return true; })();"""
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 3 && document.querySelector('#view .thum_base h1') && document.querySelector('#view .thum_base h1').textContent === 'Gamma.mkv'",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の sort 再描画完了を待てませんでした。"
                    );

                    string[] titles = await ReadJsonStringArrayValueAsync(
                        webView,
                        "Array.from(document.querySelectorAll('#view .thum_base h1')).map(x => x.textContent || '')"
                    );

                    Assert.Multiple(() =>
                    {
                        Assert.That(window.MainVM.DbInfo.Sort, Is.EqualTo("13"));
                        Assert.That(
                            window.MainVM.FilteredMovieRecs.Select(x => x.Movie_Id),
                            Is.EqualTo(new[] { 84L, 43L, 42L })
                        );
                        Assert.That(titles, Is.EqualTo(new[] { "Gamma.mkv", "Beta.mp4", "Alpha.mp4" }));
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task TutorialCallbackGrid_fixtureをMainWindow経由でaddFilter再更新しても旧DOM残骸を残さずexact_tagで絞り込める()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            ["TutorialCallbackGrid"],
            rewriteHtmlAsShiftJis: true
        );
        MovieRecords[] seededMovies =
        [
            CreateMovieRecord(42, "Alpha.mp4", "alpha.mp4", "00:01:23", 2048, 12, "idol"),
            CreateMovieRecord(43, "Beta.mp4", "beta.mp4", "00:02:34", 4096, 8, "beta"),
            CreateMovieRecord(84, "Gamma.mkv", "gamma.mkv", "00:03:45", 8192, 18, "idol"),
        ];
        string dbPath = CreateTempMainDbWithMovies(seededMovies);
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-filter-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, seededMovies);
                    window.MainVM.DbInfo.DBFullPath = dbPath;
                    window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;
                    window.MainVM.DbInfo.Sort = "12";

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "TutorialCallbackGrid";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 3 && !!document.getElementById('title42') && !!document.getElementById('title43') && !!document.getElementById('title84')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 DOM 描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """(async () => { await wb.addFilter("idol"); return true; })();"""
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 2 && !!document.getElementById('title42') && !!document.getElementById('title84') && !document.getElementById('title43')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の addFilter 再描画完了を待てませんでした。"
                    );

                    string[] titles = await ReadJsonStringArrayValueAsync(
                        webView,
                        "Array.from(document.querySelectorAll('#view .thum_base h1')).map(x => x.textContent || '')"
                    );
                    bool hasLegacyBeta = await ReadJsonBoolAsync(
                        webView,
                        "Boolean(document.getElementById('title43'))"
                    );

                    Assert.Multiple(() =>
                    {
                        Assert.That(window.MainVM.DbInfo.SearchKeyword, Is.EqualTo("!tag:idol"));
                        Assert.That(
                            window.MainVM.FilteredMovieRecs.Select(x => x.Movie_Id),
                            Is.EqualTo(new[] { 42L, 84L })
                        );
                        Assert.That(titles, Is.EqualTo(new[] { "Alpha.mp4", "Gamma.mkv" }));
                        Assert.That(hasLegacyBeta, Is.False);
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            TryDeleteFile(dbPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task TutorialCallbackGrid_fixtureをMainWindow経由でminimal_reloadしても旧DOM残骸を残さず再描画できる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            ["TutorialCallbackGrid"],
            rewriteHtmlAsShiftJis: true
        );
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-reload-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(
                        window,
                        CreateMovieRecord(42, "Alpha.mp4", "alpha.mp4", "00:01:23", 2048, 12),
                        CreateMovieRecord(84, "Gamma.mkv", "gamma.mkv", "00:03:45", 8192, 18)
                    );
                    window.MainVM.DbInfo.DBFullPath = $"fixture-reload-{Guid.NewGuid():N}.wb";
                    window.MainVM.DbInfo.DBName = "fixture-reload";
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "TutorialCallbackGrid";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 2 && !!document.getElementById('title42') && !!document.getElementById('title84')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 DOM 描画完了を待てませんでした。"
                    );
                    await ExecuteHostScriptAsync(
                        webView,
                        """(() => { document.body.dataset.reloadProbe = "before"; return true; })();"""
                    );

                    window.ExternalSkinMinimalReloadButton.RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent)
                    );
                    await WaitForDispatcherIdleAsync();

                    await WaitForWebConditionAsync(
                        webView,
                        "document.body && !document.body.dataset.reloadProbe && document.querySelectorAll('#view .thum_base').length === 2 && !!document.getElementById('title42') && !!document.getElementById('title84')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の minimal reload 後 DOM 再描画完了を待てませんでした。"
                    );

                    string[] titles = await ReadJsonStringArrayValueAsync(
                        webView,
                        "Array.from(document.querySelectorAll('#view .thum_base h1')).map(x => x.textContent || '')"
                    );

                    Assert.Multiple(() =>
                    {
                        Assert.That(titles, Is.EqualTo(new[] { "Alpha.mp4", "Gamma.mkv" }));
                        Assert.That(
                            window.ExternalSkinHostPresenter.Visibility,
                            Is.EqualTo(Visibility.Visible)
                        );
                        Assert.That(
                            window.ExternalSkinMinimalChromePanel.Visibility,
                            Is.EqualTo(Visibility.Visible)
                        );
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task TutorialCallbackGrid_fixtureからbuilt_in_skinへ切替しても旧host残骸を残さず標準表示へ戻る()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            ["TutorialCallbackGrid"],
            rewriteHtmlAsShiftJis: true
        );
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-builtin-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                TaskCompletionSource<HostPresentationEvent> builtInApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    HostPresentationEvent appliedEvent = new(generation, reason, hostReady);
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(appliedEvent);
                    }

                    if (!hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        builtInApplied.TrySetResult(appliedEvent);
                    }
                };

                try
                {
                    ReplaceVisibleMovies(
                        window,
                        CreateMovieRecord(42, "Alpha.mp4", "alpha.mp4", "00:01:23", 2048, 12)
                    );
                    window.MainVM.DbInfo.DBFullPath = $"fixture-builtin-{Guid.NewGuid():N}.wb";
                    window.MainVM.DbInfo.DBName = "fixture-builtin";
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = "TutorialCallbackGrid";
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.querySelectorAll('#view .thum_base').length === 1 && !!document.getElementById('title42')",
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid の初回 DOM 描画完了を待てませんでした。"
                    );

                    window.MainVM.DbInfo.Skin = "DefaultGrid";
                    HostPresentationEvent builtInEvent = await WaitAsync(
                        builtInApplied.Task,
                        TimeSpan.FromSeconds(15),
                        "TutorialCallbackGrid から built-in への復帰完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    Assert.Multiple(() =>
                    {
                        Assert.That(builtInEvent.HostReady, Is.False);
                        Assert.That(window.MainVM.DbInfo.Skin, Is.EqualTo("DefaultGrid"));
                        Assert.That(window.Tabs.Visibility, Is.EqualTo(Visibility.Visible));
                        Assert.That(
                            window.ExternalSkinHostPresenter.Visibility,
                            Is.EqualTo(Visibility.Collapsed)
                        );
                        Assert.That(window.ExternalSkinHostPresenter.Content, Is.Null);
                        Assert.That(
                            window.MainHeaderStandardChromePanel.Visibility,
                            Is.EqualTo(Visibility.Visible)
                        );
                        Assert.That(
                            window.ExternalSkinMinimalChromePanel.Visibility,
                            Is.EqualTo(Visibility.Collapsed)
                        );
                    });
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public async Task addFilter_removeFilter_clearFilterがSearchKeywordとfindInfoへ同期される()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.DBFullPath = $"filter-sync-{Guid.NewGuid():N}.wb";
                window.MainVM.DbInfo.Sort = "12";
                window.MainVM.MovieRecs.Add(
                    new MovieRecords
                    {
                        Movie_Id = 1,
                        Movie_Name = "idol-a.mp4",
                        Movie_Path = "idol-a.mp4",
                        Tags = "idol",
                    }
                );
                window.MainVM.MovieRecs.Add(
                    new MovieRecords
                    {
                        Movie_Id = 2,
                        Movie_Name = "beta.mp4",
                        Movie_Path = "beta.mp4",
                        Tags = "beta",
                    }
                );

                WhiteBrowserSkinApiService service = GetExternalSkinApiService(window);

                await HandleApiAsync(service, "addFilter", """{"filter":"idol"}""");
                await WaitForDispatcherIdleAsync();
                using JsonDocument afterAdd = await GetFindInfoPayloadAsync(service);

                await HandleApiAsync(service, "addFilter", """{"filter":"beta"}""");
                await WaitForDispatcherIdleAsync();
                using JsonDocument afterSecondAdd = await GetFindInfoPayloadAsync(service);

                await HandleApiAsync(service, "removeFilter", """{"filter":"idol"}""");
                await WaitForDispatcherIdleAsync();
                using JsonDocument afterRemove = await GetFindInfoPayloadAsync(service);

                await HandleApiAsync(service, "clearFilter", """{}""");
                await WaitForDispatcherIdleAsync();
                using JsonDocument afterClear = await GetFindInfoPayloadAsync(service);

                Assert.Multiple(() =>
                {
                    Assert.That(window.MainVM.DbInfo.SearchKeyword, Is.EqualTo(""));

                    Assert.That(afterAdd.RootElement.GetProperty("filter").GetArrayLength(), Is.EqualTo(1));
                    Assert.That(afterAdd.RootElement.GetProperty("filter")[0].GetString(), Is.EqualTo("idol"));

                    Assert.That(
                        afterSecondAdd.RootElement.GetProperty("filter").EnumerateArray().Select(x => x.GetString()),
                        Is.EqualTo(new[] { "idol", "beta" })
                    );

                    Assert.That(afterRemove.RootElement.GetProperty("filter").GetArrayLength(), Is.EqualTo(1));
                    Assert.That(afterRemove.RootElement.GetProperty("filter")[0].GetString(), Is.EqualTo("beta"));

                    Assert.That(afterClear.RootElement.GetProperty("filter").GetArrayLength(), Is.EqualTo(0));
                });
            }
            finally
            {
                await CloseWindowAsync(window);
            }

            return null;
        });
    }

    private static void ReplaceVisibleMovies(MainWindow window, params MovieRecords[] movies)
    {
        MovieRecords[] normalizedMovies = movies?.Where(x => x != null).ToArray() ?? [];
        window.MainVM.ReplaceMovieRecs(normalizedMovies);
        window.MainVM.ReplaceFilteredMovieRecs(
            normalizedMovies,
            FilteredMovieRecsUpdateMode.Reset
        );
        window.MainVM.DbInfo.RegisteredMovieCount = normalizedMovies.Length;
    }

    private static MovieRecords CreateMovieRecord(
        long movieId,
        string movieName,
        string moviePath,
        string movieLength,
        long movieSize,
        long score,
        string tags = ""
    )
    {
        return new MovieRecords
        {
            Movie_Id = movieId,
            Movie_Name = movieName ?? "",
            Movie_Path = moviePath ?? "",
            Movie_Length = movieLength ?? "",
            Movie_Size = movieSize,
            Score = score,
            Tags = tags ?? "",
            IsExists = true,
            Ext = Path.GetExtension(movieName ?? "") ?? "",
        };
    }

    private static string CreateTempMainDbWithMovies(params MovieRecords[] movies)
    {
        string dbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-{Guid.NewGuid():N}.wb"
        );
        bool created = SQLite.TryCreateDatabase(dbPath, out string errorMessage);
        Assert.That(created, Is.True, errorMessage);

        using SQLiteConnection connection = SQLite.CreateReadWriteConnection(dbPath);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        foreach (MovieRecords movie in movies?.Where(x => x != null) ?? [])
        {
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText =
                @"
INSERT INTO movie (
    movie_id,
    movie_name,
    movie_path,
    movie_length,
    movie_size,
    last_date,
    file_date,
    regist_date,
    score,
    view_count,
    hash,
    container,
    video,
    audio,
    extra,
    title,
    artist,
    album,
    grouping,
    writer,
    genre,
    track,
    camera,
    create_time,
    kana,
    roma,
    tag,
    comment1,
    comment2,
    comment3
)
VALUES (
    @movie_id,
    @movie_name,
    @movie_path,
    @movie_length,
    @movie_size,
    @last_date,
    @file_date,
    @regist_date,
    @score,
    @view_count,
    @hash,
    @container,
    @video,
    @audio,
    @extra,
    @title,
    @artist,
    @album,
    @grouping,
    @writer,
    @genre,
    @track,
    @camera,
    @create_time,
    @kana,
    @roma,
    @tag,
    @comment1,
    @comment2,
    @comment3
)";
            string movieDisplayName = movie.Movie_Name ?? "";
            string movieBaseName = Path.GetFileNameWithoutExtension(movieDisplayName);
            string movieFileName = string.IsNullOrWhiteSpace(movieDisplayName)
                ? $"movie-{movie.Movie_Id}.mp4"
                : movieDisplayName;
            string movieFullPath = Path.Combine(
                Path.GetTempPath(),
                "imm-mainwindow-webviewskin-fixture-movies",
                movieFileName
            );
            string timestamp = "2026-04-12 10:00:00";
            command.Parameters.AddWithValue("@movie_id", movie.Movie_Id);
            command.Parameters.AddWithValue("@movie_name", movieBaseName);
            command.Parameters.AddWithValue("@movie_path", movieFullPath);
            command.Parameters.AddWithValue("@movie_length", ParseMovieLengthSeconds(movie.Movie_Length));
            command.Parameters.AddWithValue("@movie_size", movie.Movie_Size);
            command.Parameters.AddWithValue("@last_date", timestamp);
            command.Parameters.AddWithValue("@file_date", timestamp);
            command.Parameters.AddWithValue("@regist_date", timestamp);
            command.Parameters.AddWithValue("@score", movie.Score);
            command.Parameters.AddWithValue("@view_count", 0);
            command.Parameters.AddWithValue("@hash", $"hash-{movie.Movie_Id}");
            command.Parameters.AddWithValue("@container", Path.GetExtension(movieFileName).TrimStart('.'));
            command.Parameters.AddWithValue("@video", "h264");
            command.Parameters.AddWithValue("@audio", "aac");
            command.Parameters.AddWithValue("@extra", "");
            command.Parameters.AddWithValue("@title", movieBaseName);
            command.Parameters.AddWithValue("@artist", "");
            command.Parameters.AddWithValue("@album", "");
            command.Parameters.AddWithValue("@grouping", "");
            command.Parameters.AddWithValue("@writer", "");
            command.Parameters.AddWithValue("@genre", "");
            command.Parameters.AddWithValue("@track", "");
            command.Parameters.AddWithValue("@camera", "");
            command.Parameters.AddWithValue("@create_time", "");
            command.Parameters.AddWithValue("@kana", "");
            command.Parameters.AddWithValue("@roma", "");
            command.Parameters.AddWithValue("@tag", movie.Tags ?? "");
            command.Parameters.AddWithValue("@comment1", "");
            command.Parameters.AddWithValue("@comment2", "");
            command.Parameters.AddWithValue("@comment3", "");
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return dbPath;
    }

    private static long ParseMovieLengthSeconds(string movieLength)
    {
        if (TimeSpan.TryParse(movieLength ?? "", out TimeSpan parsed))
        {
            return (long)parsed.TotalSeconds;
        }

        return 0;
    }

    private static WhiteBrowserSkinHostControl GetPresentedHostControl(MainWindow window)
    {
        return window?.ExternalSkinHostPresenter?.Content as WhiteBrowserSkinHostControl
            ?? throw new AssertionException("表示中の外部 skin host control を取得できませんでした。");
    }

    private static WebView2 GetHostWebView(WhiteBrowserSkinHostControl hostControl)
    {
        FieldInfo field = typeof(WhiteBrowserSkinHostControl).GetField(
            "SkinWebView",
            BindingFlags.Instance | BindingFlags.NonPublic
        ) ?? throw new AssertionException("WhiteBrowserSkinHostControl の SkinWebView フィールドが見つかりません。");
        return field.GetValue(hostControl) as WebView2
            ?? throw new AssertionException("WhiteBrowserSkinHostControl の WebView2 実体を取得できませんでした。");
    }

    private static async Task WaitForWebConditionAsync(
        WebView2 webView,
        string conditionScript,
        TimeSpan timeout,
        string timeoutMessage
    )
    {
        DateTime deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            string resultJson = await webView.ExecuteScriptAsync($"Boolean({conditionScript})");
            if (string.Equals(resultJson, "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new AssertionException(timeoutMessage);
    }

    private static async Task ExecuteHostScriptAsync(WebView2 webView, string script)
    {
        await webView.ExecuteScriptAsync(script);
        await Task.Yield();
    }

    private static async Task<string> ReadJsonStringAsync(WebView2 webView, string script)
    {
        string resultJson = await webView.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<string>(resultJson) ?? "";
    }

    private static async Task<string[]> ReadJsonStringArrayValueAsync(WebView2 webView, string script)
    {
        string resultJson = await webView.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<string[]>(resultJson) ?? [];
    }

    private static async Task<bool> ReadJsonBoolAsync(WebView2 webView, string script)
    {
        string resultJson = await webView.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<bool>(resultJson);
    }

    private static async Task<TutorialCallbackGridDomSnapshot> ReadTutorialCallbackGridSnapshotAsync(
        WebView2 webView,
        int movieId
    )
    {
        string movieKey = movieId.ToString();
        string json = await ReadJsonStringAsync(
            webView,
            $$"""
            JSON.stringify({
              itemCount: document.querySelectorAll('#view .thum_base').length,
              titleText: document.getElementById('title{{movieKey}}') ? document.getElementById('title{{movieKey}}').textContent : '',
              focusedImageClass: document.getElementById('img{{movieKey}}') ? document.getElementById('img{{movieKey}}').className : '',
              selectedThumbClass: document.getElementById('thum{{movieKey}}') ? document.getElementById('thum{{movieKey}}').className : ''
            })
            """
        );
        using JsonDocument document = JsonDocument.Parse(json);
        return new TutorialCallbackGridDomSnapshot(
            document.RootElement.GetProperty("itemCount").GetInt32(),
            document.RootElement.GetProperty("titleText").GetString() ?? "",
            document.RootElement.GetProperty("focusedImageClass").GetString() ?? "",
            document.RootElement.GetProperty("selectedThumbClass").GetString() ?? ""
        );
    }

    private static async Task<WhiteBrowserDefaultListDomSnapshot> ReadWhiteBrowserDefaultListSnapshotAsync(
        WebView2 webView
    )
    {
        string json = await ReadJsonStringAsync(
            webView,
            """
            JSON.stringify({
              itemCount: document.querySelectorAll('#view tr').length,
              titleText: document.getElementById('title77') ? document.getElementById('title77').textContent : '',
              sizeText: document.querySelector('#thum77 td:nth-child(3) h4') ? document.querySelector('#thum77 td:nth-child(3) h4').textContent : '',
              lengthText: document.querySelector('#thum77 td:nth-child(4) h4') ? document.querySelector('#thum77 td:nth-child(4) h4').textContent : '',
              scrollElementId: document.getElementById('scroll') ? 'scroll' : ''
            })
            """
        );
        using JsonDocument document = JsonDocument.Parse(json);
        return new WhiteBrowserDefaultListDomSnapshot(
            document.RootElement.GetProperty("itemCount").GetInt32(),
            document.RootElement.GetProperty("titleText").GetString() ?? "",
            document.RootElement.GetProperty("sizeText").GetString() ?? "",
            document.RootElement.GetProperty("lengthText").GetString() ?? "",
            document.RootElement.GetProperty("scrollElementId").GetString() ?? ""
        );
    }

    private static async Task VerifySimpleWhiteBrowserDefaultFixtureSeamlessScrollAsync(
        string fixtureName,
        string expectedFirstTitle,
        string expectedExistingLastTitle,
        string expectedAppendedTitle,
        string expectedAppendedScoreText = "",
        string expectedFindResetTitle = "",
        string expectedFindResetScoreText = ""
    )
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            [fixtureName],
            rewriteHtmlAsShiftJis: true
        );
        string thumbFolderPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-mainwindow-webviewskin-{fixtureName.ToLowerInvariant()}-seamless-thumb-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(thumbFolderPath);
        MovieRecords[] pagedMovies = Enumerable
            .Range(1, 201)
            .Select(index =>
                CreateMovieRecord(
                    index,
                    $"Movie{index:D3}.mp4",
                    $"movie-{index:D3}.mp4",
                    "00:01:23",
                    1024 + index,
                    index % 100
                )
            )
            .ToArray();
        string dbPath = string.IsNullOrWhiteSpace(expectedFindResetTitle)
            ? ""
            : CreateTempMainDbWithMovies(pagedMovies);

        try
        {
            await RunOnStaDispatcherAsync<object?>(async () =>
            {
                using TestEnvironmentScope scope = TestEnvironmentScope.Create();
                MainWindow window = CreateHiddenMainWindow();
                TaskCompletionSource<HostPresentationEvent> initialApplied = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.ExternalSkinHostPresentationAppliedForTesting = (generation, hostReady, reason) =>
                {
                    if (hostReady && string.Equals(reason, "dbinfo-Skin", StringComparison.Ordinal))
                    {
                        initialApplied.TrySetResult(new HostPresentationEvent(generation, reason, hostReady));
                    }
                };

                try
                {
                    ReplaceVisibleMovies(window, pagedMovies);
                    window.MainVM.DbInfo.DBFullPath = string.IsNullOrWhiteSpace(dbPath)
                        ? $"fixture-{fixtureName.ToLowerInvariant()}-seamless-{Guid.NewGuid():N}.wb"
                        : dbPath;
                    window.MainVM.DbInfo.DBName = string.IsNullOrWhiteSpace(dbPath)
                        ? $"fixture-{fixtureName.ToLowerInvariant()}-seamless"
                        : Path.GetFileNameWithoutExtension(dbPath);
                    window.MainVM.DbInfo.ThumbFolder = thumbFolderPath;

                    window.Show();
                    await WaitForDispatcherIdleAsync();

                    window.MainVM.DbInfo.Skin = fixtureName;
                    await WaitAsync(
                        initialApplied.Task,
                        TimeSpan.FromSeconds(15),
                        $"{fixtureName} の初回 host 表示完了を待てませんでした。"
                    );
                    await WaitForDispatcherIdleAsync();

                    WhiteBrowserSkinHostControl hostControl = GetPresentedHostControl(window);
                    WebView2 webView = GetHostWebView(hostControl);
                    await WaitForWebConditionAsync(
                        webView,
                        "document.getElementById('view') && document.getElementById('view').children.length === 200 && !!document.getElementById('title200') && !document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        $"{fixtureName} の初回 200 件描画完了を待てませんでした。"
                    );

                    await ExecuteHostScriptAsync(
                        webView,
                        """
                        (() => {
                          const view = document.getElementById('view');
                          if (!view) {
                            return false;
                          }

                          view.style.maxHeight = '120px';
                          view.style.overflowY = 'auto';
                          view.scrollTop = view.scrollHeight;
                          view.dispatchEvent(new Event('scroll'));
                          return true;
                        })()
                        """
                    );
                    await WaitForWebConditionAsync(
                        webView,
                        "document.getElementById('view') && document.getElementById('view').children.length === 201 && !!document.getElementById('title201')",
                        TimeSpan.FromSeconds(15),
                        $"{fixtureName} の seamless scroll 追記完了を待てませんでした。"
                    );

                    string[] titles = await ReadJsonStringArrayValueAsync(
                        webView,
                        "Array.from(document.querySelectorAll('#view [id^=\"title\"]')).map(x => x.textContent || '')"
                    );
                    string appendedScoreText = await ReadJsonStringAsync(
                        webView,
                        "document.getElementById('score201') ? document.getElementById('score201').textContent || '' : ''"
                    );

                    Assert.Multiple(() =>
                    {
                        Assert.That(titles.Length, Is.EqualTo(201));
                        Assert.That(titles[0], Is.EqualTo(expectedFirstTitle));
                        Assert.That(titles[199], Is.EqualTo(expectedExistingLastTitle));
                        Assert.That(titles[200], Is.EqualTo(expectedAppendedTitle));
                        Assert.That(
                            titles.Count(title => string.Equals(title, expectedExistingLastTitle, StringComparison.Ordinal)),
                            Is.EqualTo(1)
                        );
                        Assert.That(appendedScoreText, Is.EqualTo(expectedAppendedScoreText));
                        Assert.That(window.ExternalSkinMinimalSkinNameText.Text, Is.EqualTo(fixtureName));
                        Assert.That(window.Tabs.Visibility, Is.EqualTo(Visibility.Collapsed));
                    });

                    if (!string.IsNullOrWhiteSpace(expectedFindResetTitle))
                    {
                        await ExecuteHostScriptAsync(
                            webView,
                            """(async () => { await wb.find("Movie201", 0); return true; })();"""
                        );
                        await WaitForWebConditionAsync(
                            webView,
                            "document.getElementById('view') && document.getElementById('view').children.length === 1 && !!document.getElementById('title201') && !document.getElementById('title200')",
                            TimeSpan.FromSeconds(15),
                            $"{fixtureName} の find 再描画完了を待てませんでした。"
                        );

                        string[] resetTitles = await ReadJsonStringArrayValueAsync(
                            webView,
                            "Array.from(document.querySelectorAll('#view [id^=\"title\"]')).map(x => x.textContent || '')"
                        );
                        string resetScoreText = await ReadJsonStringAsync(
                            webView,
                            "document.getElementById('score201') ? document.getElementById('score201').textContent || '' : ''"
                        );

                        Assert.Multiple(() =>
                        {
                            Assert.That(window.MainVM.DbInfo.SearchKeyword, Is.EqualTo("Movie201"));
                            Assert.That(window.MainVM.FilteredMovieRecs.Select(x => x.Movie_Id), Is.EqualTo(new[] { 201L }));
                            Assert.That(resetTitles, Is.EqualTo(new[] { expectedFindResetTitle }));
                            Assert.That(resetScoreText, Is.EqualTo(expectedFindResetScoreText));
                        });
                    }
                }
                finally
                {
                    await CloseWindowAsync(window);
                }

                return null;
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(dbPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(thumbFolderPath);
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<SimpleGridDomSnapshot> ReadSimpleGridSnapshotAsync(WebView2 webView)
    {
        string json = await ReadJsonStringAsync(
            webView,
            """
            JSON.stringify({
              itemCount: document.querySelectorAll('#view .card').length,
              firstTitle: document.querySelector('#view .card .card__title') ? document.querySelector('#view .card .card__title').textContent : '',
              lastTitle: document.querySelectorAll('#view .card .card__title').length > 0 ? document.querySelectorAll('#view .card .card__title')[document.querySelectorAll('#view .card .card__title').length - 1].textContent : '',
              resultCountText: document.getElementById('resultCount') ? document.getElementById('resultCount').textContent : '',
              statusText: document.getElementById('status') ? document.getElementById('status').textContent : '',
              loadMoreVisible: document.getElementById('loadMoreButton') ? getComputedStyle(document.getElementById('loadMoreButton')).display !== 'none' : false
            })
            """
        );
        using JsonDocument document = JsonDocument.Parse(json);
        return new SimpleGridDomSnapshot(
            document.RootElement.GetProperty("itemCount").GetInt32(),
            document.RootElement.GetProperty("firstTitle").GetString() ?? "",
            document.RootElement.GetProperty("lastTitle").GetString() ?? "",
            document.RootElement.GetProperty("resultCountText").GetString() ?? "",
            document.RootElement.GetProperty("statusText").GetString() ?? "",
            document.RootElement.GetProperty("loadMoreVisible").GetBoolean()
        );
    }

    private static MainWindow CreateHiddenMainWindow()
    {
        return new MainWindow
        {
            Left = -20000,
            Top = -20000,
            Width = 960,
            Height = 720,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
            WindowStartupLocation = WindowStartupLocation.Manual,
            SkipMainWindowClosingSideEffectsForTesting = true,
        };
    }

    private static HostPresentationSnapshot CaptureSnapshot(
        MainWindow window,
        HostPresentationEvent appliedEvent
    )
    {
        return new HostPresentationSnapshot(
            appliedEvent,
            window.Tabs.Visibility,
            window.ExternalSkinHostPresenter.Visibility,
            window.ExternalSkinHostPresenter.Content,
            window.MainHeaderStandardChromePanel.Visibility,
            window.ExternalSkinMinimalChromePanel.Visibility,
            window.ExternalSkinMinimalSkinNameText.Text ?? "",
            window.ExternalSkinFallbackNoticeBorder.Visibility,
            window.ExternalSkinFallbackNoticeText.Text ?? "",
            window.ExternalSkinFallbackNoticeBorder.ToolTip as string ?? "",
            window.ExternalSkinFallbackOpenRuntimeDownloadButton.Visibility
        );
    }

    private static WhiteBrowserSkinDefinition[] GetExternalSkinDefinitions(MainWindow window)
    {
        return window.GetAvailableSkinDefinitions()
            .Where(x => x?.RequiresWebView2 == true)
            .ToArray();
    }

    private static async Task CloseWindowAsync(MainWindow window)
    {
        if (window == null)
        {
            return;
        }

        if (window.IsLoaded)
        {
            window.ExternalSkinRootPathForTesting = "";
            window.ExternalSkinHostPrepareAsyncForTesting = null;
            window.ExternalSkinHostPrepareResultAsyncForTesting = null;
            window.ExternalSkinFallbackOpenLogActionForTesting = null;
            window.ExternalSkinFallbackOpenRuntimeDownloadActionForTesting = null;
            window.ExternalSkinHostPresentationAppliedForTesting = null;
            window.Close();
            await WaitForDispatcherIdleAsync();
        }
    }

    private static async Task WaitForDispatcherIdleAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Yield();
    }

    private static async Task<T> WaitAsync<T>(Task<T> task, TimeSpan timeout, string timeoutMessage)
    {
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        if (!ReferenceEquals(completedTask, task))
        {
            throw new AssertionException(timeoutMessage);
        }

        return await task;
    }

    private static async Task WaitAsync(Task task, TimeSpan timeout, string timeoutMessage)
    {
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        if (!ReferenceEquals(completedTask, task))
        {
            throw new AssertionException(timeoutMessage);
        }

        await task;
    }

    private static WhiteBrowserSkinApiService GetExternalSkinApiService(MainWindow window)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "GetOrCreateExternalSkinApiService",
            BindingFlags.Instance | BindingFlags.NonPublic
        ) ?? throw new AssertionException("外部 skin API service の取得メソッドが見つかりません。");
        return (WhiteBrowserSkinApiService)(method.Invoke(window, []) ?? throw new AssertionException(
            "外部 skin API service を取得できませんでした。"
        ));
    }

    private static async Task HandleApiAsync(
        WhiteBrowserSkinApiService service,
        string method,
        string payloadJson
    )
    {
        using JsonDocument document = JsonDocument.Parse(payloadJson);
        WhiteBrowserSkinApiInvocationResult result = await service.HandleAsync(
            method,
            document.RootElement
        );
        Assert.That(result.Succeeded, Is.True, $"{method} が失敗しました: {result.ErrorMessage}");
    }

    private static async Task<JsonDocument> GetFindInfoPayloadAsync(WhiteBrowserSkinApiService service)
    {
        using JsonDocument request = JsonDocument.Parse("""{}""");
        WhiteBrowserSkinApiInvocationResult result = await service.HandleAsync(
            "getFindInfo",
            request.RootElement
        );
        Assert.That(result.Succeeded, Is.True, $"getFindInfo が失敗しました: {result.ErrorMessage}");
        return JsonDocument.Parse(JsonSerializer.Serialize(result.Payload));
    }

    private static Task<T> RunOnStaDispatcherAsync<T>(Func<Task<T>> action)
    {
        return RunOnSharedUiThreadAsync(action);
    }

    private sealed class TestEnvironmentScope : IDisposable
    {
        private readonly bool originalAutoOpen;
        private readonly bool originalConfirmExit;
        private readonly string originalLastDoc;
        private readonly System.Drawing.Point originalMainLocation;
        private readonly System.Drawing.Size originalMainSize;
        private readonly int originalEverythingIntegrationMode;
        private readonly string originalThemeMode;
        private readonly StringCollection originalRecentFiles;
        private readonly string originalCurrentDirectory;
        private readonly string isolatedCurrentDirectory;

        private TestEnvironmentScope(
            bool originalAutoOpen,
            bool originalConfirmExit,
            string originalLastDoc,
            System.Drawing.Point originalMainLocation,
            System.Drawing.Size originalMainSize,
            int originalEverythingIntegrationMode,
            string originalThemeMode,
            StringCollection originalRecentFiles,
            string originalCurrentDirectory,
            string isolatedCurrentDirectory
        )
        {
            this.originalAutoOpen = originalAutoOpen;
            this.originalConfirmExit = originalConfirmExit;
            this.originalLastDoc = originalLastDoc;
            this.originalMainLocation = originalMainLocation;
            this.originalMainSize = originalMainSize;
            this.originalEverythingIntegrationMode = originalEverythingIntegrationMode;
            this.originalThemeMode = originalThemeMode;
            this.originalRecentFiles = originalRecentFiles;
            this.originalCurrentDirectory = originalCurrentDirectory;
            this.isolatedCurrentDirectory = isolatedCurrentDirectory;
        }

        public static TestEnvironmentScope Create()
        {
            string originalCurrentDirectory = Environment.CurrentDirectory;
            string isolatedCurrentDirectory = Path.Combine(
                Path.GetTempPath(),
                $"imm-mainwindow-webviewskin-{Guid.NewGuid():N}"
            );
            Directory.CreateDirectory(isolatedCurrentDirectory);
            Environment.CurrentDirectory = isolatedCurrentDirectory;

            IndigoMovieManager.Properties.Settings settings = IndigoMovieManager.Properties.Settings.Default;
            StringCollection originalRecentFiles = CloneStringCollection(settings.RecentFiles);
            TestEnvironmentScope scope = new(
                settings.AutoOpen,
                settings.ConfirmExit,
                settings.LastDoc ?? "",
                settings.MainLocation,
                settings.MainSize,
                settings.EverythingIntegrationMode,
                settings.ThemeMode ?? "",
                originalRecentFiles,
                originalCurrentDirectory,
                isolatedCurrentDirectory
            );

            settings.AutoOpen = false;
            settings.ConfirmExit = false;
            settings.LastDoc = "";
            settings.EverythingIntegrationMode = 0;
            settings.ThemeMode = "Original";
            settings.MainLocation = new System.Drawing.Point(10, 10);
            settings.MainSize = new System.Drawing.Size(960, 720);
            settings.RecentFiles = new StringCollection();

            return scope;
        }

        public void Dispose()
        {
            IndigoMovieManager.Properties.Settings settings = IndigoMovieManager.Properties.Settings.Default;
            settings.AutoOpen = originalAutoOpen;
            settings.ConfirmExit = originalConfirmExit;
            settings.LastDoc = originalLastDoc;
            settings.MainLocation = originalMainLocation;
            settings.MainSize = originalMainSize;
            settings.EverythingIntegrationMode = originalEverythingIntegrationMode;
            settings.ThemeMode = originalThemeMode;
            settings.RecentFiles = CloneStringCollection(originalRecentFiles);

            Environment.CurrentDirectory = originalCurrentDirectory;
            WhiteBrowserSkinTestData.DeleteDirectorySafe(isolatedCurrentDirectory);
        }

        private static StringCollection CloneStringCollection(StringCollection source)
        {
            StringCollection clone = new();
            if (source == null)
            {
                return clone;
            }

            clone.AddRange(source.Cast<string>().ToArray());
            return clone;
        }
    }

    private static async Task<T> RunOnSharedUiThreadAsync<T>(Func<Task<T>> action)
    {
        await WaitAsync(
            EnsureSharedUiThreadReadyAsync(),
            TimeSpan.FromSeconds(10),
            "共有 UI スレッドの初期化が 10 秒以内に完了しませんでした。"
        );

        Dispatcher dispatcher = uiDispatcher
            ?? throw new AssertionException("共有 UI Dispatcher が初期化されていません。");
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        DispatcherOperation ignoredOperation = dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(() =>
            {
                Task ignored = ExecuteActionAsync();
            })
        );

        return await completion.Task;

        async Task ExecuteActionAsync()
        {
            try
            {
                T result = await action();
                completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }
    }

    private static Task EnsureSharedUiThreadReadyAsync()
    {
        lock (UiThreadSync)
        {
            if (uiThreadReady?.Task != null)
            {
                return uiThreadReady.Task;
            }

            uiThreadReady = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            uiThread = new Thread(
                () =>
                {
                    try
                    {
                        SynchronizationContext.SetSynchronizationContext(
                            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher)
                        );
                        uiDispatcher = Dispatcher.CurrentDispatcher;
                        InitializeSharedUiApplication();
                        uiThreadReady.TrySetResult(true);
                        Dispatcher.Run();
                    }
                    catch (Exception ex)
                    {
                        uiThreadReady.TrySetException(ex);
                    }
                }
            );
            uiThread.IsBackground = true;
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            return uiThreadReady.Task;
        }
    }

    private static void InitializeSharedUiApplication()
    {
        if (Application.ResourceAssembly == null)
        {
            Application.ResourceAssembly = typeof(MainWindow).Assembly;
        }

        if (Application.Current == null)
        {
            Application application = new()
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            application.Resources.MergedDictionaries.Add(
                new BundledTheme
                {
                    BaseTheme = BaseTheme.Inherit,
                    PrimaryColor = PrimaryColor.Indigo,
                    SecondaryColor = SecondaryColor.DeepPurple,
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Button.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.CheckBox.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.ComboBox.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.DataGrid.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.GroupBox.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.ListView.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Slider.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.TextBox.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/IndigoMovieManager;component/Themes/Generic.xaml",
                        UriKind.Absolute
                    ),
                }
            );
        }

        IndigoMovieManager.Properties.Settings.Default.ThemeMode = "Original";
        App.ApplyTheme(IndigoMovieManager.Properties.Settings.Default.ThemeMode);
    }

    private static void TryDeleteFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // 一時DBの掃除失敗は本体判定を優先する。
        }
    }

    private sealed record HostPresentationEvent(int Generation, string Reason, bool HostReady);

    private sealed record HostPresentationSnapshot(
        HostPresentationEvent Applied,
        Visibility TabsVisibility,
        Visibility PresenterVisibility,
        object PresenterContent,
        Visibility StandardChromeVisibility,
        Visibility MinimalChromeVisibility,
        string MinimalSkinName,
        Visibility FallbackNoticeVisibility,
        string FallbackNoticeText,
        string FallbackNoticeToolTip,
        Visibility FallbackRuntimeDownloadButtonVisibility
    );

    private sealed record RacePresentationSnapshot(
        int PrepareCallCount,
        HostPresentationEvent[] AppliedEvents,
        HostPresentationEvent LatestApplied,
        Visibility TabsVisibility,
        Visibility PresenterVisibility,
        object PresenterContent,
        Visibility StandardChromeVisibility,
        Visibility MinimalChromeVisibility
    );

    private sealed record GridReturnSnapshot(
        HostPresentationEvent Applied,
        string SkinName,
        Visibility TabsVisibility,
        Visibility PresenterVisibility,
        Visibility StandardChromeVisibility,
        Visibility MinimalChromeVisibility
    );

    private sealed record ReloadPresentationSnapshot(
        int PrepareCallCount,
        HostPresentationSnapshot Snapshot
    );

    private sealed record TutorialCallbackGridDomSnapshot(
        int ItemCount,
        string TitleText,
        string FocusedImageClass,
        string SelectedThumbClass
    );

    private sealed record WhiteBrowserDefaultListDomSnapshot(
        int ItemCount,
        string TitleText,
        string SizeText,
        string LengthText,
        string ScrollElementId
    );

    private sealed record SimpleGridDomSnapshot(
        int ItemCount,
        string FirstTitle,
        string LastTitle,
        string ResultCountText,
        string StatusText,
        bool LoadMoreVisible
    );
}
