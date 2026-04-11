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

    [Test]
    public void BuildInitialDocument_同じskinHtmlでは正規化済みdocumentを再利用できる()
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

            WhiteBrowserSkinRenderDocument first = coordinator.BuildInitialDocument(
                skinRootPath,
                htmlPath
            );
            WhiteBrowserSkinRenderDocument second = coordinator.BuildInitialDocument(
                skinRootPath,
                htmlPath
            );

            Assert.That(ReferenceEquals(first, second), Is.True);
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public void BuildInitialDocument_skinHtml更新時はキャッシュを差し替える()
    {
        string skinRootPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-render-coordinator-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(skinRootPath);
        string htmlPath = Path.Combine(skinRootPath, "skin.htm");

        try
        {
            File.WriteAllText(
                htmlPath,
                """
                <html>
                <head>
                <meta charset="utf-8">
                </head>
                <body>first</body>
                </html>
                """
            );
            File.SetLastWriteTimeUtc(htmlPath, new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc));

            WhiteBrowserSkinRenderCoordinator coordinator = new();
            WhiteBrowserSkinRenderDocument first = coordinator.BuildInitialDocument(
                skinRootPath,
                htmlPath
            );

            File.WriteAllText(
                htmlPath,
                """
                <html>
                <head>
                <meta charset="utf-8">
                </head>
                <body>second-updated</body>
                </html>
                """
            );
            File.SetLastWriteTimeUtc(htmlPath, new DateTime(2026, 4, 12, 0, 0, 5, DateTimeKind.Utc));

            WhiteBrowserSkinRenderDocument second = coordinator.BuildInitialDocument(
                skinRootPath,
                htmlPath
            );

            Assert.Multiple(() =>
            {
                Assert.That(ReferenceEquals(first, second), Is.False);
                Assert.That(first.Html, Does.Contain("first"));
                Assert.That(second.Html, Does.Contain("second-updated"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }
}
