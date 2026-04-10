namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// Host 初期化やナビゲーション結果を、例外ではなく呼び出し側が分岐しやすい形で返す。
    /// raw skin 名も結果へ残し、Runtime 未導入時でも上位で潰さず扱えるようにする。
    /// </summary>
    public sealed class WhiteBrowserSkinHostOperationResult
    {
        private WhiteBrowserSkinHostOperationResult(
            bool succeeded,
            bool runtimeAvailable,
            string requestedSkinName,
            string errorMessage,
            string errorType
        )
        {
            Succeeded = succeeded;
            RuntimeAvailable = runtimeAvailable;
            RequestedSkinName = requestedSkinName ?? "";
            ErrorMessage = errorMessage ?? "";
            ErrorType = errorType ?? "";
        }

        public bool Succeeded { get; }
        public bool RuntimeAvailable { get; }
        public string RequestedSkinName { get; }
        public string ErrorMessage { get; }
        public string ErrorType { get; }

        public static WhiteBrowserSkinHostOperationResult CreateSuccess(string requestedSkinName)
        {
            return new WhiteBrowserSkinHostOperationResult(
                succeeded: true,
                runtimeAvailable: true,
                requestedSkinName: requestedSkinName,
                errorMessage: "",
                errorType: ""
            );
        }

        public static WhiteBrowserSkinHostOperationResult CreateRuntimeUnavailable(
            string requestedSkinName,
            string errorMessage
        )
        {
            return new WhiteBrowserSkinHostOperationResult(
                succeeded: false,
                runtimeAvailable: false,
                requestedSkinName: requestedSkinName,
                errorMessage: errorMessage,
                errorType: "WebView2RuntimeNotFound"
            );
        }

        public static WhiteBrowserSkinHostOperationResult CreateMissingHtml(
            string requestedSkinName,
            string skinHtmlPath
        )
        {
            return new WhiteBrowserSkinHostOperationResult(
                succeeded: false,
                runtimeAvailable: true,
                requestedSkinName: requestedSkinName,
                errorMessage: $"Skin HTML was not found: {skinHtmlPath ?? ""}",
                errorType: "SkinHtmlMissing"
            );
        }

        public static WhiteBrowserSkinHostOperationResult CreateFailed(
            string requestedSkinName,
            string errorMessage,
            string errorType = "HostPrepareFailed"
        )
        {
            return new WhiteBrowserSkinHostOperationResult(
                succeeded: false,
                runtimeAvailable: true,
                requestedSkinName: requestedSkinName,
                errorMessage: errorMessage,
                errorType: errorType
            );
        }

        public static WhiteBrowserSkinHostOperationResult CreateFailed(
            string requestedSkinName,
            Exception exception
        )
        {
            return CreateFailed(
                requestedSkinName,
                exception?.Message ?? "",
                exception?.GetType().Name ?? ""
            );
        }
    }
}
