using IndigoMovieManager;
using IndigoMovieManager.DB;
using IndigoMovieManager.ViewModels;
using System.Collections;
using System.Data.SQLite;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchDeferredScanStatePolicyTests
{
    [Test]
    public void BuildDeferredWatchScanScopeKey_DBが違えば同じ監視フォルダでも別スコープになる()
    {
        string first = MainWindow.BuildDeferredWatchScanScopeKey(
            @"C:\Db\first.wb",
            @"E:\Movies",
            includeSubfolders: true
        );
        string second = MainWindow.BuildDeferredWatchScanScopeKey(
            @"C:\Db\second.wb",
            @"E:\Movies",
            includeSubfolders: true
        );

        Assert.That(first, Is.Not.EqualTo(second));
    }

    [Test]
    public void MergeDeferredWatchScanCursorUtc_deferred継続中は新しいcursorを保持する()
    {
        DateTime existingCursorUtc = new(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);
        DateTime observedCursorUtc = new(2026, 3, 20, 10, 5, 0, DateTimeKind.Utc);

        DateTime? result = MainWindow.MergeDeferredWatchScanCursorUtc(
            existingCursorUtc,
            observedCursorUtc
        );

        Assert.That(result, Is.EqualTo(observedCursorUtc));
    }

    [Test]
    public void CanUseWatchScanScope_同一DBかつ同一stampなら有効にする()
    {
        bool result = MainWindow.CanUseWatchScanScope(
            currentDbFullPath: @"D:\Db\Main.wb",
            snapshotDbFullPath: @"d:\db\main.wb",
            requestScopeStamp: 7,
            currentScopeStamp: 7
        );

        Assert.That(result, Is.True);
    }

    [Test]
    public void CanUseWatchScanScope_same_dbでもstampが変わっていればstaleとして捨てる()
    {
        bool result = MainWindow.CanUseWatchScanScope(
            currentDbFullPath: @"D:\Db\Main.wb",
            snapshotDbFullPath: @"D:\Db\Main.wb",
            requestScopeStamp: 7,
            currentScopeStamp: 8
        );

        Assert.That(result, Is.False);
    }

    [Test]
    public void ResolveMissingThumbnailRescueGuardAction_watch_scope_staleなら黙って捨てる()
    {
        MainWindow.MissingThumbnailRescueGuardAction result =
            MainWindow.ResolveMissingThumbnailRescueGuardAction(
                isWatchMode: true,
                isWatchSuppressedByUi: false,
                isBackgroundWorkSuppressedByUserPriority: false,
                isCurrentWatchScope: false
            );

        Assert.That(
            result,
            Is.EqualTo(MainWindow.MissingThumbnailRescueGuardAction.DropStaleScope)
        );
    }

    [Test]
    public void ReplaceDeferredWatchScanBatch_stale_scopeでは既存deferredを上書きしない()
    {
        const string dbFullPath = @"D:\Db\Main.wb";
        const string watchFolder = @"E:\Movies";
        MainWindow window = CreateMainWindowForDeferredScanStateTests(dbFullPath);
        SetPrivateField(window, "_watchScanScopeStamp", 7L);
        SetPrivateField(window, "_deferredWatchScanSync", new object());
        InitializePrivateDictionaryField(window, "_deferredWatchScanStateByScope");

        InvokeVoid(
            window,
            "ReplaceDeferredWatchScanBatch",
            dbFullPath,
            7L,
            watchFolder,
            true,
            new[] { @"E:\Movies\old-1.mp4" },
            (DateTime?)null
        );
        InvokeVoid(
            window,
            "ReplaceDeferredWatchScanBatch",
            dbFullPath,
            6L,
            watchFolder,
            true,
            new[] { @"E:\Movies\new-1.mp4" },
            (DateTime?)null
        );

        IDictionary dictionary = (IDictionary)GetPrivateField(
            window,
            "_deferredWatchScanStateByScope"
        );
        string scopeKey = MainWindow.BuildDeferredWatchScanScopeKey(dbFullPath, watchFolder, true);
        object state = dictionary[scopeKey]!;
        Queue<string> pendingPaths = (Queue<string>)state
            .GetType()
            .GetProperty("PendingPaths", BindingFlags.Instance | BindingFlags.Public)!
            .GetValue(state)!;

        Assert.That(pendingPaths.ToArray(), Is.EqualTo([@"E:\Movies\old-1.mp4"]));
    }

    [Test]
    public void SaveEverythingLastSyncUtc_stale_scopeではsystemテーブルへ保存しない()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"watch-deferred-scan-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(tempRoot);
        string dbFullPath = Path.Combine(tempRoot, "main.wb");

        try
        {
            CreateSystemTableOnlyDb(dbFullPath);

            MainWindow window = CreateMainWindowForDeferredScanStateTests(dbFullPath);
            SetPrivateField(window, "_watchScanScopeStamp", 7L);

            InvokeVoid(
                window,
                "SaveEverythingLastSyncUtc",
                dbFullPath,
                6L,
                @"E:\Movies",
                true,
                new DateTime(2026, 3, 20, 10, 5, 0, DateTimeKind.Utc)
            );

            Assert.That(CountSystemRows(dbFullPath), Is.EqualTo(0));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    private static MainWindow CreateMainWindowForDeferredScanStateTests(string dbFullPath)
    {
        MainWindow window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        MainWindowViewModel mainVm =
            (MainWindowViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainWindowViewModel));
        mainVm.DbInfo = new DBInfo
        {
            DBFullPath = dbFullPath,
        };

        SetPrivateField(window, "MainVM", mainVm);
        return window;
    }

    private static void CreateSystemTableOnlyDb(string dbFullPath)
    {
        using SQLiteConnection connection = new($"Data Source={dbFullPath}");
        connection.Open();

        using SQLiteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "create table system (attr text primary key, value text)";
        cmd.ExecuteNonQuery();
    }

    private static int CountSystemRows(string dbFullPath)
    {
        using SQLiteConnection connection = new($"Data Source={dbFullPath}");
        connection.Open();

        using SQLiteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "select count(*) from system";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void InitializePrivateDictionaryField(MainWindow window, string fieldName)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(field, Is.Not.Null, fieldName);
        object value = Activator.CreateInstance(field.FieldType, StringComparer.OrdinalIgnoreCase)!;
        field.SetValue(window, value);
    }

    private static void InvokeVoid(MainWindow window, string methodName, params object?[] args)
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
