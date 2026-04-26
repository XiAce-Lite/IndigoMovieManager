using System;
using System.IO;

namespace IndigoMovieManager;

public partial class MainWindow
{
    /// <summary>
    // Everything高速経路を使う対象かを判定する（ローカル固定ドライブ + NTFSのみ）。
    // NAS/UNC、リムーバブル、非NTFSは既存経路へ寄せる。
    private static bool IsEverythingEligiblePath(string watchFolder, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(watchFolder))
        {
            reason = "empty_path";
            return false;
        }

        try
        {
            string normalized = Path.GetFullPath(watchFolder);
            if (normalized.StartsWith(@"\\"))
            {
                reason = "unc_path";
                return false;
            }

            string root = Path.GetPathRoot(normalized) ?? "";
            if (string.IsNullOrWhiteSpace(root))
            {
                reason = "no_root";
                return false;
            }

            DriveInfo drive = new(root);
            if (drive.DriveType != DriveType.Fixed)
            {
                reason = $"drive_type_{drive.DriveType}";
                return false;
            }

            if (!string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"drive_format_{drive.DriveFormat}";
                return false;
            }

            reason = "ok";
            return true;
        }
        catch (Exception ex)
        {
            reason = $"eligibility_error:{ex.GetType().Name}";
            return false;
        }
    }
}
