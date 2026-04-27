namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowFilterSortExecutionPolicyTests
{
    [TestCase(false, false, false, "full-reload")]
    [TestCase(false, true, false, "query-only")]
    [TestCase(true, false, false, "query-only")]
    [TestCase(true, true, false, "query-only")]
    [TestCase(false, false, true, "full-reload")]
    [TestCase(true, true, true, "full-reload")]
    public void ResolveFilterSortExecutionRouteLabel_経路を短い札で返せる(
        bool hasSnapshotData,
        bool startupFeedLoadedAllPages,
        bool isGetNew,
        string expected
    )
    {
        string actual = MainWindow.ResolveFilterSortExecutionRouteLabel(
            hasSnapshotData,
            startupFeedLoadedAllPages,
            isGetNew
        );

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(false, false, false, "no-snapshot-startup-partial")]
    [TestCase(false, true, false, "none")]
    [TestCase(true, false, false, "none")]
    [TestCase(true, true, false, "none")]
    [TestCase(false, false, true, "is-get-new")]
    [TestCase(true, true, true, "is-get-new")]
    public void ResolveFilterSortFullReloadReason_full_reload理由を短い札で返せる(
        bool hasSnapshotData,
        bool startupFeedLoadedAllPages,
        bool isGetNew,
        string expected
    )
    {
        string actual = MainWindow.ResolveFilterSortFullReloadReason(
            hasSnapshotData,
            startupFeedLoadedAllPages,
            isGetNew
        );

        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(-1, false)]
    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(63, false)]
    [TestCase(64, true)]
    [TestCase(120, true)]
    public void ShouldRunFilterSortOnBackground_件数閾値で実行方式を切り替えられる(
        int sourceCount,
        bool expected
    )
    {
        bool actual = MainWindow.ShouldRunFilterSortOnBackground(sourceCount);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void LinkSearch_ユーザーコントロールから検索正本へ合流する()
    {
        string searchSource = GetRepoText("Views", "Main", "MainWindow.Search.cs");
        string tagSource = GetRepoText("UserControls", "TagControl.xaml.cs");
        string detailSource = GetRepoText("UserControls", "ExtDetail.xaml.cs");
        string bookmarkSource = GetRepoText("UserControls", "Bookmark.xaml.cs");

        Assert.That(searchSource, Does.Contain("public async Task ApplySearchKeywordFromLinkAsync("));
        Assert.That(searchSource, Does.Contain("SearchExecutor.ExecuteAsync(keyword ?? \"\", syncSearchText: true)"));
        Assert.That(searchSource, Does.Contain("if (SearchBox != null && !SearchBox.IsKeyboardFocusWithin)"));
        Assert.That(searchSource, Does.Contain("catch (Exception ex)"));
        Assert.That(searchSource, Does.Contain("link search failed:"));
        Assert.That(tagSource, Does.Contain("await ownerWindow.ApplySearchKeywordFromLinkAsync(keyword);"));
        Assert.That(detailSource, Does.Contain("await ownerWindow.ApplySearchKeywordFromLinkAsync(quoted);"));
        Assert.That(detailSource, Does.Contain("await ownerWindow.ApplySearchKeywordFromLinkAsync(mv.Ext);"));
        Assert.That(bookmarkSource, Does.Contain("await ownerWindow.ApplySearchKeywordFromLinkAsync(mv.Movie_Body ?? \"\");"));
        Assert.That(tagSource, Does.Not.Contain("FilterAndSort(ownerWindow.MainVM.DbInfo.Sort, true);"));
        Assert.That(detailSource, Does.Not.Contain("FilterAndSort(ownerWindow.MainVM.DbInfo.Sort, true);"));
        Assert.That(bookmarkSource, Does.Not.Contain("ownerWindow.SearchBox.Text ="));
    }

    private static string GetRepoText(params string[] relativePathParts)
    {
        DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
        while (current != null)
        {
            string candidate = Path.Combine([current.FullName, .. relativePathParts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            current = current.Parent;
        }

        Assert.Fail($"Repository file not found: {Path.Combine(relativePathParts)}");
        return "";
    }
}
