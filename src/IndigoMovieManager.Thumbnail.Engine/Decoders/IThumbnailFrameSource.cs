using System;
using System.Drawing;

namespace IndigoMovieManager.Thumbnail.Decoders
{
    /// <summary>
    /// サムネイル用フレームを読み取るソースの共通インターフェース。
    /// </summary>
    internal interface IThumbnailFrameSource : IDisposable
    {
        /// <summary>
        /// 指定時刻のフレームを読み取る。成功すれば true を返し、frameBitmap にセットする。
        /// </summary>
        bool TryReadFrame(TimeSpan position, out Bitmap frameBitmap);
    }
}
