using System.Text.Json;

namespace IndigoMovieManager.Skin.Runtime
{
    /// <summary>
    /// JS から来たメッセージを、後段が解釈しやすい最小形へ整える。
    /// </summary>
    public sealed class WhiteBrowserSkinWebMessageReceivedEventArgs : EventArgs
    {
        public WhiteBrowserSkinWebMessageReceivedEventArgs(
            string rawJson,
            string messageId,
            string method,
            JsonElement payload
        )
        {
            RawJson = rawJson ?? "";
            MessageId = messageId ?? "";
            Method = method ?? "";
            Payload = payload;
        }

        public string RawJson { get; }
        public string MessageId { get; }
        public string Method { get; }
        public JsonElement Payload { get; }
    }
}
