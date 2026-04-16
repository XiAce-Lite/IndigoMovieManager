using System;

namespace IndigoMovieManager.Skin
{
    public enum WhiteBrowserSkinStatePersistTargetKind
    {
        System,
        Profile,
    }

    /// <summary>
    /// skin 永続化の 1 書き込み要求を表す。
    /// system と profile を同じ単一ライターへ流すため、ここで形を揃える。
    /// </summary>
    public sealed class WhiteBrowserSkinStatePersistRequest
    {
        private WhiteBrowserSkinStatePersistRequest(
            string dbFullPath,
            WhiteBrowserSkinStatePersistTargetKind targetKind,
            string key,
            string value,
            string profileName,
            string traceText
        )
        {
            DbFullPath = dbFullPath?.Trim() ?? "";
            TargetKind = targetKind;
            Key = key?.Trim() ?? "";
            Value = value ?? "";
            ProfileName = profileName?.Trim() ?? "";
            TraceText = traceText?.Trim() ?? "";
        }

        public string DbFullPath { get; }
        public WhiteBrowserSkinStatePersistTargetKind TargetKind { get; }
        public string Key { get; }
        public string Value { get; }
        public string ProfileName { get; }
        public string TraceText { get; }

        public static WhiteBrowserSkinStatePersistRequest CreateSystem(
            string dbFullPath,
            string key,
            string value,
            string traceText = ""
        )
        {
            return new WhiteBrowserSkinStatePersistRequest(
                dbFullPath,
                WhiteBrowserSkinStatePersistTargetKind.System,
                key,
                value,
                "",
                traceText
            );
        }

        public static WhiteBrowserSkinStatePersistRequest CreateProfile(
            string dbFullPath,
            string profileName,
            string key,
            string value,
            string traceText = ""
        )
        {
            return new WhiteBrowserSkinStatePersistRequest(
                dbFullPath,
                WhiteBrowserSkinStatePersistTargetKind.Profile,
                key,
                value,
                profileName,
                traceText
            );
        }

        internal string BuildIdentityKey()
        {
            return TargetKind switch
            {
                WhiteBrowserSkinStatePersistTargetKind.System => $"system:{Key}",
                WhiteBrowserSkinStatePersistTargetKind.Profile => $"profile:{ProfileName}:{Key}",
                _ => $"{(int)TargetKind}:{ProfileName}:{Key}",
            };
        }
    }
}
