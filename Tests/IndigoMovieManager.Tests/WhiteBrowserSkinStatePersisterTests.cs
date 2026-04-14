using System.Data.SQLite;
using System.IO;
using System.Threading.Channels;
using IndigoMovieManager.DB;
using IndigoMovieManager.Skin;
using IndigoMovieManager.Skin.Runtime;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WhiteBrowserSkinStatePersisterTests
{
    [SetUp]
    public void SetUp()
    {
        WhiteBrowserSkinProfileValueCache.ClearForTesting();
    }

    [Test]
    public void TryUpsertSystemTable_同一キーを原子的に上書きできる()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string dbPath = Path.Combine(root, "main.wb");
        Directory.CreateDirectory(root);

        try
        {
            Assert.That(SQLite.TryCreateDatabase(dbPath, out string errorMessage), Is.True, errorMessage);

            Assert.Multiple(() =>
            {
                // 同じキーを連続で保存しても 1 件に収まり、最後の値が残ることを確認する。
                Assert.That(SQLite.TryUpsertSystemTable(dbPath, "skin", "OldSkin"), Is.True);
                Assert.That(SQLite.TryUpsertSystemTable(dbPath, "skin", "NewSkin"), Is.True);
                Assert.That(ReadSystemValue(dbPath, "skin"), Is.EqualTo("NewSkin"));
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task RunAsync_同一キー連続要求では最後の値だけ保存する()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string dbPath = Path.Combine(root, "main.wb");
        Directory.CreateDirectory(root);

        try
        {
            Assert.That(SQLite.TryCreateDatabase(dbPath, out string errorMessage), Is.True, errorMessage);

            Channel<WhiteBrowserSkinStatePersistRequest> channel =
                Channel.CreateUnbounded<WhiteBrowserSkinStatePersistRequest>();
            WhiteBrowserSkinStatePersister persister = new(channel.Reader, batchWindowMs: 10);

            channel.Writer.TryWrite(
                WhiteBrowserSkinStatePersistRequest.CreateSystem(dbPath, "skin", "OldSkin")
            );
            channel.Writer.TryWrite(
                WhiteBrowserSkinStatePersistRequest.CreateSystem(dbPath, "skin", "NewSkin")
            );
            channel.Writer.TryComplete();

            await persister.RunAsync();

            Assert.That(ReadSystemValue(dbPath, "skin"), Is.EqualTo("NewSkin"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task RunAsync_SystemとProfileを同じDBへ保存できる()
    {
        string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string dbPath = Path.Combine(root, "main.wb");
        Directory.CreateDirectory(root);

        try
        {
            Assert.That(SQLite.TryCreateDatabase(dbPath, out string errorMessage), Is.True, errorMessage);

            Channel<WhiteBrowserSkinStatePersistRequest> channel =
                Channel.CreateUnbounded<WhiteBrowserSkinStatePersistRequest>();
            WhiteBrowserSkinStatePersister persister = new(channel.Reader, batchWindowMs: 10);

            channel.Writer.TryWrite(
                WhiteBrowserSkinStatePersistRequest.CreateSystem(dbPath, "skin", "SampleExternalSkin")
            );
            channel.Writer.TryWrite(
                WhiteBrowserSkinStatePersistRequest.CreateProfile(
                    dbPath,
                    "SampleExternalSkin",
                    "LastUpperTab",
                    "DefaultGrid"
                )
            );
            channel.Writer.TryComplete();

            await persister.RunAsync();

            Assert.Multiple(() =>
            {
                Assert.That(ReadSystemValue(dbPath, "skin"), Is.EqualTo("SampleExternalSkin"));
                Assert.That(
                    ReadProfileValue(dbPath, "SampleExternalSkin", "LastUpperTab"),
                    Is.EqualTo("DefaultGrid")
                );
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task RunAsync_Profile保存失敗時はFaultへ落としてCacheを見せない()
    {
        string dbPath = Path.Combine(
            Path.GetTempPath(),
            Path.GetRandomFileName(),
            "missing",
            "main.wb"
        );

        Channel<WhiteBrowserSkinStatePersistRequest> channel =
            Channel.CreateUnbounded<WhiteBrowserSkinStatePersistRequest>();
        WhiteBrowserSkinStatePersister persister = new(channel.Reader, batchWindowMs: 10);

        WhiteBrowserSkinProfileValueCache.RecordPending(
            dbPath,
            "SampleExternalSkin",
            "LastUpperTab",
            "DefaultGrid"
        );
        channel.Writer.TryWrite(
            WhiteBrowserSkinStatePersistRequest.CreateProfile(
                dbPath,
                "SampleExternalSkin",
                "LastUpperTab",
                "DefaultGrid"
            )
        );
        channel.Writer.TryComplete();

        await persister.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.That(
                WhiteBrowserSkinProfileValueCache.TryGetApiVisibleValue(
                    dbPath,
                    "SampleExternalSkin",
                    "LastUpperTab",
                    out _
                ),
                Is.False
            );
            Assert.That(
                WhiteBrowserSkinProfileValueCache.TryGetPersistedValue(
                    dbPath,
                    "SampleExternalSkin",
                    "LastUpperTab",
                    out _
                ),
                Is.False
            );
        });
    }

    private static string ReadSystemValue(string dbPath, string key)
    {
        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM system WHERE attr = @attr LIMIT 1";
        command.Parameters.AddWithValue("@attr", key ?? "");
        return command.ExecuteScalar()?.ToString() ?? "";
    }

    private static string ReadProfileValue(string dbPath, string skinName, string key)
    {
        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT value FROM profile WHERE skin = @skin AND key = @key LIMIT 1";
        command.Parameters.AddWithValue("@skin", skinName ?? "");
        command.Parameters.AddWithValue("@key", key ?? "");
        return command.ExecuteScalar()?.ToString() ?? "";
    }
}
