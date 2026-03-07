namespace IndigoMovieManager.Watcher
{
    /// <summary>
    /// reasonコードの正規化・分類を一元化するテーブル。
    /// Provider差分とUI解釈の間で、判定ルールの重複を防ぐ。
    /// </summary>
    internal static class FileIndexReasonTable
    {
        // 固定値として一致判定するreasonカテゴリ。
        private static readonly string[] ExactCategories =
        [
            EverythingReasonCodes.Ok,
            EverythingReasonCodes.SettingDisabled,
            EverythingReasonCodes.AutoNotAvailable,
            EverythingReasonCodes.EverythingNotAvailable,
        ];

        // Prefix一致でカテゴリ化するreason。
        private static readonly string[] PrefixCategories =
        [
            EverythingReasonCodes.OkPrefix,
            EverythingReasonCodes.AvailabilityErrorPrefix,
            EverythingReasonCodes.EverythingQueryErrorPrefix,
            EverythingReasonCodes.EverythingThumbQueryErrorPrefix,
            EverythingReasonCodes.EverythingResultTruncatedPrefix,
            EverythingReasonCodes.PathNotEligiblePrefix,
        ];

        /// <summary>
        /// modeに応じてreasonを正規化する。
        /// ルール追加時はここだけを更新する。
        /// </summary>
        public static string NormalizeByMode(IntegrationMode mode, string reason)
        {
            string safeReason = reason ?? "";
            if (
                mode == IntegrationMode.Auto
                && IsExact(safeReason, EverythingReasonCodes.EverythingNotAvailable)
            )
            {
                return EverythingReasonCodes.AutoNotAvailable;
            }

            return safeReason;
        }

        /// <summary>
        /// reasonを比較用カテゴリへ変換する。
        /// 一致しない値はそのまま返し、未知reasonを見逃さない。
        /// </summary>
        public static string ToCategory(string reason)
        {
            string safeReason = reason ?? "";
            foreach (string exact in ExactCategories)
            {
                if (IsExact(safeReason, exact))
                {
                    return exact;
                }
            }

            foreach (string prefix in PrefixCategories)
            {
                if (safeReason.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return prefix;
                }
            }

            return safeReason;
        }

        /// <summary>
        /// FileIndexProvider系のログ軸を固定する。
        /// サムネイル高負荷やIPC劣化ログと混線しないよう、常に file-index-* を返す。
        /// </summary>
        public static string ToLogAxis(string reason)
        {
            string category = ToCategory(reason);
            if (
                IsExact(category, EverythingReasonCodes.Ok)
                || IsExact(category, EverythingReasonCodes.OkPrefix)
            )
            {
                return "file-index-ok";
            }

            if (
                IsExact(category, EverythingReasonCodes.SettingDisabled)
                || IsExact(category, EverythingReasonCodes.AutoNotAvailable)
                || IsExact(category, EverythingReasonCodes.EverythingNotAvailable)
                || IsExact(category, EverythingReasonCodes.AvailabilityErrorPrefix)
            )
            {
                return "file-index-availability";
            }

            if (IsExact(category, EverythingReasonCodes.EverythingQueryErrorPrefix))
            {
                return "file-index-query";
            }

            if (IsExact(category, EverythingReasonCodes.EverythingThumbQueryErrorPrefix))
            {
                return "file-index-thumb-query";
            }

            if (IsExact(category, EverythingReasonCodes.EverythingResultTruncatedPrefix))
            {
                return "file-index-capacity";
            }

            if (IsExact(category, EverythingReasonCodes.PathNotEligiblePrefix))
            {
                return "file-index-eligibility";
            }

            return "file-index-unknown";
        }

        private static bool IsExact(string reason, string expected)
        {
            return string.Equals(reason, expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
