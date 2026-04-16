using System.Collections.Specialized;
using System.Data.SQLite;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using IndigoMovieManager.DB;
using IndigoMovieManager.ViewModels;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public sealed class MainWindowSearchBoxEnterTests
{
    private static readonly object UiThreadSync = new();
    private static Thread? uiThread;
    private static Dispatcher? uiDispatcher;
    private static TaskCompletionSource<bool>? uiThreadReady;

    [Test]
    public async Task SearchBox_Enterで検索実行と履歴保存ができる()
    {
        SearchEnterResult result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            string dbPath = CreateTempMainDb();
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                SeedMovieRow(dbPath);
                window.MainVM.DbInfo.DBFullPath = dbPath;
                window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);
                window.MainVM.DbInfo.Sort = "1";
                window.MainVM.DbInfo.ThumbFolder = Path.Combine(
                    Path.GetTempPath(),
                    $"imm-search-enter-thumb-{Guid.NewGuid():N}"
                );
                Directory.CreateDirectory(window.MainVM.DbInfo.ThumbFolder);
                window.Tabs.SelectedIndex = 2;
                window.MainVM.DbInfo.CurrentTabIndex = 2;
                window.MainVM.DbInfo.SearchCount = 0;
                window.SearchBox.Text = "target";
                window.SearchBox.Focus();
                Keyboard.Focus(window.SearchBox);
                await WaitForDispatcherIdleAsync();

                KeyEventArgs args = CreatePreviewKeyEvent(window.SearchBox, Key.Enter);
                InvokeSearchBoxPreviewKeyDown(window, args);

                await WaitUntilAsync(
                    () => window.MainVM.DbInfo.SearchCount == 1 && window.MainVM.HistoryRecs.Count == 1,
                    TimeSpan.FromSeconds(5),
                    "Enter 検索の反映完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return new SearchEnterResult(
                    window.MainVM.DbInfo.SearchKeyword,
                    window.MainVM.DbInfo.SearchCount,
                    [.. window.MainVM.HistoryRecs.Select(x => x.Find_Text)],
                    [.. ReadHistoryTexts(dbPath)],
                    args.Handled
                );
            }
            finally
            {
                await CloseWindowAsync(window);
                TryDeleteDirectory(window.MainVM.DbInfo.ThumbFolder);
                TryDeleteFile(dbPath);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.SearchKeyword, Is.EqualTo("target"));
            Assert.That(result.SearchCount, Is.EqualTo(1));
            Assert.That(result.UiHistoryTexts, Is.EqualTo(["target"]));
            Assert.That(result.DbHistoryTexts, Is.EqualTo(["target"]));
            Assert.That(result.WasHandled, Is.True);
        });
    }

    [Test]
    public async Task ExternalSkinSearch_検索実行と履歴保存とSearchBox同期ができる()
    {
        SearchEnterResult result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            string dbPath = CreateTempMainDb();
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                SeedMovieRow(dbPath);
                window.MainVM.DbInfo.DBFullPath = dbPath;
                window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);
                window.MainVM.DbInfo.Sort = "1";
                window.MainVM.DbInfo.ThumbFolder = Path.Combine(
                    Path.GetTempPath(),
                    $"imm-search-skin-thumb-{Guid.NewGuid():N}"
                );
                Directory.CreateDirectory(window.MainVM.DbInfo.ThumbFolder);
                window.Tabs.SelectedIndex = 2;
                window.MainVM.DbInfo.CurrentTabIndex = 2;
                window.MainVM.DbInfo.SearchCount = 0;
                window.SearchBox.Text = "";
                await WaitForDispatcherIdleAsync();

                bool executed = await InvokeExternalSkinSearchAsync(window, "target");

                await WaitUntilAsync(
                    () =>
                        window.MainVM.DbInfo.SearchCount == 1
                        && window.MainVM.HistoryRecs.Count == 1
                        && window.SearchBox.Text == "target",
                    TimeSpan.FromSeconds(5),
                    "外部スキン検索の反映完了を待てませんでした。"
                );
                await WaitForDispatcherIdleAsync();

                return new SearchEnterResult(
                    window.MainVM.DbInfo.SearchKeyword,
                    window.MainVM.DbInfo.SearchCount,
                    [.. window.MainVM.HistoryRecs.Select(x => x.Find_Text)],
                    [.. ReadHistoryTexts(dbPath)],
                    executed
                );
            }
            finally
            {
                await CloseWindowAsync(window);
                TryDeleteDirectory(window.MainVM.DbInfo.ThumbFolder);
                TryDeleteFile(dbPath);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.SearchKeyword, Is.EqualTo("target"));
            Assert.That(result.SearchCount, Is.EqualTo(1));
            Assert.That(result.UiHistoryTexts, Is.EqualTo(["target"]));
            Assert.That(result.DbHistoryTexts, Is.EqualTo(["target"]));
            Assert.That(result.WasHandled, Is.True);
        });
    }

    [Test]
    public async Task ExternalSkinSearch_起動時部分ロード中は全件再取得して検索できる()
    {
        SearchReloadResult result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            string dbPath = CreateTempMainDb();
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                SeedSearchReloadRows(dbPath);
                window.MainVM.DbInfo.DBFullPath = dbPath;
                window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);
                window.MainVM.DbInfo.Sort = "12";
                window.MainVM.DbInfo.SearchKeyword = "";
                window.MainVM.DbInfo.SearchCount = 0;
                window.MainVM.DbInfo.ThumbFolder = CreateTempDirectory("imm-search-reload-thumb");
                window.Tabs.SelectedIndex = 2;
                window.MainVM.DbInfo.CurrentTabIndex = 2;

                MovieRecords partialMovie = CreateSearchMovieRecord(
                    1,
                    "alpha one",
                    @"C:\movies\alpha one.mp4"
                );
                window.MainVM.ReplaceMovieRecs([partialMovie]);
                window.MainVM.ReplaceFilteredMovieRecs([partialMovie]);
                SetPrivateField(window, "_startupFeedIsPartialActive", true);
                SetPrivateField(window, "_startupFeedLoadedAllPages", false);
                await WaitForDispatcherIdleAsync();

                bool executed = await InvokeExternalSkinSearchAsync(window, "alpha");

                await WaitUntilAsync(
                    () =>
                        window.MainVM.MovieRecs.Count == 3
                        && window.MainVM.FilteredMovieRecs.Count == 2
                        && window.MainVM.DbInfo.SearchCount == 2,
                    TimeSpan.FromSeconds(5),
                    "起動時部分ロード中の検索 full reload 完了を待てませんでした。"
                );

                return new SearchReloadResult(
                    executed,
                    window.MainVM.MovieRecs.Count,
                    window.MainVM.FilteredMovieRecs.Count,
                    window.MainVM.DbInfo.SearchCount,
                    [.. window.MainVM.FilteredMovieRecs.Select(x => x.Movie_Name)]
                );
            }
            finally
            {
                await CloseWindowAsync(window);
                TryDeleteDirectory(window.MainVM.DbInfo.ThumbFolder);
                TryDeleteFile(dbPath);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.True);
            Assert.That(result.MovieCount, Is.EqualTo(3));
            Assert.That(result.FilteredCount, Is.EqualTo(2));
            Assert.That(result.SearchCount, Is.EqualTo(2));
            Assert.That(result.FilteredMovieNames, Is.EqualTo(["alpha one.mp4", "alpha two.mp4"]));
        });
    }

    [Test]
    public async Task SearchBox_TextChanged_起動時部分ロード中に空文字へ戻すと全件再取得で一覧を戻せる()
    {
        SearchReloadResult result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            string dbPath = CreateTempMainDb();
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                SeedSearchReloadRows(dbPath);
                window.MainVM.DbInfo.DBFullPath = dbPath;
                window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);
                window.MainVM.DbInfo.Sort = "12";
                window.MainVM.DbInfo.SearchKeyword = "";
                window.MainVM.DbInfo.SearchCount = 1;
                window.MainVM.DbInfo.ThumbFolder = CreateTempDirectory("imm-search-clear-thumb");
                window.Tabs.SelectedIndex = 2;
                window.MainVM.DbInfo.CurrentTabIndex = 2;

                MovieRecords partialMovie = CreateSearchMovieRecord(
                    1,
                    "alpha one",
                    @"C:\movies\alpha one.mp4"
                );
                window.MainVM.ReplaceMovieRecs([partialMovie]);
                window.MainVM.ReplaceFilteredMovieRecs([partialMovie]);
                SetPrivateField(window, "_startupFeedIsPartialActive", true);
                SetPrivateField(window, "_startupFeedLoadedAllPages", false);
                window.SearchBox.Text = "";
                await WaitForDispatcherIdleAsync();

                InvokeSearchBoxTextChanged(
                    window,
                    CreateSearchBoxTextChangedEventArgs(window.SearchBox)
                );

                await WaitUntilAsync(
                    () =>
                        window.MainVM.MovieRecs.Count == 3
                        && window.MainVM.FilteredMovieRecs.Count == 3
                        && window.MainVM.DbInfo.SearchCount == 3,
                    TimeSpan.FromSeconds(5),
                    "部分ロード中の検索クリア full reload 完了を待てませんでした。"
                );

                return new SearchReloadResult(
                    true,
                    window.MainVM.MovieRecs.Count,
                    window.MainVM.FilteredMovieRecs.Count,
                    window.MainVM.DbInfo.SearchCount,
                    [.. window.MainVM.FilteredMovieRecs.Select(x => x.Movie_Name)]
                );
            }
            finally
            {
                await CloseWindowAsync(window);
                TryDeleteDirectory(window.MainVM.DbInfo.ThumbFolder);
                TryDeleteFile(dbPath);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.True);
            Assert.That(result.MovieCount, Is.EqualTo(3));
            Assert.That(result.FilteredCount, Is.EqualTo(3));
            Assert.That(result.SearchCount, Is.EqualTo(3));
            Assert.That(
                result.FilteredMovieNames,
                Is.EqualTo(["alpha one.mp4", "alpha two.mp4", "beta one.mp4"])
            );
        });
    }

    [Test]
    public async Task RefreshMovieViewAfterRenameAsync_メモリ上一覧だけで検索条件と並び順を再計算できる()
    {
        SearchReloadResult result = await RunOnStaDispatcherAsync(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.Sort = "13";
                window.MainVM.DbInfo.SearchKeyword = "alpha";
                window.MainVM.DbInfo.SearchCount = 0;
                window.Tabs.SelectedIndex = 2;
                window.MainVM.DbInfo.CurrentTabIndex = 2;

                MovieRecords alphaOne = CreateSearchMovieRecord(
                    1,
                    "alpha one.mp4",
                    @"C:\movies\alpha one.mp4"
                );
                MovieRecords alphaTwo = CreateSearchMovieRecord(
                    2,
                    "alpha two.mp4",
                    @"C:\movies\alpha two.mp4"
                );
                MovieRecords betaOne = CreateSearchMovieRecord(
                    3,
                    "beta one.mp4",
                    @"C:\movies\beta one.mp4"
                );
                window.MainVM.ReplaceMovieRecs([alphaOne, alphaTwo, betaOne]);
                window.MainVM.ReplaceFilteredMovieRecs([betaOne]);
                await WaitForDispatcherIdleAsync();

                await InvokePrivateTask(window, "RefreshMovieViewAfterRenameAsync", "13");

                return new SearchReloadResult(
                    true,
                    window.MainVM.MovieRecs.Count,
                    window.MainVM.FilteredMovieRecs.Count,
                    window.MainVM.DbInfo.SearchCount,
                    [.. window.MainVM.FilteredMovieRecs.Select(x => x.Movie_Name)]
                );
            }
            finally
            {
                await CloseWindowAsync(window);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.True);
            Assert.That(result.MovieCount, Is.EqualTo(3));
            Assert.That(result.FilteredCount, Is.EqualTo(2));
            Assert.That(result.SearchCount, Is.EqualTo(2));
            Assert.That(result.FilteredMovieNames, Is.EqualTo(["alpha two.mp4", "alpha one.mp4"]));
        });
    }

    [Test]
    public async Task UpdateSort_単一ライター経由でsystemへ保存できる()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            string dbPath = CreateTempMainDb();
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.DBFullPath = dbPath;
                window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);
                window.MainVM.DbInfo.Sort = "13";

                InvokePrivateVoid(window, "UpdateSort");

                await WaitUntilAsync(
                    () => string.Equals(ReadSystemValue(dbPath, "sort"), "13", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5),
                    "UpdateSort の persister 保存完了を待てませんでした。"
                );

                Assert.That(ReadSystemValue(dbPath, "sort"), Is.EqualTo("13"));
            }
            finally
            {
                await CloseWindowAsync(window);
                TryDeleteFile(dbPath);
            }

            return null;
        });
    }

    [Test]
    public async Task UpdateSort_skinPersister入力完了後はfallback直書きでsystemへ保存できる()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            string dbPath = CreateTempMainDb();
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.DBFullPath = dbPath;
                window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);
                window.MainVM.DbInfo.Sort = "13";

                InvokePrivateVoid(window, "BeginWhiteBrowserSkinStatePersisterShutdown");
                await WaitForDispatcherIdleAsync();

                InvokePrivateVoid(window, "UpdateSort");

                await WaitUntilAsync(
                    () => string.Equals(ReadSystemValue(dbPath, "sort"), "13", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5),
                    "UpdateSort の fallback 直書き完了を待てませんでした。"
                );

                Assert.That(ReadSystemValue(dbPath, "sort"), Is.EqualTo("13"));
            }
            finally
            {
                await CloseWindowAsync(window);
                TryDeleteFile(dbPath);
            }

            return null;
        });
    }

    [Test]
    public async Task UpdateSkin_外部skinを単一ライター経由でsystemとprofileへ保存できる()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            string dbPath = CreateTempMainDb();
            string skinRootPath = CreateExternalSkinRoot("PersistExternalSkin");
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.DBFullPath = dbPath;
                window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);
                window.MainVM.DbInfo.Skin = "PersistExternalSkin";
                window.Tabs.SelectedIndex = 3;
                window.MainVM.DbInfo.CurrentTabIndex = 3;

                InvokePrivateVoid(window, "UpdateSkin");

                await WaitUntilAsync(
                    () =>
                        string.Equals(ReadSystemValue(dbPath, "skin"), "PersistExternalSkin", StringComparison.Ordinal)
                        && string.Equals(ReadProfileValue(dbPath, "PersistExternalSkin", "LastUpperTab"), "DefaultList", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5),
                    "UpdateSkin の persister 保存完了を待てませんでした。"
                );

                Assert.Multiple(() =>
                {
                    Assert.That(ReadSystemValue(dbPath, "skin"), Is.EqualTo("PersistExternalSkin"));
                    Assert.That(
                        ReadProfileValue(dbPath, "PersistExternalSkin", "LastUpperTab"),
                        Is.EqualTo("DefaultList")
                    );
                });
            }
            finally
            {
                await CloseWindowAsync(window);
                TryDeleteFile(dbPath);
                TryDeleteDirectory(skinRootPath);
            }

            return null;
        });
    }

    [Test]
    public async Task UpdateSkin_skinPersister入力完了後はfallback直書きでsystemとprofileへ保存できる()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            string dbPath = CreateTempMainDb();
            string skinRootPath = CreateExternalSkinRoot("PersistExternalSkin");
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.ExternalSkinRootPathForTesting = skinRootPath;
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.DBFullPath = dbPath;
                window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);
                window.MainVM.DbInfo.Skin = "PersistExternalSkin";
                window.Tabs.SelectedIndex = 3;
                window.MainVM.DbInfo.CurrentTabIndex = 3;

                InvokePrivateVoid(window, "BeginWhiteBrowserSkinStatePersisterShutdown");
                await WaitForDispatcherIdleAsync();

                InvokePrivateVoid(window, "UpdateSkin");

                await WaitUntilAsync(
                    () =>
                        string.Equals(ReadSystemValue(dbPath, "skin"), "PersistExternalSkin", StringComparison.Ordinal)
                        && string.Equals(ReadProfileValue(dbPath, "PersistExternalSkin", "LastUpperTab"), "DefaultList", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5),
                    "UpdateSkin の fallback 直書き完了を待てませんでした。"
                );

                Assert.Multiple(() =>
                {
                    Assert.That(ReadSystemValue(dbPath, "skin"), Is.EqualTo("PersistExternalSkin"));
                    Assert.That(
                        ReadProfileValue(dbPath, "PersistExternalSkin", "LastUpperTab"),
                        Is.EqualTo("DefaultList")
                    );
                });
            }
            finally
            {
                await CloseWindowAsync(window);
                TryDeleteFile(dbPath);
                TryDeleteDirectory(skinRootPath);
            }

            return null;
        });
    }

    [Test]
    public async Task ApplySkinByName_組み込みskinを単一ライター経由でsystemへ保存できる()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            string dbPath = CreateTempMainDb();
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.DBFullPath = dbPath;
                window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);

                bool applied = window.ApplySkinByName("DefaultGrid", persistToCurrentDb: true);
                Assert.That(applied, Is.True, "ApplySkinByName が built-in skin を解決できませんでした。");

                await WaitUntilAsync(
                    () => string.Equals(ReadSystemValue(dbPath, "skin"), "DefaultGrid", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5),
                    "ApplySkinByName built-in の persister 保存完了を待てませんでした。"
                );

                Assert.Multiple(() =>
                {
                    Assert.That(ReadSystemValue(dbPath, "skin"), Is.EqualTo("DefaultGrid"));
                    Assert.That(ReadProfileValue(dbPath, "DefaultGrid", "LastUpperTab"), Is.EqualTo(""));
                });
            }
            finally
            {
                await CloseWindowAsync(window);
                TryDeleteFile(dbPath);
            }

            return null;
        });
    }

    [Test]
    public async Task ApplySkinByName_組み込みskinもskinPersister入力完了後はfallback直書きでsystemへ保存できる()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            string dbPath = CreateTempMainDb();
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.DBFullPath = dbPath;
                window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);

                InvokePrivateVoid(window, "BeginWhiteBrowserSkinStatePersisterShutdown");
                await WaitForDispatcherIdleAsync();

                bool applied = window.ApplySkinByName("DefaultGrid", persistToCurrentDb: true);
                Assert.That(applied, Is.True, "ApplySkinByName が built-in skin を解決できませんでした。");

                await WaitUntilAsync(
                    () => string.Equals(ReadSystemValue(dbPath, "skin"), "DefaultGrid", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5),
                    "ApplySkinByName built-in の fallback 直書き完了を待てませんでした。"
                );

                Assert.Multiple(() =>
                {
                    Assert.That(ReadSystemValue(dbPath, "skin"), Is.EqualTo("DefaultGrid"));
                    Assert.That(ReadProfileValue(dbPath, "DefaultGrid", "LastUpperTab"), Is.EqualTo(""));
                });
            }
            finally
            {
                await CloseWindowAsync(window);
                TryDeleteFile(dbPath);
            }

            return null;
        });
    }

    [Test]
    public async Task SaveEverythingLastSyncUtc_単一ライター経由でsystemへ保存できる()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            string dbPath = CreateTempMainDb();
            string watchFolder = CreateTempDirectory("watch-last-sync");
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.DBFullPath = dbPath;
                window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);
                SetPrivateField(window, "_watchScanScopeStamp", 7L);

                DateTime lastSyncUtc = new(2026, 4, 15, 1, 2, 3, DateTimeKind.Utc);
                string attr = BuildEverythingLastSyncAttrForTest(watchFolder, sub: true);

                InvokePrivateMethod(
                    window,
                    "SaveEverythingLastSyncUtc",
                    dbPath,
                    7L,
                    watchFolder,
                    true,
                    lastSyncUtc
                );

                await WaitUntilAsync(
                    () =>
                        string.Equals(
                            ReadSystemValue(dbPath, attr),
                            lastSyncUtc.ToString("O"),
                            StringComparison.Ordinal
                        ),
                    TimeSpan.FromSeconds(5),
                    "SaveEverythingLastSyncUtc の persister 保存完了を待てませんでした。"
                );

                Assert.That(ReadSystemValue(dbPath, attr), Is.EqualTo(lastSyncUtc.ToString("O")));
            }
            finally
            {
                await CloseWindowAsync(window);
                TryDeleteDirectory(watchFolder);
                TryDeleteFile(dbPath);
            }

            return null;
        });
    }

    [Test]
    public async Task SaveEverythingLastSyncUtc_skinPersister入力完了後はfallback直書きでsystemへ保存できる()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            string dbPath = CreateTempMainDb();
            string watchFolder = CreateTempDirectory("watch-last-sync");
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.DBFullPath = dbPath;
                window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);
                SetPrivateField(window, "_watchScanScopeStamp", 9L);

                DateTime lastSyncUtc = new(2026, 4, 15, 4, 5, 6, DateTimeKind.Utc);
                string attr = BuildEverythingLastSyncAttrForTest(watchFolder, sub: false);

                InvokePrivateVoid(window, "BeginWhiteBrowserSkinStatePersisterShutdown");
                await WaitForDispatcherIdleAsync();

                InvokePrivateMethod(
                    window,
                    "SaveEverythingLastSyncUtc",
                    dbPath,
                    9L,
                    watchFolder,
                    false,
                    lastSyncUtc
                );

                await WaitUntilAsync(
                    () =>
                        string.Equals(
                            ReadSystemValue(dbPath, attr),
                            lastSyncUtc.ToString("O"),
                            StringComparison.Ordinal
                        ),
                    TimeSpan.FromSeconds(5),
                    "SaveEverythingLastSyncUtc の fallback 直書き完了を待てませんでした。"
                );

                Assert.That(ReadSystemValue(dbPath, attr), Is.EqualTo(lastSyncUtc.ToString("O")));
            }
            finally
            {
                await CloseWindowAsync(window);
                TryDeleteDirectory(watchFolder);
                TryDeleteFile(dbPath);
            }

            return null;
        });
    }

    [Test]
    public async Task PersistDbSettingsValues_単一ライター経由でsystemへ保存できる()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            string dbPath = CreateTempMainDb();
            string thumbFolder = CreateTempDirectory("thumb-root");
            string bookmarkFolder = CreateTempDirectory("bookmark-root");
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.DBFullPath = dbPath;
                window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);

                InvokePrivateMethod(
                    window,
                    "PersistDbSettingsValues",
                    dbPath,
                    thumbFolder,
                    bookmarkFolder,
                    "15",
                    @"C:\Tools\Player\player.exe",
                    "/start <ms>"
                );

                await WaitUntilAsync(
                    () =>
                        string.Equals(ReadSystemValue(dbPath, "thum"), thumbFolder, StringComparison.Ordinal)
                        && string.Equals(ReadSystemValue(dbPath, "bookmark"), bookmarkFolder, StringComparison.Ordinal)
                        && string.Equals(ReadSystemValue(dbPath, "keepHistory"), "15", StringComparison.Ordinal)
                        && string.Equals(ReadSystemValue(dbPath, "playerPrg"), @"C:\Tools\Player\player.exe", StringComparison.Ordinal)
                        && string.Equals(ReadSystemValue(dbPath, "playerParam"), "/start <ms>", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5),
                    "PersistDbSettingsValues の persister 保存完了を待てませんでした。"
                );

                Assert.Multiple(() =>
                {
                    Assert.That(ReadSystemValue(dbPath, "thum"), Is.EqualTo(thumbFolder));
                    Assert.That(ReadSystemValue(dbPath, "bookmark"), Is.EqualTo(bookmarkFolder));
                    Assert.That(ReadSystemValue(dbPath, "keepHistory"), Is.EqualTo("15"));
                    Assert.That(ReadSystemValue(dbPath, "playerPrg"), Is.EqualTo(@"C:\Tools\Player\player.exe"));
                    Assert.That(ReadSystemValue(dbPath, "playerParam"), Is.EqualTo("/start <ms>"));
                    Assert.That(window.MainVM.DbInfo.ThumbFolder, Does.Contain("thumb-root-"));
                    Assert.That(window.MainVM.DbInfo.BookmarkFolder, Is.EqualTo(bookmarkFolder));
                });
            }
            finally
            {
                await CloseWindowAsync(window);
                TryDeleteDirectory(thumbFolder);
                TryDeleteDirectory(bookmarkFolder);
                TryDeleteFile(dbPath);
            }

            return null;
        });
    }

    [Test]
    public async Task PersistDbSettingsValues_skinPersister入力完了後はfallback直書きでsystemへ保存できる()
    {
        await RunOnStaDispatcherAsync<object?>(async () =>
        {
            using TestEnvironmentScope scope = TestEnvironmentScope.Create();
            string dbPath = CreateTempMainDb();
            string thumbFolder = CreateTempDirectory("thumb-root");
            string bookmarkFolder = CreateTempDirectory("bookmark-root");
            MainWindow window = CreateHiddenMainWindow();

            try
            {
                window.Show();
                await WaitForDispatcherIdleAsync();

                window.MainVM.DbInfo.DBFullPath = dbPath;
                window.MainVM.DbInfo.DBName = Path.GetFileNameWithoutExtension(dbPath);

                InvokePrivateVoid(window, "BeginWhiteBrowserSkinStatePersisterShutdown");
                await WaitForDispatcherIdleAsync();

                InvokePrivateMethod(
                    window,
                    "PersistDbSettingsValues",
                    dbPath,
                    thumbFolder,
                    bookmarkFolder,
                    "30",
                    @"C:\Tools\Player\player2.exe",
                    "<file> player -seek pos=<ms>"
                );

                await WaitUntilAsync(
                    () =>
                        string.Equals(ReadSystemValue(dbPath, "thum"), thumbFolder, StringComparison.Ordinal)
                        && string.Equals(ReadSystemValue(dbPath, "bookmark"), bookmarkFolder, StringComparison.Ordinal)
                        && string.Equals(ReadSystemValue(dbPath, "keepHistory"), "30", StringComparison.Ordinal)
                        && string.Equals(ReadSystemValue(dbPath, "playerPrg"), @"C:\Tools\Player\player2.exe", StringComparison.Ordinal)
                        && string.Equals(ReadSystemValue(dbPath, "playerParam"), "<file> player -seek pos=<ms>", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(5),
                    "PersistDbSettingsValues の fallback 直書き完了を待てませんでした。"
                );

                Assert.Multiple(() =>
                {
                    Assert.That(ReadSystemValue(dbPath, "thum"), Is.EqualTo(thumbFolder));
                    Assert.That(ReadSystemValue(dbPath, "bookmark"), Is.EqualTo(bookmarkFolder));
                    Assert.That(ReadSystemValue(dbPath, "keepHistory"), Is.EqualTo("30"));
                    Assert.That(ReadSystemValue(dbPath, "playerPrg"), Is.EqualTo(@"C:\Tools\Player\player2.exe"));
                    Assert.That(ReadSystemValue(dbPath, "playerParam"), Is.EqualTo("<file> player -seek pos=<ms>"));
                    Assert.That(window.MainVM.DbInfo.ThumbFolder, Does.Contain("thumb-root-"));
                    Assert.That(window.MainVM.DbInfo.BookmarkFolder, Is.EqualTo(bookmarkFolder));
                });
            }
            finally
            {
                await CloseWindowAsync(window);
                TryDeleteDirectory(thumbFolder);
                TryDeleteDirectory(bookmarkFolder);
                TryDeleteFile(dbPath);
            }

            return null;
        });
    }

    private static MainWindow CreateHiddenMainWindow()
    {
        return new MainWindow
        {
            Left = -20000,
            Top = -20000,
            Width = 960,
            Height = 720,
            Opacity = 0.01,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
            WindowStartupLocation = WindowStartupLocation.Manual,
            SkipMainWindowClosingSideEffectsForTesting = true,
        };
    }

    private static KeyEventArgs CreatePreviewKeyEvent(UIElement source, Key key)
    {
        PresentationSource presentationSource = PresentationSource.FromVisual(source)
            ?? throw new AssertionException("PreviewKeyDown 用の PresentationSource を取得できません。");
        KeyEventArgs args = new(Keyboard.PrimaryDevice, presentationSource, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        args.Source = source;
        return args;
    }

    private static void InvokeSearchBoxPreviewKeyDown(MainWindow window, KeyEventArgs args)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "SearchBox_PreviewKeyDown",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null, "SearchBox_PreviewKeyDown");
        method.Invoke(window, [window.SearchBox, args]);
    }

    private static TextChangedEventArgs CreateSearchBoxTextChangedEventArgs(UIElement source)
    {
        TextChangedEventArgs args = new(TextBox.TextChangedEvent, UndoAction.None)
        {
            RoutedEvent = TextBox.TextChangedEvent,
        };
        args.Source = source;
        return args;
    }

    private static void InvokeSearchBoxTextChanged(MainWindow window, TextChangedEventArgs args)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "SearchBox_TextChanged",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null, "SearchBox_TextChanged");
        method.Invoke(window, [window.SearchBox, args]);
    }

    private static void InvokePrivateVoid(MainWindow window, string methodName)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null
        )!;
        Assert.That(method, Is.Not.Null, methodName);
        method.Invoke(window, null);
    }

    private static object? InvokePrivateMethod(MainWindow window, string methodName, params object[] args)
    {
        Type[] parameterTypes = args.Select(static arg => arg.GetType()).ToArray();
        MethodInfo method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: parameterTypes,
            modifiers: null
        )!;
        Assert.That(method, Is.Not.Null, methodName);
        return method.Invoke(window, args);
    }

    private static async Task InvokePrivateTask(MainWindow window, string methodName, params object[] args)
    {
        object? result = InvokePrivateMethod(window, methodName, args);
        Assert.That(result, Is.AssignableTo<Task>(), methodName);
        await (Task)result!;
    }

    private static async Task<bool> InvokeExternalSkinSearchAsync(MainWindow window, string keyword)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "ExecuteExternalSkinSearchAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null, "ExecuteExternalSkinSearchAsync");
        Task<bool> task = (Task<bool>)method.Invoke(window, [keyword])!;
        return await task;
    }

    private static string CreateTempMainDb()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"imm-search-enter-{Guid.NewGuid():N}.wb");
        bool created = SQLite.TryCreateDatabase(dbPath, out string errorMessage);
        Assert.That(created, Is.True, errorMessage);
        return dbPath;
    }

    private static string CreateTempDirectory(string prefix)
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            $"{prefix}-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private static string CreateExternalSkinRoot(string skinName)
    {
        string rootPath = Path.Combine(Path.GetTempPath(), $"imm-skin-root-{Guid.NewGuid():N}");
        string skinDirectoryPath = Path.Combine(rootPath, skinName);
        Directory.CreateDirectory(skinDirectoryPath);
        File.WriteAllText(
            Path.Combine(skinDirectoryPath, skinName + ".htm"),
            """
            <html>
            <body>
              <div id="config">
                thum-width : 160;
                thum-height : 120;
                thum-column : 1;
                thum-row : 1;
              </div>
            </body>
            </html>
            """
        );
        return rootPath;
    }

    private static void SeedMovieRow(string dbPath)
    {
        SeedSearchMovieRow(
            dbPath,
            1,
            "target movie",
            @"C:\movies\target movie.mp4",
            "target"
        );
    }

    private static void SeedSearchReloadRows(string dbPath)
    {
        SeedSearchMovieRow(dbPath, 1, "alpha one", @"C:\movies\alpha one.mp4", "alpha");
        SeedSearchMovieRow(dbPath, 2, "alpha two", @"C:\movies\alpha two.mp4", "alpha");
        SeedSearchMovieRow(dbPath, 3, "beta one", @"C:\movies\beta one.mp4", "beta");
    }

    private static void SeedSearchMovieRow(
        string dbPath,
        long movieId,
        string movieName,
        string moviePath,
        string kana
    )
    {
        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = @"
INSERT INTO movie (
    movie_id,
    movie_name,
    movie_path,
    movie_length,
    movie_size,
    last_date,
    file_date,
    regist_date,
    score,
    view_count,
    hash,
    container,
    video,
    audio,
    kana,
    tag,
    comment1,
    comment2,
    comment3
)
VALUES (
    @movie_id,
    @movie_name,
    @movie_path,
    60,
    100,
    '2026-04-07 10:00:00',
    '2026-04-07 10:00:00',
    '2026-04-07 10:00:00',
    1,
    1,
    @hash,
    'mp4',
    'h264',
    'aac',
    @kana,
    '',
    '',
    '',
    ''
);";
        command.Parameters.AddWithValue("@movie_id", movieId);
        command.Parameters.AddWithValue("@movie_name", movieName);
        command.Parameters.AddWithValue("@movie_path", moviePath);
        command.Parameters.AddWithValue("@hash", $"hash-{movieId}");
        command.Parameters.AddWithValue("@kana", kana);
        command.ExecuteNonQuery();
    }

    private static MovieRecords CreateSearchMovieRecord(long movieId, string movieName, string moviePath)
    {
        return new MovieRecords
        {
            Movie_Id = movieId,
            Movie_Name = movieName,
            Movie_Path = moviePath,
            Kana = movieName,
            Movie_Length = "00:01:00",
            Movie_Size = 100,
            Last_Date = "2026-04-07 10:00:00",
            File_Date = "2026-04-07 10:00:00",
            Regist_Date = "2026-04-07 10:00:00",
            Score = 1,
            View_Count = 1,
            Hash = $"hash-{movieId}",
            Container = "mp4",
            Video = "h264",
            Audio = "aac",
        };
    }

    private static string[] ReadHistoryTexts(string dbPath)
    {
        using SQLiteConnection connection = SQLite.CreateReadOnlyConnection(dbPath);
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = "select find_text from history order by find_id";
        using SQLiteDataReader reader = command.ExecuteReader();
        List<string> result = [];
        while (reader.Read())
        {
            result.Add(reader["find_text"]?.ToString() ?? "");
        }

        return [.. result];
    }

    private static string ReadSystemValue(string dbPath, string key)
    {
        using SQLiteConnection connection = SQLite.CreateReadOnlyConnection(dbPath);
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = "select value from system where attr = @attr limit 1";
        command.Parameters.AddWithValue("@attr", key ?? "");
        return command.ExecuteScalar()?.ToString() ?? "";
    }

    private static string ReadProfileValue(string dbPath, string skinName, string key)
    {
        using SQLiteConnection connection = SQLite.CreateReadOnlyConnection(dbPath);
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = "select value from profile where skin = @skin and key = @key limit 1";
        command.Parameters.AddWithValue("@skin", skinName ?? "");
        command.Parameters.AddWithValue("@key", key ?? "");
        return command.ExecuteScalar()?.ToString() ?? "";
    }

    private static string BuildEverythingLastSyncAttrForTest(string watchFolder, bool sub)
    {
        string normalized = Path.GetFullPath(watchFolder).Trim().ToLowerInvariant();
        string material = $"{normalized}|sub={(sub ? 1 : 0)}";
        byte[] bytes = Encoding.UTF8.GetBytes(material);
        byte[] hash = SHA256.HashData(bytes);
        string hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"everything_last_sync_utc_{hex[..16]}";
    }

    private static async Task CloseWindowAsync(MainWindow window)
    {
        if (window == null || !window.IsLoaded)
        {
            return;
        }

        window.Close();
        await WaitForDispatcherIdleAsync();
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string timeoutMessage
    )
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await WaitForDispatcherIdleAsync();
            await Task.Delay(50);
        }

        throw new AssertionException(timeoutMessage);
    }

    private static async Task WaitForDispatcherIdleAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Yield();
    }

    private static Task<T> RunOnStaDispatcherAsync<T>(Func<Task<T>> action)
    {
        return RunOnSharedUiThreadAsync(action);
    }

    private sealed class TestEnvironmentScope : IDisposable
    {
        private readonly bool originalAutoOpen;
        private readonly bool originalConfirmExit;
        private readonly string originalLastDoc;
        private readonly System.Drawing.Point originalMainLocation;
        private readonly System.Drawing.Size originalMainSize;
        private readonly int originalEverythingIntegrationMode;
        private readonly string originalThemeMode;
        private readonly StringCollection originalRecentFiles;
        private readonly string originalCurrentDirectory;
        private readonly string isolatedCurrentDirectory;

        private TestEnvironmentScope(
            bool originalAutoOpen,
            bool originalConfirmExit,
            string originalLastDoc,
            System.Drawing.Point originalMainLocation,
            System.Drawing.Size originalMainSize,
            int originalEverythingIntegrationMode,
            string originalThemeMode,
            StringCollection originalRecentFiles,
            string originalCurrentDirectory,
            string isolatedCurrentDirectory
        )
        {
            this.originalAutoOpen = originalAutoOpen;
            this.originalConfirmExit = originalConfirmExit;
            this.originalLastDoc = originalLastDoc;
            this.originalMainLocation = originalMainLocation;
            this.originalMainSize = originalMainSize;
            this.originalEverythingIntegrationMode = originalEverythingIntegrationMode;
            this.originalThemeMode = originalThemeMode;
            this.originalRecentFiles = originalRecentFiles;
            this.originalCurrentDirectory = originalCurrentDirectory;
            this.isolatedCurrentDirectory = isolatedCurrentDirectory;
        }

        public static TestEnvironmentScope Create()
        {
            string originalCurrentDirectory = Environment.CurrentDirectory;
            string isolatedCurrentDirectory = Path.Combine(
                Path.GetTempPath(),
                $"imm-search-enter-{Guid.NewGuid():N}"
            );
            Directory.CreateDirectory(isolatedCurrentDirectory);
            Environment.CurrentDirectory = isolatedCurrentDirectory;

            IndigoMovieManager.Properties.Settings settings = IndigoMovieManager.Properties.Settings.Default;
            StringCollection originalRecentFiles = CloneStringCollection(settings.RecentFiles);
            TestEnvironmentScope scope = new(
                settings.AutoOpen,
                settings.ConfirmExit,
                settings.LastDoc ?? "",
                settings.MainLocation,
                settings.MainSize,
                settings.EverythingIntegrationMode,
                settings.ThemeMode ?? "",
                originalRecentFiles,
                originalCurrentDirectory,
                isolatedCurrentDirectory
            );

            settings.AutoOpen = false;
            settings.ConfirmExit = false;
            settings.LastDoc = "";
            settings.EverythingIntegrationMode = 0;
            settings.ThemeMode = "Original";
            settings.MainLocation = new System.Drawing.Point(10, 10);
            settings.MainSize = new System.Drawing.Size(960, 720);
            settings.RecentFiles = new StringCollection();

            return scope;
        }

        public void Dispose()
        {
            IndigoMovieManager.Properties.Settings settings = IndigoMovieManager.Properties.Settings.Default;
            settings.AutoOpen = originalAutoOpen;
            settings.ConfirmExit = originalConfirmExit;
            settings.LastDoc = originalLastDoc;
            settings.MainLocation = originalMainLocation;
            settings.MainSize = originalMainSize;
            settings.EverythingIntegrationMode = originalEverythingIntegrationMode;
            settings.ThemeMode = originalThemeMode;
            settings.RecentFiles = CloneStringCollection(originalRecentFiles);

            Environment.CurrentDirectory = originalCurrentDirectory;
            TryDeleteDirectory(isolatedCurrentDirectory);
        }

        private static StringCollection CloneStringCollection(StringCollection source)
        {
            StringCollection clone = new();
            if (source == null)
            {
                return clone;
            }

            clone.AddRange(source.Cast<string>().ToArray());
            return clone;
        }
    }

    private static async Task<T> RunOnSharedUiThreadAsync<T>(Func<Task<T>> action)
    {
        await WaitAsync(
            EnsureSharedUiThreadReadyAsync(),
            TimeSpan.FromSeconds(10),
            "共有 UI スレッドの初期化が 10 秒以内に完了しませんでした。"
        );

        Dispatcher dispatcher = uiDispatcher
            ?? throw new AssertionException("共有 UI Dispatcher が初期化されていません。");
        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = dispatcher.BeginInvoke(
            DispatcherPriority.Normal,
            new Action(() =>
            {
                _ = ExecuteActionAsync();
            })
        );

        return await completion.Task;

        async Task ExecuteActionAsync()
        {
            try
            {
                T result = await action();
                completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }
    }

    private static Task EnsureSharedUiThreadReadyAsync()
    {
        lock (UiThreadSync)
        {
            if (uiThreadReady?.Task != null)
            {
                return uiThreadReady.Task;
            }

            uiThreadReady = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            uiThread = new Thread(
                () =>
                {
                    try
                    {
                        SynchronizationContext.SetSynchronizationContext(
                            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher)
                        );
                        uiDispatcher = Dispatcher.CurrentDispatcher;
                        InitializeSharedUiApplication();
                        uiThreadReady.TrySetResult(true);
                        Dispatcher.Run();
                    }
                    catch (Exception ex)
                    {
                        uiThreadReady.TrySetException(ex);
                    }
                }
            );
            uiThread.IsBackground = true;
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
            return uiThreadReady.Task;
        }
    }

    private static void InitializeSharedUiApplication()
    {
        if (Application.ResourceAssembly == null)
        {
            Application.ResourceAssembly = typeof(MainWindow).Assembly;
        }

        if (Application.Current == null)
        {
            Application application = new()
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            application.Resources.MergedDictionaries.Add(
                new BundledTheme
                {
                    BaseTheme = BaseTheme.Inherit,
                    PrimaryColor = PrimaryColor.Indigo,
                    SecondaryColor = SecondaryColor.DeepPurple,
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign3.Defaults.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Button.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.CheckBox.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.ComboBox.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.DataGrid.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.GroupBox.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.ListView.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Slider.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.TextBox.xaml",
                        UriKind.Absolute
                    ),
                }
            );
            application.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new Uri(
                        "pack://application:,,,/IndigoMovieManager;component/Themes/Generic.xaml",
                        UriKind.Absolute
                    ),
                }
            );
        }

        IndigoMovieManager.Properties.Settings.Default.ThemeMode = "Original";
        App.ApplyTheme(IndigoMovieManager.Properties.Settings.Default.ThemeMode);
    }

    private static async Task WaitAsync(Task task, TimeSpan timeout, string timeoutMessage)
    {
        Task completedTask = await Task.WhenAny(task, Task.Delay(timeout));
        if (!ReferenceEquals(completedTask, task))
        {
            throw new AssertionException(timeoutMessage);
        }

        await task;
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

    private static void TryDeleteFile(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // 一時ファイル掃除失敗は本体判定を優先する。
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // 一時フォルダ掃除失敗は本体判定を優先する。
            }
        }
    }

    private readonly record struct SearchEnterResult(
        string SearchKeyword,
        int SearchCount,
        string[] UiHistoryTexts,
        string[] DbHistoryTexts,
        bool WasHandled
    );

    private readonly record struct SearchReloadResult(
        bool Executed,
        int MovieCount,
        int FilteredCount,
        int SearchCount,
        string[] FilteredMovieNames
    );
}
