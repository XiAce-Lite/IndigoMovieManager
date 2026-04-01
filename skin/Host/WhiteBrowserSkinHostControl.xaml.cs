using System.Windows.Controls;
using IndigoMovieManager.Skin.Runtime;

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

            WhiteBrowserSkinRenderDocument document = renderCoordinator.BuildInitialDocument(
                skinRootPath,
                skinHtmlPath
            );
            SkinWebView.NavigateToString(document.Html);
            return WhiteBrowserSkinHostOperationResult.CreateSuccess(requestedSkinName);
        }

        public void Clear()
        {
            runtimeBridge.ClearRegisteredExternalThumbnailPaths();
            SkinWebView.NavigateToString("<html><body></body></html>");
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
    }
}
