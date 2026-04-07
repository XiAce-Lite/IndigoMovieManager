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
    public void FilterMovies_!notagでタグ未設定だけ返す()
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
