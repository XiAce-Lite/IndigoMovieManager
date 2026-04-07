using IndigoMovieManager.Skin;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private WhiteBrowserSkinOrchestrator _skinOrchestrator;

        // skin 管理の実体は Orchestrator へ寄せ、MainWindow 側は橋渡しだけにする。
        internal WhiteBrowserSkinOrchestrator GetSkinOrchestrator()
        {
            _skinOrchestrator ??= new WhiteBrowserSkinOrchestrator(
                getCurrentDbFullPath: () => MainVM?.DbInfo?.DBFullPath ?? "",
                getCurrentSkinNameFromViewModel: () => MainVM?.DbInfo?.Skin ?? "",
                setCurrentSkinNameToViewModel: skinName =>
                {
                    if (MainVM?.DbInfo != null)
                    {
                        MainVM.DbInfo.Skin = skinName ?? "";
                    }
                },
                normalizeTabStateName: NormalizeSkinName,
                selectUpperTabDefaultViewBySkinName: SelectUpperTabDefaultViewBySkinName,
                getCurrentUpperTabFixedIndex: GetCurrentUpperTabFixedIndex,
                resolvePersistedSkinNameByTabIndex: ResolveUpperTabStateNameByFixedIndex,
                resolveUpperTabStateNameByFixedIndex: ResolveUpperTabStateNameByFixedIndex
            );

            return _skinOrchestrator;
        }

        public IReadOnlyList<WhiteBrowserSkinDefinition> GetAvailableSkinDefinitions()
        {
            return GetSkinOrchestrator().GetAvailableSkinDefinitions();
        }

        public string GetCurrentSkinName()
        {
            return GetSkinOrchestrator().GetCurrentSkinName();
        }

        public bool ApplySkinByName(string skinName, bool persistToCurrentDb = true)
        {
            bool applied = GetSkinOrchestrator().ApplySkinByName(skinName, persistToCurrentDb);
            if (!applied)
            {
                return false;
            }

            // 設定画面経由では PropertyChanged の取りこぼしが見えづらいので、
            // 成功時は host refresh を明示的に積んで見た目の切替を必ず走らせる。
            QueueExternalSkinHostRefresh("apply-skin");
            return true;
        }

        private string NormalizeStoredSkinName(string skin)
        {
            return GetSkinOrchestrator().NormalizeStoredSkinName(skin);
        }

        // system.skin にはスキン名、profile には外部スキン時だけ現在タブを分けて保存する。
        private void PersistCurrentSkinState(string dbFullPath)
        {
            GetSkinOrchestrator().PersistCurrentSkinState(dbFullPath);
        }
    }
}
