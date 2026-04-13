using System;
using System.Collections.Generic;
using System.IO;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager.Skin
{
    /// <summary>
    /// skin 一覧、現在スキン、永続化の流れを 1 か所へ寄せる司令塔。
    /// MainWindow 側は「今の状態を渡す」「選択結果を反映する」だけに薄く保つ。
    /// </summary>
    public sealed class WhiteBrowserSkinOrchestrator
    {
        private const string DefaultGridSkinName = "DefaultGrid";
        private const string SkinProfileLastUpperTabKey = "LastUpperTab";

        private readonly Func<string> getCurrentDbFullPath;
        private readonly Func<string> getCurrentSkinNameFromViewModel;
        private readonly Action<string> setCurrentSkinNameToViewModel;
        private readonly Func<string, string> normalizeTabStateName;
        private readonly Action<string> selectUpperTabDefaultViewBySkinName;
        private readonly Func<int> getCurrentUpperTabFixedIndex;
        private readonly Func<int, string> resolvePersistedSkinNameByTabIndex;
        private readonly Func<int, string> resolveUpperTabStateNameByFixedIndex;
        private readonly Func<WhiteBrowserSkinStatePersistRequest, bool> enqueuePersistRequest;
        private readonly string skinRootPath;

        private IReadOnlyList<WhiteBrowserSkinDefinition> availableSkinDefinitions =
            Array.Empty<WhiteBrowserSkinDefinition>();
        private WhiteBrowserSkinDefinition activeSkinDefinition;

        public WhiteBrowserSkinOrchestrator(
            Func<string> getCurrentDbFullPath,
            Func<string> getCurrentSkinNameFromViewModel,
            Action<string> setCurrentSkinNameToViewModel,
            Func<string, string> normalizeTabStateName,
            Action<string> selectUpperTabDefaultViewBySkinName,
            Func<int> getCurrentUpperTabFixedIndex,
            Func<int, string> resolvePersistedSkinNameByTabIndex,
            Func<int, string> resolveUpperTabStateNameByFixedIndex,
            Func<WhiteBrowserSkinStatePersistRequest, bool> enqueuePersistRequest,
            string skinRootPath = ""
        )
        {
            this.getCurrentDbFullPath =
                getCurrentDbFullPath ?? throw new ArgumentNullException(nameof(getCurrentDbFullPath));
            this.getCurrentSkinNameFromViewModel =
                getCurrentSkinNameFromViewModel
                ?? throw new ArgumentNullException(nameof(getCurrentSkinNameFromViewModel));
            this.setCurrentSkinNameToViewModel =
                setCurrentSkinNameToViewModel
                ?? throw new ArgumentNullException(nameof(setCurrentSkinNameToViewModel));
            this.normalizeTabStateName =
                normalizeTabStateName ?? throw new ArgumentNullException(nameof(normalizeTabStateName));
            this.selectUpperTabDefaultViewBySkinName =
                selectUpperTabDefaultViewBySkinName
                ?? throw new ArgumentNullException(nameof(selectUpperTabDefaultViewBySkinName));
            this.getCurrentUpperTabFixedIndex =
                getCurrentUpperTabFixedIndex
                ?? throw new ArgumentNullException(nameof(getCurrentUpperTabFixedIndex));
            this.resolvePersistedSkinNameByTabIndex =
                resolvePersistedSkinNameByTabIndex
                ?? throw new ArgumentNullException(nameof(resolvePersistedSkinNameByTabIndex));
            this.resolveUpperTabStateNameByFixedIndex =
                resolveUpperTabStateNameByFixedIndex
                ?? throw new ArgumentNullException(nameof(resolveUpperTabStateNameByFixedIndex));
            this.enqueuePersistRequest =
                enqueuePersistRequest ?? throw new ArgumentNullException(nameof(enqueuePersistRequest));
            this.skinRootPath = string.IsNullOrWhiteSpace(skinRootPath)
                ? WhiteBrowserSkinCatalogService.ResolveSkinRootPath(AppContext.BaseDirectory)
                : skinRootPath;
        }

        public IReadOnlyList<WhiteBrowserSkinDefinition> GetAvailableSkinDefinitions()
        {
            ReloadAvailableSkinDefinitions();
            return BuildAvailableSkinDefinitionSnapshot();
        }

        public string GetCurrentSkinName()
        {
            return NormalizeStoredSkinName(getCurrentSkinNameFromViewModel());
        }

        public WhiteBrowserSkinDefinition GetCurrentSkinDefinition()
        {
            return ResolveCurrentDefinition();
        }

        public bool ApplySkinByName(string skinName, bool persistToCurrentDb = true)
        {
            WhiteBrowserSkinDefinition definition = ResolveDefinitionByName(skinName);
            if (definition == null)
            {
                return false;
            }

            activeSkinDefinition = definition;
            setCurrentSkinNameToViewModel(definition.Name);

            string dbFullPath = getCurrentDbFullPath() ?? "";
            string targetTabStateName = ResolveInitialTabStateNameForSkin(definition, dbFullPath);
            selectUpperTabDefaultViewBySkinName(targetTabStateName);

            if (persistToCurrentDb && !string.IsNullOrWhiteSpace(dbFullPath))
            {
                PersistCurrentSkinState(dbFullPath);
            }

            return true;
        }

        public string NormalizeStoredSkinName(string skinName)
        {
            string normalizedSkinName = skinName?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(normalizedSkinName))
            {
                return DefaultGridSkinName;
            }

            WhiteBrowserSkinDefinition exactDefinition = ResolveDefinitionByName(normalizedSkinName);
            return exactDefinition?.Name ?? normalizedSkinName;
        }

        public void PersistCurrentSkinState(string dbFullPath)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return;
            }

            int currentTabIndex = getCurrentUpperTabFixedIndex();
            string currentTabStateName = resolveUpperTabStateNameByFixedIndex(currentTabIndex);
            WhiteBrowserSkinDefinition currentDefinition = ResolveCurrentDefinition();

            if (currentDefinition != null && !currentDefinition.IsBuiltIn)
            {
                enqueuePersistRequest(
                    WhiteBrowserSkinStatePersistRequest.CreateSystem(
                        dbFullPath,
                        "skin",
                        currentDefinition.Name
                    )
                );
                enqueuePersistRequest(
                    WhiteBrowserSkinStatePersistRequest.CreateProfile(
                        dbFullPath,
                        currentDefinition.Name,
                        SkinProfileLastUpperTabKey,
                        currentTabStateName
                    )
                );
                return;
            }

            enqueuePersistRequest(
                WhiteBrowserSkinStatePersistRequest.CreateSystem(
                    dbFullPath,
                    "skin",
                    resolvePersistedSkinNameByTabIndex(currentTabIndex)
                )
            );
        }

        private WhiteBrowserSkinDefinition ResolveCurrentDefinition()
        {
            string currentSkinName = GetCurrentSkinName();
            if (
                activeSkinDefinition != null
                && string.Equals(activeSkinDefinition.Name, currentSkinName, StringComparison.OrdinalIgnoreCase)
            )
            {
                return activeSkinDefinition;
            }

            activeSkinDefinition =
                ResolveDefinitionByName(currentSkinName)
                ?? CreateMissingExternalDefinition(currentSkinName);
            return activeSkinDefinition;
        }

        private WhiteBrowserSkinDefinition ResolveDefinitionByName(string skinName)
        {
            ReloadAvailableSkinDefinitions();
            return WhiteBrowserSkinCatalogService.TryResolveExactByName(
                availableSkinDefinitions,
                skinName
            );
        }

        private void ReloadAvailableSkinDefinitions()
        {
            availableSkinDefinitions = WhiteBrowserSkinCatalogService.Load(skinRootPath);
        }

        private IReadOnlyList<WhiteBrowserSkinDefinition> BuildAvailableSkinDefinitionSnapshot()
        {
            WhiteBrowserSkinDefinition currentDefinition = ResolveCurrentDefinition();
            if (
                currentDefinition == null
                || !currentDefinition.IsMissing
                || WhiteBrowserSkinCatalogService.TryResolveExactByName(
                    availableSkinDefinitions,
                    currentDefinition.Name
                ) != null
            )
            {
                return availableSkinDefinitions;
            }

            List<WhiteBrowserSkinDefinition> snapshot = [.. availableSkinDefinitions, currentDefinition];
            return snapshot;
        }

        private string ResolveInitialTabStateNameForSkin(
            WhiteBrowserSkinDefinition definition,
            string dbFullPath
        )
        {
            if (definition == null)
            {
                return DefaultGridSkinName;
            }

            if (definition.IsBuiltIn)
            {
                return normalizeTabStateName(definition.Name);
            }

            if (!string.IsNullOrWhiteSpace(dbFullPath))
            {
                string savedTabState = SelectProfileValue(
                    dbFullPath,
                    definition.Name,
                    SkinProfileLastUpperTabKey
                );
                if (!string.IsNullOrWhiteSpace(savedTabState))
                {
                    return normalizeTabStateName(savedTabState);
                }
            }

            return normalizeTabStateName(definition.PreferredTabStateName);
        }

        private WhiteBrowserSkinDefinition CreateMissingExternalDefinition(string skinName)
        {
            if (string.IsNullOrWhiteSpace(skinName))
            {
                return WhiteBrowserSkinCatalogService.ResolveByName(availableSkinDefinitions, DefaultGridSkinName);
            }

            return new WhiteBrowserSkinDefinition(
                skinName.Trim(),
                "",
                "",
                WhiteBrowserSkinConfig.Empty,
                DefaultGridSkinName,
                isBuiltIn: false,
                isMissing: true
            );
        }

        internal string ResolvePersistedSkinNameForCurrentState()
        {
            int currentTabIndex = getCurrentUpperTabFixedIndex();
            return resolvePersistedSkinNameByTabIndex(currentTabIndex);
        }
    }
}
