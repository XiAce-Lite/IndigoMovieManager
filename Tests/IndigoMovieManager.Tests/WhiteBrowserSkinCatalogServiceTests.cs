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
        Func<int, string>? resolveUpperTabStateNameByFixedIndex = null
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
            skinRootPath: skinRootPath
        );
    }
}
