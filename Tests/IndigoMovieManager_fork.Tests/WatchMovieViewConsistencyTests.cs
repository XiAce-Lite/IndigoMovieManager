using IndigoMovieManager;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class WatchMovieViewConsistencyTests
{
    [Test]
    public void BuildMoviePathLookup_大小文字違いを同一パスとして扱う()
    {
        HashSet<string> lookup = MainWindow.BuildMoviePathLookup(
            [
                @"D:\Movies\Sample.mp4",
                @"d:\movies\sample.mp4",
                "",
            ]
        );

        Assert.That(lookup.Count, Is.EqualTo(1));
        Assert.That(lookup.Contains(@"D:\MOVIES\Sample.mp4"), Is.True);
    }

    [Test]
    public void ShouldRepairExistingMovieView_画面ソースに無い既存動画は補正対象にする()
    {
        HashSet<string> lookup = MainWindow.BuildMoviePathLookup([@"D:\Movies\exists.mp4"]);

        bool result = MainWindow.ShouldRepairExistingMovieView(
            lookup,
            @"D:\Movies\missing.mp4"
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldRepairExistingMovieView_既に画面ソースへ載っている動画は補正しない()
    {
        HashSet<string> lookup = MainWindow.BuildMoviePathLookup([@"D:\Movies\exists.mp4"]);

        bool result = MainWindow.ShouldRepairExistingMovieView(
            lookup,
            @"d:\movies\EXISTS.mp4"
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldRepairExistingMovieView_空パスは補正しない()
    {
        HashSet<string> lookup = MainWindow.BuildMoviePathLookup([]);

        bool result = MainWindow.ShouldRepairExistingMovieView(lookup, "");

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldRefreshDisplayedMovieView_検索未使用で表示一覧に無ければ再描画する()
    {
        HashSet<string> lookup = MainWindow.BuildMoviePathLookup([@"D:\Movies\shown.mp4"]);

        bool result = MainWindow.ShouldRefreshDisplayedMovieView(
            "",
            lookup,
            @"D:\Movies\missing.mp4"
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldRefreshDisplayedMovieView_検索中は再描画しない()
    {
        HashSet<string> lookup = MainWindow.BuildMoviePathLookup([@"D:\Movies\shown.mp4"]);

        bool result = MainWindow.ShouldRefreshDisplayedMovieView(
            "sample",
            lookup,
            @"D:\Movies\missing.mp4"
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void EvaluateMovieViewConsistency_DBにあり画面ソースに無ければ画面補正を返す()
    {
        HashSet<string> existingViewMoviePaths = MainWindow.BuildMoviePathLookup(
            [@"D:\Movies\shown.mp4"]
        );
        HashSet<string> displayedMoviePaths = MainWindow.BuildMoviePathLookup(
            [@"D:\Movies\shown.mp4"]
        );

        MainWindow.MovieViewConsistencyDecision result = MainWindow.EvaluateMovieViewConsistency(
            existsInDb: true,
            existingViewMoviePaths,
            searchKeyword: "",
            displayedMoviePaths,
            movieFullPath: @"D:\Movies\missing.mp4"
        );

        Assert.That(result.ShouldRepairView, Is.True);
        Assert.That(result.ShouldRefreshDisplayedView, Is.False);
    }

    [Test]
    public void EvaluateMovieViewConsistency_画面ソースにあるが表示一覧に無ければ再描画を返す()
    {
        HashSet<string> existingViewMoviePaths = MainWindow.BuildMoviePathLookup(
            [@"D:\Movies\shown.mp4", @"D:\Movies\missing.mp4"]
        );
        HashSet<string> displayedMoviePaths = MainWindow.BuildMoviePathLookup(
            [@"D:\Movies\shown.mp4"]
        );

        MainWindow.MovieViewConsistencyDecision result = MainWindow.EvaluateMovieViewConsistency(
            existsInDb: true,
            existingViewMoviePaths,
            searchKeyword: "",
            displayedMoviePaths,
            movieFullPath: @"D:\Movies\missing.mp4"
        );

        Assert.That(result.ShouldRepairView, Is.False);
        Assert.That(result.ShouldRefreshDisplayedView, Is.True);
    }

    [Test]
    public void EvaluateMovieViewConsistency_検索中は表示補正しない()
    {
        HashSet<string> existingViewMoviePaths = MainWindow.BuildMoviePathLookup(
            [@"D:\Movies\shown.mp4", @"D:\Movies\missing.mp4"]
        );
        HashSet<string> displayedMoviePaths = MainWindow.BuildMoviePathLookup(
            [@"D:\Movies\shown.mp4"]
        );

        MainWindow.MovieViewConsistencyDecision result = MainWindow.EvaluateMovieViewConsistency(
            existsInDb: true,
            existingViewMoviePaths,
            searchKeyword: "sample",
            displayedMoviePaths,
            movieFullPath: @"D:\Movies\missing.mp4"
        );

        Assert.That(result.ShouldRepairView, Is.False);
        Assert.That(result.ShouldRefreshDisplayedView, Is.False);
    }
}
