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

        private static bool IsExact(string reason, string expected)
        {
            return string.Equals(reason, expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
