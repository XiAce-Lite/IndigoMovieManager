using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IndigoMovieManager.Skin;

namespace IndigoMovieManager.Tests;

[TestFixture]
public class WhiteBrowserSkinCatalogServiceTests
{
    static WhiteBrowserSkinCatalogServiceTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [SetUp]
    public void SetUp()
    {
        WhiteBrowserSkinCatalogService.ResetCacheForTesting();
    }

    [Test]
    public void Load_ParsesExternalSkinConfigAndMapsPreferredTab()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string skinDirectory = Path.Combine(root, "SampleGrid");
        Directory.CreateDirectory(skinDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(skinDirectory, "SampleGrid.htm"),
                """
                <html>
                <body>
                  <div id="config">
                    skin-version : 1;
                    thum-width : 160;
                    thum-height : 120;
                    thum-column : 1;
                    thum-row : 1;
                    multi-select : 1;
                  </div>
                </body>
                </html>
                """
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> definitions =
                WhiteBrowserSkinCatalogService.Load(root);
            WhiteBrowserSkinDefinition sample = WhiteBrowserSkinCatalogService.ResolveByName(
                definitions,
                "SampleGrid"
            );

            Assert.That(sample, Is.Not.Null);
            Assert.That(sample.IsBuiltIn, Is.False);
            Assert.That(sample.Config.ThumbWidth, Is.EqualTo(160));
            Assert.That(sample.Config.ThumbHeight, Is.EqualTo(120));
            Assert.That(sample.PreferredTabStateName, Is.EqualTo("DefaultGrid"));
            Assert.That(sample.RequiresWebView2, Is.True);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ResolveByName_FallsBackToDefaultGridWhenRequestedSkinIsMissing()
    {
        IReadOnlyList<WhiteBrowserSkinDefinition> definitions =
            WhiteBrowserSkinCatalogService.Load("");

        WhiteBrowserSkinDefinition definition = WhiteBrowserSkinCatalogService.ResolveByName(
            definitions,
            "MissingSkin"
        );

        Assert.That(definition, Is.Not.Null);
        Assert.That(definition.Name, Is.EqualTo("DefaultGrid"));
    }

    [Test]
    public void Load_ParsesBooleanFlagsInConfigAsOneAndZero()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string skinDirectory = Path.Combine(root, "BooleanFlags");
        Directory.CreateDirectory(skinDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(skinDirectory, "BooleanFlags.htm"),
                """
                <html>
                <body>
                  <div id="config">
                    multi-select : true;
                    seamless-scroll : false;
                  </div>
                </body>
                </html>
                """
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> definitions =
                WhiteBrowserSkinCatalogService.Load(root);
            WhiteBrowserSkinDefinition sample = WhiteBrowserSkinCatalogService.ResolveByName(
                definitions,
                "BooleanFlags"
            );

            Assert.That(sample, Is.Not.Null);
            Assert.That(sample.Config.MultiSelect, Is.EqualTo(1));
            Assert.That(sample.Config.SeamlessScroll, Is.EqualTo(0));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Load_ReadsShiftJisSkinConfigWithoutMojibakeFallbackFailure()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string skinDirectory = Path.Combine(root, "ShiftJisSkin");
        Directory.CreateDirectory(skinDirectory);

        try
        {
            string html =
                """
                <html>
                <body>
                  <!-- 日本語コメント -->
                  <div id="config">
                    thum-width : 180;
                    thum-height : 135;
                    thum-column : 1;
                    thum-row : 1;
                  </div>
                </body>
                </html>
                """;

            File.WriteAllBytes(
                Path.Combine(skinDirectory, "ShiftJisSkin.htm"),
                Encoding.GetEncoding(932).GetBytes(html)
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> definitions =
                WhiteBrowserSkinCatalogService.Load(root);
            WhiteBrowserSkinDefinition definition =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(definitions, "ShiftJisSkin");

            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.Config.ThumbWidth, Is.EqualTo(180));
            Assert.That(definition.Config.ThumbHeight, Is.EqualTo(135));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Load_PrefersBuiltInWhenExternalSkinHasSameName()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string skinDirectory = Path.Combine(root, "DefaultGrid");
        Directory.CreateDirectory(skinDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(skinDirectory, "DefaultGrid.htm"),
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 999;
                  </div>
                </body>
                </html>
                """
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> definitions =
                WhiteBrowserSkinCatalogService.Load(root);
            WhiteBrowserSkinDefinition definition =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(definitions, "DefaultGrid");

            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.IsBuiltIn, Is.True);
            Assert.That(definition.HtmlPath, Is.Empty);
            Assert.That(definition.Config.ThumbWidth, Is.EqualTo(160));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Load_WhiteBrowser既定skin実物由来fixtureをconfigベースで安全にマップできる()
    {
        string root = WhiteBrowserSkinTestData.CreateSkinRootCopy(
            [
                "WhiteBrowserDefaultGrid",
                "WhiteBrowserDefaultBig",
                "WhiteBrowserDefaultList",
                "WhiteBrowserDefaultSmall",
            ],
            rewriteHtmlAsShiftJis: true
        );

        try
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> definitions =
                WhiteBrowserSkinCatalogService.Load(root);

            AssertFixtureDefinition(
                definitions,
                "WhiteBrowserDefaultGrid",
                expectedWidth: 160,
                expectedHeight: 120,
                expectedColumn: 1,
                expectedRow: 1,
                expectedTabStateName: "DefaultGrid"
            );
            AssertFixtureDefinition(
                definitions,
                "WhiteBrowserDefaultBig",
                expectedWidth: 200,
                expectedHeight: 150,
                expectedColumn: 3,
                expectedRow: 1,
                expectedTabStateName: "DefaultBig"
            );
            AssertFixtureDefinition(
                definitions,
                "WhiteBrowserDefaultList",
                expectedWidth: 56,
                expectedHeight: 42,
                expectedColumn: 5,
                expectedRow: 1,
                expectedTabStateName: "DefaultList"
            );
            AssertFixtureDefinition(
                definitions,
                "WhiteBrowserDefaultSmall",
                expectedWidth: 120,
                expectedHeight: 90,
                expectedColumn: 3,
                expectedRow: 1,
                expectedTabStateName: "DefaultSmall"
            );
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(root);
        }
    }

    [Test]
    public void Load_チュートリアル由来fixtureでもコールバック系skin設定を読める()
    {
        string root = WhiteBrowserSkinTestData.CreateSkinRootCopy(
            ["TutorialCallbackGrid"],
            rewriteHtmlAsShiftJis: true
        );

        try
        {
            IReadOnlyList<WhiteBrowserSkinDefinition> definitions =
                WhiteBrowserSkinCatalogService.Load(root);
            WhiteBrowserSkinDefinition definition =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(
                    definitions,
                    "TutorialCallbackGrid"
                );

            Assert.That(definition, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(definition.IsBuiltIn, Is.False);
                Assert.That(definition.RequiresWebView2, Is.True);
                Assert.That(definition.Config.MultiSelect, Is.EqualTo(1));
                Assert.That(definition.Config.SeamlessScroll, Is.EqualTo(2));
                Assert.That(definition.Config.ThumbWidth, Is.EqualTo(160));
                Assert.That(definition.Config.ThumbHeight, Is.EqualTo(120));
                Assert.That(definition.PreferredTabStateName, Is.EqualTo("DefaultGrid"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(root);
        }
    }

    [Test]
    public void Load_同じroot連続呼び出しではcatalogを再読込しない()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string skinDirectory = Path.Combine(root, "CachedGrid");
        Directory.CreateDirectory(skinDirectory);

        try
        {
            File.WriteAllText(
                Path.Combine(skinDirectory, "CachedGrid.htm"),
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 160;
                    thum-height : 120;
                  </div>
                </body>
                </html>
                """
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> first = WhiteBrowserSkinCatalogService.Load(root);
            IReadOnlyList<WhiteBrowserSkinDefinition> second = WhiteBrowserSkinCatalogService.Load(root);

            Assert.Multiple(() =>
            {
                Assert.That(second, Is.SameAs(first));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(1));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Load_html更新後はCatalogCacheを無効化して再読込する()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string skinDirectory = Path.Combine(root, "ReloadedGrid");
        string htmlPath = Path.Combine(skinDirectory, "ReloadedGrid.htm");
        Directory.CreateDirectory(skinDirectory);

        try
        {
            File.WriteAllText(
                htmlPath,
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 160;
                  </div>
                </body>
                </html>
                """
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> first = WhiteBrowserSkinCatalogService.Load(root);
            WhiteBrowserSkinDefinition firstDefinition =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(first, "ReloadedGrid");

            System.Threading.Thread.Sleep(1100);
            File.WriteAllText(
                htmlPath,
                """
                <html>
                <body>
                  <div id="config">
                    thum-width : 220;
                  </div>
                </body>
                </html>
                """
            );

            IReadOnlyList<WhiteBrowserSkinDefinition> second = WhiteBrowserSkinCatalogService.Load(root);
            WhiteBrowserSkinDefinition secondDefinition =
                WhiteBrowserSkinCatalogService.TryResolveExactByName(second, "ReloadedGrid");

            Assert.Multiple(() =>
            {
                Assert.That(firstDefinition, Is.Not.Null);
                Assert.That(secondDefinition, Is.Not.Null);
                Assert.That(firstDefinition.Config.ThumbWidth, Is.EqualTo(160));
                Assert.That(secondDefinition.Config.ThumbWidth, Is.EqualTo(220));
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(WhiteBrowserSkinCatalogService.GetCatalogLoadMissCountForTesting(), Is.EqualTo(2));
            });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void PersistCurrentSkinState_外部Skin時はSystemとProfileをenqueueする()
    {
        List<WhiteBrowserSkinStatePersistRequest> requests = [];
        WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
            currentSkinName: "SampleExternalSkin",
            currentTabIndex: 3,
            enqueuePersistRequest: request =>
            {
                requests.Add(request);
                return true;
            }
        );

        orchestrator.PersistCurrentSkinState(@"C:\temp\sample.wb");

        Assert.That(requests.Count, Is.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(requests[0].TargetKind, Is.EqualTo(WhiteBrowserSkinStatePersistTargetKind.System));
            Assert.That(requests[0].Key, Is.EqualTo("skin"));
            Assert.That(requests[0].Value, Is.EqualTo("SampleExternalSkin"));
            Assert.That(requests[1].TargetKind, Is.EqualTo(WhiteBrowserSkinStatePersistTargetKind.Profile));
            Assert.That(requests[1].ProfileName, Is.EqualTo("SampleExternalSkin"));
            Assert.That(requests[1].Key, Is.EqualTo("LastUpperTab"));
            Assert.That(requests[1].Value, Is.EqualTo("DefaultList"));
        });
    }

    [Test]
    public void PersistCurrentSkinState_組み込みSkin時はSystemだけenqueueする()
    {
        List<WhiteBrowserSkinStatePersistRequest> requests = [];
        WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
            currentSkinName: "DefaultBig",
            currentTabIndex: 1,
            enqueuePersistRequest: request =>
            {
                requests.Add(request);
                return true;
            }
        );

        orchestrator.PersistCurrentSkinState(@"C:\temp\sample.wb");

        Assert.That(requests.Count, Is.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(requests[0].TargetKind, Is.EqualTo(WhiteBrowserSkinStatePersistTargetKind.System));
            Assert.That(requests[0].Key, Is.EqualTo("skin"));
            Assert.That(requests[0].Value, Is.EqualTo("DefaultBig"));
        });
    }

    [Test]
    public void Orchestrator_KeepsUnknownExternalSkinNameAsRawValue()
    {
        WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
            currentSkinName: "MissingExternalSkin",
            skinRootPath: Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
        );

        string normalized = orchestrator.NormalizeStoredSkinName("MissingExternalSkin");
        WhiteBrowserSkinDefinition current = orchestrator.GetCurrentSkinDefinition();

        Assert.That(normalized, Is.EqualTo("MissingExternalSkin"));
        Assert.That(orchestrator.GetCurrentSkinName(), Is.EqualTo("MissingExternalSkin"));
        Assert.That(current, Is.Not.Null);
        Assert.That(current.IsBuiltIn, Is.False);
        Assert.That(current.IsMissing, Is.True);
        Assert.That(current.Name, Is.EqualTo("MissingExternalSkin"));
    }

    [Test]
    public void Orchestrator_ExposesMissingCurrentSkinInAvailableDefinitions()
    {
        WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
            currentSkinName: "MissingExternalSkin",
            skinRootPath: Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())
        );

        IReadOnlyList<WhiteBrowserSkinDefinition> definitions = orchestrator.GetAvailableSkinDefinitions();
        WhiteBrowserSkinDefinition definition =
            WhiteBrowserSkinCatalogService.TryResolveExactByName(definitions, "MissingExternalSkin");

        Assert.That(definition, Is.Not.Null);
        Assert.That(definition.IsMissing, Is.True);
    }

    [Test]
    public void Orchestrator_PreservesDefaultBig10RoundTripForCurrentState()
    {
        WhiteBrowserSkinOrchestrator orchestrator = CreateOrchestrator(
            currentSkinName: "DefaultBig10",
            currentTabIndex: 4,
            resolvePersistedSkinNameByTabIndex: tabIndex => tabIndex == 4 ? "DefaultBig10" : "DefaultGrid",
            resolveUpperTabStateNameByFixedIndex: tabIndex => tabIndex == 4 ? "DefaultBig10" : "DefaultGrid"
        );

        string persisted = orchestrator.ResolvePersistedSkinNameForCurrentState();

        Assert.That(persisted, Is.EqualTo("DefaultBig10"));
    }

    private static WhiteBrowserSkinOrchestrator CreateOrchestrator(
        string currentSkinName,
        int currentTabIndex = 2,
        string skinRootPath = "",
        Func<int, string>? resolvePersistedSkinNameByTabIndex = null,
        Func<int, string>? resolveUpperTabStateNameByFixedIndex = null,
        Func<WhiteBrowserSkinStatePersistRequest, bool>? enqueuePersistRequest = null
    )
    {
        return new WhiteBrowserSkinOrchestrator(
            getCurrentDbFullPath: () => "",
            getCurrentSkinNameFromViewModel: () => currentSkinName,
            setCurrentSkinNameToViewModel: _ => { },
            normalizeTabStateName: skinName => skinName,
            selectUpperTabDefaultViewBySkinName: _ => { },
            getCurrentUpperTabFixedIndex: () => currentTabIndex,
            resolvePersistedSkinNameByTabIndex:
                resolvePersistedSkinNameByTabIndex ?? (tabIndex => tabIndex switch
                {
                    0 => "DefaultSmall",
                    1 => "DefaultBig",
                    2 => "DefaultGrid",
                    3 => "DefaultList",
                    4 => "DefaultBig10",
                    _ => "DefaultGrid",
                }),
            resolveUpperTabStateNameByFixedIndex:
                resolveUpperTabStateNameByFixedIndex ?? (tabIndex => tabIndex switch
                {
                    0 => "DefaultSmall",
                    1 => "DefaultBig",
                    2 => "DefaultGrid",
                    3 => "DefaultList",
                    4 => "DefaultBig10",
                    _ => "DefaultGrid",
                }),
            enqueuePersistRequest: enqueuePersistRequest ?? (_ => true),
            skinRootPath: skinRootPath
        );
    }

    private static void AssertFixtureDefinition(
        IReadOnlyList<WhiteBrowserSkinDefinition> definitions,
        string skinName,
        int expectedWidth,
        int expectedHeight,
        int expectedColumn,
        int expectedRow,
        string expectedTabStateName
    )
    {
        WhiteBrowserSkinDefinition definition =
            WhiteBrowserSkinCatalogService.TryResolveExactByName(definitions, skinName);

        Assert.That(definition, Is.Not.Null, $"fixture が見つかりません: {skinName}");
        Assert.Multiple(() =>
        {
            Assert.That(definition.IsBuiltIn, Is.False);
            Assert.That(definition.Config.ThumbWidth, Is.EqualTo(expectedWidth));
            Assert.That(definition.Config.ThumbHeight, Is.EqualTo(expectedHeight));
            Assert.That(definition.Config.ThumbColumn, Is.EqualTo(expectedColumn));
            Assert.That(definition.Config.ThumbRow, Is.EqualTo(expectedRow));
            Assert.That(definition.PreferredTabStateName, Is.EqualTo(expectedTabStateName));
        });
    }
}
