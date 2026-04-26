using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// WebView2 と C# の境界だけを担当する橋。
    /// API 実装本体は持たず、設定・仮想ホスト・メッセージ往復に絞る。
    /// </summary>
    public sealed class WhiteBrowserSkinRuntimeBridge : IDisposable
    {
        private const string ThumbnailFilterUri = "https://thum.local/*";
        private CoreWebView2 coreWebView2;
        private bool isAttached;
        private string managedThumbnailRootPath = "";
        private readonly HashSet<string> registeredExternalThumbnailPaths =
            new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<WhiteBrowserSkinWebMessageReceivedEventArgs> WebMessageReceived;

        public async Task<WhiteBrowserSkinHostOperationResult> TryEnsureAttachedAsync(
            WebView2 webView,
            string requestedSkinName,
            string userDataFolder,
            string skinRootPath,
            string thumbRootPath
        )
        {
            ArgumentNullException.ThrowIfNull(webView);

            try
            {
                if (webView.CoreWebView2 == null)
                {
                    CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                        browserExecutableFolder: null,
                        userDataFolder: userDataFolder
                    );
                    await webView.EnsureCoreWebView2Async(environment);
                }

                Attach(webView.CoreWebView2, skinRootPath, thumbRootPath);
                return WhiteBrowserSkinHostOperationResult.CreateSuccess(requestedSkinName);
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                return WhiteBrowserSkinHostOperationResult.CreateRuntimeUnavailable(
                    requestedSkinName,
                    ex.Message
                );
            }
            catch (Exception ex)
            {
                return WhiteBrowserSkinHostOperationResult.CreateFailed(requestedSkinName, ex);
            }
        }

        public void Attach(
            CoreWebView2 targetCoreWebView2,
            string skinRootPath,
            string thumbRootPath
        )
        {
            ArgumentNullException.ThrowIfNull(targetCoreWebView2);

            if (isAttached && ReferenceEquals(coreWebView2, targetCoreWebView2))
            {
                managedThumbnailRootPath = thumbRootPath ?? "";
                registeredExternalThumbnailPaths.Clear();
                UpdateVirtualHostMappings(skinRootPath, thumbRootPath);
                return;
            }

            Detach();

            coreWebView2 = targetCoreWebView2;
            managedThumbnailRootPath = thumbRootPath ?? "";
            registeredExternalThumbnailPaths.Clear();
            ConfigureSettings(coreWebView2.Settings);
            UpdateVirtualHostMappings(skinRootPath, thumbRootPath);
            coreWebView2.AddWebResourceRequestedFilter(
                ThumbnailFilterUri,
                CoreWebView2WebResourceContext.Image
            );
            coreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            coreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;
            isAttached = true;
        }

        public Task ExecuteScriptAsync(string script)
        {
            return coreWebView2 == null
                ? Task.CompletedTask
                : coreWebView2.ExecuteScriptAsync(script);
        }

        public Task ResolveRequestAsync(string messageId, object payload)
        {
            return ExecuteCompatDispatchAsync("resolve", messageId, payload);
        }

        public Task RejectRequestAsync(string messageId, string errorMessage)
        {
            return ExecuteCompatDispatchAsync("reject", messageId, errorMessage ?? "");
        }

        public Task DispatchCallbackAsync(string callbackName, object payload)
        {
            return ExecuteCompatDispatchAsync("dispatchCallback", callbackName, payload);
        }

        public Task HandleSkinLeaveAsync()
        {
            return ExecuteCompatDispatchAsync("handleSkinLeave");
        }

        public void RegisterExternalThumbnailPath(string thumbPath)
        {
            string normalizedPath = NormalizePath(thumbPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return;
            }

            registeredExternalThumbnailPaths.Add(normalizedPath);
        }

        public void ClearRegisteredExternalThumbnailPaths()
        {
            registeredExternalThumbnailPaths.Clear();
        }

        public void Dispose()
        {
            Detach();
        }

        private void UpdateVirtualHostMappings(string skinRootPath, string thumbRootPath)
        {
            if (coreWebView2 == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(skinRootPath))
            {
                coreWebView2.SetVirtualHostNameToFolderMapping(
                    WhiteBrowserSkinHostPaths.SkinVirtualHostName,
                    skinRootPath,
                    CoreWebView2HostResourceAccessKind.DenyCors
                );
            }

            // thum.local は bridge が都度応答を返す。
            // 仮想ホスト割り当てを残すと実 WebView2 では WebResourceRequested が発火しない。
            coreWebView2.ClearVirtualHostNameToFolderMapping(
                WhiteBrowserSkinHostPaths.ThumbnailVirtualHostName
            );
        }

        private static void ConfigureSettings(CoreWebView2Settings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.IsScriptEnabled = true;
            settings.AreDefaultScriptDialogsEnabled = false;
            settings.IsWebMessageEnabled = true;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
        }

        private void CoreWebView2_WebMessageReceived(
            object sender,
            CoreWebView2WebMessageReceivedEventArgs e
        )
        {
            string rawJson = e.TryGetWebMessageAsString() ?? "";
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(rawJson);
                JsonElement root = document.RootElement;
                string messageId = root.TryGetProperty("id", out JsonElement idElement)
                    ? idElement.GetString() ?? ""
                    : "";
                string method = root.TryGetProperty("method", out JsonElement methodElement)
                    ? methodElement.GetString() ?? ""
                    : "";
                JsonElement payload = root.TryGetProperty("payload", out JsonElement payloadElement)
                    ? payloadElement.Clone()
                    : default;

                WebMessageReceived?.Invoke(
                    this,
                    new WhiteBrowserSkinWebMessageReceivedEventArgs(
                        rawJson,
                        messageId,
                        method,
                        payload
                    )
                );
            }
            catch (JsonException)
            {
                // transport 層では不正メッセージを握りつぶし、後段の UI 巻き込みを避ける。
            }
        }

        private void CoreWebView2_WebResourceRequested(
            object sender,
            CoreWebView2WebResourceRequestedEventArgs e
        )
        {
            if (coreWebView2?.Environment == null || e?.Request == null)
            {
                return;
            }

            if (!IsThumbnailRequest(e.Request.Uri))
            {
                return;
            }

            if (!TryResolveRequestedThumbnailPath(e.Request.Uri, out string thumbPath))
            {
                e.Response = CreateEmptyResponse(404, "Not Found");
                return;
            }

            if (IsExternalThumbnailRequest(e.Request.Uri) && !registeredExternalThumbnailPaths.Contains(thumbPath))
            {
                e.Response = CreateEmptyResponse(403, "Forbidden");
                return;
            }

            e.Response = CreateThumbnailResponse(thumbPath);
        }

        private Task ExecuteCompatDispatchAsync(string functionName, params object[] arguments)
        {
            if (string.IsNullOrWhiteSpace(functionName))
            {
                return Task.CompletedTask;
            }

            string serializedArguments = string.Join(
                ", ",
                arguments.Select(argument => JsonSerializer.Serialize(argument))
            );
            // __immWbCompat が壊れていても、固定 alias 側へ落として pending 解決を守る。
            string script =
                $"(() => {{ const name = {JsonSerializer.Serialize(functionName)}; const primary = window.__immWbCompat; const alias = window.__immWbCompatBridge; const bridge = primary && typeof primary[name] === 'function' ? primary : alias; if (bridge && typeof bridge[name] === 'function') {{ bridge[name]({serializedArguments}); }} }})();";
            return ExecuteScriptAsync(script);
        }

        private void Detach()
        {
            if (coreWebView2 != null)
            {
                coreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                coreWebView2.WebResourceRequested -= CoreWebView2_WebResourceRequested;
                try
                {
                    coreWebView2.RemoveWebResourceRequestedFilter(
                        ThumbnailFilterUri,
                        CoreWebView2WebResourceContext.Image
                    );
                }
                catch
                {
                    // 破棄順の違いで remove が失敗しても、購読解除は済んでいる。
                }
            }

            coreWebView2 = null;
            isAttached = false;
            managedThumbnailRootPath = "";
            registeredExternalThumbnailPaths.Clear();
        }

        private bool TryResolveRequestedThumbnailPath(string requestUri, out string thumbPath)
        {
            thumbPath = "";
            if (!IsThumbnailRequest(requestUri))
            {
                return false;
            }

            if (
                !WhiteBrowserSkinThumbnailUrlCodec.TryResolveThumbPath(
                    requestUri,
                    managedThumbnailRootPath,
                    out string decodedPath
                )
            )
            {
                return false;
            }

            string normalizedPath = NormalizePath(decodedPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            thumbPath = normalizedPath;
            return true;
        }

        private static bool IsThumbnailRequest(string requestUri)
        {
            if (!Uri.TryCreate(requestUri, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            return string.Equals(
                uri.Host,
                WhiteBrowserSkinHostPaths.ThumbnailVirtualHostName,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static bool IsExternalThumbnailRequest(string requestUri)
        {
            if (!IsThumbnailRequest(requestUri) || !Uri.TryCreate(requestUri, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string relativePath = uri.AbsolutePath.Trim('/');
            return relativePath.StartsWith(
                $"{WhiteBrowserSkinThumbnailUrlCodec.ExternalRoutePrefix}/",
                StringComparison.OrdinalIgnoreCase
            );
        }

        private CoreWebView2WebResourceResponse CreateThumbnailResponse(string thumbPath)
        {
            if (coreWebView2?.Environment == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(thumbPath))
            {
                return CreateEmptyResponse(404, "Not Found");
            }

            try
            {
                FileStream stream = new(
                    thumbPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete
                );
                return coreWebView2.Environment.CreateWebResourceResponse(
                    stream,
                    200,
                    "OK",
                    $"Content-Type: {ResolveContentType(thumbPath)}\r\nCache-Control: no-store"
                );
            }
            catch (FileNotFoundException)
            {
                return CreateEmptyResponse(404, "Not Found");
            }
            catch (DirectoryNotFoundException)
            {
                return CreateEmptyResponse(404, "Not Found");
            }
            catch
            {
                return CreateEmptyResponse(500, "Internal Server Error");
            }
        }

        private CoreWebView2WebResourceResponse CreateEmptyResponse(int statusCode, string reasonPhrase)
        {
            return coreWebView2?.Environment?.CreateWebResourceResponse(
                Stream.Null,
                statusCode,
                reasonPhrase,
                "Cache-Control: no-store"
            );
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            try
            {
                return Path.GetFullPath(path.Trim().Trim('"')).Replace('/', '\\');
            }
            catch
            {
                return path.Trim().Trim('"').Replace('/', '\\');
            }
        }

        private static string ResolveContentType(string path)
        {
            return Path.GetExtension(path)?.ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                _ => "application/octet-stream",
            };
        }
    }
}
