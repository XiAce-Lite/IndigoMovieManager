using System.Text;

namespace IndigoMovieManager.Thumbnail.Ipc
{
    // IPC方式と接続ポリシーの最小固定値をここへ集約する。
    // 実際の接続実装が後から入っても、pipe名とタイムアウト方針をぶらさないための土台。
    public static class ThumbnailIpcTransportPolicy
    {
        public const string TransportKind = "named-pipe";
        public const string MessageFormat = "length-prefixed-json";
        public const string SerializationKind = "system-text-json-utf8";
        public const string AdminServicePipeName = "IndigoMovieManager.AdminTelemetry.v1";
        public const string ThumbnailEnginePipeNamePrefix =
            "IndigoMovieManager.Thumbnail.Engine.v1";
        public const int ConnectTimeoutMs = 1000;
        public const int RequestTimeoutMs = 2000;
        public const int HealthCheckTimeoutMs = 500;
        public const int ReconnectDelayMs = 5000;

        // エンジンpipeはオーケストレータのインスタンス単位で分ける。
        // これにより多重起動時も接続先を誤らない。
        public static string ResolveThumbnailEnginePipeName(string orchestratorInstanceId)
        {
            string normalizedInstanceId = NormalizeInstanceId(orchestratorInstanceId);
            return $"{ThumbnailEnginePipeNamePrefix}.{normalizedInstanceId}";
        }

        // Windowsのpipe名へ混ぜても読めるよう、英数字と一部記号以外は`-`へ寄せる。
        internal static string NormalizeInstanceId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "default";
            }

            StringBuilder builder = new(value.Length);
            foreach (char c in value.Trim())
            {
                if (
                    (c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || c == '-'
                    || c == '_'
                )
                {
                    _ = builder.Append(c);
                }
                else
                {
                    _ = builder.Append('-');
                }
            }

            string normalized = builder.ToString().Trim('-');
            return string.IsNullOrWhiteSpace(normalized) ? "default" : normalized;
        }
    }
}
