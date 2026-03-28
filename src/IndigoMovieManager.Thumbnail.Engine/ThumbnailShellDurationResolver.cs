using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 最後のフォールバックとなる Shell 経由の秒数取得を切り離す。
    /// </summary>
    internal static class ThumbnailShellDurationResolver
    {
        public static double? TryGetDurationSec(string fileName)
        {
            object shellObj = null;
            object folderObj = null;
            object itemObj = null;
            try
            {
                var shellAppType = Type.GetTypeFromProgID("Shell.Application");
                if (shellAppType == null)
                {
                    return null;
                }

                shellObj = Activator.CreateInstance(shellAppType);
                if (shellObj == null)
                {
                    return null;
                }

                dynamic shell = shellObj;
                folderObj = shell.NameSpace(Path.GetDirectoryName(fileName));
                if (folderObj == null)
                {
                    return null;
                }

                dynamic folder = folderObj;
                itemObj = folder.ParseName(Path.GetFileName(fileName));
                if (itemObj == null)
                {
                    return null;
                }

                string timeString = folder.GetDetailsOf(itemObj, 27);
                if (TimeSpan.TryParse(timeString, out TimeSpan ts) && ts.TotalSeconds > 0)
                {
                    return Math.Truncate(ts.TotalSeconds);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"duration shell err = {e.Message} Movie = {fileName}");
            }
            finally
            {
                ReleaseComObject(itemObj);
                ReleaseComObject(folderObj);
                ReleaseComObject(shellObj);
            }

            return null;
        }

        private static void ReleaseComObject(object comObj)
        {
            if (comObj == null)
            {
                return;
            }

            try
            {
                if (Marshal.IsComObject(comObj))
                {
                    Marshal.FinalReleaseComObject(comObj);
                }
            }
            catch
            {
                // COM解放失敗時は処理継続を優先する。
            }
        }
    }
}
