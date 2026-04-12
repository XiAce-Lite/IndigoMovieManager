using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class WhiteBrowserSkinCompatScriptIntegrationTests
{
    [Test]
    public async Task FocusAndSelectThum_callbackが二重発火しない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-script");

        try
        {
            CompatScriptVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyCompatCallbacksAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.FocusEvents, Is.EqualTo(["42:true"]));
                Assert.That(result.FocusSelectionEvents, Is.EqualTo(["42:true"]));
                Assert.That(result.SelectEvents, Is.EqualTo(["77:true"]));
                Assert.That(result.SelectFocusEvents, Is.Empty);
                Assert.That(
                    result.ThumbnailUpdateEvents,
                    Is.EqualTo(["db-main:77|https://thum.local/sample.jpg?rev=thumb-1|thumb-1|managed-thumbnail|160x120|1x1"])
                );
                Assert.That(result.TagRequests, Is.EqualTo(["addTag:42:idol", "flipTag:77:beta"]));
                Assert.That(result.TagModifyEvents, Is.EqualTo(["idol:true", "beta:true"]));
                Assert.That(
                    result.LifecycleEvents,
                    Is.EqualTo(["focus:90:false", "select:90:false", "clear", "leave"])
                );
                Assert.That(result.ScrollSucceeded, Is.True);
                Assert.That(
                    result.InfoRequestMethods,
                    Is.EqualTo(["getFindInfo", "getFocusThum", "getSelectThums"])
                );
                Assert.That(result.InfoSummary, Is.EqualTo("idol|3|2|42|42,77"));
                Assert.That(
                    result.FilterRequestMethods,
                    Is.EqualTo(["addFilter", "removeFilter", "clearFilter"])
                );
                Assert.That(result.FilterUpdateCounts, Is.EqualTo(["2", "1", "3"]));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task Update系callbackは先頭再更新だけclearを先行できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-reset-view");

        try
        {
            ResetViewVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyResetViewBehaviorAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Sequence,
                    Is.EqualTo(["clear", "update:2", "update:1", "clear", "update:1"])
                );
                Assert.That(
                    result.Methods,
                    Is.EqualTo(["update:0:200", "update:120:80", "find:0:200"])
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task GetInfosはmovieIdsと範囲指定payloadを投げ分けできる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-getinfos");

        try
        {
            GetInfosRequestVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyGetInfosRequestShapesAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.Methods, Is.EqualTo(["getInfos", "getInfos", "getInfos"]));
                Assert.That(
                    result.Payloads,
                    Is.EqualTo(
                        [
                            "{\"movieIds\":[42,77]}",
                            "{\"startIndex\":120,\"count\":200}",
                            "{\"recordKeys\":[\"db-main:42\"]}"
                        ]
                    )
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task 既定onUpdatefallbackはstartIndex付きupdateを追記描画できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-default-append");

        try
        {
            DefaultUpdateAppendVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyDefaultUpdateAppendBehaviorAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Sequence,
                    Is.EqualTo(["clear", "create:1", "create:2", "create:3"])
                );
                Assert.That(
                    result.Methods,
                    Is.EqualTo(["update:0:2", "update:2:1"])
                );
                Assert.That(
                    result.Titles,
                    Is.EqualTo(["Alpha.mp4", "Beta.avi", "Gamma.mkv"])
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task scrollSettingはseamless_scrollでstartIndex付きupdateを自動追記できる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-seamless-scroll");

        try
        {
            SeamlessScrollVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifySeamlessScrollBehaviorAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.Sequence, Is.EqualTo(["clear", "create:1", "create:2", "create:3"]));
                Assert.That(result.Methods, Is.EqualTo(["update:0:2", "update:2:1"]));
                Assert.That(result.Titles, Is.EqualTo(["Alpha.mp4", "Beta.avi", "Gamma.mkv"]));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task seamless_scrollは空振り追記後に再要求しない()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-seamless-stop");

        try
        {
            SeamlessScrollStopVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifySeamlessScrollStopBehaviorAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.Methods, Is.EqualTo(["update:0:2", "update:2:2"]));
                Assert.That(result.Titles, Is.EqualTo(["Alpha.mp4", "Beta.avi"]));
                Assert.That(result.PendingRequestCount, Is.EqualTo(0));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    [Test]
    public async Task 既定thumbfallbackはcallback未実装でも最小表示と差分更新を流せる()
    {
        string tempRootPath = CreateTempDirectory("imm-wbskin-compat-thumb-fallback");

        try
        {
            DefaultThumbnailFallbackVerificationResult result = await RunOnStaDispatcherAsync(
                () => VerifyDefaultThumbnailFallbackAsync(tempRootPath)
            );

            if (!string.IsNullOrWhiteSpace(result.IgnoreReason))
            {
                Assert.Ignore(result.IgnoreReason);
            }

            Assert.Multiple(() =>
            {
                Assert.That(result.CreatedThumbSrc, Is.EqualTo("https://thum.local/original.jpg?rev=thumb-0"));
                Assert.That(result.UpdatedThumbSrc, Is.EqualTo("https://thum.local/updated.jpg?rev=thumb-2"));
                Assert.That(result.TitleText, Is.EqualTo("Beta.avi"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(tempRootPath);
        }
    }

    private static async Task<CompatScriptVerificationResult> VerifyCompatCallbacksAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return CompatScriptVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return CompatScriptVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため compat script 統合確認をスキップします: {ex.Message}"
                );
            }

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

            webView.NavigateToString(BuildHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return CompatScriptVerificationResult.Failed(
                    "compat script の harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await ExecuteScenarioAsync(
                webView,
                "window.__wbResults.focus = []; window.__wbResults.select = []; window.__wbDone = false; wb.focusThum(42);",
                "{ movieId: 42, id: 42, focused: true, focusedMovieId: 42, selected: true }"
            );
            string focusJson = await ReadCompatResultAsync(webView);

            await ExecuteScenarioAsync(
                webView,
                "window.__wbResults.focus = []; window.__wbResults.select = []; window.__wbDone = false; wb.selectThum(77);",
                "{ movieId: 77, id: 77, focused: false, focusedMovieId: 42, selected: true }"
            );
            string selectJson = await ReadCompatResultAsync(webView);

            string lifecycleJson = await ExecuteScriptAndReadJsonAsync(
                webView,
                """
                (() => {
                  window.__immWbCompat.handleClearAll();
                  window.__wbResults.focus = [];
                  window.__wbResults.select = [];
                  window.__wbSequence = [];
                  window.__wbDone = false;
                  wb.focusThum(90);
                  return true;
                })();
                """,
                "{ movieId: 90, id: 90, focused: true, focusedMovieId: 90, selected: true }",
                """
                (() => {
                  window.__wbSequence = [];
                  window.dispatchEvent(new Event("beforeunload"));
                  window.dispatchEvent(new Event("beforeunload"));
                  return JSON.stringify(window.__wbSequence);
                })();
                """
            );

            string tagRequestJson = await ExecuteTagRequestScenarioAsync(webView);
            string thumbnailUpdateJson = await ExecuteThumbnailUpdateCallbackScenarioAsync(webView);
            bool scrollSucceeded = await ExecuteScrollScenarioAsync(webView);
            InfoGetterVerificationResult infoResult = await ExecuteInfoGetterScenarioAsync(webView);
            FilterApiVerificationResult filterResult = await ExecuteFilterApiScenarioAsync(webView);

            return CompatScriptVerificationResult.Succeeded(
                ExtractEventList(focusJson, "focus"),
                ExtractEventList(focusJson, "select"),
                ExtractEventList(selectJson, "focus"),
                ExtractEventList(selectJson, "select"),
                DeserializeStringArray(thumbnailUpdateJson),
                DeserializeStringArray(tagRequestJson),
                await ReadTagModifyEventsAsync(webView),
                DeserializeStringArray(lifecycleJson),
                scrollSucceeded,
                infoResult.RequestMethods,
                infoResult.Summary,
                filterResult.RequestMethods,
                filterResult.UpdateCounts
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<ResetViewVerificationResult> VerifyResetViewBehaviorAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return ResetViewVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return ResetViewVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため reset view 確認をスキップします: {ex.Message}"
                );
            }

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

            webView.NavigateToString(BuildHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return ResetViewVerificationResult.Failed(
                    "reset view harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbResetDone = false;
                  window.__wbResetError = "";
                  window.__wbResetResult = { sequence: [], methods: [] };
                  window.__immMessages = [];
                  window.wb.onClearAll = function () {
                    window.__wbResetResult.sequence.push("clear");
                    return true;
                  };
                  window.wb.onUpdate = function (items) {
                    window.__wbResetResult.sequence.push("update:" + String(Array.isArray(items) ? items.length : 0));
                    return true;
                  };

                  const firstPromise = wb.update(0, 200);
                  const firstRequest = window.__immMessages.shift();
                  if (!firstRequest) {
                    throw new Error("first update request was not captured.");
                  }
                  window.__wbResetResult.methods.push(
                    firstRequest.method + ":" +
                    String(firstRequest.payload.startIndex || 0) + ":" +
                    String(firstRequest.payload.count || 0)
                  );
                  window.__immWbCompat.resolve(firstRequest.id, { items: [{ id: 1 }, { id: 2 }] });

                  firstPromise.then(function () {
                    const secondPromise = wb.update(120, 80);
                    const secondRequest = window.__immMessages.shift();
                    if (!secondRequest) {
                      throw new Error("second update request was not captured.");
                    }
                    window.__wbResetResult.methods.push(
                      secondRequest.method + ":" +
                      String(secondRequest.payload.startIndex || 0) + ":" +
                      String(secondRequest.payload.count || 0)
                    );
                    window.__immWbCompat.resolve(secondRequest.id, { items: [{ id: 3 }] });

                    return secondPromise.then(function () {
                      const thirdPromise = wb.find("idol");
                      const thirdRequest = window.__immMessages.shift();
                      if (!thirdRequest) {
                        throw new Error("find request was not captured.");
                      }

                      const thirdStartIndex = Object.prototype.hasOwnProperty.call(thirdRequest.payload || {}, "startIndex")
                        ? thirdRequest.payload.startIndex
                        : 0;
                      const thirdCount = Object.prototype.hasOwnProperty.call(thirdRequest.payload || {}, "count")
                        ? thirdRequest.payload.count
                        : 0;
                      window.__wbResetResult.methods.push(
                        thirdRequest.method + ":" +
                        String(thirdStartIndex || 0) + ":" +
                        String(thirdCount || 0)
                      );
                      window.__immWbCompat.resolve(thirdRequest.id, { items: [{ id: 9 }] });

                      return thirdPromise.then(function () {
                        window.__wbResetDone = true;
                      });
                    });
                  }).catch(function (error) {
                    window.__wbResetError = String(error && error.message ? error.message : error);
                    window.__wbResetDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbResetDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbResetError ? JSON.stringify(window.__wbResetError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbResetResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            using JsonDocument document = JsonDocument.Parse(json);
            return ResetViewVerificationResult.Succeeded(
                DeserializeStringArray(document.RootElement.GetProperty("sequence").GetRawText()),
                DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText())
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<GetInfosRequestVerificationResult> VerifyGetInfosRequestShapesAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return GetInfosRequestVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return GetInfosRequestVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため getInfos payload 確認をスキップします: {ex.Message}"
                );
            }

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
                        new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}")
                    );
                }
            };

            webView.NavigateToString(BuildHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return GetInfosRequestVerificationResult.Failed(
                    "getInfos harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbGetInfosDone = false;
                  window.__wbGetInfosError = "";
                  window.__wbGetInfosResult = { methods: [], payloads: [] };
                  window.__immMessages = [];

                  const firstPromise = wb.getInfos([42, 77]);
                  const firstRequest = window.__immMessages.shift();
                  if (!firstRequest) {
                    throw new Error("first getInfos request was not captured.");
                  }
                  window.__wbGetInfosResult.methods.push(firstRequest.method);
                  window.__wbGetInfosResult.payloads.push(JSON.stringify(firstRequest.payload || {}));
                  window.__immWbCompat.resolve(firstRequest.id, []);

                  firstPromise.then(function () {
                    const secondPromise = wb.getInfos(120);
                    const secondRequest = window.__immMessages.shift();
                    if (!secondRequest) {
                      throw new Error("second getInfos request was not captured.");
                    }
                    window.__wbGetInfosResult.methods.push(secondRequest.method);
                    window.__wbGetInfosResult.payloads.push(JSON.stringify(secondRequest.payload || {}));
                    window.__immWbCompat.resolve(secondRequest.id, []);

                    return secondPromise.then(function () {
                      const thirdPromise = wb.getInfos({ recordKeys: ["db-main:42"] });
                      const thirdRequest = window.__immMessages.shift();
                      if (!thirdRequest) {
                        throw new Error("third getInfos request was not captured.");
                      }
                      window.__wbGetInfosResult.methods.push(thirdRequest.method);
                      window.__wbGetInfosResult.payloads.push(JSON.stringify(thirdRequest.payload || {}));
                      window.__immWbCompat.resolve(thirdRequest.id, []);

                      return thirdPromise.then(function () {
                        window.__wbGetInfosDone = true;
                      });
                    });
                  }).catch(function (error) {
                    window.__wbGetInfosError = String(error && error.message ? error.message : error);
                    window.__wbGetInfosDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbGetInfosDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbGetInfosError ? JSON.stringify(window.__wbGetInfosError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbGetInfosResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            using JsonDocument document = JsonDocument.Parse(json);
            return GetInfosRequestVerificationResult.Succeeded(
                DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText()),
                DeserializeStringArray(document.RootElement.GetProperty("payloads").GetRawText())
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<DefaultUpdateAppendVerificationResult> VerifyDefaultUpdateAppendBehaviorAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return DefaultUpdateAppendVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return DefaultUpdateAppendVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため default onUpdate append 確認をスキップします: {ex.Message}"
                );
            }

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
                        new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}")
                    );
                }
            };

            webView.NavigateToString(BuildHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return DefaultUpdateAppendVerificationResult.Failed(
                    "default append harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbDefaultAppendDone = false;
                  window.__wbDefaultAppendError = "";
                  window.__wbDefaultAppendResult = { sequence: [], methods: [], titles: [] };
                  window.__immMessages = [];

                  window.wb.onClearAll = function () {
                    window.__wbDefaultAppendResult.sequence.push("clear");
                    document.getElementById("view").innerHTML = "";
                    return true;
                  };

                  delete window.wb.onUpdate;
                  window.wb.onCreateThum = function (mv) {
                    window.__wbDefaultAppendResult.sequence.push("create:" + String(mv && mv.id ? mv.id : 0));
                    var node = document.createElement("div");
                    node.className = "card";
                    node.textContent = String(mv && mv.title ? mv.title : "") + String(mv && mv.ext ? mv.ext : "");
                    document.getElementById("view").appendChild(node);
                    return true;
                  };

                  const firstPromise = wb.update(0, 2);
                  const firstRequest = window.__immMessages.shift();
                  if (!firstRequest) {
                    throw new Error("first update request was not captured.");
                  }
                  window.__wbDefaultAppendResult.methods.push(
                    firstRequest.method + ":" +
                    String(firstRequest.payload.startIndex || 0) + ":" +
                    String(firstRequest.payload.count || 0)
                  );
                  window.__immWbCompat.resolve(firstRequest.id, {
                    items: [
                      { id: 1, title: "Alpha", ext: ".mp4" },
                      { id: 2, title: "Beta", ext: ".avi" }
                    ]
                  });

                  firstPromise.then(function () {
                    const secondPromise = wb.update(2, 1);
                    const secondRequest = window.__immMessages.shift();
                    if (!secondRequest) {
                      throw new Error("second update request was not captured.");
                    }
                    window.__wbDefaultAppendResult.methods.push(
                      secondRequest.method + ":" +
                      String(secondRequest.payload.startIndex || 0) + ":" +
                      String(secondRequest.payload.count || 0)
                    );
                    window.__immWbCompat.resolve(secondRequest.id, {
                      items: [
                        { id: 3, title: "Gamma", ext: ".mkv" }
                      ]
                    });

                    return secondPromise.then(function () {
                      window.__wbDefaultAppendResult.titles = Array.from(
                        document.querySelectorAll("#view .card")
                      ).map(function (node) {
                        return node.textContent || "";
                      });
                      window.__wbDefaultAppendDone = true;
                    });
                  }).catch(function (error) {
                    window.__wbDefaultAppendError = String(error && error.message ? error.message : error);
                    window.__wbDefaultAppendDone = true;
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbDefaultAppendDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbDefaultAppendError ? JSON.stringify(window.__wbDefaultAppendError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbDefaultAppendResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            using JsonDocument document = JsonDocument.Parse(json);
            return DefaultUpdateAppendVerificationResult.Succeeded(
                DeserializeStringArray(document.RootElement.GetProperty("sequence").GetRawText()),
                DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText()),
                DeserializeStringArray(document.RootElement.GetProperty("titles").GetRawText())
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<SeamlessScrollVerificationResult> VerifySeamlessScrollBehaviorAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return SeamlessScrollVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 20,
            Top = 20,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return SeamlessScrollVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため seamless scroll 確認をスキップします: {ex.Message}"
                );
            }

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
                        new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}")
                    );
                }
            };

            webView.NavigateToString(BuildHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return SeamlessScrollVerificationResult.Failed(
                    "seamless scroll harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbSeamlessDone = false;
                  window.__wbSeamlessError = "";
                  window.__wbSeamlessResult = { sequence: [], methods: [], titles: [] };
                  window.__immMessages = [];
                  window.g_thumbs_limit = 2;

                  const scroll = document.getElementById("scroll");
                  scroll.style.height = "120px";
                  scroll.style.overflowY = "auto";
                  const view = document.getElementById("view");
                  view.innerHTML = "";

                  window.wb.onClearAll = function () {
                    window.__wbSeamlessResult.sequence.push("clear");
                    view.innerHTML = "";
                    return true;
                  };

                  delete window.wb.onUpdate;
                  window.wb.onCreateThum = function (mv) {
                    window.__wbSeamlessResult.sequence.push("create:" + String(mv && mv.id ? mv.id : 0));
                    const node = document.createElement("div");
                    node.className = "card";
                    node.style.height = "70px";
                    node.textContent = String(mv && mv.title ? mv.title : "") + String(mv && mv.ext ? mv.ext : "");
                    view.appendChild(node);
                    return true;
                  };

                  wb.scrollSetting(2, "scroll").then(function () {
                    const pumpSecondRequest = function (remaining) {
                      const secondRequest = window.__immMessages.shift();
                      if (secondRequest) {
                        window.__wbSeamlessResult.methods.push(
                          secondRequest.method + ":" +
                          String(secondRequest.payload.startIndex || 0) + ":" +
                          String(secondRequest.payload.count || 0)
                        );
                        window.__immWbCompat.resolve(secondRequest.id, {
                          startIndex: 2,
                          requestedCount: 2,
                          totalCount: 3,
                          items: [{ id: 3, title: "Gamma", ext: ".mkv" }]
                        });

                        setTimeout(function () {
                          window.__wbSeamlessResult.titles = Array.from(
                            document.querySelectorAll("#view .card")
                          ).map(function (node) {
                            return node.textContent || "";
                          });
                          window.__wbSeamlessDone = true;
                        }, 0);
                        return;
                      }

                      if (remaining <= 0) {
                        throw new Error("second seamless update request was not captured.");
                      }

                      setTimeout(function () {
                        try {
                          pumpSecondRequest(remaining - 1);
                        } catch (error) {
                          window.__wbSeamlessError = String(error && error.message ? error.message : error);
                          window.__wbSeamlessDone = true;
                        }
                      }, 20);
                    };

                    try {
                      scroll.scrollTop = scroll.scrollHeight;
                      scroll.dispatchEvent(new Event("scroll"));
                      pumpSecondRequest(25);
                    } catch (error) {
                      window.__wbSeamlessError = String(error && error.message ? error.message : error);
                      window.__wbSeamlessDone = true;
                    }
                  }).catch(function (error) {
                    window.__wbSeamlessError = String(error && error.message ? error.message : error);
                    window.__wbSeamlessDone = true;
                  });

                  const firstRequest = window.__immMessages.shift();
                  if (!firstRequest) {
                    throw new Error("first seamless update request was not captured.");
                  }
                  window.__wbSeamlessResult.methods.push(
                    firstRequest.method + ":" +
                    String(firstRequest.payload.startIndex || 0) + ":" +
                    String(firstRequest.payload.count || 0)
                  );
                  window.__immWbCompat.resolve(firstRequest.id, {
                    startIndex: 0,
                    requestedCount: 2,
                    totalCount: 3,
                    items: [
                      { id: 1, title: "Alpha", ext: ".mp4" },
                      { id: 2, title: "Beta", ext: ".avi" }
                    ]
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbSeamlessDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbSeamlessError ? JSON.stringify(window.__wbSeamlessError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbSeamlessResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            using JsonDocument document = JsonDocument.Parse(json);
            return SeamlessScrollVerificationResult.Succeeded(
                DeserializeStringArray(document.RootElement.GetProperty("sequence").GetRawText()),
                DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText()),
                DeserializeStringArray(document.RootElement.GetProperty("titles").GetRawText())
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task<SeamlessScrollStopVerificationResult> VerifySeamlessScrollStopBehaviorAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return SeamlessScrollStopVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 20,
            Top = 20,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return SeamlessScrollStopVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため seamless scroll 空振り停止確認をスキップします: {ex.Message}"
                );
            }

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
                        new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}")
                    );
                }
            };

            webView.NavigateToString(BuildHarnessHtml(compatScript));
            Task navTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(navTask, navigationCompleted.Task))
            {
                return SeamlessScrollStopVerificationResult.Failed(
                    "seamless scroll 空振り停止 harness 読込が 10 秒以内に完了しませんでした。"
                );
            }

            await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__wbSeamlessStopDone = false;
                  window.__wbSeamlessStopError = "";
                  window.__wbSeamlessStopResult = { methods: [], titles: [], pendingRequestCount: 0 };
                  window.__immMessages = [];
                  window.g_thumbs_limit = 2;

                  const scroll = document.getElementById("scroll");
                  scroll.style.height = "120px";
                  scroll.style.overflowY = "auto";
                  const view = document.getElementById("view");
                  view.innerHTML = "";

                  window.wb.onClearAll = function () {
                    view.innerHTML = "";
                    return true;
                  };

                  delete window.wb.onUpdate;
                  window.wb.onCreateThum = function (mv) {
                    const node = document.createElement("div");
                    node.className = "card";
                    node.style.height = "70px";
                    node.textContent = String(mv && mv.title ? mv.title : "") + String(mv && mv.ext ? mv.ext : "");
                    view.appendChild(node);
                    return true;
                  };

                  wb.scrollSetting(2, "scroll").then(function () {
                    const pumpSecondRequest = function (remaining) {
                      const secondRequest = window.__immMessages.shift();
                      if (secondRequest) {
                        window.__wbSeamlessStopResult.methods.push(
                          secondRequest.method + ":" +
                          String(secondRequest.payload.startIndex || 0) + ":" +
                          String(secondRequest.payload.count || 0)
                        );
                        window.__immWbCompat.resolve(secondRequest.id, {
                          startIndex: 2,
                          requestedCount: 2,
                          totalCount: 4,
                          items: []
                        });

                        setTimeout(function () {
                          try {
                            scroll.scrollTop = scroll.scrollHeight;
                            scroll.dispatchEvent(new Event("scroll"));
                            setTimeout(function () {
                              window.__wbSeamlessStopResult.titles = Array.from(
                                document.querySelectorAll("#view .card")
                              ).map(function (node) {
                                return node.textContent || "";
                              });
                              window.__wbSeamlessStopResult.pendingRequestCount = window.__immMessages.length;
                              window.__wbSeamlessStopDone = true;
                            }, 120);
                          } catch (error) {
                            window.__wbSeamlessStopError = String(error && error.message ? error.message : error);
                            window.__wbSeamlessStopDone = true;
                          }
                        }, 0);
                        return;
                      }

                      if (remaining <= 0) {
                        throw new Error("second seamless stop request was not captured.");
                      }

                      setTimeout(function () {
                        try {
                          pumpSecondRequest(remaining - 1);
                        } catch (error) {
                          window.__wbSeamlessStopError = String(error && error.message ? error.message : error);
                          window.__wbSeamlessStopDone = true;
                        }
                      }, 20);
                    };

                    try {
                      scroll.scrollTop = scroll.scrollHeight;
                      scroll.dispatchEvent(new Event("scroll"));
                      pumpSecondRequest(25);
                    } catch (error) {
                      window.__wbSeamlessStopError = String(error && error.message ? error.message : error);
                      window.__wbSeamlessStopDone = true;
                    }
                  }).catch(function (error) {
                    window.__wbSeamlessStopError = String(error && error.message ? error.message : error);
                    window.__wbSeamlessStopDone = true;
                  });

                  const firstRequest = window.__immMessages.shift();
                  if (!firstRequest) {
                    throw new Error("first seamless stop request was not captured.");
                  }
                  window.__wbSeamlessStopResult.methods.push(
                    firstRequest.method + ":" +
                    String(firstRequest.payload.startIndex || 0) + ":" +
                    String(firstRequest.payload.count || 0)
                  );
                  window.__immWbCompat.resolve(firstRequest.id, {
                    startIndex: 0,
                    requestedCount: 2,
                    totalCount: 4,
                    items: [
                      { id: 1, title: "Alpha", ext: ".mp4" },
                      { id: 2, title: "Beta", ext: ".avi" }
                    ]
                  });

                  return true;
                })();
                """
            );
            await WaitForWebFlagAsync(webView, "__wbSeamlessStopDone");

            string errorJson = await webView.ExecuteScriptAsync(
                "window.__wbSeamlessStopError ? JSON.stringify(window.__wbSeamlessStopError) : \"\""
            );
            string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new AssertionException(error);
            }

            string resultJson = await webView.ExecuteScriptAsync(
                "JSON.stringify(window.__wbSeamlessStopResult)"
            );
            string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
            using JsonDocument document = JsonDocument.Parse(json);
            return SeamlessScrollStopVerificationResult.Succeeded(
                DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText()),
                DeserializeStringArray(document.RootElement.GetProperty("titles").GetRawText()),
                document.RootElement.GetProperty("pendingRequestCount").GetInt32()
            );
        }
        finally
        {
            hostWindow.Close();
            webView.Dispose();
        }
    }

    private static async Task ExecuteScenarioAsync(
        WebView2 webView,
        string startScript,
        string responseLiteral
    )
    {
        string script =
            $$"""
            (async () => {
              window.__immMessages = [];
              {{startScript}}
              const request = window.__immMessages.shift();
              if (!request) {
                throw new Error("wb request was not captured.");
              }

              window.__immWbCompat.resolve(request.id, {{responseLiteral}});
              await Promise.resolve();
              await Promise.resolve();
              window.__wbDone = true;
              return true;
            })();
            """;
        await webView.ExecuteScriptAsync(script);
        await WaitForWebFlagAsync(webView, "__wbDone");
    }

    private static async Task<string> ReadCompatResultAsync(WebView2 webView)
    {
        string resultJson = await webView.ExecuteScriptAsync("JSON.stringify(window.__wbResults)");
        return JsonSerializer.Deserialize<string>(resultJson) ?? "";
    }

    private static async Task<string> ExecuteScriptAndReadJsonAsync(
        WebView2 webView,
        string requestStartScript,
        string responseLiteral,
        string readScript
    )
    {
        await ExecuteScenarioAsync(webView, requestStartScript, responseLiteral);
        string resultJson = await webView.ExecuteScriptAsync(readScript);
        return JsonSerializer.Deserialize<string>(resultJson) ?? "";
    }

    private static async Task<string> ExecuteTagRequestScenarioAsync(WebView2 webView)
    {
        await webView.ExecuteScriptAsync(
            """
            (() => {
              window.__wbTagDone = false;
              window.__wbTagError = "";
              window.__wbTagResult = [];
              window.__immMessages = [];
              window.__wbTagOps = [];

              const focusPromise = wb.focusThum(42);
              const focusRequest = window.__immMessages.shift();
              if (!focusRequest) {
                throw new Error("focusThum request was not captured before tag mutation.");
              }
              window.__immWbCompat.resolve(focusRequest.id, {
                found: true,
                focused: true,
                focusedMovieId: 42,
                movieId: 42,
                id: 42,
                selected: true
              });

              focusPromise.then(function () {
                const addPromise = wb.addTag("idol");
                const addRequest = window.__immMessages.shift();
                if (!addRequest) {
                  throw new Error("addTag request was not captured.");
                }
                window.__immWbCompat.resolve(addRequest.id, {
                  found: true,
                  changed: true,
                  hasTag: true,
                  movieId: 42,
                  id: 42,
                  tag: "idol",
                  item: {
                    MovieId: 42,
                    Tags: ["idol"]
                  }
                });

                return addPromise.then(function () {
                const flipPromise = wb.flipTag("beta", "77");
                const flipRequest = window.__immMessages.shift();
                if (!flipRequest) {
                  throw new Error("flipTag request was not captured.");
                }

                window.__immWbCompat.resolve(flipRequest.id, {
                  found: true,
                  changed: true,
                  hasTag: false,
                  movieId: 77,
                  id: 77,
                  tag: "beta",
                  item: {
                    MovieId: 77,
                    Tags: []
                  }
                });

                return flipPromise.then(function () {
                  window.__wbTagResult = [
                    addRequest.method + ":" + String(addRequest.payload.movieId || 0) + ":" + String(addRequest.payload.tag || ""),
                    flipRequest.method + ":" + String(flipRequest.payload.movieId || 0) + ":" + String(flipRequest.payload.tag || "")
                  ];
                  window.__wbTagDone = true;
                });
                });
              }).catch(function (error) {
                window.__wbTagError = String(error && error.message ? error.message : error);
                window.__wbTagDone = true;
              });

              return true;
            })();
            """
        );
        await WaitForWebFlagAsync(webView, "__wbTagDone");

        string errorJson = await webView.ExecuteScriptAsync(
            "window.__wbTagError ? JSON.stringify(window.__wbTagError) : \"\""
        );
        string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new AssertionException(error);
        }

        string resultJson = await webView.ExecuteScriptAsync(
            "JSON.stringify(window.__wbTagResult)"
        );
        return JsonSerializer.Deserialize<string>(resultJson) ?? "[]";
    }

    private static async Task<string> ExecuteThumbnailUpdateCallbackScenarioAsync(WebView2 webView)
    {
        string resultJson = await webView.ExecuteScriptAsync(
            """
            (() => {
              window.__wbThumbUpdates = [];
              window.__immWbCompat.dispatchCallback("onUpdateThum", {
                recordKey: "db-main:77",
                thumbUrl: "https://thum.local/sample.jpg?rev=thumb-1",
                thumbRevision: "thumb-1",
                thumbSourceKind: "managed-thumbnail",
                sizeInfo: {
                  thumbNaturalWidth: 160,
                  thumbNaturalHeight: 120,
                  thumbSheetColumns: 1,
                  thumbSheetRows: 1,
                  naturalWidth: 160,
                  naturalHeight: 120,
                  sheetColumns: 1,
                  sheetRows: 1
                },
                __immCallArgs: [
                  "db-main:77",
                  "https://thum.local/sample.jpg?rev=thumb-1",
                  "thumb-1",
                  "managed-thumbnail",
                  {
                    thumbNaturalWidth: 160,
                    thumbNaturalHeight: 120,
                    thumbSheetColumns: 1,
                    thumbSheetRows: 1,
                    naturalWidth: 160,
                    naturalHeight: 120,
                    sheetColumns: 1,
                    sheetRows: 1
                  }
                ]
              });
              return JSON.stringify(window.__wbThumbUpdates);
            })();
            """
        );
        return JsonSerializer.Deserialize<string>(resultJson) ?? "[]";
    }

    private static async Task<DefaultThumbnailFallbackVerificationResult> VerifyDefaultThumbnailFallbackAsync(
        string tempRootPath
    )
    {
        string userDataFolderPath = Path.Combine(tempRootPath, "wv2-userdata");
        Directory.CreateDirectory(userDataFolderPath);

        string compatScriptPath = FindRepositoryFile("skin", "Compat", "wblib-compat.js");
        if (string.IsNullOrWhiteSpace(compatScriptPath) || !File.Exists(compatScriptPath))
        {
            return DefaultThumbnailFallbackVerificationResult.Failed(
                $"compat script が見つかりません: {compatScriptPath}"
            );
        }

        string compatScript = File.ReadAllText(compatScriptPath).Replace(
            "</script>",
            "<\\/script>",
            StringComparison.OrdinalIgnoreCase
        );

        Window hostWindow = new()
        {
            Width = 220,
            Height = 160,
            Left = 12,
            Top = 12,
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

            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolderPath
                );
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return DefaultThumbnailFallbackVerificationResult.Ignored(
                    $"WebView2 Runtime 未導入のため compat thumb fallback 統合確認をスキップします: {ex.Message}"
                );
            }

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

            string htmlPath = Path.Combine(tempRootPath, "compat-thumb-fallback.html");
            await File.WriteAllTextAsync(htmlPath, BuildMinimalThumbnailFallbackHarnessHtml(compatScript));
            webView.Source = new Uri(htmlPath);
            await navigationCompleted.Task;

            string createdJson = await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__immWbCompat.handleClearAll();
                  wb.onCreateThum({
                    id: 77,
                    movieId: 77,
                    title: 'Beta',
                    ext: '.avi',
                    thum: 'https://thum.local/original.jpg?rev=thumb-0',
                    exist: true,
                    select: 0
                  }, 1);

                  return JSON.stringify({
                    thumbSrc: document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : '',
                    titleText: document.getElementById('title77') ? (document.getElementById('title77').textContent || '') : ''
                  });
                })();
                """
            );
            string updatedJson = await webView.ExecuteScriptAsync(
                """
                (() => {
                  window.__immWbCompat.dispatchCallback('onUpdateThum', {
                    movieId: 77,
                    id: 77,
                    recordKey: 'db-main:77',
                    thumbUrl: 'https://thum.local/updated.jpg?rev=thumb-2',
                    thum: 'https://thum.local/updated.jpg?rev=thumb-2',
                    thumbRevision: 'thumb-2',
                    thumbSourceKind: 'managed-thumbnail',
                    __immCallArgs: [
                      'db-main:77',
                      'https://thum.local/updated.jpg?rev=thumb-2',
                      'thumb-2',
                      'managed-thumbnail',
                      null
                    ]
                  });

                  return JSON.stringify({
                    thumbSrc: document.getElementById('img77') ? (document.getElementById('img77').getAttribute('src') || '') : ''
                  });
                })();
                """
            );

            using JsonDocument createdDocument = JsonDocument.Parse(
                JsonSerializer.Deserialize<string>(createdJson) ?? "{}"
            );
            using JsonDocument updatedDocument = JsonDocument.Parse(
                JsonSerializer.Deserialize<string>(updatedJson) ?? "{}"
            );

            return DefaultThumbnailFallbackVerificationResult.Succeeded(
                createdDocument.RootElement.GetProperty("thumbSrc").GetString() ?? "",
                updatedDocument.RootElement.GetProperty("thumbSrc").GetString() ?? "",
                createdDocument.RootElement.GetProperty("titleText").GetString() ?? ""
            );
        }
        finally
        {
            hostWindow.Close();
        }
    }

    private static async Task<string[]> ReadTagModifyEventsAsync(WebView2 webView)
    {
        string resultJson = await webView.ExecuteScriptAsync("JSON.stringify(window.__wbTagOps)");
        string json = JsonSerializer.Deserialize<string>(resultJson) ?? "[]";
        return DeserializeStringArray(json);
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

    private static async Task<bool> ExecuteScrollScenarioAsync(WebView2 webView)
    {
        await webView.ExecuteScriptAsync(
            """
            (() => {
              window.__wbScrollDone = false;
              window.__wbScrollResult = null;
              window.__wbScrollTarget = "";
              const scroll = document.getElementById("scroll");
              const view = document.getElementById("view");
              view.innerHTML = "<div style='height:180px'></div><div id='thum77' style='display:block;height:10px;'></div>";
              const target = document.getElementById("thum77");
              scroll.scrollTop = 0;
              view.scrollTop = 0;
              scroll.scrollTo = function (options) {
                window.__wbScrollTarget = "scroll";
                this.scrollTop = options && typeof options.top === "number" ? options.top : 0;
              };
              view.scrollTo = function (options) {
                window.__wbScrollTarget = "view";
                this.scrollTop = options && typeof options.top === "number" ? options.top : 0;
              };
              target.scrollIntoView = undefined;

              wb.scrollSetting(0, "scroll").then(function () {
                return wb.scrollTo(77);
              }).then(function (scrolled) {
                window.__wbScrollResult = {
                  scrolled: scrolled,
                  scrollTop: scroll.scrollTop,
                  target: window.__wbScrollTarget
                };
                window.__wbScrollDone = true;
              });
              return true;
            })();
            """
        );
        await WaitForWebFlagAsync(webView, "__wbScrollDone");

        string resultJson = await webView.ExecuteScriptAsync(
            "JSON.stringify(window.__wbScrollResult)"
        );
        string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("scrolled").GetBoolean()
            && document.RootElement.GetProperty("target").GetString() == "scroll";
    }

    private static async Task<InfoGetterVerificationResult> ExecuteInfoGetterScenarioAsync(
        WebView2 webView
    )
    {
        await webView.ExecuteScriptAsync(
            """
            (() => {
              window.__wbInfoDone = false;
              window.__wbInfoError = "";
              window.__wbInfoResult = { methods: [], summary: "" };
              window.__immMessages = [];

              const findPromise = wb.getFindInfo();
              const findRequest = window.__immMessages.shift();
              if (!findRequest) {
                throw new Error("getFindInfo request was not captured.");
              }

              window.__immWbCompat.resolve(findRequest.id, {
                find: "idol",
                sort: ["ファイル名(昇順)", "#スコア(低い順)"],
                filter: [],
                where: "score >= 80",
                total: 3,
                result: 2
              });

              findPromise.then(function (findInfo) {
                const focusPromise = wb.getFocusThum();
                const focusRequest = window.__immMessages.shift();
                if (!focusRequest) {
                  throw new Error("getFocusThum request was not captured.");
                }

                window.__immWbCompat.resolve(focusRequest.id, 42);

                return focusPromise.then(function (focusId) {
                  const selectPromise = wb.getSelectThums();
                  const selectRequest = window.__immMessages.shift();
                  if (!selectRequest) {
                    throw new Error("getSelectThums request was not captured.");
                  }

                  window.__immWbCompat.resolve(selectRequest.id, [42, 77]);

                  return selectPromise.then(function (selectedIds) {
                    window.__wbInfoResult = {
                      methods: [findRequest.method, focusRequest.method, selectRequest.method],
                      summary:
                        String(findInfo && findInfo.find ? findInfo.find : "") + "|" +
                        String(findInfo && findInfo.total ? findInfo.total : 0) + "|" +
                        String(findInfo && findInfo.result ? findInfo.result : 0) + "|" +
                        String(focusId || 0) + "|" +
                        (Array.isArray(selectedIds) ? selectedIds.join(",") : "")
                    };
                    window.__wbInfoDone = true;
                  });
                });
              }).catch(function (error) {
                window.__wbInfoError = String(error && error.message ? error.message : error);
                window.__wbInfoDone = true;
              });

              return true;
            })();
            """
        );
        await WaitForWebFlagAsync(webView, "__wbInfoDone");

        string errorJson = await webView.ExecuteScriptAsync(
            "window.__wbInfoError ? JSON.stringify(window.__wbInfoError) : \"\""
        );
        string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new AssertionException(error);
        }

        string resultJson = await webView.ExecuteScriptAsync(
            "JSON.stringify(window.__wbInfoResult)"
        );
        string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
        using JsonDocument document = JsonDocument.Parse(json);
        return new InfoGetterVerificationResult(
            DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText()),
            document.RootElement.GetProperty("summary").GetString() ?? ""
        );
    }

    private static async Task<FilterApiVerificationResult> ExecuteFilterApiScenarioAsync(
        WebView2 webView
    )
    {
        await webView.ExecuteScriptAsync(
            """
            (() => {
              window.__wbFilterDone = false;
              window.__wbFilterError = "";
              window.__wbFilterResult = { methods: [], counts: [] };
              window.__immMessages = [];
              window.wb.onUpdate = function (items) {
                window.__wbFilterResult.counts.push(String(Array.isArray(items) ? items.length : 0));
                return true;
              };

              const addPromise = wb.addFilter("idol");
              const addRequest = window.__immMessages.shift();
              if (!addRequest) {
                throw new Error("addFilter request was not captured.");
              }
              window.__immWbCompat.resolve(addRequest.id, {
                items: [{ id: 1 }, { id: 2 }]
              });

              addPromise.then(function () {
                const removePromise = wb.removeFilter("idol");
                const removeRequest = window.__immMessages.shift();
                if (!removeRequest) {
                  throw new Error("removeFilter request was not captured.");
                }
                window.__immWbCompat.resolve(removeRequest.id, {
                  items: [{ id: 2 }]
                });

                return removePromise.then(function () {
                  const clearPromise = wb.clearFilter();
                  const clearRequest = window.__immMessages.shift();
                  if (!clearRequest) {
                    throw new Error("clearFilter request was not captured.");
                  }
                  window.__immWbCompat.resolve(clearRequest.id, {
                    items: [{ id: 1 }, { id: 2 }, { id: 3 }]
                  });

                  return clearPromise.then(function () {
                    window.__wbFilterResult.methods = [
                      addRequest.method,
                      removeRequest.method,
                      clearRequest.method
                    ];
                    window.__wbFilterDone = true;
                  });
                });
              }).catch(function (error) {
                window.__wbFilterError = String(error && error.message ? error.message : error);
                window.__wbFilterDone = true;
              });

              return true;
            })();
            """
        );
        await WaitForWebFlagAsync(webView, "__wbFilterDone");

        string errorJson = await webView.ExecuteScriptAsync(
            "window.__wbFilterError ? JSON.stringify(window.__wbFilterError) : \"\""
        );
        string error = JsonSerializer.Deserialize<string>(errorJson) ?? "";
        if (!string.IsNullOrWhiteSpace(error))
        {
            throw new AssertionException(error);
        }

        string resultJson = await webView.ExecuteScriptAsync(
            "JSON.stringify(window.__wbFilterResult)"
        );
        string json = JsonSerializer.Deserialize<string>(resultJson) ?? "{}";
        using JsonDocument document = JsonDocument.Parse(json);
        return new FilterApiVerificationResult(
            DeserializeStringArray(document.RootElement.GetProperty("methods").GetRawText()),
            DeserializeStringArray(document.RootElement.GetProperty("counts").GetRawText())
        );
    }

    private static async Task WaitForWebFlagAsync(WebView2 webView, string flagName)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            string resultJson = await webView.ExecuteScriptAsync(
                $"Boolean(window.{flagName})"
            );
            if (string.Equals(resultJson, "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"WebView2 側の待機フラグ '{flagName}' が立ちませんでした。");
    }

    private static string[] ExtractEventList(string json, string propertyName)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (
            !document.RootElement.TryGetProperty(propertyName, out JsonElement element)
            || element.ValueKind != JsonValueKind.Array
        )
        {
            return [];
        }

        List<string> values = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 2)
            {
                continue;
            }

            long movieId = item[0].GetInt64();
            bool state = item[1].GetBoolean();
            values.Add($"{movieId}:{state.ToString().ToLowerInvariant()}");
        }

        return [.. values];
    }

    private static string BuildHarnessHtml(string compatScript)
    {
        return
            $$"""
            <html>
            <head>
              <meta charset="utf-8">
              <script>
                window.__immMessages = [];
                window.__wbResults = { focus: [], select: [] };
                window.__wbSequence = [];
                window.__wbDone = false;
                window.chrome = {
                  webview: {
                    postMessage: function (message) {
                      window.__immMessages.push(JSON.parse(message));
                    }
                  }
                };

                function onSetFocus(id, isFocus) {
                  window.__wbResults.focus.push([Number(id || 0), !!isFocus]);
                  window.__wbSequence.push("focus:" + String(id || 0) + ":" + String(!!isFocus));
                  return true;
                }

                function onSetSelect(id, isSel) {
                  window.__wbResults.select.push([Number(id || 0), !!isSel]);
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

                function onModifyTags(payload) {
                  var tagName = payload && payload.tag ? payload.tag : "";
                  window.__wbTagOps = window.__wbTagOps || [];
                  window.__wbTagOps.push(tagName + ":" + String(!!(payload && payload.changed)));
                  return true;
                }

                function onUpdateThum(recordKey, thumbUrl, thumbRevision, thumbSourceKind, sizeInfo) {
                  window.__wbThumbUpdates = window.__wbThumbUpdates || [];
                  var width = sizeInfo && sizeInfo.thumbNaturalWidth ? sizeInfo.thumbNaturalWidth : 0;
                  var height = sizeInfo && sizeInfo.thumbNaturalHeight ? sizeInfo.thumbNaturalHeight : 0;
                  var columns = sizeInfo && sizeInfo.thumbSheetColumns ? sizeInfo.thumbSheetColumns : 0;
                  var rows = sizeInfo && sizeInfo.thumbSheetRows ? sizeInfo.thumbSheetRows : 0;
                  window.__wbThumbUpdates.push(
                    String(recordKey || "") + "|" +
                    String(thumbUrl || "") + "|" +
                    String(thumbRevision || "") + "|" +
                    String(thumbSourceKind || "") + "|" +
                    String(width) + "x" + String(height) + "|" +
                    String(columns) + "x" + String(rows)
                  );
                  return true;
                }
              </script>
              <script>
            {{compatScript}}
              </script>
            </head>
            <body>
              <div id="scroll"><div id="view"><div id="thum77"></div></div></div>
              <div id="config">multi-select : 1; scroll-id : scroll;</div>
            </body>
            </html>
            """;
    }

    private static string BuildMinimalThumbnailFallbackHarnessHtml(string compatScript)
    {
        return
            $$"""
            <html>
            <head>
              <meta charset="utf-8">
              <script>
                window.chrome = {
                  webview: {
                    postMessage: function () {
                    }
                  }
                };
              </script>
              <script>
            {{compatScript}}
              </script>
            </head>
            <body>
              <div id="view"></div>
              <div id="config">multi-select : 1; scroll-id : view;</div>
            </body>
            </html>
            """;
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

            DirectoryInfo parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        return "";
    }

    private static string CreateTempDirectory(string prefix)
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
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

    private sealed record CompatScriptVerificationResult(
        string[] FocusEvents,
        string[] FocusSelectionEvents,
        string[] SelectFocusEvents,
        string[] SelectEvents,
        string[] ThumbnailUpdateEvents,
        string[] TagRequests,
        string[] TagModifyEvents,
        string[] LifecycleEvents,
        bool ScrollSucceeded,
        string[] InfoRequestMethods,
        string InfoSummary,
        string[] FilterRequestMethods,
        string[] FilterUpdateCounts,
        string IgnoreReason
    )
    {
        public static CompatScriptVerificationResult Succeeded(
            string[] focusEvents,
            string[] focusSelectionEvents,
            string[] selectFocusEvents,
            string[] selectEvents,
            string[] thumbnailUpdateEvents,
            string[] tagRequests,
            string[] tagModifyEvents,
            string[] lifecycleEvents,
            bool scrollSucceeded,
            string[] infoRequestMethods,
            string infoSummary,
            string[] filterRequestMethods,
            string[] filterUpdateCounts
        )
        {
            return new CompatScriptVerificationResult(
                focusEvents,
                focusSelectionEvents,
                selectFocusEvents,
                selectEvents,
                thumbnailUpdateEvents,
                tagRequests,
                tagModifyEvents,
                lifecycleEvents,
                scrollSucceeded,
                infoRequestMethods,
                infoSummary,
                filterRequestMethods,
                filterUpdateCounts,
                ""
            );
        }

        public static CompatScriptVerificationResult Ignored(string reason)
        {
            return new CompatScriptVerificationResult(
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                false,
                [],
                "",
                [],
                [],
                reason
            );
        }

        public static CompatScriptVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record InfoGetterVerificationResult(string[] RequestMethods, string Summary);

    private sealed record FilterApiVerificationResult(string[] RequestMethods, string[] UpdateCounts);

    private sealed record GetInfosRequestVerificationResult(
        string[] Methods,
        string[] Payloads,
        string IgnoreReason
    )
    {
        public static GetInfosRequestVerificationResult Succeeded(
            string[] methods,
            string[] payloads
        )
        {
            return new GetInfosRequestVerificationResult(methods, payloads, "");
        }

        public static GetInfosRequestVerificationResult Ignored(string reason)
        {
            return new GetInfosRequestVerificationResult([], [], reason);
        }

        public static GetInfosRequestVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record DefaultUpdateAppendVerificationResult(
        string[] Sequence,
        string[] Methods,
        string[] Titles,
        string IgnoreReason
    )
    {
        public static DefaultUpdateAppendVerificationResult Succeeded(
            string[] sequence,
            string[] methods,
            string[] titles
        )
        {
            return new DefaultUpdateAppendVerificationResult(sequence, methods, titles, "");
        }

        public static DefaultUpdateAppendVerificationResult Ignored(string reason)
        {
            return new DefaultUpdateAppendVerificationResult([], [], [], reason);
        }

        public static DefaultUpdateAppendVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record DefaultThumbnailFallbackVerificationResult(
        string CreatedThumbSrc,
        string UpdatedThumbSrc,
        string TitleText,
        string IgnoreReason
    )
    {
        public static DefaultThumbnailFallbackVerificationResult Succeeded(
            string createdThumbSrc,
            string updatedThumbSrc,
            string titleText
        )
        {
            return new DefaultThumbnailFallbackVerificationResult(
                createdThumbSrc,
                updatedThumbSrc,
                titleText,
                ""
            );
        }

        public static DefaultThumbnailFallbackVerificationResult Ignored(string reason)
        {
            return new DefaultThumbnailFallbackVerificationResult("", "", "", reason);
        }

        public static DefaultThumbnailFallbackVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record SeamlessScrollVerificationResult(
        string[] Sequence,
        string[] Methods,
        string[] Titles,
        string IgnoreReason
    )
    {
        public static SeamlessScrollVerificationResult Succeeded(
            string[] sequence,
            string[] methods,
            string[] titles
        )
        {
            return new SeamlessScrollVerificationResult(sequence, methods, titles, "");
        }

        public static SeamlessScrollVerificationResult Ignored(string reason)
        {
            return new SeamlessScrollVerificationResult([], [], [], reason);
        }

        public static SeamlessScrollVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record SeamlessScrollStopVerificationResult(
        string[] Methods,
        string[] Titles,
        int PendingRequestCount,
        string IgnoreReason
    )
    {
        public static SeamlessScrollStopVerificationResult Succeeded(
            string[] methods,
            string[] titles,
            int pendingRequestCount
        )
        {
            return new SeamlessScrollStopVerificationResult(
                methods,
                titles,
                pendingRequestCount,
                ""
            );
        }

        public static SeamlessScrollStopVerificationResult Ignored(string reason)
        {
            return new SeamlessScrollStopVerificationResult([], [], 0, reason);
        }

        public static SeamlessScrollStopVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }

    private sealed record ResetViewVerificationResult(
        string[] Sequence,
        string[] Methods,
        string IgnoreReason
    )
    {
        public static ResetViewVerificationResult Succeeded(string[] sequence, string[] methods)
        {
            return new ResetViewVerificationResult(sequence, methods, "");
        }

        public static ResetViewVerificationResult Ignored(string reason)
        {
            return new ResetViewVerificationResult([], [], reason);
        }

        public static ResetViewVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }
}
