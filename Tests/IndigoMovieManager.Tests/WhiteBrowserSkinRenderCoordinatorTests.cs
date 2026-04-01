using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WhiteBrowserSkinRenderCoordinatorTests
{
    [Test]
    public void BuildInitialDocument_WhiteBrowser既定List実物由来fixtureからbase情報を組み立てられる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopy(
            ["WhiteBrowserDefaultList"],
            rewriteHtmlAsShiftJis: true
        );

        try
        {
            string htmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                "WhiteBrowserDefaultList"
            );
            WhiteBrowserSkinRenderCoordinator coordinator = new();

            WhiteBrowserSkinRenderDocument document = coordinator.BuildInitialDocument(
                skinRootPath,
                htmlPath
            );

            Assert.Multiple(() =>
            {
                Assert.That(
                    document.SkinBaseUri,
                    Is.EqualTo("https://skin.local/WhiteBrowserDefaultList/")
                );
                Assert.That(
                    document.ThumbnailBaseUri,
                    Is.EqualTo(WhiteBrowserSkinHostPaths.BuildThumbnailBaseUri())
                );
                Assert.That(document.SourceEncodingName, Does.StartWith("shift"));
                Assert.That(document.Html, Does.Contain("scroll-id : scroll;"));
                Assert.That(
                    document.Html,
                    Does.Contain("<base href=\"https://skin.local/WhiteBrowserDefaultList/\">")
                );
                Assert.That(
                    document.Html,
                    Does.Contain("<script src=\"https://skin.local/Compat/wblib-compat.js\"></script>")
                );
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }
}
