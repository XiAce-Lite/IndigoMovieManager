namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 既知の非動画シグネチャを、先頭数バイトだけで軽く判定する。
    /// </summary>
    internal static class ThumbnailFileSignatureInspector
    {
        private const int SignatureReadLength = 64;
        private const int MinimumUnknownDecisionBytes = 16;

        private static readonly byte[] AppleDoubleMagic = [0x00, 0x05, 0x16, 0x07];
        private static readonly byte[] AsfHeaderGuid =
        [
            0x30,
            0x26,
            0xB2,
            0x75,
            0x8E,
            0x66,
            0xCF,
            0x11,
            0xA6,
            0xD9,
            0x00,
            0xAA,
            0x00,
            0x62,
            0xCE,
            0x6C,
        ];

        public static ThumbnailFileSignatureKind Inspect(string moviePath)
        {
            if (!TryReadLeadingBytes(moviePath, SignatureReadLength, out byte[] header, out int read))
            {
                return ThumbnailFileSignatureKind.Unavailable;
            }

            if (HasPrefix(header, read, AppleDoubleMagic))
            {
                return ThumbnailFileSignatureKind.AppleDouble;
            }

            if (IsShockwaveFlashCore(header, read))
            {
                return ThumbnailFileSignatureKind.ShockwaveFlash;
            }

            if (IsKnownMovieContainerCore(header, read))
            {
                return ThumbnailFileSignatureKind.KnownMovie;
            }

            // WMV/ASF を含めた最長 16 バイト判定までは読めた時だけ Unknown へ落とす。
            return read >= MinimumUnknownDecisionBytes
                ? ThumbnailFileSignatureKind.Unknown
                : ThumbnailFileSignatureKind.InsufficientData;
        }

        public static bool IsAppleDouble(string moviePath)
        {
            return Inspect(moviePath) == ThumbnailFileSignatureKind.AppleDouble;
        }

        public static bool IsShockwaveFlash(string moviePath)
        {
            return Inspect(moviePath) == ThumbnailFileSignatureKind.ShockwaveFlash;
        }

        public static bool HasKnownMovieSignature(string moviePath)
        {
            return Inspect(moviePath) == ThumbnailFileSignatureKind.KnownMovie;
        }

        private static bool IsKnownMovieContainerCore(IReadOnlyList<byte> header, int read)
        {
            if (header == null || read < 3)
            {
                return false;
            }

            if (IsAvi(header, read) || IsAsf(header, read) || IsEbml(header, read) || IsOgg(header, read))
            {
                return true;
            }

            if (IsFlv(header, read) || HasMp4OrMovAtom(header, read))
            {
                return true;
            }

            return false;
        }

        private static bool IsAvi(IReadOnlyList<byte> header, int read)
        {
            return read >= 12
                && header[0] == 0x52
                && header[1] == 0x49
                && header[2] == 0x46
                && header[3] == 0x46
                && header[8] == 0x41
                && header[9] == 0x56
                && header[10] == 0x49
                && header[11] == 0x20;
        }

        private static bool IsAsf(IReadOnlyList<byte> header, int read)
        {
            return HasPrefix(header, read, AsfHeaderGuid);
        }

        private static bool HasMp4OrMovAtom(IReadOnlyList<byte> header, int read)
        {
            if (read >= 8 && MatchesAscii(header, 4, "ftyp"))
            {
                return true;
            }

            return ContainsAscii(header, read, "moov") || ContainsAscii(header, read, "ftypqt");
        }

        private static bool IsEbml(IReadOnlyList<byte> header, int read)
        {
            return read >= 4
                && header[0] == 0x1A
                && header[1] == 0x45
                && header[2] == 0xDF
                && header[3] == 0xA3;
        }

        private static bool IsOgg(IReadOnlyList<byte> header, int read)
        {
            return read >= 4 && MatchesAscii(header, 0, "OggS");
        }

        private static bool IsFlv(IReadOnlyList<byte> header, int read)
        {
            return read >= 3 && MatchesAscii(header, 0, "FLV");
        }

        private static bool IsShockwaveFlashCore(IReadOnlyList<byte> header, int read)
        {
            return read >= 3
                && header[1] == 0x57
                && header[2] == 0x53
                && (header[0] == 0x46 || header[0] == 0x43 || header[0] == 0x5A);
        }

        private static bool HasPrefix(IReadOnlyList<byte> header, int read, IReadOnlyList<byte> expectedHeader)
        {
            if (header == null || expectedHeader == null || expectedHeader.Count < 1 || read < expectedHeader.Count)
            {
                return false;
            }

            for (int i = 0; i < expectedHeader.Count; i++)
            {
                if (header[i] != expectedHeader[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesAscii(IReadOnlyList<byte> header, int offset, string text)
        {
            if (
                header == null
                || string.IsNullOrEmpty(text)
                || offset < 0
                || (offset + text.Length) > header.Count
            )
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                if (header[offset + i] != text[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsAscii(IReadOnlyList<byte> header, int read, string text)
        {
            if (header == null || string.IsNullOrEmpty(text) || read < text.Length)
            {
                return false;
            }

            int lastStart = read - text.Length;
            for (int offset = 0; offset <= lastStart; offset++)
            {
                if (MatchesAscii(header, offset, text))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadLeadingBytes(
            string moviePath,
            int maxLength,
            out byte[] header,
            out int read
        )
        {
            header = [];
            read = 0;
            if (string.IsNullOrWhiteSpace(moviePath) || maxLength < 1 || !File.Exists(moviePath))
            {
                return false;
            }

            try
            {
                using FileStream stream = new(
                    moviePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete
                );
                int bufferLength = (int)Math.Min(Math.Max(1, stream.Length), maxLength);
                header = new byte[bufferLength];
                read = stream.Read(header, 0, bufferLength);
                return read > 0;
            }
            catch
            {
                header = [];
                read = 0;
                return false;
            }
        }
    }

    internal enum ThumbnailFileSignatureKind
    {
        Unavailable = 0,
        InsufficientData = 1,
        KnownMovie = 2,
        AppleDouble = 3,
        ShockwaveFlash = 4,
        Unknown = 5,
    }
}
