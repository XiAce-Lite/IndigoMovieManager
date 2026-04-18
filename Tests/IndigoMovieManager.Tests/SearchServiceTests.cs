using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class SearchServiceTests
{
    [Test]
    public void FilterMovies_通常検索でタグにもヒットする()
    {
        MovieRecords target = CreateMovie("target", tags: "出演者/日本人");
        MovieRecords other = CreateMovie("other", tags: "出演者/海外");

        MovieRecords[] actual = SearchService.FilterMovies([target, other], "日本人").ToArray();

        Assert.That(actual, Is.EqualTo([target]));
    }

    [Test]
    public void FilterMovies_タグ専用構文で完全一致タグに絞れる()
    {
        MovieRecords target = CreateMovie("target", tags: "猫\n出演者/日本人");
        MovieRecords other = CreateMovie("other", tags: "猫好き\n出演者/日本人");

        MovieRecords[] actual = SearchService.FilterMovies([target, other], "!tag:猫").ToArray();

        Assert.That(actual, Is.EqualTo([target]));
    }

    [Test]
    public void FilterMovies_空白入りexact_tag構文で完全一致タグに絞れる()
    {
        MovieRecords target = CreateMovie("target", tags: "シリーズ A\n主演");
        MovieRecords other = CreateMovie("other", tags: "シリーズ\nA");

        MovieRecords[] actual = SearchService
            .FilterMovies([target, other], "!tag:\"シリーズ A\"")
            .ToArray();

        Assert.That(actual, Is.EqualTo([target]));
    }

    [Test]
    public void FilterMovies_複数exact_tag構文なら両方を持つものだけ返す()
    {
        MovieRecords target = CreateMovie("target", tags: "シリーズ A\n主演");
        MovieRecords onlySeries = CreateMovie("only-series", tags: "シリーズ A");
        MovieRecords onlyLead = CreateMovie("only-lead", tags: "主演");

        MovieRecords[] actual = SearchService
            .FilterMovies([target, onlySeries, onlyLead], "!tag:\"シリーズ A\" !tag:主演")
            .ToArray();

        Assert.That(actual, Is.EqualTo([target]));
    }

    [Test]
    public void FilterMovies_自由入力とexact_tag構文を同時に満たすものだけ返す()
    {
        MovieRecords target = CreateMovie("idol-target", tags: "シリーズ A\n主演");
        MovieRecords wrongTag = CreateMovie("idol-wrong-tag", tags: "主演");
        MovieRecords wrongText = CreateMovie("other-target", tags: "シリーズ A\n主演");

        MovieRecords[] actual = SearchService
            .FilterMovies([target, wrongTag, wrongText], "idol !tag:\"シリーズ A\"")
            .ToArray();

        Assert.That(actual, Is.EqualTo([target]));
    }

    [Test]
    public void FilterMovies_quoted_phraseとexact_tag構文を同時に満たすものだけ返す()
    {
        MovieRecords target = CreateMovie(
            "target",
            tags: "シリーズ A",
            comment1: "青い 空 のメモ"
        );
        MovieRecords wrongTag = CreateMovie(
            "wrong-tag",
            tags: "主演",
            comment1: "青い 空 のメモ"
        );
        MovieRecords wrongText = CreateMovie(
            "wrong-text",
            tags: "シリーズ A",
            comment1: "赤い 花 のメモ"
        );

        MovieRecords[] actual = SearchService
            .FilterMovies([target, wrongTag, wrongText], "\"青い 空\" !tag:\"シリーズ A\"")
            .ToArray();

        Assert.That(actual, Is.EqualTo([target]));
    }

    [Test]
    public void FilterMovies_否定quoted_phraseとexact_tag構文を同時に満たすものだけ返す()
    {
        MovieRecords target = CreateMovie(
            "target",
            tags: "シリーズ A",
            comment1: "赤い 花 のメモ"
        );
        MovieRecords excluded = CreateMovie(
            "excluded",
            tags: "シリーズ A",
            comment1: "青い 空 のメモ"
        );

        MovieRecords[] actual = SearchService
            .FilterMovies([target, excluded], "-\"青い 空\" !tag:\"シリーズ A\"")
            .ToArray();

        Assert.That(actual, Is.EqualTo([target]));
    }

    [Test]
    public void FilterMovies_notag構文でタグ未設定だけ返す()
    {
        MovieRecords target = CreateMovie("target");
        MovieRecords other = CreateMovie("other", tags: "シリーズA");

        MovieRecords[] actual = SearchService.FilterMovies([target, other], "!notag").ToArray();

        Assert.That(actual, Is.EqualTo([target]));
    }

    [Test]
    public void FilterMovies_フレーズ検索でコメントにもヒットする()
    {
        MovieRecords target = CreateMovie("target", comment1: "青い空のメモ");
        MovieRecords other = CreateMovie("other", comment1: "赤い花のメモ");

        MovieRecords[] actual = SearchService.FilterMovies([target, other], "\"青い空\"").ToArray();

        Assert.That(actual, Is.EqualTo([target]));
    }

    [Test]
    public void FilterMovies_OR検索でどちらかにヒットする()
    {
        MovieRecords first = CreateMovie("tokyo-love");
        MovieRecords second = CreateMovie("osaka-love");
        MovieRecords third = CreateMovie("nagoya-love");

        MovieRecords[] actual = SearchService
            .FilterMovies([first, second, third], "tokyo | nagoya")
            .ToArray();

        Assert.That(actual, Is.EqualTo([first, third]));
    }

    [Test]
    public void FilterMovies_重複検索で同一hashだけ返す()
    {
        MovieRecords first = CreateMovie("first", hash: "same");
        MovieRecords second = CreateMovie("second", hash: "same");
        MovieRecords other = CreateMovie("other", hash: "unique");

        MovieRecords[] actual = SearchService
            .FilterMovies([first, second, other], "{dup}")
            .ToArray();

        Assert.That(actual, Is.EqualTo([first, second]));
    }

    [Test]
    public void IsDuplicateSearchKeyword_dup構文だけTrueを返す()
    {
        Assert.That(SearchService.IsDuplicateSearchKeyword("{dup}"), Is.True);
        Assert.That(SearchService.IsDuplicateSearchKeyword(" { DUP } "), Is.True);
        Assert.That(SearchService.IsDuplicateSearchKeyword("{notag}"), Is.False);
        Assert.That(SearchService.IsDuplicateSearchKeyword("dup"), Is.False);
        Assert.That(SearchService.IsDuplicateSearchKeyword(""), Is.False);
    }

    [Test]
    public void IsTagOnlySearchKeyword_タグ専用構文だけTrueを返す()
    {
        Assert.That(SearchService.IsTagOnlySearchKeyword("!tag:猫"), Is.True);
        Assert.That(SearchService.IsTagOnlySearchKeyword("!tag:\"シリーズ A\""), Is.True);
        Assert.That(SearchService.IsTagOnlySearchKeyword("!notag"), Is.True);
        Assert.That(SearchService.IsTagOnlySearchKeyword("{notag}"), Is.True);
        Assert.That(SearchService.IsTagOnlySearchKeyword("idol !tag:猫"), Is.False);
        Assert.That(SearchService.IsTagOnlySearchKeyword("{dup}"), Is.False);
    }

    [Test]
    public void FilterMovies_検索対象更新後は古い検索キャッシュを使わない()
    {
        MovieRecords target = CreateMovie("target", comment1: "青い空");

        MovieRecords[] first = SearchService.FilterMovies([target], "青い").ToArray();
        target.Comment1 = "赤い花";
        MovieRecords[] second = SearchService.FilterMovies([target], "青い").ToArray();
        MovieRecords[] third = SearchService.FilterMovies([target], "赤い").ToArray();

        Assert.That(first, Is.EqualTo([target]));
        Assert.That(second, Is.Empty);
        Assert.That(third, Is.EqualTo([target]));
    }

    [Test]
    public void FilterMovies_タグ更新後は古いタグキャッシュを使わない()
    {
        MovieRecords target = CreateMovie("target", tags: "犬");

        MovieRecords[] first = SearchService.FilterMovies([target], "!tag:犬").ToArray();
        target.Tags = "猫";
        MovieRecords[] second = SearchService.FilterMovies([target], "!tag:犬").ToArray();
        MovieRecords[] third = SearchService.FilterMovies([target], "!tag:猫").ToArray();

        Assert.That(first, Is.EqualTo([target]));
        Assert.That(second, Is.Empty);
        Assert.That(third, Is.EqualTo([target]));
    }

    private static MovieRecords CreateMovie(
        string name,
        string tags = "",
        string comment1 = "",
        string hash = ""
    )
    {
        return new MovieRecords
        {
            Movie_Name = name,
            Movie_Path = $@"C:\movies\{name}.mp4",
            Tags = tags,
            Comment1 = comment1,
            Hash = hash,
        };
    }
}
