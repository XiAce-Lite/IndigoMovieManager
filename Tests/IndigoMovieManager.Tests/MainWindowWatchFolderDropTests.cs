using System.Windows;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowWatchFolderDropTests
{
    [Test]
    public void CanAcceptWatchFolderDrop_MainDb未選択でも有効フォルダなら受け付ける()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string directoryPath = Directory.CreateDirectory(Path.Combine(tempRoot, "drop")).FullName;

            bool result = MainWindow.CanAcceptWatchFolderDrop("", [directoryPath]);

            Assert.That(result, Is.True);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void CanAcceptWatchFolderDrop_MainDb選択済みかつ有効フォルダなら受け付ける()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string directoryPath = Directory.CreateDirectory(Path.Combine(tempRoot, "drop")).FullName;
            string dbPath = Path.Combine(tempRoot, "sample.wb");

            bool result = MainWindow.CanAcceptWatchFolderDrop(dbPath, [directoryPath]);

            Assert.That(result, Is.True);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void CanAcceptWatchFolderDrop_ファイルだけなら受け付けない()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string filePath = Path.Combine(tempRoot, "sample.txt");
            File.WriteAllText(filePath, "sample");
            string dbPath = Path.Combine(tempRoot, "sample.wb");

            bool result = MainWindow.CanAcceptWatchFolderDrop(dbPath, [filePath]);

            Assert.That(result, Is.False);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void CanAcceptWatchFolderDrop_wbファイルなら受け付ける()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string dbPath = Path.Combine(tempRoot, "drop.wb");
            File.WriteAllText(dbPath, "sample");

            bool result = MainWindow.CanAcceptWatchFolderDrop("", [dbPath]);

            Assert.That(result, Is.True);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Test]
    public void ResolveDroppedMainDbPath_wbだけを切替対象として返す()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            string textFile = Path.Combine(tempRoot, "memo.txt");
            string dbPath = Path.Combine(tempRoot, "main.WB");
            File.WriteAllText(textFile, "sample");
            File.WriteAllText(dbPath, "sample");

            string result = MainWindow.ResolveDroppedMainDbPath([textFile, dbPath]);

            Assert.That(result, Is.EqualTo(Path.GetFullPath(dbPath)));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [TestCase(0, "DBを切り替えました: main.wb", Notification.Wpf.NotificationType.Success)]
    [TestCase(1, "既に開いています: main.wb", Notification.Wpf.NotificationType.Information)]
    [TestCase(2, "DBを開けませんでした: main.wb", Notification.Wpf.NotificationType.Error)]
    public void BuildDroppedMainDbSwitchToast_DBドロップ結果に応じた文言を返す(
        int kindValue,
        string expectedMessage,
        Notification.Wpf.NotificationType expectedType
    )
    {
        MainWindow.DroppedMainDbSwitchToastKind kind =
            (MainWindow.DroppedMainDbSwitchToastKind)kindValue;
        (string title, string message, Notification.Wpf.NotificationType type) =
            MainWindow.BuildDroppedMainDbSwitchToast(@"C:\db\main.wb", kind);

        Assert.That(title, Is.EqualTo("DB切替"));
        Assert.That(message, Is.EqualTo(expectedMessage));
        Assert.That(type, Is.EqualTo(expectedType));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_MainWindowWatchFolderDropTests",
            Guid.NewGuid().ToString("N")
        );
        return Directory.CreateDirectory(path).FullName;
    }
}
