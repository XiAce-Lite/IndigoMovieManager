using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class LogFileTimeWindowSeparatorTests
{
    [Test]
    public void BuildWindowLabel_同日の時刻差では同じ日付ラベルになる()
    {
        string morningLabel = LogFileTimeWindowSeparator.BuildWindowLabel(
            new DateTime(2026, 3, 17, 0, 0, 0)
        );
        string nightLabel = LogFileTimeWindowSeparator.BuildWindowLabel(
            new DateTime(2026, 3, 17, 23, 59, 59)
        );

        Assert.That(morningLabel, Is.EqualTo("20260317"));
        Assert.That(nightLabel, Is.EqualTo("20260317"));
    }

    [Test]
    public void BuildWindowLabel_日付が変わるとラベルも変わる()
    {
        string todayLabel = LogFileTimeWindowSeparator.BuildWindowLabel(
            new DateTime(2026, 3, 17, 23, 59, 59)
        );
        string nextDayLabel = LogFileTimeWindowSeparator.BuildWindowLabel(
            new DateTime(2026, 3, 18, 0, 0, 0)
        );

        Assert.That(todayLabel, Is.EqualTo("20260317"));
        Assert.That(nextDayLabel, Is.EqualTo("20260318"));
    }

    [Test]
    public void PrepareForWrite_同日内では退避しない()
    {
        string tempRoot = CreateTempRoot();

        try
        {
            string logPath = Path.Combine(tempRoot, "debug-runtime.log");
            File.WriteAllText(logPath, "same-day");
            File.SetLastWriteTime(logPath, new DateTime(2026, 3, 17, 13, 50, 0));

            string resolvedPath = LogFileTimeWindowSeparator.PrepareForWrite(
                logPath,
                new DateTime(2026, 3, 17, 23, 5, 0)
            );

            Assert.That(resolvedPath, Is.EqualTo(Path.GetFullPath(logPath)));
            Assert.That(File.Exists(logPath), Is.True);
            Assert.That(File.ReadAllText(logPath), Is.EqualTo("same-day"));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Test]
    public void PrepareForWrite_日跨ぎで前日ログを退避する()
    {
        string tempRoot = CreateTempRoot();

        try
        {
            string logPath = Path.Combine(tempRoot, "thumbnail-create-process.csv");
            File.WriteAllText(logPath, "previous-day");
            File.SetLastWriteTime(logPath, new DateTime(2026, 3, 17, 18, 30, 0));

            string resolvedPath = LogFileTimeWindowSeparator.PrepareForWrite(
                logPath,
                new DateTime(2026, 3, 18, 9, 15, 0)
            );

            string archivedPath = Path.Combine(
                tempRoot,
                "thumbnail-create-process_20260317.csv"
            );

            Assert.That(resolvedPath, Is.EqualTo(Path.GetFullPath(logPath)));
            Assert.That(File.Exists(archivedPath), Is.True);
            Assert.That(File.Exists(logPath), Is.False);
            Assert.That(File.ReadAllText(archivedPath), Is.EqualTo("previous-day"));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Test]
    public void PrepareForWrite_サイズ超過で同日ログを退避する()
    {
        string tempRoot = CreateTempRoot();

        try
        {
            string logPath = Path.Combine(tempRoot, "debug-runtime.log");
            File.WriteAllText(logPath, "123456");
            File.SetLastWriteTime(logPath, new DateTime(2026, 3, 17, 10, 30, 0));

            string resolvedPath = LogFileTimeWindowSeparator.PrepareForWrite(
                logPath,
                new DateTime(2026, 3, 17, 11, 0, 0),
                maxFileBytes: 5
            );

            string archivedPath = Path.Combine(tempRoot, "debug-runtime_20260317.log");

            Assert.That(resolvedPath, Is.EqualTo(Path.GetFullPath(logPath)));
            Assert.That(File.Exists(archivedPath), Is.True);
            Assert.That(File.Exists(logPath), Is.False);
            Assert.That(File.ReadAllText(archivedPath), Is.EqualTo("123456"));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string tempRoot)
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
