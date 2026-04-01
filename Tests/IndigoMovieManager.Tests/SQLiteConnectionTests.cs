using System.Data.SQLite;
using IndigoMovieManager.DB;

namespace IndigoMovieManager.Tests;

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
        Assert.That(builder.BusyTimeout, Is.EqualTo(5000));
        Assert.That(builder.DefaultTimeout, Is.EqualTo(5));
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
        Assert.That(builder.BusyTimeout, Is.EqualTo(5000));
        Assert.That(builder.DefaultTimeout, Is.EqualTo(5));
    }

    [Test]
    public void IsTransientMainDbOpenException_一時的な共有失敗文言ならtrue()
    {
        Exception exception = new("unable to open database file");

        bool actual = SQLite.IsTransientMainDbOpenException(exception);

        Assert.That(actual, Is.True);
    }

    [Test]
    public void IsTransientMainDbOpenException_スキーマ不一致文言ならfalse()
    {
        Exception exception = new("必須テーブル 'movie' が見つかりません。");

        bool actual = SQLite.IsTransientMainDbOpenException(exception);

        Assert.That(actual, Is.False);
    }
}
