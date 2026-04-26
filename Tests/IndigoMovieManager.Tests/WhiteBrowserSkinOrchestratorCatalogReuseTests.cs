using System.Collections.Generic;
using System.IO;
using IndigoMovieManager.Skin;
using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WhiteBrowserSkinOrchestratorCatalogReuseTests
{
    [SetUp]
    public void SetUp()
    {
        WhiteBrowserSkinCatalogService.ResetCacheForTesting();
        WhiteBrowserSkinProfileValueCache.ClearForTesting();
    }

    [Test]
    public void GetAvailableSkinDefinitionsとApplySkinByNameは同一catalog_cacheを再利用する()
    {
        string rootPath = CreateSkinRootWithSingleSkin("ReuseGrid");
        string currentSkinName = "";
        List<string> selectedTabs = [];

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? "",
                selectUpperTabDefaultViewBySkinName: tabStateName => selectedTabs.Add(tabStateName ?? "")
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> first = orchestrator.GetAvailableSkinDefinitions();
            bool applied = orchestrator.ApplySkinByName("ReuseGrid", persistToCurrentDb: false);
            IReadOnlyList<WhiteBrowserSkinDefinition> second = orchestrator.GetAvailableSkinDefinitions();

            Assert.Multiple(() =>
            {
                Assert.That(applied, Is.True);
                Assert.That(currentSkinName, Is.EqualTo("ReuseGrid"));
                Assert.That(selectedTabs, Is.EqualTo(new[] { "DefaultGrid" }));
                Assert.That(first, Is.SameAs(second));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(1));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadHitCountForTesting(), Is.EqualTo(2));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void ApplySkinByName後にhtml更新が入ると次回一覧取得でcatalog_cacheを再読込する()
    {
        string rootPath = CreateSkinRootWithSingleSkin("ReloadOrchestratorGrid", thumbWidth: 160);
        string htmlPath = Path.Combine(
            rootPath,
            "ReloadOrchestratorGrid",
            "ReloadOrchestratorGrid.htm"
        );
        string currentSkinName = "";

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? ""
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> first = orchestrator.GetAvailableSkinDefinitions();
            bool applied = orchestrator.ApplySkinByName("ReloadOrchestratorGrid", persistToCurrentDb: false);
            File.WriteAllText(
                htmlPath,
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 220;
                    thum-height : 160;
                    thum-column : 1;
                    thum-row : 1;
                  </div>
                </body>
                </html>
                """
            );
            File.SetLastWriteTimeUtc(htmlPath, DateTime.UtcNow.AddSeconds(1));

            IReadOnlyList<WhiteBrowserSkinDefinition> second = orchestrator.GetAvailableSkinDefinitions();
            WhiteBrowserSkinDefinition reloaded = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                second,
                "ReloadOrchestratorGrid"
            );

            Assert.Multiple(() =>
            {
                Assert.That(applied, Is.True);
                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(reloaded, Is.Not.Null);
                Assert.That(reloaded.Config.ThumbWidth, Is.EqualTo(220));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(2));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadHitCountForTesting(), Is.EqualTo(1));
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    [Test]
    public void 一部skin更新後の一覧再取得では未変更skin定義を参照再利用する()
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-skin-orchestrator-reuse-{Guid.NewGuid():N}"
        );
        string keepSkinDirectoryPath = Path.Combine(rootPath, "ReuseKeepSkin");
        string changedSkinDirectoryPath = Path.Combine(rootPath, "ReuseChangedSkin");
        Directory.CreateDirectory(keepSkinDirectoryPath);
        Directory.CreateDirectory(changedSkinDirectoryPath);
        File.WriteAllText(
            Path.Combine(keepSkinDirectoryPath, "ReuseKeepSkin.htm"),
            """
            <html>
            <body>
              <div id="config">
                thum-width : 160;
                thum-height : 120;
                thum-column : 1;
                thum-row : 1;
              </div>
            </body>
            </html>
            """
        );
        string changedHtmlPath = Path.Combine(changedSkinDirectoryPath, "ReuseChangedSkin.htm");
        File.WriteAllText(
            changedHtmlPath,
            """
            <html>
            <body>
              <div id="config">
                thum-width : 200;
                thum-height : 120;
                thum-column : 1;
                thum-row : 1;
              </div>
            </body>
            </html>
            """
        );
        string currentSkinName = "";

        try
        {
            WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
                skinRootPath: rootPath,
                getCurrentSkinNameFromViewModel: () => currentSkinName,
                setCurrentSkinNameToViewModel: skinName => currentSkinName = skinName ?? ""
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> first = orchestrator.GetAvailableSkinDefinitions();
            WhiteBrowserSkinDefinition firstKeep = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                first,
                "ReuseKeepSkin"
            );
            WhiteBrowserSkinDefinition firstChanged = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                first,
                "ReuseChangedSkin"
            );

            File.WriteAllText(
                changedHtmlPath,
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 240;
                    thum-height : 120;
                    thum-column : 1;
                    thum-row : 1;
                  </div>
                </body>
                </html>
                """
            );
            File.SetLastWriteTimeUtc(changedHtmlPath, DateTime.UtcNow.AddSeconds(1));

            IReadOnlyList<WhiteBrowserSkinDefinition> second = orchestrator.GetAvailableSkinDefinitions();
            WhiteBrowserSkinDefinition secondKeep = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                second,
                "ReuseKeepSkin"
            );
            WhiteBrowserSkinDefinition secondChanged = WhiteBrowserSkinCatalogService.TryResolveExactByName(
                second,
                "ReuseChangedSkin"
            );

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.SameAs(second));
                Assert.That(firstKeep, Is.SameAs(secondKeep));
                Assert.That(firstChanged, Is.Not.SameAs(secondChanged));
                Assert.That(secondChanged, Is.Not.Null);
                Assert.That(secondChanged.Config.ThumbWidth, Is.EqualTo(240));
                Assert.That(
                    WhiteBrowserSkinCatalogService.GetLastCatalogLoadCoreReusedDefinitionCountForTesting(),
                    Is.EqualTo(1)
                );
            });
        }
        finally
        {
            TryDeleteDirectory(rootPath);
        }
    }

    private static WhiteBrowserSkinOrchestrator CreateOrchestrator(
        string skinRootPath,
        Func<string> getCurrentSkinNameFromViewModel,
        Action<string> setCurrentSkinNameToViewModel,
        Action<string>? selectUpperTabDefaultViewBySkinName = null
    )
    {
        return new WhiteBrowserSkinOrchestrator(
            getCurrentDbFullPath: () => "",
            getCurrentSkinNameFromViewModel: getCurrentSkinNameFromViewModel,
            setCurrentSkinNameToViewModel: setCurrentSkinNameToViewModel,
            normalizeTabStateName: skinName => string.IsNullOrWhiteSpace(skinName) ? "DefaultGrid" : skinName,
            selectUpperTabDefaultViewBySkinName: selectUpperTabDefaultViewBySkinName ?? (_ => { }),
            getCurrentUpperTabFixedIndex: () => 0,
            resolvePersistedSkinNameByTabIndex: _ => "DefaultGrid",
            resolveUpperTabStateNameByFixedIndex: _ => "DefaultGrid",
            enqueuePersistRequest: _ => true,
            skinRootPath: skinRootPath
        );
    }

    private static string CreateSkinRootWithSingleSkin(string skinName, int thumbWidth = 160)
    {
        string rootPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-skin-orchestrator-cache-{Guid.NewGuid():N}"
        );
        string skinDirectoryPath = Path.Combine(rootPath, skinName);
        Directory.CreateDirectory(skinDirectoryPath);
        File.WriteAllText(
            Path.Combine(skinDirectoryPath, $"{skinName}.htm"),
            $$"""
            <html>
            <body>
              <div id="config">
                thum-width : {{thumbWidth}};
                thum-height : 120;
                thum-column : 1;
                thum-row : 1;
              </div>
            </body>
            </html>
            """
        );
        return rootPath;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // テスト後掃除の失敗は本体判定を優先する。
        }
    }
}
