using System;

namespace IndigoMovieManager.Thumbnail.Decoders
{
    /// <summary>
    /// デコーダー種別に応じた IThumbnailFrameSource を生成するファクトリ。
    /// 環境変数 IMM_THUMB_DECODER で既定デコーダーを切替できる。
    /// </summary>
    internal static class ThumbnailFrameDecoderFactory
    {
        /// <summary>
        /// エンジンIDに対応するフレームソースを生成する。
        /// </summary>
        public static IThumbnailFrameSource Create(string engineId, string movieFullPath)
        {
            // 環境変数でデコーダーを明示指定できる
            string decoderOverride = ThumbnailEnvConfig.GetThumbDecoder();
            string resolvedId = string.IsNullOrEmpty(decoderOverride) ? engineId : decoderOverride;

            try
            {
                return resolvedId?.ToLowerInvariant() switch
                {
                    "opencv" => new OpenCvThumbnailFrameDecoder(movieFullPath),
                    "ffmediatoolkit" => new FfMediaToolkitThumbnailFrameDecoder(movieFullPath),
                    _ => new FfMediaToolkitThumbnailFrameDecoder(movieFullPath),
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"ThumbnailFrameDecoderFactory.Create failed: decoder={resolvedId}, err={ex.Message}"
                );
                return null;
            }
        }
    }
}
