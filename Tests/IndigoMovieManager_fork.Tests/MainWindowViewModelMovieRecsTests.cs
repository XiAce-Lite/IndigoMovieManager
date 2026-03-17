using System.Collections.Specialized;
using System.Threading;
using IndigoMovieManager;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class MainWindowViewModelMovieRecsTests
{
    [Test]
    public void ReplaceMovieRecsは単発Reset通知で全件差し替える()
    {
        MainWindowViewModel viewModel = new();
        List<NotifyCollectionChangedEventArgs> events = [];
        viewModel.MovieRecs.CollectionChanged += (_, e) => events.Add(e);

        viewModel.ReplaceMovieRecs([CreateMovie("A"), CreateMovie("B"), CreateMovie("C")]);

        Assert.That(events.Count, Is.EqualTo(1));
        Assert.That(events[0].Action, Is.EqualTo(NotifyCollectionChangedAction.Reset));
        Assert.That(viewModel.MovieRecs.Select(movie => movie.Movie_Name), Is.EqualTo(["A", "B", "C"]));
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
