namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// wb.* 呼び出しの成否と戻り値を、bridge 側が扱いやすい最小形へ束ねる。
    /// </summary>
    public sealed class WhiteBrowserSkinApiInvocationResult
    {
        private WhiteBrowserSkinApiInvocationResult(bool succeeded, object payload, string errorMessage)
        {
            Succeeded = succeeded;
            Payload = payload;
            ErrorMessage = errorMessage ?? "";
        }

        public bool Succeeded { get; }
        public object Payload { get; }
        public string ErrorMessage { get; }

        public static WhiteBrowserSkinApiInvocationResult Success(object payload)
        {
            return new WhiteBrowserSkinApiInvocationResult(true, payload, "");
        }

        public static WhiteBrowserSkinApiInvocationResult Failure(string errorMessage)
        {
            return new WhiteBrowserSkinApiInvocationResult(false, null, errorMessage ?? "");
        }
    }
}
