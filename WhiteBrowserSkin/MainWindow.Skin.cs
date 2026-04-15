using System;
using IndigoMovieManager.Skin;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private WhiteBrowserSkinOrchestrator _skinOrchestrator;
        private string _externalSkinRootPathForTesting = "";

        internal string ExternalSkinRootPathForTesting
        {
            get => _externalSkinRootPathForTesting;
            set
            {
                string normalizedPath = value?.Trim() ?? "";
                if (string.Equals(_externalSkinRootPathForTesting, normalizedPath, StringComparison.Ordinal))
                {
                    return;
                }

                _externalSkinRootPathForTesting = normalizedPath;
                _skinOrchestrator = null;
            }
        }

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
                resolveUpperTabStateNameByFixedIndex: ResolveUpperTabStateNameByFixedIndex,
                enqueuePersistRequest: TryEnqueueWhiteBrowserSkinStatePersistRequest,
                fallbackPersistRequest: PersistWhiteBrowserSkinStateRequestFallback,
                skinRootPath: ResolveExternalSkinRootPath()
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
            return applied;
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

        private string ResolveExternalSkinRootPath()
        {
            return string.IsNullOrWhiteSpace(ExternalSkinRootPathForTesting)
                ? WhiteBrowserSkinCatalogService.ResolveSkinRootPath(AppContext.BaseDirectory)
                : ExternalSkinRootPathForTesting;
        }
    }
}
