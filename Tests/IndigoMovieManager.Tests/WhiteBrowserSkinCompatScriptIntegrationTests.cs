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
                    result.LifecycleEvents,
                    Is.EqualTo(["focus:90:false", "select:90:false", "clear", "leave"])
                );
                Assert.That(result.ScrollSucceeded, Is.True);
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

            bool scrollSucceeded = await ExecuteScrollScenarioAsync(webView);

            return CompatScriptVerificationResult.Succeeded(
                ExtractEventList(focusJson, "focus"),
                ExtractEventList(focusJson, "select"),
                ExtractEventList(selectJson, "focus"),
                ExtractEventList(selectJson, "select"),
                JsonSerializer.Deserialize<string[]>(lifecycleJson) ?? [],
                scrollSucceeded
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
            (() => {
              {{startScript}}
              const request = window.__immMessages.shift();
              if (!request) {
                throw new Error("wb request was not captured.");
              }

              window.__immWbCompat.resolve(request.id, {{responseLiteral}});
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
        string[] LifecycleEvents,
        bool ScrollSucceeded,
        string IgnoreReason
    )
    {
        public static CompatScriptVerificationResult Succeeded(
            string[] focusEvents,
            string[] focusSelectionEvents,
            string[] selectFocusEvents,
            string[] selectEvents,
            string[] lifecycleEvents,
            bool scrollSucceeded
        )
        {
            return new CompatScriptVerificationResult(
                focusEvents,
                focusSelectionEvents,
                selectFocusEvents,
                selectEvents,
                lifecycleEvents,
                scrollSucceeded,
                ""
            );
        }

        public static CompatScriptVerificationResult Ignored(string reason)
        {
            return new CompatScriptVerificationResult([], [], [], [], [], false, reason);
        }

        public static CompatScriptVerificationResult Failed(string reason)
        {
            throw new AssertionException(reason);
        }
    }
}
