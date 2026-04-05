using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WhiteBrowserSkinEncodingNormalizerTests
{
    [Test]
    public void Normalize_repo内Utf8サンプルskinを文字化けさせずに読める()
    {
        string repoRootPath = ResolveRepoRootPath();
        string htmlPath = Path.Combine(repoRootPath, "skin", "SimpleGridWB", "SimpleGridWB.htm");

        Assert.That(File.Exists(htmlPath), Is.True, $"sample skin が見つかりません: {htmlPath}");

        WhiteBrowserSkinEncodingNormalizationResult result =
            WhiteBrowserSkinEncodingNormalizer.NormalizeFromFile(
                htmlPath,
                WhiteBrowserSkinHostPaths.BuildSkinBaseUri("SimpleGridWB")
            );

        Assert.Multiple(() =>
        {
            Assert.That(result.SourceEncodingName, Is.EqualTo("utf-8"));
            Assert.That(result.NormalizedHtml, Does.Contain("<meta charset=\"utf-8\">"));
            Assert.That(result.NormalizedHtml, Does.Contain("placeholder=\"検索ワード\""));
            Assert.That(result.NormalizedHtml, Does.Contain("読み込み中..."));
            Assert.That(result.NormalizedHtml, Does.Contain("対象がありません。"));
        });
    }

    [Test]
    public void Normalize_WhiteBrowser既定Grid実物由来fixtureを互換HTMLへ正規化できる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopy(
            ["WhiteBrowserDefaultGrid"],
            rewriteHtmlAsShiftJis: true
        );

        try
        {
            string htmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                "WhiteBrowserDefaultGrid"
            );
            string skinBaseUri = WhiteBrowserSkinHostPaths.BuildSkinBaseUri(
                "WhiteBrowserDefaultGrid"
            );

            WhiteBrowserSkinEncodingNormalizationResult result =
                WhiteBrowserSkinEncodingNormalizer.NormalizeFromFile(htmlPath, skinBaseUri);

            Assert.Multiple(() =>
            {
                Assert.That(result.SourceEncodingName, Does.StartWith("shift"));
                Assert.That(result.InjectedBaseUri, Is.EqualTo(skinBaseUri));
                Assert.That(result.RewroteCharsetMeta, Is.True);
                Assert.That(result.RewroteCompatibilityScripts, Is.True);
                Assert.That(
                    result.NormalizedHtml,
                    Does.Contain("<base href=\"https://skin.local/WhiteBrowserDefaultGrid/\">")
                );
                Assert.That(
                    result.NormalizedHtml,
                    Does.Contain("<script src=\"https://skin.local/Compat/prototype.js\"></script>")
                );
                Assert.That(
                    result.NormalizedHtml,
                    Does.Contain("<script src=\"https://skin.local/Compat/wblib-compat.js\"></script>")
                );
                Assert.That(result.NormalizedHtml, Does.Not.Contain("../prototype.js"));
                Assert.That(result.NormalizedHtml, Does.Not.Contain("../wblib.js"));
                Assert.That(result.NormalizedHtml, Does.Contain("thum-width :"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    [Test]
    public void Normalize_チュートリアル由来fixtureの主要コールバックを壊さずscript差し替えできる()
    {
        string skinRootPath = WhiteBrowserSkinTestData.CreateSkinRootCopy(
            ["TutorialCallbackGrid"],
            rewriteHtmlAsShiftJis: true
        );

        try
        {
            string htmlPath = WhiteBrowserSkinTestData.GetFixtureHtmlPath(
                skinRootPath,
                "TutorialCallbackGrid"
            );
            WhiteBrowserSkinEncodingNormalizationResult result =
                WhiteBrowserSkinEncodingNormalizer.NormalizeFromFile(
                    htmlPath,
                    WhiteBrowserSkinHostPaths.BuildSkinBaseUri("TutorialCallbackGrid")
                );

            Assert.Multiple(() =>
            {
                Assert.That(result.SourceEncodingName, Does.StartWith("shift"));
                Assert.That(result.RewroteCompatibilityScripts, Is.True);
                Assert.That(result.NormalizedHtml, Does.Contain("wb.onSkinEnter = function()"));
                Assert.That(result.NormalizedHtml, Does.Contain("wb.onUpdate = function(mvs)"));
                Assert.That(result.NormalizedHtml, Does.Contain("wb.onCreateThum = function(mv, dir)"));
                Assert.That(result.NormalizedHtml, Does.Contain("wb.onSetFocus = function(id, isFocus)"));
                Assert.That(result.NormalizedHtml, Does.Contain("wb.onSetSelect = function(id, isSel)"));
                Assert.That(result.NormalizedHtml, Does.Contain("multi-select : 1;"));
                Assert.That(result.NormalizedHtml, Does.Contain("seamless-scroll : 2;"));
            });
        }
        finally
        {
            WhiteBrowserSkinTestData.DeleteDirectorySafe(skinRootPath);
        }
    }

    private static string ResolveRepoRootPath()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IndigoMovieManager.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("リポジトリ ルートを解決できませんでした。");
    }
}
