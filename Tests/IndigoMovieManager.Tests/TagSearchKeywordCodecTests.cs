using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class TagSearchKeywordCodecTests
{
    [Test]
    public void BuildKeyword_空白を含むタグでもexact_tag構文へ正規化できる()
    {
        string actual = TagSearchKeywordCodec.BuildKeyword(["シリーズ A", "主演"]);

        Assert.That(actual, Is.EqualTo("!tag:\"シリーズ A\" !tag:主演"));
    }

    [Test]
    public void TryParsePureTagQuery_複数タグと空白入りタグを復元できる()
    {
        bool parsed = TagSearchKeywordCodec.TryParsePureTagQuery(
            "!tag:\"シリーズ A\" !tag:主演",
            out string[] actual
        );

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(actual, Is.EqualTo(["シリーズ A", "主演"]));
        });
    }

    [Test]
    public void ExtractActiveTags_自由入力だけならchecked対象としては拾わない()
    {
        string[] actual = TagSearchKeywordCodec.ExtractActiveTags("idol beta");

        Assert.That(actual, Is.Empty);
    }

    [Test]
    public void ExtractActiveTagsForUi_単純検索1語ならchecked対象として拾える()
    {
        string[] actual = TagSearchKeywordCodec.ExtractActiveTagsForUi("主演");

        Assert.That(actual, Is.EqualTo(["主演"]));
    }

    [Test]
    public void ExtractActiveTagsForUi_exact_tagがあれば自由入力候補よりexact_tagを優先する()
    {
        string[] actual = TagSearchKeywordCodec.ExtractActiveTagsForUi(
            "\"青い 空\" !tag:\"シリーズ A\""
        );

        Assert.That(actual, Is.EqualTo(["シリーズ A"]));
    }

    [Test]
    public void TryResolveSingleTag_単一exact_tag構文ならタグ名へ戻せる()
    {
        bool resolved = TagSearchKeywordCodec.TryResolveSingleTag(
            "!tag:\"シリーズ A\"",
            out string actual
        );

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(actual, Is.EqualTo("シリーズ A"));
        });
    }

    [Test]
    public void ReplaceTagFilters_自由入力を保持したままexact_tagを差し替えられる()
    {
        string actual = TagSearchKeywordCodec.ReplaceTagFilters(
            "idol sort:recent !tag:beta",
            ["シリーズ A", "主演"]
        );

        Assert.That(actual, Is.EqualTo("idol sort:recent !tag:\"シリーズ A\" !tag:主演"));
    }

    [Test]
    public void ReplaceTagFilters_quoted_phraseを保持したままexact_tagを差し替えられる()
    {
        string actual = TagSearchKeywordCodec.ReplaceTagFilters(
            "\"青い 空\" !tag:beta",
            ["シリーズ A"]
        );

        Assert.That(actual, Is.EqualTo("\"青い 空\" !tag:\"シリーズ A\""));
    }

    [Test]
    public void ReplaceTagFilters_否定quoted_phraseを保持したままexact_tagを差し替えられる()
    {
        string actual = TagSearchKeywordCodec.ReplaceTagFilters(
            "-\"青い 空\" !tag:beta",
            ["シリーズ A"]
        );

        Assert.That(actual, Is.EqualTo("-\"青い 空\" !tag:\"シリーズ A\""));
    }

    [Test]
    public void ExtractActiveTags_混在クエリからexact_tagだけ取り出せる()
    {
        string[] actual = TagSearchKeywordCodec.ExtractActiveTags(
            "idol !tag:\"シリーズ A\" !tag:主演"
        );

        Assert.That(actual, Is.EqualTo(["シリーズ A", "主演"]));
    }

    [Test]
    public void TryResolveSingleTag_単純検索1語は一括タグ付け用に単一タグとして扱える()
    {
        bool resolved = TagSearchKeywordCodec.TryResolveSingleTag("主演", out string actual);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(actual, Is.EqualTo("主演"));
        });
    }

    [Test]
    public void TryResolveTagAssignmentCandidate_単一exact_tagと自由入力混在でもexact_tagを優先できる()
    {
        bool resolved = TagSearchKeywordCodec.TryResolveTagAssignmentCandidate(
            "\"青い 空\" !tag:\"シリーズ A\"",
            out string actual
        );

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(actual, Is.EqualTo("シリーズ A"));
        });
    }

    [Test]
    public void TryResolveTagAssignmentCandidate_複数exact_tagなら曖昧として弾く()
    {
        bool resolved = TagSearchKeywordCodec.TryResolveTagAssignmentCandidate(
            "!tag:\"シリーズ A\" !tag:主演",
            out string actual
        );

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.False);
            Assert.That(actual, Is.Empty);
        });
    }
}
