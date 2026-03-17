using System.Diagnostics;
using System.IO;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// temp配下の作業用jpg掃除だけを切り出す。
    /// </summary>
    public static class ThumbnailTempFileCleaner
    {
        public static void ClearCurrentWorkingTempJpg()
        {
            // 失敗時の再生成を安定させるため、temp配下のjpgを先に掃除する。
            string currentPath = Directory.GetCurrentDirectory();
            string tempPath = Path.Combine(currentPath, "temp");
            if (!Path.Exists(tempPath))
            {
                return;
            }

            string[] oldTempFiles;
            try
            {
                oldTempFiles = Directory.GetFiles(tempPath, "*.jpg", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ClearTempJpg enumerate failed: {ex.Message}");
                return;
            }

            foreach (string oldFile in oldTempFiles)
            {
                try
                {
                    if (File.Exists(oldFile))
                    {
                        File.Delete(oldFile);
                    }
                }
                catch (Exception ex)
                {
                    // 1件消せなくても残りの掃除は続行し、起動処理を止めない。
                    Debug.WriteLine($"ClearTempJpg delete skipped: '{oldFile}' {ex.Message}");
                }
            }
        }
    }
}
