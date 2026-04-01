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
        private CoreWebView2 coreWebView2;
        private bool isAttached;

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
                UpdateVirtualHostMappings(skinRootPath, thumbRootPath);
                return;
            }

            Detach();

            coreWebView2 = targetCoreWebView2;
            ConfigureSettings(coreWebView2.Settings);
            UpdateVirtualHostMappings(skinRootPath, thumbRootPath);
            coreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
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
            return ExecuteCompatDispatchAsync("__immWbCompat.resolve", messageId, payload);
        }

        public Task RejectRequestAsync(string messageId, string errorMessage)
        {
            return ExecuteCompatDispatchAsync("__immWbCompat.reject", messageId, errorMessage ?? "");
        }

        public Task DispatchCallbackAsync(string callbackName, object payload)
        {
            return ExecuteCompatDispatchAsync("__immWbCompat.dispatchCallback", callbackName, payload);
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

            if (!string.IsNullOrWhiteSpace(thumbRootPath))
            {
                coreWebView2.SetVirtualHostNameToFolderMapping(
                    WhiteBrowserSkinHostPaths.ThumbnailVirtualHostName,
                    thumbRootPath,
                    CoreWebView2HostResourceAccessKind.DenyCors
                );
            }
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

        private Task ExecuteCompatDispatchAsync(string functionPath, params object[] arguments)
        {
            string serializedArguments = string.Join(
                ", ",
                arguments.Select(argument => JsonSerializer.Serialize(argument))
            );
            string script =
                $"if (window.{functionPath}) window.{functionPath}({serializedArguments});";
            return ExecuteScriptAsync(script);
        }

        private void Detach()
        {
            if (coreWebView2 != null)
            {
                coreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
            }

            coreWebView2 = null;
            isAttached = false;
        }
    }
}
