using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using IndigoMovieManager.Skin.Host;
using IndigoMovieManager.Skin.Runtime;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class WhiteBrowserSkinRuntimeBridgeIntegrationTests
{
    [Test]
    public async Task ExternalThumbnailRoute_実WebView2で200_403_404とヘッダーを返せる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-webview2");

        try
        {
            RuntimeBridgeVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyExternalThumbnailResponsesAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.OkResponse.StatusCode, Is.EqualTo(200));
                Assert.That(result.OkResponse.ReasonPhrase, Is.EqualTo("OK"));
                Assert.That(result.OkResponse.ContentType, Does.StartWith("image/png"));
                Assert.That(result.OkResponse.CacheControl, Does.Contain("no-store"));
                Assert.That(result.OkResponse.BodyLength, Is.GreaterThan(0));

                Assert.That(result.ForbiddenResponse.StatusCode, Is.EqualTo(403));
                Assert.That(result.ForbiddenResponse.ReasonPhrase, Is.EqualTo("Forbidden"));
                Assert.That(result.ForbiddenResponse.CacheControl, Does.Contain("no-store"));

                Assert.That(result.MissingResponse.StatusCode, Is.EqualTo(404));
                Assert.That(result.MissingResponse.ReasonPhrase, Is.EqualTo("Not Found"));
                Assert.That(result.MissingResponse.CacheControl, Does.Contain("no-store"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task HandleSkinLeaveAsync_実WebView2でclear_leaveを一度だけ返せる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-lifecycle");

        try
        {
            RuntimeBridgeLifecycleVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyHandleSkinLeaveAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.That(
                result.LifecycleEvents,
                Is.EqualTo(["focus:90:false", "select:90:false", "clear", "leave"])
            );
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TutorialCallbackGrid_実WebView2で初回update_focus_leave_clearまで流せる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-tutorial-grid");

        try
        {
            TutorialCallbackGridVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyTutorialCallbackGridAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.UpdateRequestCount, Is.EqualTo(2));
                Assert.That(result.UpdateStartIndex, Is.EqualTo(0));
                Assert.That(result.UpdateCount, Is.EqualTo(200));
                Assert.That(result.FirstFocusRequestMovieId, Is.EqualTo(42));
                Assert.That(result.SecondFocusRequestMovieId, Is.EqualTo(84));
                Assert.That(result.ItemCountBeforeLeave, Is.EqualTo(2));
                Assert.That(result.TitleTextBeforeLeave, Is.EqualTo("Alpha.mp4"));
                Assert.That(result.FocusedImageClassBeforeLeave, Is.EqualTo("img_base img_f"));
                Assert.That(result.SelectedThumbClassBeforeLeave, Is.EqualTo("thum_base thum_s"));
                Assert.That(result.ItemCountAfterRefresh, Is.EqualTo(1));
                Assert.That(result.TitleTextAfterRefresh, Is.EqualTo("Gamma.mkv"));
                Assert.That(result.FocusedImageClassAfterRefresh, Is.EqualTo("img_base img_f"));
                Assert.That(result.SelectedThumbClassAfterRefresh, Is.EqualTo("thum_base thum_s"));
                Assert.That(result.ItemCountAfterLeave, Is.EqualTo(0));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task WhiteBrowserDefaultList_実WebView2でdefault_onUpdateとscroll_list描画を流せる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-default-list");

        try
        {
            WhiteBrowserDefaultListVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyWhiteBrowserDefaultListAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.UpdateRequestCount, Is.EqualTo(1));
                Assert.That(result.UpdateStartIndex, Is.EqualTo(0));
                Assert.That(result.UpdateCount, Is.EqualTo(200));
                Assert.That(result.ItemCountBeforeLeave, Is.EqualTo(2));
                Assert.That(result.TitleTextBeforeLeave, Is.EqualTo("Beta.avi"));
                Assert.That(result.SizeTextBeforeLeave, Is.EqualTo("2.0 GB"));
                Assert.That(result.LengthTextBeforeLeave, Is.EqualTo("01:23:45"));
                Assert.That(result.ScrollElementId, Is.EqualTo("scroll"));
                Assert.That(result.ItemCountAfterLeave, Is.EqualTo(0));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task WhiteBrowserDefaultGrid_実WebView2でdefault_onUpdateとgrid描画を流せる()
    {
        SimpleWhiteBrowserDefaultFixtureVerificationResult result = await RunSimpleWhiteBrowserDefaultFixtureAsync(
            "imm-wbskin-runtimebridge-default-grid",
            "WhiteBrowserDefaultGrid",
            expectedTitleText: "Beta.avi",
            expectedSelectedClass: "thum_select"
        );

        AssertSimpleDefaultFixture(result);
    }

    [Test]
    public async Task WhiteBrowserDefaultSmall_実WebView2でscore付きsmall描画を流せる()
    {
        SimpleWhiteBrowserDefaultFixtureVerificationResult result = await RunSimpleWhiteBrowserDefaultFixtureAsync(
            "imm-wbskin-runtimebridge-default-small",
            "WhiteBrowserDefaultSmall",
            expectedTitleText: "Beta.avi",
            expectedSelectedClass: "thum_select",
            expectedScoreText: "88.5"
        );

        AssertSimpleDefaultFixture(result);
    }

    [Test]
    public async Task WhiteBrowserDefaultBig_実WebView2でscore付きbig描画を流せる()
    {
        SimpleWhiteBrowserDefaultFixtureVerificationResult result = await RunSimpleWhiteBrowserDefaultFixtureAsync(
            "imm-wbskin-runtimebridge-default-big",
            "WhiteBrowserDefaultBig",
            expectedTitleText: "No.77 : Beta.avi",
            expectedSelectedClass: "thum_select",
            expectedScoreText: "88.5"
        );

        AssertSimpleDefaultFixture(result);
    }

    [Test]
    public async Task TutorialCallbackGridからWhiteBrowserDefaultListへ切替しても旧DOM残骸を残さず描画を切り替えられる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-switch-fixtures");

        try
        {
            FixtureSwitchVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyFixtureSwitchAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.UpdateRequestCount, Is.EqualTo(2));
                Assert.That(result.FirstFixtureItemCount, Is.EqualTo(2));
                Assert.That(result.SecondFixtureItemCount, Is.EqualTo(2));
                Assert.That(result.FirstFixtureTitleText, Is.EqualTo("Alpha.mp4"));
                Assert.That(result.SecondFixtureTitleText, Is.EqualTo("Delta.mpg"));
                Assert.That(result.SecondFixtureScrollExists, Is.True);
                Assert.That(result.SecondFixtureLegacyNodeGone, Is.True);
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task TutorialCallbackGridを同一fixtureで再navigateしてもleave順を返して再描画できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-runtimebridge-renavigate-same-fixture");

        try
        {
            SameFixtureRenavigateVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifySameFixtureRenavigateAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.UpdateRequestCount, Is.EqualTo(2));
                Assert.That(result.FirstFocusRequestMovieId, Is.EqualTo(42));
                Assert.That(result.SecondFocusRequestMovieId, Is.EqualTo(84));
                Assert.That(
                    result.LifecycleEvents,
                    Is.EqualTo(["focus:42:false", "select:42:false", "clear", "leave"])
                );
                Assert.That(result.SecondItemCount, Is.EqualTo(1));
                Assert.That(result.SecondTitleText, Is.EqualTo("Gamma.mkv"));
                Assert.That(result.LegacyNodeGone, Is.True);
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    private static async Task<RuntimeBridgeVerificationResult> VerifyExternalThumbnailResponsesAsync(
        string tempRootPath
    )
    {
        string skinRootPath = Path.Combine(tempRootPath, "skin");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        string okImagePath = Path.Combine(tempRootPath, "external-ok.png");
        string missingImagePath = Path.Combine(tempRootPath, "external-missing.png");
        string forbiddenImagePath = Path.Combine(tempRootPath, "external-forbidden.png");
        Directory.CreateDirectory(skinRootPath);
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);
        CreateSamplePng(okImagePath, 32, 18);

        WhiteBrowserSkinRuntimeBridge runtimeBridge = new();
        Window hostWindow = new()
        {
            Width = 160,
            Height = 120,
            Left = 8,
            Top = 8,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult attachResult =
                await runtimeBridge.TryEnsureAttachedAsync(
                    webView,
                    "RuntimeBridgeTest",
                    userDataFolderPath,
                    skinRootPath,
                    thumbRootPath
                );
            if (!attachResult.Succeeded)
            {
                return attachResult.RuntimeAvailable
                    ? RuntimeBridgeVerificationResult.Failed(
                        $"WebView2 初期化に失敗しました: {attachResult.ErrorType} {attachResult.ErrorMessage}"
                    )
                    : RuntimeBridgeVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため統合確認をスキップします: {attachResult.ErrorMessage}"
                    );
            }

            string okUrl = WhiteBrowserSkinThumbnailUrlCodec.BuildThumbUrl(
                okImagePath,
                thumbRootPath,
                "ok"
            );
            string forbiddenUrl = WhiteBrowserSkinThumbnailUrlCodec.BuildThumbUrl(
                forbiddenImagePath,
                thumbRootPath,
                "forbidden"
            );
            string missingUrl = WhiteBrowserSkinThumbnailUrlCodec.BuildThumbUrl(
                missingImagePath,
                thumbRootPath,
                "missing"
            );
            runtimeBridge.RegisterExternalThumbnailPath(okImagePath);
            runtimeBridge.RegisterExternalThumbnailPath(missingImagePath);

            ConcurrentDictionary<string, ExternalThumbnailResponseSnapshot> responses = new(
                StringComparer.Ordinal
            );
            ConcurrentBag<string> observedThumbnailUrls = [];
            TaskCompletionSource<bool> allResponsesArrived = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            Dictionary<string, string> responseKeysByUrl = new(StringComparer.Ordinal)
            {
                [okUrl] = "ok",
                [forbiddenUrl] = "forbidden",
                [missingUrl] = "missing",
            };

            webView.CoreWebView2.WebResourceResponseReceived += (_, args) =>
            {
                _ = CaptureResponseAsync(args);
            };

            TaskCompletionSource<bool> navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult(true);
                }
                else
                {
                    navigationCompleted.TrySetException(
                        new InvalidOperationException(
                            $"Navigation failed: {args.WebErrorStatus}"
                        )
                    );
                }
            };
            webView.NavigateToString("<html><body>runtime bridge integration</body></html>");

            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return RuntimeBridgeVerificationResult.Failed(
                    "WebView2 の初期 document 読込が 10 秒以内に完了しませんでした。"
                );
            }

            string injectScript =
                $$"""
                (() => {
                  const urls = [
                    {{ToJavaScriptStringLiteral(okUrl)}},
                    {{ToJavaScriptStringLiteral(forbiddenUrl)}},
                    {{ToJavaScriptStringLiteral(missingUrl)}}
                  ];
                  for (const url of urls) {
                    const img = new Image();
                    img.src = url;
                    document.body.appendChild(img);
                  }
                  return urls.length;
                })();
                """;
            string imageProbeResultsJson = await webView.ExecuteScriptAsync(injectScript);

            Task completedTask = await Task.WhenAny(
                allResponsesArrived.Task,
                Task.Delay(TimeSpan.FromSeconds(30))
            );
            if (!ReferenceEquals(completedTask, allResponsesArrived.Task))
            {
                return RuntimeBridgeVerificationResult.Failed(
                    "WebView2 から thum.local 応答を 30 秒以内に回収できませんでした。"
                        + $" probes={imageProbeResultsJson}"
                        + $" observed=[{string.Join(", ", observedThumbnailUrls.OrderBy(x => x, StringComparer.Ordinal))}]"
                );
            }

            return RuntimeBridgeVerificationResult.Succeeded(
                responses["ok"],
                responses["forbidden"],
                responses["missing"]
            );

            async Task CaptureResponseAsync(CoreWebView2WebResourceResponseReceivedEventArgs args)
            {
                if (
                    args?.Request == null
                )
                {
                    return;
                }

                if (
                    string.Equals(
                        new Uri(args.Request.Uri).Host,
                        WhiteBrowserSkinHostPaths.ThumbnailVirtualHostName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    observedThumbnailUrls.Add(args.Request.Uri);
                }

                if (
                    !responseKeysByUrl.TryGetValue(args.Request.Uri, out string? responseKey)
                    || string.IsNullOrWhiteSpace(responseKey)
                )
                {
                    return;
                }

                ExternalThumbnailResponseSnapshot snapshot =
                    await ExternalThumbnailResponseSnapshot.CreateAsync(args.Response);
                responses[responseKey] = snapshot;
                if (
                    responses.ContainsKey("ok")
                    && responses.ContainsKey("forbidden")
                    && responses.ContainsKey("missing")
                )
                {
                    allResponsesArrived.TrySetResult(true);
                }
            }
        }
        finally
        {
            runtimeBridge.Dispose();
            hostWindow.Close();
        }
    }

    private static async Task<TutorialCallbackGridVerificationResult> VerifyTutorialCallbackGridAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateFixtureSkinRootWithCompat("TutorialCallbackGrid");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 20,
            Top = 20,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        int updateRequestCount = 0;
        int updateStartIndex = -1;
        int updateCount = -1;
        int firstFocusRequestMovieId = 0;
        int secondFocusRequestMovieId = 0;
        TaskCompletionSource<bool> firstFocusResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> secondFocusResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            if (string.Equals(e.Method, "update", StringComparison.Ordinal))
            {
                updateRequestCount += 1;
                if (e.Payload.ValueKind == JsonValueKind.Object)
                {
                    if (e.Payload.TryGetProperty("startIndex", out JsonElement startIndexElement))
                    {
                        updateStartIndex = startIndexElement.GetInt32();
                    }

                    if (e.Payload.TryGetProperty("count", out JsonElement countElement))
                    {
                        updateCount = countElement.GetInt32();
                    }
                }

                // 実 fixture が期待する旧 alias 形で返し、onCreateThum と focus 遷移を一気に確認する。
                _ = hostControl.ResolveRequestAsync(
                    e.MessageId,
                    updateRequestCount == 1
                        ? new
                        {
                            items = new object[]
                            {
                                new
                                {
                                    id = 42,
                                    title = "Alpha",
                                    ext = ".mp4",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = true,
                                    select = 0,
                                },
                                new
                                {
                                    id = 77,
                                    title = "Beta",
                                    ext = ".avi",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = false,
                                    select = 0,
                                },
                            },
                        }
                        : new
                        {
                            items = new object[]
                            {
                                new
                                {
                                    id = 84,
                                    title = "Gamma",
                                    ext = ".mkv",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = true,
                                    select = 0,
                                },
                            },
                        }
                );
                return;
            }

            if (string.Equals(e.Method, "focusThum", StringComparison.Ordinal))
            {
                if (e.Payload.ValueKind == JsonValueKind.Object
                    && e.Payload.TryGetProperty("movieId", out JsonElement movieIdElement))
                {
                    int requestedMovieId = movieIdElement.GetInt32();
                    if (!firstFocusResolved.Task.IsCompleted)
                    {
                        firstFocusRequestMovieId = requestedMovieId;
                    }
                    else
                    {
                        secondFocusRequestMovieId = requestedMovieId;
                    }
                }

                _ = hostControl.ResolveRequestAsync(
                    e.MessageId,
                    !firstFocusResolved.Task.IsCompleted
                        ? new
                        {
                            movieId = 42,
                            id = 42,
                            focused = true,
                            focusedMovieId = 42,
                            selected = true,
                        }
                        : new
                        {
                            movieId = 84,
                            id = 84,
                            focused = true,
                            focusedMovieId = 84,
                            selected = true,
                        }
                );
                if (!firstFocusResolved.Task.IsCompleted)
                {
                    firstFocusResolved.TrySetResult(true);
                }
                else
                {
                    secondFocusResolved.TrySetResult(true);
                }
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "TutorialCallbackGrid",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "TutorialCallbackGrid"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? TutorialCallbackGridVerificationResult.Failed(
                        $"TutorialCallbackGrid 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : TutorialCallbackGridVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため TutorialCallbackGrid 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                firstFocusResolved.Task,
                TimeSpan.FromSeconds(10),
                "TutorialCallbackGrid の初回 focus 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view .thum_base').length === 2
                  && document.getElementById('img42')?.className === 'img_base img_f'
                  && document.getElementById('thum42')?.className === 'thum_base thum_s'
                """,
                TimeSpan.FromSeconds(5),
                "TutorialCallbackGrid の初回描画完了を待てませんでした。"
            );

            TutorialCallbackGridDomSnapshot beforeLeave = await ReadTutorialCallbackGridSnapshotAsync(
                webView,
                42
            );

            await webView.ExecuteScriptAsync("wb.update(0, 200);");
            await WaitAsync(
                secondFocusResolved.Task,
                TimeSpan.FromSeconds(10),
                "TutorialCallbackGrid の再 update 後 focus 要求を待てませんでした。"
            );
            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view .thum_base').length === 1
                  && document.getElementById('title84')?.textContent === 'Gamma.mkv'
                  && document.getElementById('img84')?.className === 'img_base img_f'
                  && document.getElementById('thum84')?.className === 'thum_base thum_s'
                  && !document.getElementById('thum42')
                """,
                TimeSpan.FromSeconds(5),
                "TutorialCallbackGrid の再 update 後 clear + 再描画完了を待てませんでした。"
            );

            TutorialCallbackGridDomSnapshot afterRefresh = await ReadTutorialCallbackGridSnapshotAsync(
                webView,
                84
            );

            await hostControl.HandleSkinLeaveAsync();
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#view .thum_base').length === 0",
                TimeSpan.FromSeconds(5),
                "TutorialCallbackGrid の leave 後 clear 完了を待てませんでした。"
            );

            TutorialCallbackGridDomSnapshot afterLeave = await ReadTutorialCallbackGridSnapshotAsync(
                webView,
                84
            );

            return TutorialCallbackGridVerificationResult.Succeeded(
                updateRequestCount,
                updateStartIndex,
                updateCount,
                firstFocusRequestMovieId,
                secondFocusRequestMovieId,
                beforeLeave.ItemCount,
                beforeLeave.TitleText,
                beforeLeave.FocusedImageClass,
                beforeLeave.SelectedThumbClass,
                afterRefresh.ItemCount,
                afterRefresh.TitleText,
                afterRefresh.FocusedImageClass,
                afterRefresh.SelectedThumbClass,
                afterLeave.ItemCount
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<WhiteBrowserDefaultListVerificationResult> VerifyWhiteBrowserDefaultListAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateFixtureSkinRootWithCompat("WhiteBrowserDefaultList");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 24,
            Top = 24,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        int updateRequestCount = 0;
        int updateStartIndex = -1;
        int updateCount = -1;
        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            if (!string.Equals(e.Method, "update", StringComparison.Ordinal))
            {
                return;
            }

            updateRequestCount += 1;
            if (e.Payload.ValueKind == JsonValueKind.Object)
            {
                if (e.Payload.TryGetProperty("startIndex", out JsonElement startIndexElement))
                {
                    updateStartIndex = startIndexElement.GetInt32();
                }

                if (e.Payload.TryGetProperty("count", out JsonElement countElement))
                {
                    updateCount = countElement.GetInt32();
                }
            }

            _ = hostControl.ResolveRequestAsync(
                e.MessageId,
                new
                {
                    items = new object[]
                    {
                        new
                        {
                            id = 42,
                            title = "Alpha",
                            ext = ".mp4",
                            thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                            exist = true,
                            select = 0,
                            size = "1.0 GB",
                            len = "00:10:00",
                        },
                        new
                        {
                            id = 77,
                            title = "Beta",
                            ext = ".avi",
                            thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                            exist = false,
                            select = 1,
                            size = "2.0 GB",
                            len = "01:23:45",
                        },
                    },
                }
            );
            updateResolved.TrySetResult(true);
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                "WhiteBrowserDefaultList",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "WhiteBrowserDefaultList"),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? WhiteBrowserDefaultListVerificationResult.Failed(
                        $"WhiteBrowserDefaultList 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : WhiteBrowserDefaultListVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため WhiteBrowserDefaultList 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                "WhiteBrowserDefaultList の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view tr').length === 2
                  && document.getElementById('title77')?.textContent === 'Beta.avi'
                """,
                TimeSpan.FromSeconds(5),
                "WhiteBrowserDefaultList の初回描画完了を待てませんでした。"
            );

            WhiteBrowserDefaultListDomSnapshot beforeLeave =
                await ReadWhiteBrowserDefaultListSnapshotAsync(webView);

            await hostControl.HandleSkinLeaveAsync();
            await WaitForWebConditionAsync(
                webView,
                "document.querySelectorAll('#view tr').length === 0",
                TimeSpan.FromSeconds(5),
                "WhiteBrowserDefaultList の leave 後 clear 完了を待てませんでした。"
            );

            WhiteBrowserDefaultListDomSnapshot afterLeave =
                await ReadWhiteBrowserDefaultListSnapshotAsync(webView);

            return WhiteBrowserDefaultListVerificationResult.Succeeded(
                updateRequestCount,
                updateStartIndex,
                updateCount,
                beforeLeave.ItemCount,
                beforeLeave.TitleText,
                beforeLeave.SizeText,
                beforeLeave.LengthText,
                beforeLeave.ScrollElementId,
                afterLeave.ItemCount
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<SimpleWhiteBrowserDefaultFixtureVerificationResult> RunSimpleWhiteBrowserDefaultFixtureAsync(
        string tempDirectoryPrefix,
        string fixtureName,
        string expectedTitleText,
        string expectedSelectedClass,
        string expectedScoreText = ""
    )
    {
        string tempRootPath = CreateTempDirectory(tempDirectoryPrefix);

        try
        {
            SimpleWhiteBrowserDefaultFixtureVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifySimpleWhiteBrowserDefaultFixtureAsync(tempRootPath, fixtureName)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.UpdateRequestCount, Is.EqualTo(1));
                Assert.That(result.UpdateStartIndex, Is.EqualTo(0));
                Assert.That(result.UpdateCount, Is.EqualTo(200));
                Assert.That(result.ItemCountBeforeLeave, Is.EqualTo(2));
                Assert.That(result.TitleTextBeforeLeave, Is.EqualTo(expectedTitleText));
                Assert.That(result.SelectedClassBeforeLeave, Is.EqualTo(expectedSelectedClass));
                Assert.That(result.ScoreTextBeforeLeave, Is.EqualTo(expectedScoreText));
                Assert.That(result.ItemCountAfterLeave, Is.EqualTo(0));
            });

            return result;
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    private static void AssertSimpleDefaultFixture(
        SimpleWhiteBrowserDefaultFixtureVerificationResult result
    )
    {
        Assert.That(result, Is.Not.Null);
    }

    private static async Task<SimpleWhiteBrowserDefaultFixtureVerificationResult> VerifySimpleWhiteBrowserDefaultFixtureAsync(
        string tempRootPath,
        string fixtureName
    )
    {
        string skinRootPath = CreateFixtureSkinRootWithCompat(fixtureName);
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 220,
            Height = 180,
            Left = 28,
            Top = 28,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        int updateRequestCount = 0;
        int updateStartIndex = -1;
        int updateCount = -1;
        TaskCompletionSource<bool> updateResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            if (!string.Equals(e.Method, "update", StringComparison.Ordinal))
            {
                return;
            }

            updateRequestCount += 1;
            if (e.Payload.ValueKind == JsonValueKind.Object)
            {
                if (e.Payload.TryGetProperty("startIndex", out JsonElement startIndexElement))
                {
                    updateStartIndex = startIndexElement.GetInt32();
                }

                if (e.Payload.TryGetProperty("count", out JsonElement countElement))
                {
                    updateCount = countElement.GetInt32();
                }
            }

            _ = hostControl.ResolveRequestAsync(
                e.MessageId,
                new
                {
                    items = new object[]
                    {
                        new
                        {
                            id = 42,
                            title = "Alpha",
                            ext = ".mp4",
                            thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                            exist = true,
                            select = 0,
                            score = 11.0,
                        },
                        new
                        {
                            id = 77,
                            title = "Beta",
                            ext = ".avi",
                            thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                            exist = false,
                            select = 1,
                            score = 88.5,
                        },
                    },
                }
            );
            updateResolved.TrySetResult(true);
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult navigateResult = await hostControl.TryNavigateAsync(
                fixtureName,
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, fixtureName),
                thumbRootPath
            );
            if (!navigateResult.Succeeded)
            {
                return navigateResult.RuntimeAvailable
                    ? SimpleWhiteBrowserDefaultFixtureVerificationResult.Failed(
                        $"{fixtureName} 読込に失敗しました: {navigateResult.ErrorType} {navigateResult.ErrorMessage}"
                    )
                    : SimpleWhiteBrowserDefaultFixtureVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため {fixtureName} 統合確認をスキップします: {navigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                updateResolved.Task,
                TimeSpan.FromSeconds(10),
                $"{fixtureName} の初回 update 要求を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view #thum77').length === 1
                  && document.getElementById('title77') != null
                """,
                TimeSpan.FromSeconds(5),
                $"{fixtureName} の初回描画完了を待てませんでした。"
            );

            SimpleWhiteBrowserDefaultFixtureDomSnapshot beforeLeave =
                await ReadSimpleWhiteBrowserDefaultFixtureSnapshotAsync(webView);

            await hostControl.HandleSkinLeaveAsync();
            await WaitForWebConditionAsync(
                webView,
                """
                document.getElementById('view')
                  && document.getElementById('view').children.length === 0
                """,
                TimeSpan.FromSeconds(5),
                $"{fixtureName} の leave 後 clear 完了を待てませんでした。"
            );

            SimpleWhiteBrowserDefaultFixtureDomSnapshot afterLeave =
                await ReadSimpleWhiteBrowserDefaultFixtureSnapshotAsync(webView);

            return SimpleWhiteBrowserDefaultFixtureVerificationResult.Succeeded(
                updateRequestCount,
                updateStartIndex,
                updateCount,
                beforeLeave.ItemCount,
                beforeLeave.TitleText,
                beforeLeave.SelectedClass,
                beforeLeave.ScoreText,
                afterLeave.ItemCount
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<FixtureSwitchVerificationResult> VerifyFixtureSwitchAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateFixtureSkinRootWithCompat(
            "TutorialCallbackGrid",
            "WhiteBrowserDefaultList"
        );
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 240,
            Height = 180,
            Left = 32,
            Top = 32,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        int updateRequestCount = 0;
        TaskCompletionSource<bool> firstFixtureReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> secondFixtureReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> firstFocusResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            if (string.Equals(e.Method, "update", StringComparison.Ordinal))
            {
                updateRequestCount += 1;
                _ = hostControl.ResolveRequestAsync(
                    e.MessageId,
                    updateRequestCount == 1
                        ? new
                        {
                            items = new object[]
                            {
                                new
                                {
                                    id = 42,
                                    title = "Alpha",
                                    ext = ".mp4",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = true,
                                    select = 0,
                                },
                                new
                                {
                                    id = 77,
                                    title = "Beta",
                                    ext = ".avi",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = false,
                                    select = 0,
                                },
                            },
                        }
                        : new
                        {
                            items = new object[]
                            {
                                new
                                {
                                    id = 501,
                                    title = "Gamma",
                                    ext = ".wmv",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = true,
                                    select = 0,
                                    size = "512 MB",
                                    len = "00:20:00",
                                },
                                new
                                {
                                    id = 777,
                                    title = "Delta",
                                    ext = ".mpg",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = true,
                                    select = 1,
                                    size = "4.0 GB",
                                    len = "02:34:56",
                                },
                            },
                        }
                );

                if (updateRequestCount == 2)
                {
                    secondFixtureReady.TrySetResult(true);
                }

                return;
            }

            if (string.Equals(e.Method, "focusThum", StringComparison.Ordinal))
            {
                _ = hostControl.ResolveRequestAsync(
                    e.MessageId,
                    new
                    {
                        movieId = 42,
                        id = 42,
                        focused = true,
                        focusedMovieId = 42,
                        selected = true,
                    }
                );
                firstFocusResolved.TrySetResult(true);
                firstFixtureReady.TrySetResult(true);
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult firstNavigateResult = await hostControl.TryNavigateAsync(
                "TutorialCallbackGrid",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "TutorialCallbackGrid"),
                thumbRootPath
            );
            if (!firstNavigateResult.Succeeded)
            {
                return firstNavigateResult.RuntimeAvailable
                    ? FixtureSwitchVerificationResult.Failed(
                        $"TutorialCallbackGrid 読込に失敗しました: {firstNavigateResult.ErrorType} {firstNavigateResult.ErrorMessage}"
                    )
                    : FixtureSwitchVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため fixture 切替確認をスキップします: {firstNavigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                firstFocusResolved.Task,
                TimeSpan.FromSeconds(10),
                "最初の fixture focus 完了を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view .thum_base').length === 2
                  && document.getElementById('title42')?.textContent === 'Alpha.mp4'
                """,
                TimeSpan.FromSeconds(5),
                "最初の fixture 描画完了を待てませんでした。"
            );
            string firstFixtureTitleText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('title42') ? document.getElementById('title42').textContent : ''"
            );
            int firstFixtureItemCount = await ReadJsonIntAsync(
                webView,
                "document.querySelectorAll('#view .thum_base').length"
            );

            WhiteBrowserSkinHostOperationResult secondNavigateResult = await hostControl.TryNavigateAsync(
                "WhiteBrowserDefaultList",
                userDataFolderPath,
                skinRootPath,
                WhiteBrowserSkinTestData.GetFixtureHtmlPath(skinRootPath, "WhiteBrowserDefaultList"),
                thumbRootPath
            );
            if (!secondNavigateResult.Succeeded)
            {
                return FixtureSwitchVerificationResult.Failed(
                    $"WhiteBrowserDefaultList 読込に失敗しました: {secondNavigateResult.ErrorType} {secondNavigateResult.ErrorMessage}"
                );
            }

            await WaitAsync(
                secondFixtureReady.Task,
                TimeSpan.FromSeconds(10),
                "2つ目の fixture update 完了を待てませんでした。"
            );
            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view tr').length === 2
                  && document.getElementById('title777')?.textContent === 'Delta.mpg'
                  && document.getElementById('scroll') != null
                  && document.getElementById('title42') == null
                """,
                TimeSpan.FromSeconds(5),
                "2つ目の fixture 描画完了を待てませんでした。"
            );

            int secondFixtureItemCount = await ReadJsonIntAsync(
                webView,
                "document.querySelectorAll('#view tr').length"
            );
            string secondFixtureTitleText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('title777') ? document.getElementById('title777').textContent : ''"
            );
            bool secondFixtureScrollExists = await ReadJsonBoolAsync(
                webView,
                "document.getElementById('scroll') != null"
            );
            bool secondFixtureLegacyNodeGone = await ReadJsonBoolAsync(
                webView,
                "document.getElementById('title42') == null"
            );

            return FixtureSwitchVerificationResult.Succeeded(
                updateRequestCount,
                firstFixtureItemCount,
                secondFixtureItemCount,
                firstFixtureTitleText,
                secondFixtureTitleText,
                secondFixtureScrollExists,
                secondFixtureLegacyNodeGone
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<SameFixtureRenavigateVerificationResult> VerifySameFixtureRenavigateAsync(
        string tempRootPath
    )
    {
        string skinRootPath = CreateFixtureSkinRootWithCompat("TutorialCallbackGrid");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        Window hostWindow = new()
        {
            Width = 240,
            Height = 180,
            Left = 36,
            Top = 36,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WhiteBrowserSkinHostControl hostControl = new();
        hostWindow.Content = hostControl;

        int updateRequestCount = 0;
        int firstFocusRequestMovieId = 0;
        int secondFocusRequestMovieId = 0;
        List<string> lifecycleEvents = [];
        TaskCompletionSource<bool> firstFocusResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> secondFocusResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> leaveResolved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        hostControl.WebMessageReceived += (_, e) =>
        {
            if (string.Equals(e.Method, "update", StringComparison.Ordinal))
            {
                updateRequestCount += 1;
                _ = hostControl.ResolveRequestAsync(
                    e.MessageId,
                    updateRequestCount == 1
                        ? new
                        {
                            items = new object[]
                            {
                                new
                                {
                                    id = 42,
                                    title = "Alpha",
                                    ext = ".mp4",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = true,
                                    select = 0,
                                },
                                new
                                {
                                    id = 77,
                                    title = "Beta",
                                    ext = ".avi",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = false,
                                    select = 0,
                                },
                            },
                        }
                        : new
                        {
                            items = new object[]
                            {
                                new
                                {
                                    id = 84,
                                    title = "Gamma",
                                    ext = ".mkv",
                                    thum = "data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=",
                                    exist = true,
                                    select = 0,
                                },
                            },
                        }
                );
                return;
            }

            if (string.Equals(e.Method, "focusThum", StringComparison.Ordinal))
            {
                if (e.Payload.ValueKind == JsonValueKind.Object
                    && e.Payload.TryGetProperty("movieId", out JsonElement movieIdElement))
                {
                    int movieId = movieIdElement.GetInt32();
                    if (!firstFocusResolved.Task.IsCompleted)
                    {
                        firstFocusRequestMovieId = movieId;
                    }
                    else
                    {
                        secondFocusRequestMovieId = movieId;
                    }
                }

                _ = hostControl.ResolveRequestAsync(
                    e.MessageId,
                    !firstFocusResolved.Task.IsCompleted
                        ? new
                        {
                            movieId = 42,
                            id = 42,
                            focused = true,
                            focusedMovieId = 42,
                            selected = true,
                        }
                        : new
                        {
                            movieId = 84,
                            id = 84,
                            focused = true,
                            focusedMovieId = 84,
                            selected = true,
                        }
                );

                if (!firstFocusResolved.Task.IsCompleted)
                {
                    firstFocusResolved.TrySetResult(true);
                }
                else
                {
                    secondFocusResolved.TrySetResult(true);
                }

                return;
            }

            if (string.Equals(e.Method, "probeSequence", StringComparison.Ordinal))
            {
                if (e.Payload.ValueKind == JsonValueKind.Object
                    && e.Payload.TryGetProperty("event", out JsonElement eventElement))
                {
                    string eventName = eventElement.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(eventName))
                    {
                        lifecycleEvents.Add(eventName);
                        if (string.Equals(eventName, "leave", StringComparison.Ordinal))
                        {
                            leaveResolved.TrySetResult(true);
                        }
                    }
                }
            }
        };

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            string tutorialHtmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                "TutorialCallbackGrid"
            );
            WhiteBrowserSkinHostOperationResult firstNavigateResult = await hostControl.TryNavigateAsync(
                "TutorialCallbackGrid",
                userDataFolderPath,
                skinRootPath,
                tutorialHtmlPath,
                thumbRootPath
            );
            if (!firstNavigateResult.Succeeded)
            {
                return firstNavigateResult.RuntimeAvailable
                    ? SameFixtureRenavigateVerificationResult.Failed(
                        $"TutorialCallbackGrid 読込に失敗しました: {firstNavigateResult.ErrorType} {firstNavigateResult.ErrorMessage}"
                    )
                    : SameFixtureRenavigateVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため同一 fixture 再 navigate 確認をスキップします: {firstNavigateResult.ErrorMessage}"
                    );
            }

            await WaitAsync(
                firstFocusResolved.Task,
                TimeSpan.FromSeconds(10),
                "最初の TutorialCallbackGrid focus 完了を待てませんでした。"
            );

            WebView2 webView = (WebView2)(hostControl.FindName("SkinWebView")
                ?? throw new AssertionException("SkinWebView が取得できませんでした。"));

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  const originalFocus = typeof wb.onSetFocus === "function" ? wb.onSetFocus : null;
                  const originalSelect = typeof wb.onSetSelect === "function" ? wb.onSetSelect : null;
                  const originalClear = typeof wb.onClearAll === "function" ? wb.onClearAll : null;
                  const originalLeave = typeof wb.onSkinLeave === "function" ? wb.onSkinLeave : null;
                  function postEvent(name) {
                    chrome.webview.postMessage(JSON.stringify({
                      id: "probe-" + String(Math.random()),
                      method: "probeSequence",
                      payload: { event: name }
                    }));
                  }

                  wb.onSetFocus = function(id, isFocus) {
                    postEvent("focus:" + String(id || 0) + ":" + String(!!isFocus));
                    return originalFocus ? originalFocus.apply(this, arguments) : true;
                  };

                  wb.onSetSelect = function(id, isSel) {
                    postEvent("select:" + String(id || 0) + ":" + String(!!isSel));
                    return originalSelect ? originalSelect.apply(this, arguments) : true;
                  };

                  wb.onClearAll = function() {
                    postEvent("clear");
                    return originalClear ? originalClear.apply(this, arguments) : true;
                  };

                  wb.onSkinLeave = function() {
                    postEvent("leave");
                    return originalLeave ? originalLeave.apply(this, arguments) : true;
                  };

                  return true;
                })();
                """
            );

            WhiteBrowserSkinHostOperationResult secondNavigateResult = await hostControl.TryNavigateAsync(
                "TutorialCallbackGrid",
                userDataFolderPath,
                skinRootPath,
                tutorialHtmlPath,
                thumbRootPath
            );
            if (!secondNavigateResult.Succeeded)
            {
                return SameFixtureRenavigateVerificationResult.Failed(
                    $"TutorialCallbackGrid 再 navigate に失敗しました: {secondNavigateResult.ErrorType} {secondNavigateResult.ErrorMessage}"
                );
            }

            await WaitAsync(
                leaveResolved.Task,
                TimeSpan.FromSeconds(10),
                "再 navigate 時の leave 完了を待てませんでした。"
            );
            await WaitAsync(
                secondFocusResolved.Task,
                TimeSpan.FromSeconds(10),
                "再 navigate 後の focus 完了を待てませんでした。"
            );
            await WaitForWebConditionAsync(
                webView,
                """
                document.querySelectorAll('#view .thum_base').length === 1
                  && document.getElementById('title84')?.textContent === 'Gamma.mkv'
                  && document.getElementById('title42') == null
                """,
                TimeSpan.FromSeconds(5),
                "再 navigate 後の TutorialCallbackGrid 描画完了を待てませんでした。"
            );

            int secondItemCount = await ReadJsonIntAsync(
                webView,
                "document.querySelectorAll('#view .thum_base').length"
            );
            string secondTitleText = await ReadJsonStringAsync(
                webView,
                "document.getElementById('title84') ? document.getElementById('title84').textContent : ''"
            );
            bool legacyNodeGone = await ReadJsonBoolAsync(
                webView,
                "document.getElementById('title42') == null"
            );

            return SameFixtureRenavigateVerificationResult.Succeeded(
                updateRequestCount,
                firstFocusRequestMovieId,
                secondFocusRequestMovieId,
                [.. lifecycleEvents],
                secondItemCount,
                secondTitleText,
                legacyNodeGone
            );
        }
        finally
        {
            hostWindow.Close();
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static async Task<RuntimeBridgeLifecycleVerificationResult> VerifyHandleSkinLeaveAsync(
        string tempRootPath
    )
    {
        string skinRootPath = Path.Combine(tempRootPath, "skin");
        string thumbRootPath = Path.Combine(tempRootPath, "thumb");
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(skinRootPath);
        Directory.CreateDirectory(thumbRootPath);
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            throw new AssertionException($"compat script が見つかりません: {compatScriptPath}");
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        WhiteBrowserSkinRuntimeBridge runtimeBridge = new();
        Window hostWindow = new()
        {
            Width = 180,
            Height = 120,
            Left = 16,
            Top = 16,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
        };
        WebView2 webView = new();
        hostWindow.Content = webView;

        try
        {
            hostWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);

            WhiteBrowserSkinHostOperationResult attachResult =
                await runtimeBridge.TryEnsureAttachedAsync(
                    webView,
                    "RuntimeBridgeLifecycleTest",
                    userDataFolderPath,
                    skinRootPath,
                    thumbRootPath
                );
            if (!attachResult.Succeeded)
            {
                return attachResult.RuntimeAvailable
                    ? RuntimeBridgeLifecycleVerificationResult.Failed(
                        $"WebView2 初期化に失敗しました: {attachResult.ErrorType} {attachResult.ErrorMessage}"
                    )
                    : RuntimeBridgeLifecycleVerificationResult.Ignored(
                        $"WebView2 Runtime 未導入のため lifecycle 統合確認をスキップします: {attachResult.ErrorMessage}"
                    );
            }

            runtimeBridge.WebMessageReceived += (_, e) =>
            {
                if (!string.Equals(e.Method, "focusThum", StringComparison.Ordinal))
                {
                    return;
                }

                _ = runtimeBridge.ResolveRequestAsync(
                    e.MessageId,
                    new
                    {
                        movieId = 90,
                        id = 90,
                        focused = true,
                        focusedMovieId = 90,
                        selected = true,
                    }
                );
            };

            TaskCompletionSource<bool> navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            webView.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult(true);
                }
                else
                {
                    navigationCompleted.TrySetException(
                        new InvalidOperationException(
                            $"Navigation failed: {args.WebErrorStatus}"
                        )
                    );
                }
            };

            webView.NavigateToString(BuildLifecycleHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                throw new AssertionException(
                    "runtime bridge lifecycle harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbDone = false;
                  window.__wbError = "";
                  window.__wbSequence = [];
                  wb.focusThum(90).then(function () {
                    window.__wbSequence = [];
                    window.__wbDone = true;
                  }).catch(function (error) {
                    window.__wbError = String(error && error.message ? error.message : error);
                    window.__wbDone = true;
                  });
                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbDone");

            string error = await ReadJsonStringAsync(webView, "window.__wbError || \"\"");
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            await runtimeBridge.HandleSkinLeaveAsync();
            await runtimeBridge.HandleSkinLeaveAsync();

            string lifecycleJson = await ReadJsonStringAsync(
                webView,
                "JSON.stringify(window.__wbSequence)"
            );
            return RuntimeBridgeLifecycleVerificationResult.Succeeded(
                DeserializeStringArray(lifecycleJson)
            );
        }
        finally
        {
            runtimeBridge.Dispose();
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static Task<T> RunOnStaDispatcherAsync<T>(Func<Task<T>> action)
    {
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(
            () =>
            {
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher)
                );
                _ = ExecuteAsync();
                Dispatcher.Run();

                async Task ExecuteAsync()
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
                    finally
                    {
                        Dispatcher.CurrentDispatcher.BeginInvokeShutdown(
                            DispatcherPriority.Background
                        );
                    }
                }
            }
        );
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static void CreateSamplePng(string filePath, int width, int height)
    {
        using Bitmap bitmap = new(width, height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.DarkSeaGreen);
        graphics.FillRectangle(Brushes.Crimson, 0, 0, width / 2, height / 2);
        bitmap.Save(filePath, ImageFormat.Png);
    }

    private static string CreateTempDirectory(string prefix)
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private static async Task WaitForWebFlagAsync(WebView2 webView, string flagName)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            string resultJson = await webView.ExecuteScriptAsync($"Boolean(window.{flagName})");
            if (string.Equals(resultJson, "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"WebView2 側の待機フラグ '{flagName}' が立ちませんでした。");
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

        throw new TimeoutException(timeoutMessage);
    }

    private static async Task<string> ReadJsonStringAsync(WebView2 webView, string script)
    {
        string resultJson = await webView.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<string>(resultJson) ?? "";
    }

    private static async Task<int> ReadJsonIntAsync(WebView2 webView, string script)
    {
        string resultJson = await webView.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<int>(resultJson);
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

    private static async Task<SimpleWhiteBrowserDefaultFixtureDomSnapshot> ReadSimpleWhiteBrowserDefaultFixtureSnapshotAsync(
        WebView2 webView
    )
    {
        string json = await ReadJsonStringAsync(
            webView,
            """
            JSON.stringify({
              itemCount: document.getElementById('view') ? document.getElementById('view').children.length : 0,
              titleText: document.getElementById('title77') ? document.getElementById('title77').textContent : '',
              selectedClass: document.getElementById('thum77') ? document.getElementById('thum77').className : '',
              scoreText: document.getElementById('score77') ? document.getElementById('score77').textContent : ''
            })
            """
        );
        using JsonDocument document = JsonDocument.Parse(json);
        return new SimpleWhiteBrowserDefaultFixtureDomSnapshot(
            document.RootElement.GetProperty("itemCount").GetInt32(),
            document.RootElement.GetProperty("titleText").GetString() ?? "",
            document.RootElement.GetProperty("selectedClass").GetString() ?? "",
            document.RootElement.GetProperty("scoreText").GetString() ?? ""
        );
    }

    private static string[] DeserializeStringArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.String)
        {
            string wrapped = document.RootElement.GetString() ?? "[]";
            return JsonSerializer.Deserialize<string[]>(wrapped) ?? [];
        }

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> values = [];
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            values.Add(item.GetString() ?? "");
        }

        return [.. values];
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        string current = TestContext.CurrentContext.TestDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine([current, .. relativeSegments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        return "";
    }

    private static string CreateFixtureSkinRootWithCompat(params string[] fixtureNames)
    {
        return WhiteBrowserSkinTestData.CreateSkinRootCopyWithCompat(
            fixtureNames,
            rewriteHtmlAsShiftJis: true
        );
    }

    private static string BuildLifecycleHarnessHtml(string compatScript)
    {
        return
            $$"""
            <html>
            <head>
              <meta charset="utf-8">
              <script>
                window.__wbDone = false;
                window.__wbError = "";
                window.__wbSequence = [];

                function onSetFocus(id, isFocus) {
                  window.__wbSequence.push("focus:" + String(id || 0) + ":" + String(!!isFocus));
                  return true;
                }

                function onSetSelect(id, isSel) {
                  window.__wbSequence.push("select:" + String(id || 0) + ":" + String(!!isSel));
                  return true;
                }

                function onClearAll() {
                  window.__wbSequence.push("clear");
                  return true;
                }

                function onSkinLeave() {
                  window.__wbSequence.push("leave");
                  return true;
                }
              </script>
              <script>
            {{compatScript}}
              </script>
            </head>
            <body>
              <div id="config">multi-select : 1;</div>
            </body>
            </html>
            """;
    }

    private static string ToJavaScriptStringLiteral(string value)
    {
        string normalized = value ?? "";
        normalized = normalized.Replace("\\", "\\\\");
        normalized = normalized.Replace("'", "\\'");
        return $"'{normalized}'";
    }

    private sealed record RuntimeBridgeVerificationResult(
        string IgnoreReason,
        string FailureMessage,
        ExternalThumbnailResponseSnapshot OkResponse,
        ExternalThumbnailResponseSnapshot ForbiddenResponse,
        ExternalThumbnailResponseSnapshot MissingResponse
    )
    {
        public static RuntimeBridgeVerificationResult Ignored(string reason)
        {
            return new RuntimeBridgeVerificationResult(
                reason,
                "",
                ExternalThumbnailResponseSnapshot.Empty,
                ExternalThumbnailResponseSnapshot.Empty,
                ExternalThumbnailResponseSnapshot.Empty
            );
        }

        public static RuntimeBridgeVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static RuntimeBridgeVerificationResult Succeeded(
            ExternalThumbnailResponseSnapshot okResponse,
            ExternalThumbnailResponseSnapshot forbiddenResponse,
            ExternalThumbnailResponseSnapshot missingResponse
        )
        {
            return new RuntimeBridgeVerificationResult(
                "",
                "",
                okResponse,
                forbiddenResponse,
                missingResponse
            );
        }
    }

    private sealed record RuntimeBridgeLifecycleVerificationResult(
        string IgnoreReason,
        string[] LifecycleEvents
    )
    {
        public static RuntimeBridgeLifecycleVerificationResult Ignored(string reason)
        {
            return new RuntimeBridgeLifecycleVerificationResult(reason, []);
        }

        public static RuntimeBridgeLifecycleVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static RuntimeBridgeLifecycleVerificationResult Succeeded(string[] lifecycleEvents)
        {
            return new RuntimeBridgeLifecycleVerificationResult("", lifecycleEvents);
        }
    }

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

    private sealed record SimpleWhiteBrowserDefaultFixtureDomSnapshot(
        int ItemCount,
        string TitleText,
        string SelectedClass,
        string ScoreText
    );

    private sealed record TutorialCallbackGridVerificationResult(
        string IgnoreReason,
        int UpdateRequestCount,
        int UpdateStartIndex,
        int UpdateCount,
        int FirstFocusRequestMovieId,
        int SecondFocusRequestMovieId,
        int ItemCountBeforeLeave,
        string TitleTextBeforeLeave,
        string FocusedImageClassBeforeLeave,
        string SelectedThumbClassBeforeLeave,
        int ItemCountAfterRefresh,
        string TitleTextAfterRefresh,
        string FocusedImageClassAfterRefresh,
        string SelectedThumbClassAfterRefresh,
        int ItemCountAfterLeave
    )
    {
        public static TutorialCallbackGridVerificationResult Ignored(string reason)
        {
            return new TutorialCallbackGridVerificationResult(
                reason,
                0,
                0,
                0,
                0,
                0,
                0,
                "",
                "",
                "",
                0,
                "",
                "",
                "",
                0
            );
        }

        public static TutorialCallbackGridVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static TutorialCallbackGridVerificationResult Succeeded(
            int updateRequestCount,
            int updateStartIndex,
            int updateCount,
            int firstFocusRequestMovieId,
            int secondFocusRequestMovieId,
            int itemCountBeforeLeave,
            string titleTextBeforeLeave,
            string focusedImageClassBeforeLeave,
            string selectedThumbClassBeforeLeave,
            int itemCountAfterRefresh,
            string titleTextAfterRefresh,
            string focusedImageClassAfterRefresh,
            string selectedThumbClassAfterRefresh,
            int itemCountAfterLeave
        )
        {
            return new TutorialCallbackGridVerificationResult(
                "",
                updateRequestCount,
                updateStartIndex,
                updateCount,
                firstFocusRequestMovieId,
                secondFocusRequestMovieId,
                itemCountBeforeLeave,
                titleTextBeforeLeave,
                focusedImageClassBeforeLeave,
                selectedThumbClassBeforeLeave,
                itemCountAfterRefresh,
                titleTextAfterRefresh,
                focusedImageClassAfterRefresh,
                selectedThumbClassAfterRefresh,
                itemCountAfterLeave
            );
        }
    }

    private sealed record WhiteBrowserDefaultListVerificationResult(
        string IgnoreReason,
        int UpdateRequestCount,
        int UpdateStartIndex,
        int UpdateCount,
        int ItemCountBeforeLeave,
        string TitleTextBeforeLeave,
        string SizeTextBeforeLeave,
        string LengthTextBeforeLeave,
        string ScrollElementId,
        int ItemCountAfterLeave
    )
    {
        public static WhiteBrowserDefaultListVerificationResult Ignored(string reason)
        {
            return new WhiteBrowserDefaultListVerificationResult(
                reason,
                0,
                0,
                0,
                0,
                "",
                "",
                "",
                "",
                0
            );
        }

        public static WhiteBrowserDefaultListVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static WhiteBrowserDefaultListVerificationResult Succeeded(
            int updateRequestCount,
            int updateStartIndex,
            int updateCount,
            int itemCountBeforeLeave,
            string titleTextBeforeLeave,
            string sizeTextBeforeLeave,
            string lengthTextBeforeLeave,
            string scrollElementId,
            int itemCountAfterLeave
        )
        {
            return new WhiteBrowserDefaultListVerificationResult(
                "",
                updateRequestCount,
                updateStartIndex,
                updateCount,
                itemCountBeforeLeave,
                titleTextBeforeLeave,
                sizeTextBeforeLeave,
                lengthTextBeforeLeave,
                scrollElementId,
                itemCountAfterLeave
            );
        }
    }

    private sealed record SimpleWhiteBrowserDefaultFixtureVerificationResult(
        string IgnoreReason,
        int UpdateRequestCount,
        int UpdateStartIndex,
        int UpdateCount,
        int ItemCountBeforeLeave,
        string TitleTextBeforeLeave,
        string SelectedClassBeforeLeave,
        string ScoreTextBeforeLeave,
        int ItemCountAfterLeave
    )
    {
        public static SimpleWhiteBrowserDefaultFixtureVerificationResult Ignored(string reason)
        {
            return new SimpleWhiteBrowserDefaultFixtureVerificationResult(
                reason,
                0,
                0,
                0,
                0,
                "",
                "",
                "",
                0
            );
        }

        public static SimpleWhiteBrowserDefaultFixtureVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static SimpleWhiteBrowserDefaultFixtureVerificationResult Succeeded(
            int updateRequestCount,
            int updateStartIndex,
            int updateCount,
            int itemCountBeforeLeave,
            string titleTextBeforeLeave,
            string selectedClassBeforeLeave,
            string scoreTextBeforeLeave,
            int itemCountAfterLeave
        )
        {
            return new SimpleWhiteBrowserDefaultFixtureVerificationResult(
                "",
                updateRequestCount,
                updateStartIndex,
                updateCount,
                itemCountBeforeLeave,
                titleTextBeforeLeave,
                selectedClassBeforeLeave,
                scoreTextBeforeLeave,
                itemCountAfterLeave
            );
        }
    }

    private sealed record FixtureSwitchVerificationResult(
        string IgnoreReason,
        int UpdateRequestCount,
        int FirstFixtureItemCount,
        int SecondFixtureItemCount,
        string FirstFixtureTitleText,
        string SecondFixtureTitleText,
        bool SecondFixtureScrollExists,
        bool SecondFixtureLegacyNodeGone
    )
    {
        public static FixtureSwitchVerificationResult Ignored(string reason)
        {
            return new FixtureSwitchVerificationResult(reason, 0, 0, 0, "", "", false, false);
        }

        public static FixtureSwitchVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static FixtureSwitchVerificationResult Succeeded(
            int updateRequestCount,
            int firstFixtureItemCount,
            int secondFixtureItemCount,
            string firstFixtureTitleText,
            string secondFixtureTitleText,
            bool secondFixtureScrollExists,
            bool secondFixtureLegacyNodeGone
        )
        {
            return new FixtureSwitchVerificationResult(
                "",
                updateRequestCount,
                firstFixtureItemCount,
                secondFixtureItemCount,
                firstFixtureTitleText,
                secondFixtureTitleText,
                secondFixtureScrollExists,
                secondFixtureLegacyNodeGone
            );
        }
    }

    private sealed record SameFixtureRenavigateVerificationResult(
        string IgnoreReason,
        int UpdateRequestCount,
        int FirstFocusRequestMovieId,
        int SecondFocusRequestMovieId,
        string[] LifecycleEvents,
        int SecondItemCount,
        string SecondTitleText,
        bool LegacyNodeGone
    )
    {
        public static SameFixtureRenavigateVerificationResult Ignored(string reason)
        {
            return new SameFixtureRenavigateVerificationResult(reason, 0, 0, 0, [], 0, "", false);
        }

        public static SameFixtureRenavigateVerificationResult Failed(string message)
        {
            throw new AssertionException(message);
        }

        public static SameFixtureRenavigateVerificationResult Succeeded(
            int updateRequestCount,
            int firstFocusRequestMovieId,
            int secondFocusRequestMovieId,
            string[] lifecycleEvents,
            int secondItemCount,
            string secondTitleText,
            bool legacyNodeGone
        )
        {
            return new SameFixtureRenavigateVerificationResult(
                "",
                updateRequestCount,
                firstFocusRequestMovieId,
                secondFocusRequestMovieId,
                lifecycleEvents,
                secondItemCount,
                secondTitleText,
                legacyNodeGone
            );
        }
    }

    private sealed record ExternalThumbnailResponseSnapshot(
        int StatusCode,
        string ReasonPhrase,
        string ContentType,
        string CacheControl,
        int BodyLength
    )
    {
        public static ExternalThumbnailResponseSnapshot Empty { get; } = new(0, "", "", "", 0);

        public static async Task<ExternalThumbnailResponseSnapshot> CreateAsync(
            CoreWebView2WebResourceResponseView response
        )
        {
            if (response == null)
            {
                return Empty;
            }

            string contentType = TryGetHeader(response, "Content-Type");
            string cacheControl = TryGetHeader(response, "Cache-Control");
            int bodyLength = await TryGetBodyLengthAsync(response);

            return new ExternalThumbnailResponseSnapshot(
                response.StatusCode,
                response.ReasonPhrase ?? "",
                contentType,
                cacheControl,
                bodyLength
            );
        }

        private static string TryGetHeader(
            CoreWebView2WebResourceResponseView response,
            string headerName
        )
        {
            try
            {
                return response.Headers.GetHeader(headerName) ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static async Task<int> TryGetBodyLengthAsync(
            CoreWebView2WebResourceResponseView response
        )
        {
            if (response == null)
            {
                return 0;
            }

            try
            {
                using Stream contentStream = await response.GetContentAsync();
                if (contentStream == null)
                {
                    return 0;
                }

                if (contentStream.CanSeek)
                {
                    return checked((int)contentStream.Length);
                }

                using MemoryStream buffer = new();
                await contentStream.CopyToAsync(buffer);
                return checked((int)buffer.Length);
            }
            catch
            {
                return 0;
            }
        }
    }
}
