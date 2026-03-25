using System.Data.SQLite;
using IndigoMovieManager.DB;

namespace IndigoMovieManager_fork.Tests;

public sealed class SQLiteConnectionTests
{
    [Test]
    public void BuildConnectionString_読取専用はReadOnlyとFailIfMissingを有効にする()
    {
        string dbPath = @"C:\WhiteBrowser\maimai.wb";

        string connectionString = SQLite.BuildConnectionString(dbPath, readOnly: true);
        SQLiteConnectionStringBuilder builder = new(connectionString);

        Assert.That(builder.DataSource, Is.EqualTo(dbPath));
        Assert.That(builder.ReadOnly, Is.True);
        Assert.That(builder.FailIfMissing, Is.True);
    }

    [Test]
    public void BuildConnectionString_通常接続はReadOnlyを無効にする()
    {
        string dbPath = @"C:\WhiteBrowser\maimai.wb";

        string connectionString = SQLite.BuildConnectionString(dbPath, readOnly: false);
        SQLiteConnectionStringBuilder builder = new(connectionString);

        Assert.That(builder.DataSource, Is.EqualTo(dbPath));
        Assert.That(builder.ReadOnly, Is.False);
        Assert.That(builder.FailIfMissing, Is.True);
    }
}
