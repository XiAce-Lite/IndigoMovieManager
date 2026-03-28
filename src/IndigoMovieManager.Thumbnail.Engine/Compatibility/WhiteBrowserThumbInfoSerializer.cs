using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// WhiteBrowser互換のJPEG末尾メタを、仕様データとの相互変換だけに閉じ込める。
    /// </summary>
    public static class WhiteBrowserThumbInfoSerializer
    {
        private const int InfoBufferLength = 60;
        private const int FooterMarker = 1398033709;

        public static void CreateBuffers(
            ThumbnailSheetSpec spec,
            out byte[] secBuffer,
            out byte[] infoBuffer
        )
        {
            ThumbnailSheetSpec normalizedSpec = spec?.Clone() ?? new ThumbnailSheetSpec();
            List<int> captureSeconds = normalizedSpec.CaptureSeconds ?? [];

            secBuffer = new byte[(captureSeconds.Count * 4) + 4];
            for (int i = 0; i < captureSeconds.Count; i++)
            {
                byte[] captureSecBytes = BitConverter.GetBytes(captureSeconds[i]);
                captureSecBytes.CopyTo(secBuffer, i * 4);
            }

            BitConverter.GetBytes(FooterMarker).CopyTo(secBuffer, captureSeconds.Count * 4);

            infoBuffer = new byte[InfoBufferLength];
            BitConverter.GetBytes(normalizedSpec.ThumbCount).CopyTo(infoBuffer, 0);
            BitConverter.GetBytes(normalizedSpec.ThumbWidth).CopyTo(infoBuffer, 12);
            BitConverter.GetBytes(normalizedSpec.ThumbHeight).CopyTo(infoBuffer, 16);
            BitConverter.GetBytes(normalizedSpec.ThumbColumns).CopyTo(infoBuffer, 20);
            BitConverter.GetBytes(normalizedSpec.ThumbRows).CopyTo(infoBuffer, 24);
        }

        public static void AppendToJpeg(string fileName, ThumbnailSheetSpec spec)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            CreateBuffers(spec, out byte[] secBuffer, out byte[] infoBuffer);
            using FileStream dest = new(fileName, FileMode.Append, FileAccess.Write);
            dest.Write(secBuffer);
            dest.Write(infoBuffer);
        }

        public static bool TryReadFromJpeg(string fileName, out ThumbnailSheetSpec spec)
        {
            spec = null;
            if (!Path.Exists(fileName))
            {
                return false;
            }

            try
            {
                using FileStream src = new(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (src.Length < InfoBufferLength)
                {
                    return false;
                }

                src.Seek(-4, SeekOrigin.End);
                byte[] lastBuf = new byte[4];
                if (src.Read(lastBuf, 0, lastBuf.Length) != lastBuf.Length)
                {
                    return false;
                }

                if (IsFooterMarker(lastBuf))
                {
                    return false;
                }

                src.Seek(-InfoBufferLength, SeekOrigin.End);
                byte[] infoBuffer = new byte[InfoBufferLength];
                if (src.Read(infoBuffer, 0, infoBuffer.Length) != infoBuffer.Length)
                {
                    return false;
                }

                int thumbCount = BitConverter.ToInt32(infoBuffer, 0);
                if (thumbCount < 1 || thumbCount > 100)
                {
                    return false;
                }

                List<int> captureSeconds = [];
                if (!TryReadCaptureSecondsFromTail(src, thumbCount, captureSeconds))
                {
                    return false;
                }

                spec = new ThumbnailSheetSpec
                {
                    ThumbCount = thumbCount,
                    ThumbWidth = BitConverter.ToInt32(infoBuffer, 12),
                    ThumbHeight = BitConverter.ToInt32(infoBuffer, 16),
                    ThumbColumns = BitConverter.ToInt32(infoBuffer, 20),
                    ThumbRows = BitConverter.ToInt32(infoBuffer, 24),
                    CaptureSeconds = captureSeconds,
                };
                return true;
            }
            catch (Exception ex)
            {
                ThumbnailRuntimeLog.Write(
                    "thumbnail",
                    $"thumb info parse failed: file='{fileName}', err='{ex.Message}'"
                );
                return false;
            }
        }

        private static bool TryReadCaptureSecondsFromTail(
            FileStream src,
            int thumbCount,
            List<int> captureSeconds
        )
        {
            if (thumbCount < 1)
            {
                return false;
            }

            long footerOffsetFromEnd = InfoBufferLength + 4;
            long captureBytes = thumbCount * 4L;
            long totalMetadataBytes = footerOffsetFromEnd + captureBytes;
            if (src.Length < totalMetadataBytes)
            {
                return false;
            }

            // 最新の infoBuffer は末尾固定なので、その直前にある秒数列だけを読む。
            src.Seek(-footerOffsetFromEnd, SeekOrigin.End);
            byte[] footerBuffer = new byte[4];
            if (src.Read(footerBuffer, 0, footerBuffer.Length) != footerBuffer.Length)
            {
                return false;
            }

            if (!IsFooterMarker(footerBuffer))
            {
                return false;
            }

            src.Seek(-totalMetadataBytes, SeekOrigin.End);
            byte[] secBuffer = new byte[4];
            for (int i = 0; i < thumbCount; i++)
            {
                if (src.Read(secBuffer, 0, secBuffer.Length) != secBuffer.Length)
                {
                    return false;
                }

                captureSeconds.Add(BitConverter.ToInt32(secBuffer, 0));
            }

            return true;
        }

        private static bool IsFooterMarker(byte[] buffer)
        {
            return buffer != null
                && buffer.Length == 4
                && BitConverter.ToInt32(buffer, 0) == FooterMarker;
        }
    }
}
