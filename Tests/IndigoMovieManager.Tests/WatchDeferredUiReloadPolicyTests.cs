using IndigoMovieManager;
using IndigoMovieManager.DB;
using IndigoMovieManager.ViewModels;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchDeferredUiReloadPolicyTests
{
    [Test]
    public void ShouldUseDeferredWatchUiReload_Watch更新ありなら遅延reloadを使う()
    {
        bool result = MainWindow.ShouldUseDeferredWatchUiReload(
            hasChanges: true,
            isWatchMode: true
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void ShouldUseDeferredWatchUiReload_Manual時は即時reloadのままにする()
    {
        bool result = MainWindow.ShouldUseDeferredWatchUiReload(
            hasChanges: true,
            isWatchMode: false
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ShouldUseQueryOnlyWatchUiReload_安全条件が揃ったwatchだけTrueを返す()
    {
        Assert.That(
            MainWindow.ShouldUseQueryOnlyWatchUiReload(
                hasChanges: true,
                isWatchMode: true,
                canUseQueryOnlyReload: true
            ),
            Is.True
        );
        Assert.That(
            MainWindow.ShouldUseQueryOnlyWatchUiReload(
                hasChanges: true,
                isWatchMode: true,
                canUseQueryOnlyReload: false
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldUseQueryOnlyWatchUiReload(
                hasChanges: true,
                isWatchMode: false,
                canUseQueryOnlyReload: true
            ),
            Is.False
        );
        Assert.That(
            MainWindow.ShouldUseQueryOnlyWatchUiReload(
                hasChanges: false,
                isWatchMode: true,
                canUseQueryOnlyReload: true
            ),
            Is.False
        );
    }

    [Test]
    public void CanApplyDeferredWatchUiReload_同一DBかつ最新revisionなら適用する()
    {
        bool result = MainWindow.CanApplyDeferredWatchUiReload(
            currentDbFullPath: @"D:\Db\Main.wb",
            scheduledDbFullPath: @"d:\db\main.wb",
            isWatchSuppressedByUi: false,
            requestRevision: 4,
            currentRevision: 4
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void CanApplyDeferredWatchUiReload_revisionが古ければ適用しない()
    {
        bool result = MainWindow.CanApplyDeferredWatchUiReload(
            currentDbFullPath: @"D:\Db\Main.wb",
            scheduledDbFullPath: @"D:\Db\Main.wb",
            isWatchSuppressedByUi: false,
            requestRevision: 3,
            currentRevision: 4
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void CanApplyDeferredWatchUiReload_DBが切り替わっていたら適用しない()
    {
        bool result = MainWindow.CanApplyDeferredWatchUiReload(
            currentDbFullPath: @"D:\Db\Other.wb",
            scheduledDbFullPath: @"D:\Db\Main.wb",
            isWatchSuppressedByUi: false,
            requestRevision: 4,
            currentRevision: 4
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void CanApplyDeferredWatchUiReload_suppression中は適用しない()
    {
        bool result = MainWindow.CanApplyDeferredWatchUiReload(
            currentDbFullPath: @"D:\Db\Main.wb",
            scheduledDbFullPath: @"D:\Db\Main.wb",
            isWatchSuppressedByUi: true,
            requestRevision: 4,
            currentRevision: 4
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ApplyDeferredWatchUiReloadOnUiThread_suppression中はapplyせずdeferへ戻す()
    {
        const string dbFullPath = @"D:\Db\Main.wb";
        MainWindow window = CreateMainWindowForDeferredReloadTests(dbFullPath, "28");
        SetPrivateField(window, "_watchUiSuppressionSync", new object());
        SetPrivateField(window, "_watchUiSuppressionCount", 1);
        SetPrivateField(window, "_watchDeferredUiReloadSync", new object());
        SetPrivateField(window, "_watchDeferredUiReloadRevision", 4);
        SetPrivateField(window, "_watchDeferredUiReloadPending", true);

        int filterAndSortCount = 0;
        window.FilterAndSortForTesting = (_, _) => filterAndSortCount++;

        InvokeVoid(
            window,
            "ApplyDeferredWatchUiReloadOnUiThread",
            dbFullPath,
            4,
            "watch-test"
        );

        Assert.That(filterAndSortCount, Is.EqualTo(0));
        Assert.That(
            (bool)GetPrivateField(window, "_watchWorkDeferredWhileSuppressed"),
            Is.True
        );
    }

    [Test]
    public void ApplyDeferredWatchUiReloadOnUiThread_queryOnlyならin_memory再計算を呼ぶ()
    {
        const string dbFullPath = @"D:\Db\Main.wb";
        MainWindow window = CreateMainWindowForDeferredReloadTests(dbFullPath, "28");
        SetPrivateField(window, "_watchUiSuppressionSync", new object());
        SetPrivateField(window, "_watchDeferredUiReloadSync", new object());
        SetPrivateField(window, "_watchDeferredUiReloadRevision", 4);
        SetPrivateField(window, "_watchDeferredUiReloadPending", true);
        SetPrivateField(window, "_watchDeferredUiReloadQueryOnly", true);

        List<(string Sort, string Reason, IReadOnlyList<MainWindow.WatchChangedMovie> ChangedMovies)> refreshCalls = [];
        List<(string Sort, bool IsGetNew)> fullReloadCalls = [];
        window.RefreshMovieViewFromCurrentSourceForTesting = (sort, reason, changedMovies) =>
        {
            refreshCalls.Add((sort, reason, changedMovies));
        };
        window.FilterAndSortForTesting = (sort, isGetNew) =>
        {
            fullReloadCalls.Add((sort, isGetNew));
        };
        SetPrivateField(
            window,
            "_watchDeferredUiReloadChangedMovies",
            new List<MainWindow.WatchChangedMovie>
            {
                new(
                    @"E:\Movies\sample.mp4",
                    MainWindow.WatchMovieChangeKind.ViewRepaired,
                    MainWindow.WatchMovieDirtyFields.None
                ),
            }
        );

        InvokeVoid(
            window,
            "ApplyDeferredWatchUiReloadOnUiThread",
            dbFullPath,
            4,
            "watch-test"
        );

        Assert.That(refreshCalls, Has.Count.EqualTo(1));
        Assert.That(refreshCalls[0].Sort, Is.EqualTo("28"));
        Assert.That(refreshCalls[0].Reason, Is.EqualTo("deferred:watch-test"));
        Assert.That(refreshCalls[0].ChangedMovies.Select(x => x.MoviePath), Is.EqualTo([@"E:\Movies\sample.mp4"]));
        Assert.That(
            refreshCalls[0].ChangedMovies[0].ChangeKind,
            Is.EqualTo(MainWindow.WatchMovieChangeKind.ViewRepaired)
        );
        Assert.That(fullReloadCalls, Is.Empty);
    }

    [Test]
    public void MergeChangedMovies_casing違いは1件へ潰し種別をORで保つ()
    {
        List<MainWindow.WatchChangedMovie> result = MainWindow.MergeChangedMovies(
            [
                new(
                    @"E:\Movies\Alpha.mp4",
                    MainWindow.WatchMovieChangeKind.SourceInserted,
                    MainWindow.WatchMovieDirtyFields.MovieName,
                    new MainWindow.WatchMovieObservedState("2026-04-17 10:00:00", 4)
                ),
            ],
            [
                new(
                    @"e:\movies\alpha.mp4",
                    MainWindow.WatchMovieChangeKind.ViewRepaired,
                    MainWindow.WatchMovieDirtyFields.MoviePath
                ),
                new(
                    @"E:\Movies\Beta.mp4",
                    MainWindow.WatchMovieChangeKind.DisplayedViewRefresh,
                    MainWindow.WatchMovieDirtyFields.None
                ),
            ]
        );

        Assert.That(result.Select(x => x.MoviePath), Is.EqualTo([@"E:\Movies\Alpha.mp4", @"E:\Movies\Beta.mp4"]));
        Assert.That(
            result[0].ChangeKind,
            Is.EqualTo(
                MainWindow.WatchMovieChangeKind.SourceInserted
                | MainWindow.WatchMovieChangeKind.ViewRepaired
            )
        );
        Assert.That(
            result[0].DirtyFields,
            Is.EqualTo(
                MainWindow.WatchMovieDirtyFields.MovieName
                | MainWindow.WatchMovieDirtyFields.MoviePath
            )
        );
        Assert.That(result[0].ObservedState.HasValue, Is.True);
        Assert.That(result[0].ObservedState!.Value.MovieSizeKb, Is.EqualTo(4));
    }

    [Test]
    public void TryBuildChangedMovieRefreshSource_changed_pathsだけ再評価して追加と除外を反映する()
    {
        MovieRecords alpha = new() { Movie_Path = @"E:\Movies\alpha.mp4", Movie_Name = "alpha.mp4" };
        MovieRecords betaOld = new() { Movie_Path = @"E:\Movies\beta.mp4", Movie_Name = "beta.mp4" };
        MovieRecords betaNew = new() { Movie_Path = @"E:\Movies\beta.mp4", Movie_Name = "gamma.mp4" };
        MovieRecords delta = new() { Movie_Path = @"E:\Movies\delta.mp4", Movie_Name = "delta.mp4" };

        bool result = MainWindow.TryBuildChangedMovieRefreshSource(
            [alpha, betaNew, delta],
            [alpha, betaOld],
            "alpha | delta",
            "12",
            [
                new MainWindow.WatchChangedMovie(
                    @"E:\Movies\beta.mp4",
                    MainWindow.WatchMovieChangeKind.SourceInserted,
                    MainWindow.WatchMovieDirtyFields.MovieName
                ),
                new MainWindow.WatchChangedMovie(
                    @"E:\Movies\delta.mp4",
                    MainWindow.WatchMovieChangeKind.SourceInserted,
                    MainWindow.WatchMovieDirtyFields.MovieName
                ),
            ],
            IndigoMovieManager.Infrastructure.SearchService.FilterMovies,
            out MovieRecords[] nextFilteredMovies,
            out bool canReuseCurrentOrder
        );

        Assert.That(result, Is.True);
        Assert.That(nextFilteredMovies, Is.EqualTo([alpha, delta]));
        Assert.That(canReuseCurrentOrder, Is.False);
    }

    [Test]
    public void TryBuildChangedMovieRefreshSource_empty検索かつview_repairならfilter呼び出しを省く()
    {
        MovieRecords alpha = new() { Movie_Path = @"E:\Movies\alpha.mp4", Movie_Name = "alpha.mp4" };
        MovieRecords beta = new() { Movie_Path = @"E:\Movies\beta.mp4", Movie_Name = "beta.mp4" };
        int filterCallCount = 0;

        bool result = MainWindow.TryBuildChangedMovieRefreshSource(
            [alpha, beta],
            [alpha],
            "",
            "6",
            [
                new MainWindow.WatchChangedMovie(
                    @"E:\Movies\beta.mp4",
                    MainWindow.WatchMovieChangeKind.ViewRepaired,
                    MainWindow.WatchMovieDirtyFields.None
                ),
            ],
            (movies, keyword) =>
            {
                filterCallCount++;
                return movies;
            },
            out MovieRecords[] nextFilteredMovies,
            out bool canReuseCurrentOrder
        );

        Assert.That(result, Is.True);
        Assert.That(filterCallCount, Is.EqualTo(0));
        Assert.That(nextFilteredMovies, Is.EqualTo([alpha, beta]));
        Assert.That(canReuseCurrentOrder, Is.False);
    }

    [Test]
    public void TryBuildChangedMovieRefreshSource_sort非依存のrenameなら既存順を再利用する()
    {
        MovieRecords alpha = new() { Movie_Path = @"E:\Movies\alpha.mp4", Movie_Name = "alpha.mp4", Score = 10 };
        MovieRecords beta = new() { Movie_Path = @"E:\Movies\beta.mp4", Movie_Name = "beta-renamed.mp4", Score = 5 };

        bool result = MainWindow.TryBuildChangedMovieRefreshSource(
            [alpha, beta],
            [beta],
            "beta",
            "6",
            [
                new MainWindow.WatchChangedMovie(
                    @"E:\Movies\beta.mp4",
                    MainWindow.WatchMovieChangeKind.None,
                    MainWindow.WatchMovieDirtyFields.MovieName
                        | MainWindow.WatchMovieDirtyFields.MoviePath
                        | MainWindow.WatchMovieDirtyFields.Kana
                ),
            ],
            IndigoMovieManager.Infrastructure.SearchService.FilterMovies,
            out MovieRecords[] nextFilteredMovies,
            out bool canReuseCurrentOrder
        );

        Assert.That(result, Is.True);
        Assert.That(nextFilteredMovies, Is.EqualTo([beta]));
        Assert.That(canReuseCurrentOrder, Is.True);
    }

    [Test]
    public void TryBuildChangedMovieRefreshSource_existing_watch観測値をsourceへ反映する()
    {
        MovieRecords alpha = new()
        {
            Movie_Path = @"E:\Movies\alpha.mp4",
            Movie_Name = "alpha.mp4",
            File_Date = "2026-04-17 10:00:00",
            Movie_Size = 2,
        };
        MovieRecords beta = new()
        {
            Movie_Path = @"E:\Movies\beta.mp4",
            Movie_Name = "beta.mp4",
            File_Date = "2026-03-20 10:00:00",
            Movie_Size = 1,
        };

        bool result = MainWindow.TryBuildChangedMovieRefreshSource(
            [alpha, beta],
            [alpha, beta],
            "",
            "2",
            [
                new MainWindow.WatchChangedMovie(
                    @"E:\Movies\beta.mp4",
                    MainWindow.WatchMovieChangeKind.None,
                    MainWindow.WatchMovieDirtyFields.FileDate
                        | MainWindow.WatchMovieDirtyFields.MovieSize,
                    new MainWindow.WatchMovieObservedState("2026-04-17 12:34:56", 4)
                ),
            ],
            IndigoMovieManager.Infrastructure.SearchService.FilterMovies,
            out MovieRecords[] nextFilteredMovies,
            out bool canReuseCurrentOrder
        );

        Assert.That(result, Is.True);
        Assert.That(nextFilteredMovies, Is.EqualTo([alpha, beta]));
        Assert.That(beta.File_Date, Is.EqualTo("2026-04-17 12:34:56"));
        Assert.That(beta.Movie_Size, Is.EqualTo(4));
        Assert.That(canReuseCurrentOrder, Is.False);
    }

    [Test]
    public void TryBuildChangedMovieRefreshSource_movie_length観測値をsourceへ反映する()
    {
        MovieRecords beta = new()
        {
            Movie_Path = @"E:\Movies\beta.mp4",
            Movie_Name = "beta.mp4",
            Movie_Length = "00:01:00",
        };

        bool result = MainWindow.TryBuildChangedMovieRefreshSource(
            [beta],
            [beta],
            "",
            "20",
            [
                new MainWindow.WatchChangedMovie(
                    @"E:\Movies\beta.mp4",
                    MainWindow.WatchMovieChangeKind.None,
                    MainWindow.WatchMovieDirtyFields.MovieLength,
                    new MainWindow.WatchMovieObservedState(
                        "2026-04-17 12:34:56",
                        4,
                        120
                    )
                ),
            ],
            IndigoMovieManager.Infrastructure.SearchService.FilterMovies,
            out MovieRecords[] nextFilteredMovies,
            out bool canReuseCurrentOrder
        );

        Assert.That(result, Is.True);
        Assert.That(nextFilteredMovies, Is.EqualTo([beta]));
        Assert.That(beta.Movie_Length, Is.EqualTo("00:02:00"));
        Assert.That(canReuseCurrentOrder, Is.False);
    }

    [Test]
    public void TryBuildChangedMovieRefreshSource_検索非依存dirtyなら現在の一致状態を再利用する()
    {
        MovieRecords alpha = new()
        {
            Movie_Path = @"E:\Movies\alpha.mp4",
            Movie_Name = "alpha.mp4",
            Movie_Size = 1,
        };
        int filterCallCount = 0;

        bool result = MainWindow.TryBuildChangedMovieRefreshSource(
            [alpha],
            [alpha],
            "alpha",
            "12",
            [
                new MainWindow.WatchChangedMovie(
                    @"E:\Movies\alpha.mp4",
                    MainWindow.WatchMovieChangeKind.None,
                    MainWindow.WatchMovieDirtyFields.MovieSize,
                    new MainWindow.WatchMovieObservedState("2026-04-17 12:34:56", 4)
                ),
            ],
            (movies, keyword) =>
            {
                filterCallCount++;
                return IndigoMovieManager.Infrastructure.SearchService.FilterMovies(movies, keyword);
            },
            out MovieRecords[] nextFilteredMovies,
            out bool canReuseCurrentOrder
        );

        Assert.That(result, Is.True);
        Assert.That(filterCallCount, Is.EqualTo(0));
        Assert.That(nextFilteredMovies, Is.EqualTo([alpha]));
        Assert.That(alpha.Movie_Size, Is.EqualTo(4));
        Assert.That(canReuseCurrentOrder, Is.False);
    }

    [Test]
    public void TryBuildChangedMovieRefreshSource_dup検索でhash変更ありなら局所更新を使わない()
    {
        MovieRecords first = new()
        {
            Movie_Path = @"E:\Movies\first.mp4",
            Movie_Name = "first.mp4",
            Hash = "same",
        };
        MovieRecords second = new()
        {
            Movie_Path = @"E:\Movies\second.mp4",
            Movie_Name = "second.mp4",
            Hash = "same",
        };

        bool result = MainWindow.TryBuildChangedMovieRefreshSource(
            [first, second],
            [second],
            "{dup}",
            "12",
            [
                new MainWindow.WatchChangedMovie(
                    @"E:\Movies\second.mp4",
                    MainWindow.WatchMovieChangeKind.SourceInserted,
                    MainWindow.WatchMovieDirtyFields.Hash
                ),
            ],
            IndigoMovieManager.Infrastructure.SearchService.FilterMovies,
            out MovieRecords[] nextFilteredMovies,
            out bool canReuseCurrentOrder
        );

        Assert.That(result, Is.False);
        Assert.That(nextFilteredMovies, Is.Empty);
        Assert.That(canReuseCurrentOrder, Is.False);
    }

    [Test]
    public void DoesSearchDependOnDirtyFields_検索列に無関係ならFalseを返す()
    {
        Assert.That(
            MainWindow.DoesSearchDependOnDirtyFields(
                "alpha",
                MainWindow.WatchMovieDirtyFields.MovieSize
            ),
            Is.False
        );
        Assert.That(
            MainWindow.DoesSearchDependOnDirtyFields(
                "alpha",
                MainWindow.WatchMovieDirtyFields.MovieName
            ),
            Is.True
        );
        Assert.That(
            MainWindow.DoesSearchDependOnDirtyFields(
                "{dup}",
                MainWindow.WatchMovieDirtyFields.Hash
            ),
            Is.True
        );
    }

    [Test]
    public void DoesCurrentSortDependOnDirtyFields_現在のsortに無関係ならFalseを返す()
    {
        Assert.That(
            MainWindow.DoesCurrentSortDependOnDirtyFields(
                "6",
                MainWindow.WatchMovieDirtyFields.MovieName
            ),
            Is.False
        );
        Assert.That(
            MainWindow.DoesCurrentSortDependOnDirtyFields(
                "12",
                MainWindow.WatchMovieDirtyFields.MovieName
            ),
            Is.True
        );
    }

    [Test]
    public void BeginWatchUiSuppression_予約済みold_reloadは解除後もapplyされずcatch_upへ戻す()
    {
        const string dbFullPath = @"D:\Db\Main.wb";
        MainWindow window = CreateMainWindowForDeferredReloadTests(dbFullPath, "28");
        SetPrivateField(window, "_watchUiSuppressionSync", new object());
        SetPrivateField(window, "_checkFolderRequestSync", new object());
        SetPrivateField(window, "_checkFolderRunLock", new SemaphoreSlim(0, 1));
        SetPrivateField(window, "_watchDeferredUiReloadSync", new object());
        SetPrivateField(window, "_watchDeferredUiReloadCts", new CancellationTokenSource());
        SetPrivateField(window, "_watchDeferredUiReloadRevision", 4);
        SetPrivateField(window, "_watchDeferredUiReloadPending", true);

        int filterAndSortCount = 0;
        List<string> queuedTriggers = [];
        window.FilterAndSortForTesting = (_, _) => filterAndSortCount++;
        window.QueueCheckFolderAsyncRequestedForTesting = (mode, trigger) =>
        {
            queuedTriggers.Add($"{mode}:{trigger}");
        };

        InvokeVoid(window, "BeginWatchUiSuppression", "drawer");
        InvokeVoid(window, "EndWatchUiSuppression", "drawer");
        InvokeVoid(
            window,
            "ApplyDeferredWatchUiReloadOnUiThread",
            dbFullPath,
            4,
            "watch-test"
        );

        Assert.That(filterAndSortCount, Is.EqualTo(0));
        Assert.That(queuedTriggers, Is.EqualTo(["Watch:ui-resume:drawer"]));
    }

    [Test]
    public void InvalidateWatchScanScope_同一DBでもold_reloadはapplyされない()
    {
        const string dbFullPath = @"D:\Db\Main.wb";
        MainWindow window = CreateMainWindowForDeferredReloadTests(dbFullPath, "28");
        SetPrivateField(window, "_watchUiSuppressionSync", new object());
        SetPrivateField(window, "_watchDeferredUiReloadSync", new object());
        SetPrivateField(window, "_watchDeferredUiReloadCts", new CancellationTokenSource());
        SetPrivateField(window, "_watchDeferredUiReloadRevision", 4);
        SetPrivateField(window, "_watchDeferredUiReloadPending", true);

        int filterAndSortCount = 0;
        window.FilterAndSortForTesting = (_, _) => filterAndSortCount++;

        InvokeVoid(window, "InvalidateWatchScanScope", "scope-reset");
        InvokeVoid(
            window,
            "ApplyDeferredWatchUiReloadOnUiThread",
            dbFullPath,
            4,
            "watch-test"
        );

        Assert.That(filterAndSortCount, Is.EqualTo(0));
    }

    private static MainWindow CreateMainWindowForDeferredReloadTests(
        string dbFullPath,
        string sort
    )
    {
        MainWindow window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        MainWindowViewModel mainVm =
            (MainWindowViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainWindowViewModel));
        mainVm.DbInfo = new DBInfo
        {
            DBFullPath = dbFullPath,
            Sort = sort,
        };

        SetPrivateField(window, "MainVM", mainVm);
        return window;
    }

    private static void InvokeVoid(MainWindow window, string methodName, params object[] args)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null, methodName);
        method.Invoke(window, args);
    }

    private static void SetPrivateField(MainWindow window, string fieldName, object value)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
        )!;
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(window, value);
    }

    private static object GetPrivateField(MainWindow window, string fieldName)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(field, Is.Not.Null, fieldName);
        return field.GetValue(window)!;
    }
}
