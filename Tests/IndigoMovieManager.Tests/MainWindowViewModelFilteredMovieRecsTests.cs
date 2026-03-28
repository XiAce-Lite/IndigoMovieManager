using System.Threading;
using IndigoMovieManager;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class MainWindowViewModelFilteredMovieRecsTests
{
    [Test]
    public void 同一順序なら無変更で終わる()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords movieA = CreateMovie("A");
        MovieRecords movieB = CreateMovie("B");
        MovieRecords movieC = CreateMovie("C");

        _ = viewModel.ReplaceFilteredMovieRecs([movieA, movieB, movieC]);

        FilteredMovieRecsUpdateResult result = viewModel.ReplaceFilteredMovieRecs(
            [movieA, movieB, movieC]
        );

        Assert.That(result.HasChanges, Is.False);
        Assert.That(result.RetainedPrefixCount, Is.EqualTo(3));
        Assert.That(result.RetainedSuffixCount, Is.EqualTo(0));
        Assert.That(result.MovedCount, Is.EqualTo(0));
        Assert.That(viewModel.FilteredMovieRecs, Is.EqualTo([movieA, movieB, movieC]));
    }

    [Test]
    public void 共通prefixとsuffixを残して中間だけ差し替える()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords movieA = CreateMovie("A");
        MovieRecords movieB = CreateMovie("B");
        MovieRecords movieC = CreateMovie("C");
        MovieRecords movieD = CreateMovie("D");
        MovieRecords movieE = CreateMovie("E");
        MovieRecords movieX = CreateMovie("X");
        MovieRecords movieY = CreateMovie("Y");

        _ = viewModel.ReplaceFilteredMovieRecs([movieA, movieB, movieC, movieD, movieE]);

        FilteredMovieRecsUpdateResult result = viewModel.ReplaceFilteredMovieRecs(
            [movieA, movieX, movieY, movieE]
        );

        Assert.That(result.HasChanges, Is.True);
        Assert.That(result.RetainedPrefixCount, Is.EqualTo(1));
        Assert.That(result.RetainedSuffixCount, Is.EqualTo(1));
        Assert.That(result.RemovedCount, Is.EqualTo(3));
        Assert.That(result.InsertedCount, Is.EqualTo(2));
        Assert.That(result.MovedCount, Is.EqualTo(0));
        Assert.That(viewModel.FilteredMovieRecs, Is.EqualTo([movieA, movieX, movieY, movieE]));
    }

    [Test]
    public void sort_onlyはMove中心で並び替える()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords movieA = CreateMovie("A");
        MovieRecords movieB = CreateMovie("B");
        MovieRecords movieC = CreateMovie("C");
        MovieRecords movieD = CreateMovie("D");

        _ = viewModel.ReplaceFilteredMovieRecs([movieA, movieB, movieC, movieD]);

        FilteredMovieRecsUpdateResult result = viewModel.ReplaceFilteredMovieRecs(
            [movieD, movieB, movieA, movieC],
            updateMode: FilteredMovieRecsUpdateMode.Move
        );

        Assert.That(result.HasChanges, Is.True);
        Assert.That(result.RemovedCount, Is.EqualTo(0));
        Assert.That(result.InsertedCount, Is.EqualTo(0));
        Assert.That(result.MovedCount, Is.GreaterThan(0));
        Assert.That(viewModel.FilteredMovieRecs, Is.EqualTo([movieD, movieB, movieA, movieC]));
    }

    [Test]
    public void Resetモードは全件入れ直し経路へ落ちる()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords movieA = CreateMovie("A");
        MovieRecords movieB = CreateMovie("B");
        MovieRecords movieC = CreateMovie("C");

        _ = viewModel.ReplaceFilteredMovieRecs([movieA, movieB, movieC]);

        FilteredMovieRecsUpdateResult result = viewModel.ReplaceFilteredMovieRecs(
            [movieC, movieB, movieA],
            updateMode: FilteredMovieRecsUpdateMode.Reset
        );

        Assert.That(result.HasChanges, Is.True);
        Assert.That(result.MovedCount, Is.EqualTo(0));
        Assert.That(result.RemovedCount, Is.EqualTo(3));
        Assert.That(result.InsertedCount, Is.EqualTo(3));
        Assert.That(viewModel.FilteredMovieRecs, Is.EqualTo([movieC, movieB, movieA]));
    }

    private static MovieRecords CreateMovie(string name)
    {
        return new MovieRecords
        {
            Movie_Name = name,
            Movie_Path = $@"C:\movies\{name}.mp4",
        };
    }
}
