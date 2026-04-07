using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowViewModelSearchTests
{
    [Test]
    public void FilterMovies_カタカナ検索でひらがなkana列にヒットする()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords target = CreateMovie("tokyo-love", kana: "とうきょうらぶすとーりー");
        MovieRecords other = CreateMovie("osaka-love", kana: "おおさからぶすとーりー");

        MovieRecords[] actual = viewModel.FilterMovies([target, other], "トウキョウ").ToArray();

        Assert.That(actual, Is.EqualTo([target]));
    }

    [Test]
    public void FilterMovies_ローマ字検索で長音を縮めた入力にもヒットする()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords target = CreateMovie("tokyo-love", kana: "とうきょうらぶすとーりー");
        MovieRecords other = CreateMovie("nagoya-love", kana: "なごやらぶすとーりー");

        MovieRecords[] actual = viewModel.FilterMovies([target, other], "tokyo").ToArray();

        Assert.That(actual, Is.EqualTo([target]));
    }

    [Test]
    public void FilterMovies_roma列が空でも名前からローマ字検索できる()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords target = CreateMovie("かなものがたり", kana: "", roma: "");
        MovieRecords other = CreateMovie("べつさく", kana: "", roma: "");

        MovieRecords[] actual = viewModel.FilterMovies([target, other], "kana").ToArray();

        Assert.That(actual, Is.EqualTo([target]));
    }

    [Test]
    public void FilterMovies_NOT検索でもローマ字列を除外対象に含める()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords target = CreateMovie("tokyo-love", kana: "とうきょうらぶすとーりー");
        MovieRecords other = CreateMovie("osaka-love", kana: "おおさからぶすとーりー");

        MovieRecords[] actual = viewModel.FilterMovies([target, other], "love -tokyo").ToArray();

        Assert.That(actual, Is.EqualTo([other]));
    }

    private static MovieRecords CreateMovie(string name, string kana, string roma = "")
    {
        return new MovieRecords
        {
            Movie_Name = name,
            Movie_Path = $@"C:\movies\{name}.mp4",
            Kana = kana,
            Roma = roma,
        };
    }
}