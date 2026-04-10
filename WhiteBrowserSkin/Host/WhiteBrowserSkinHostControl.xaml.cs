using System.Windows.Controls;
using IndigoMovieManager.Skin.Runtime;
using Microsoft.Web.WebView2.Core;

namespace IndigoMovieManager.Skin.Host
{
    /// <summary>
    /// WhiteBrowser 互換スキン専用の WebView2 ホスト。
    /// MainWindow 側はこの control を出し入れするだけで良い形を目指す。
    /// </summary>
    public partial class WhiteBrowserSkinHostControl : UserControl
    {
        private readonly WhiteBrowserSkinRuntimeBridge runtimeBridge = new();
        private readonly WhiteBrowserSkinRenderCoordinator renderCoordinator = new();

        public WhiteBrowserSkinHostControl()
        {
            InitializeComponent();
            runtimeBridge.WebMessageReceived += RuntimeBridge_WebMessageReceived;
        }

        public WhiteBrowserSkinRuntimeBridge RuntimeBridge => runtimeBridge;

        public event EventHandler<WhiteBrowserSkinWebMessageReceivedEventArgs> WebMessageReceived;

        public async Task NavigateAsync(
            string requestedSkinName,
            string userDataFolder,
            string skinRootPath,
            string skinHtmlPath,
            string thumbRootPath
        )
        {
            WhiteBrowserSkinHostOperationResult result = await TryNavigateAsync(
                requestedSkinName,
                userDataFolder,
                skinRootPath,
                skinHtmlPath,
                thumbRootPath
            );
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.ErrorMessage);
            }
        }

        public async Task<WhiteBrowserSkinHostOperationResult> TryNavigateAsync(
            string requestedSkinName,
            string userDataFolder,
            string skinRootPath,
            string skinHtmlPath,
            string thumbRootPath
        )
        {
            WhiteBrowserSkinHostOperationResult attachResult = await runtimeBridge.TryEnsureAttachedAsync(
                SkinWebView,
                requestedSkinName,
                userDataFolder,
                skinRootPath,
                thumbRootPath
            );
            if (!attachResult.Succeeded)
            {
                return attachResult;
            }

            // 旧ページの終了 callback を先に返してから、新しい skin を流し込む。
            await runtimeBridge.HandleSkinLeaveAsync();
            WhiteBrowserSkinRenderDocument document = renderCoordinator.BuildInitialDocument(
                skinRootPath,
                skinHtmlPath
            );
            await NavigateToStringAsync(document.Html);
            return WhiteBrowserSkinHostOperationResult.CreateSuccess(requestedSkinName);
        }

        public void Clear()
        {
            _ = ClearIgnoringErrorsAsync();
        }

        public async Task ClearAsync()
        {
            runtimeBridge.ClearRegisteredExternalThumbnailPaths();
            // 終了経路では未初期化の host も来るので、その時は空 HTML への遷移を無理に撃たない。
            if (SkinWebView.CoreWebView2 == null)
            {
                return;
            }

            await runtimeBridge.HandleSkinLeaveAsync();
            await NavigateToStringAsync("<html><body></body></html>");
        }

        public Task HandleSkinLeaveAsync()
        {
            return runtimeBridge.HandleSkinLeaveAsync();
        }

        public void RegisterExternalThumbnailPath(string thumbPath)
        {
            runtimeBridge.RegisterExternalThumbnailPath(thumbPath);
        }

        public Task ResolveRequestAsync(string messageId, object payload)
        {
            return runtimeBridge.ResolveRequestAsync(messageId, payload);
        }

        public Task RejectRequestAsync(string messageId, string errorMessage)
        {
            return runtimeBridge.RejectRequestAsync(messageId, errorMessage);
        }

        public Task DispatchCallbackAsync(string callbackName, object payload)
        {
            return runtimeBridge.DispatchCallbackAsync(callbackName, payload);
        }

        private void RuntimeBridge_WebMessageReceived(
            object sender,
            WhiteBrowserSkinWebMessageReceivedEventArgs e
        )
        {
            WebMessageReceived?.Invoke(this, e);
        }

        private async Task ClearIgnoringErrorsAsync()
        {
            try
            {
                await ClearAsync();
            }
            catch
            {
                // 終了経路では host 破棄を優先し、blank 遷移失敗は握りつぶす。
            }
        }

        private async Task NavigateToStringAsync(string html)
        {
            TaskCompletionSource<bool> navigationCompleted = new(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = null;
            handler = (_, args) =>
            {
                SkinWebView.NavigationCompleted -= handler;
                if (args.IsSuccess)
                {
                    navigationCompleted.TrySetResult(true);
                    return;
                }

                navigationCompleted.TrySetException(
                    new InvalidOperationException($"Navigation failed: {args.WebErrorStatus}")
                );
            };

            SkinWebView.NavigationCompleted += handler;
            try
            {
                SkinWebView.NavigateToString(html ?? "<html><body></body></html>");
            }
            catch
            {
                SkinWebView.NavigationCompleted -= handler;
                throw;
            }

            Task completedTask = await Task.WhenAny(
                navigationCompleted.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            if (!ReferenceEquals(completedTask, navigationCompleted.Task))
            {
                SkinWebView.NavigationCompleted -= handler;
                throw new TimeoutException(
                    "WhiteBrowser skin host の NavigateToString が 10 秒以内に完了しませんでした。"
                );
            }

            await navigationCompleted.Task;
        }
    }
}
