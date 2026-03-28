using System.IO;
using System.IO.Hashing;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル命名に使う動画ハッシュ計算だけを切り出す。
    /// </summary>
    public static class MovieHashCalculator
    {
        public static string GetHashCrc32(string filePath = "")
        {
            string fileName = filePath;
            if (!Path.Exists(filePath))
            {
                return "";
            }

            try
            {
                using BinaryReader reader = new(
                    new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read)
                );
                byte[] buffer = reader.ReadBytes(1024 * 128);
                Span<byte> crc32AsBytes = stackalloc byte[4];
                Crc32.Hash(buffer, crc32AsBytes);

                // 既存(Crc32.NET)と同じ文字列表現を維持するため、バイト順を反転してから16進化する。
                byte[] normalized = crc32AsBytes.ToArray();
                Array.Reverse(normalized);
                return Convert.ToHexString(normalized).ToLowerInvariant();
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
        }
    }
}
