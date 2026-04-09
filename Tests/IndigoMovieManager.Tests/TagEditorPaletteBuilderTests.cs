using IndigoMovieManager.BottomTabs.TagEditor;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class TagEditorPaletteBuilderTests
{
    [Test]
    public void BuildTagEditorPaletteItemsCore_固定タグ_選択動画タグ_集計タグの順でユニークに並ぶ()
    {
        TagEditorPaletteItem[] actual = MainWindow.BuildTagEditorPaletteItemsCore(
            ["★", "★★"],
            ["主演", "★", "シリーズA", "主演"],
            new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase)
            {
                ["シリーズA"] = 8,
                ["主演"] = 5,
                ["新作"] = 9,
                ["★★"] = 3,
            },
            ["シリーズA"]
        );

        Assert.Multiple(() =>
        {
            Assert.That(
                actual.Select(x => x.TagName).ToArray(),
                Is.EqualTo(["★", "★★", "主演", "シリーズA", "新作"])
            );
            Assert.That(
                actual.Select(x => x.DisplayLabel).ToArray(),
                Is.EqualTo(["★", "★★", "主演", "シリーズA", "新作 (9)"])
            );
            Assert.That(actual.Single(x => x.TagName == "シリーズA").IsActive, Is.True);
        });
    }

    [Test]
    public void BuildTagEditorPaletteItemsCore_集計なしでも固定と選択動画タグだけ返す()
    {
        TagEditorPaletteItem[] actual = MainWindow.BuildTagEditorPaletteItemsCore(
            ["★"],
            ["タグA", "タグB", "タグA"],
            null,
            []
        );

        Assert.That(actual.Select(x => x.TagName).ToArray(), Is.EqualTo(["★", "タグA", "タグB"]));
    }
}
