using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
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

    private static async Task<string> ReadJsonStringAsync(WebView2 webView, string script)
    {
        string resultJson = await webView.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<string>(resultJson) ?? "";
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
