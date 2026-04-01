using System.Data.SQLite;
using IndigoMovieManager.DB;
using IndigoMovieManager.Thumbnail.FailureDb;
using IndigoMovieManager.Thumbnail.QueueDb;
using IndigoMovieManager.Thumbnail.SQLite;

namespace IndigoMovieManager.Tests;

public sealed class SQLiteUncConnectionStringTests
{
    [Test]
    public void EscapeDataSourcePath_UNCの連続バックスラッシュを二重化する()
    {
        string uncPath = @"\\server\share\main.wb";
        string escaped = SQLiteConnectionStringPathHelper.EscapeDataSourcePath(uncPath);

        Assert.That(escaped, Is.EqualTo(@"\\\\server\share\main.wb"));
    }

    [Test]
    public void EscapeDataSourcePath_ローカルパスはそのまま返す()
    {
        string localPath = @"C:\temp\main.wb";
        string escaped = SQLiteConnectionStringPathHelper.EscapeDataSourcePath(localPath);

        Assert.That(escaped, Is.EqualTo(localPath));
    }

    [Test]
    public void BuildConnectionString_MainDbがUNCならDataSourceへ公式仕様の二重化値を入れる()
    {
        string uncPath = @"\\server\share\main.wb";

        string connectionString = SQLite.BuildConnectionString(uncPath, readOnly: true);

        Assert.That(connectionString, Does.Contain(@"\\\\server\share\main.wb"));
        Assert.That(connectionString, Does.Contain("read only=True"));
        Assert.That(connectionString, Does.Contain("failifmissing=True"));
    }

    [Test]
    public void BuildConnectionString_QueueDbがUNCならDataSourceへ公式仕様の二重化値を入れる()
    {
        string uncPath = @"\\server\share\queue\thumbqueue.db";

        string connectionString = QueueDbService.BuildConnectionString(uncPath);

        Assert.That(connectionString, Does.Contain(@"\\\\server\share\queue\thumbqueue.db"));
    }

    [Test]
    public void BuildConnectionString_FailureDbがUNCならDataSourceへ公式仕様の二重化値を入れる()
    {
        string uncPath = @"\\server\share\failure\thumbfailure.db";

        string connectionString = ThumbnailFailureDbService.BuildConnectionString(uncPath);

        Assert.That(connectionString, Does.Contain(@"\\\\server\share\failure\thumbfailure.db"));
    }
}
