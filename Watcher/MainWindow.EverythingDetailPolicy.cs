using System;
using IndigoMovieManager.Watcher;

namespace IndigoMovieManager;

public partial class MainWindow
{
    // Everything連携の詳細コードを、ログとUI通知で同じ解釈に統一する。
    private static (string Code, string Message) DescribeEverythingDetail(string detail)
    {
        string safeDetail = string.IsNullOrWhiteSpace(detail) ? "unknown" : detail;
        if (
            safeDetail.StartsWith(
                EverythingReasonCodes.PathNotEligiblePrefix,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            string rawReason = safeDetail[EverythingReasonCodes.PathNotEligiblePrefix.Length..];
            string message = rawReason switch
            {
                "empty_path" => "監視フォルダが未設定です",
                "unc_path" => "UNC/NASパスはEverything高速経路の対象外です",
                "no_root" => "ドライブ情報を解決できません",
                "ok" => "対象フォルダ判定は正常です",
                _ when rawReason.StartsWith(
                        "drive_type_",
                        StringComparison.OrdinalIgnoreCase
                    ) => $"ローカル固定ドライブ以外のため対象外です ({rawReason})",
                _ when rawReason.StartsWith(
                        "drive_format_",
                        StringComparison.OrdinalIgnoreCase
                    ) => $"NTFS以外のため対象外です ({rawReason})",
                _ when rawReason.StartsWith(
                        "eligibility_error:",
                        StringComparison.OrdinalIgnoreCase
                    ) => $"対象判定で例外が発生しました ({rawReason})",
                _ => $"対象外です ({rawReason})",
            };
            return (safeDetail, message);
        }

        if (
            safeDetail.Equals(
                EverythingReasonCodes.SettingDisabled,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return (safeDetail, "設定でEverything連携が無効です");
        }

        if (
            safeDetail.Equals(
                EverythingReasonCodes.EverythingNotAvailable,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return (safeDetail, "Everythingが起動していないかIPC接続できません");
        }
        if (
            safeDetail.Equals(
                EverythingReasonCodes.AutoNotAvailable,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return (
                safeDetail,
                "AUTO設定中ですがEverythingが見つからないため通常監視で動作します"
            );
        }

        if (
            safeDetail.StartsWith(
                EverythingReasonCodes.EverythingResultTruncatedPrefix,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return (safeDetail, "検索結果が上限件数に達したため通常監視へ切り替えます");
        }

        if (
            safeDetail.StartsWith(
                EverythingReasonCodes.AvailabilityErrorPrefix,
                StringComparison.OrdinalIgnoreCase
            )
            || safeDetail.StartsWith(
                EverythingReasonCodes.EverythingQueryErrorPrefix,
                StringComparison.OrdinalIgnoreCase
            )
            || safeDetail.StartsWith(
                EverythingReasonCodes.EverythingThumbQueryErrorPrefix,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return (safeDetail, $"Everything連携で例外が発生しました ({safeDetail})");
        }

        if (
            safeDetail.StartsWith(
                $"{EverythingReasonCodes.OkPrefix}watch_deferred_batch",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return (safeDetail, "前回繰り延べた watch 候補の処理を再開しています");
        }

        if (
            safeDetail.StartsWith(
                EverythingReasonCodes.OkPrefix,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return (safeDetail, "Everything連携で候補収集に成功しました");
        }

        return (safeDetail, $"不明な理由のため通常監視へ切り替えます ({safeDetail})");
    }
}
