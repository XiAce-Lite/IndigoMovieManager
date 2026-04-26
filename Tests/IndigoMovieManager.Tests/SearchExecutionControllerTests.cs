using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class SearchExecutionControllerTests
{
    [Test]
    public async Task ExecuteAsync_DB未選択なら何もしない()
    {
        int filterCallCount = 0;
        SearchExecutionController controller = new(
            getDbFullPath: () => "",
            getSortId: () => "1",
            setSearchKeyword: _ => Assert.Fail("DB未選択では検索語を更新しないはずです。"),
            syncSearchBoxText: _ => Assert.Fail("DB未選択ではSearchBox同期しないはずです。"),
            beginUserPriorityWork: _ => Assert.Fail("DB未選択では優先制御しないはずです。"),
            endUserPriorityWork: _ => Assert.Fail("DB未選択では優先制御しないはずです。"),
            restartThumbnailTask: () => Assert.Fail("DB未選択では再起動しないはずです。"),
            refreshSearchResultsAsync: _ =>
            {
                filterCallCount++;
                return Task.CompletedTask;
            },
            selectFirstItem: () => Assert.Fail("DB未選択では選択変更しないはずです。")
        );

        bool actual = await controller.ExecuteAsync("target", true);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.False);
            Assert.That(filterCallCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ExecuteAsync_検索語更新とFilterAndSortを順に実行する()
    {
        string searchKeyword = "";
        string searchBoxText = "";
        string sortId = "";
        int restartCallCount = 0;
        int selectCallCount = 0;
        List<string> priorityCalls = [];

        SearchExecutionController controller = new(
            getDbFullPath: () => @"C:\temp\sample.wb",
            getSortId: () => "7",
            setSearchKeyword: keyword => searchKeyword = keyword,
            syncSearchBoxText: text => searchBoxText = text,
            beginUserPriorityWork: reason => priorityCalls.Add($"begin:{reason}"),
            endUserPriorityWork: reason => priorityCalls.Add($"end:{reason}"),
            restartThumbnailTask: () => restartCallCount++,
            refreshSearchResultsAsync: resolvedSortId =>
            {
                sortId = resolvedSortId;
                return Task.CompletedTask;
            },
            selectFirstItem: () => selectCallCount++
        );

        bool actual = await controller.ExecuteAsync("tokyo", true);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.True);
            Assert.That(searchKeyword, Is.EqualTo("tokyo"));
            Assert.That(searchBoxText, Is.EqualTo("tokyo"));
            Assert.That(sortId, Is.EqualTo("7"));
            Assert.That(restartCallCount, Is.EqualTo(1));
            Assert.That(selectCallCount, Is.EqualTo(1));
            Assert.That(priorityCalls, Is.EqualTo(["begin:search", "end:search"]));
        });
    }

    [Test]
    public async Task ExecuteAsync_syncSearchTextがfalseならSearchBox同期しない()
    {
        int syncCallCount = 0;
        string searchKeyword = "";
        SearchExecutionController controller = new(
            getDbFullPath: () => @"C:\temp\sample.wb",
            getSortId: () => "3",
            setSearchKeyword: keyword => searchKeyword = keyword,
            syncSearchBoxText: _ => syncCallCount++,
            beginUserPriorityWork: _ => { },
            endUserPriorityWork: _ => { },
            restartThumbnailTask: () => { },
            refreshSearchResultsAsync: _ => Task.CompletedTask,
            selectFirstItem: () => { }
        );

        bool actual = await controller.ExecuteAsync("sora", false);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.True);
            Assert.That(searchKeyword, Is.EqualTo("sora"));
            Assert.That(syncCallCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void ExecuteAsync_refresh失敗時も優先制御を終了する()
    {
        List<string> priorityCalls = [];
        SearchExecutionController controller = new(
            getDbFullPath: () => @"C:\temp\sample.wb",
            getSortId: () => "1",
            setSearchKeyword: _ => { },
            syncSearchBoxText: _ => { },
            beginUserPriorityWork: reason => priorityCalls.Add($"begin:{reason}"),
            endUserPriorityWork: reason => priorityCalls.Add($"end:{reason}"),
            restartThumbnailTask: () => { },
            refreshSearchResultsAsync: _ => throw new InvalidOperationException("boom"),
            selectFirstItem: () => { }
        );

        Assert.ThrowsAsync<InvalidOperationException>(async () => await controller.ExecuteAsync("x", true));
        Assert.That(priorityCalls, Is.EqualTo(["begin:search", "end:search"]));
    }
}
