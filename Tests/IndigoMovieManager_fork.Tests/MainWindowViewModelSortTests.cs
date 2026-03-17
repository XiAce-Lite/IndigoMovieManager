using System.Linq;
using System.Threading;
using IndigoMovieManager;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class MainWindowViewModelSortTests
{
    [Test]
    public void SortLists_エラー順を含む()
    {
        MainWindowViewModel viewModel = new();

        Assert.That(
            viewModel.SortLists.Any(x => x.Id == "28" && x.Name == "エラー(多い順)"),
            Is.True
        );
    }

    [Test]
    public void エラーソートはplaceholder数が多い順で並ぶ()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords noError = CreateMovie("C");
        MovieRecords oneError = CreateMovie("B", thumbPathSmall: @"C:\app\Images\errorSmall.jpg");
        MovieRecords twoErrors = CreateMovie(
            "A",
            thumbPathSmall: @"C:\app\Images\errorSmall.jpg",
            thumbDetail: @"C:\app\Images\errorGrid.jpg"
        );

        MovieRecords[] sorted = viewModel.SortMovies([noError, oneError, twoErrors], "28").ToArray();

        Assert.That(sorted, Is.EqualTo([twoErrors, oneError, noError]));
    }

    [Test]
    public void エラーソート同数時は名前順で安定させる()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords movieB = CreateMovie("b", thumbPathSmall: @"C:\app\Images\errorSmall.jpg");
        MovieRecords movieA = CreateMovie("A", thumbDetail: @"C:\app\Images\errorGrid.jpg");

        MovieRecords[] sorted = viewModel.SortMovies([movieB, movieA], "28").ToArray();

        Assert.That(sorted, Is.EqualTo([movieA, movieB]));
    }

    private static MovieRecords CreateMovie(
        string name,
        string thumbPathSmall = @"C:\thumbs\ok-small.jpg",
        string thumbDetail = @"C:\thumbs\ok-detail.jpg"
    )
    {
        return new MovieRecords
        {
            Movie_Name = name,
            Movie_Path = $@"C:\movies\{name}.mp4",
            ThumbPathSmall = thumbPathSmall,
            ThumbPathBig = @"C:\thumbs\ok-big.jpg",
            ThumbPathGrid = @"C:\thumbs\ok-grid.jpg",
            ThumbPathList = @"C:\thumbs\ok-list.jpg",
            ThumbPathBig10 = @"C:\thumbs\ok-big10.jpg",
            ThumbDetail = thumbDetail,
        };
    }
}
