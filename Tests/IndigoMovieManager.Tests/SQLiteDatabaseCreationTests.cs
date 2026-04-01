using System.Data.SQLite;
using IndigoMovieManager.DB;

namespace IndigoMovieManager.Tests;

public sealed class SQLiteDatabaseCreationTests
{
    [Test]
    public void TryCreateDatabase_正常パスならDB作成に成功する()
    {
        string dbPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-create-db-{Guid.NewGuid():N}.wb"
        );

        try
        {
            bool created = SQLite.TryCreateDatabase(dbPath, out string errorMessage);

            Assert.That(created, Is.True);
            Assert.That(errorMessage, Is.Empty);
            Assert.That(File.Exists(dbPath), Is.True);

            using SQLiteConnection connection = SQLite.CreateReadOnlyConnection(dbPath);
            connection.Open();

            Assert.That(TableExists(connection, "system"), Is.True);
            Assert.That(TableExists(connection, "movie"), Is.True);
            Assert.That(TableExists(connection, "history"), Is.True);
            Assert.That(TableExists(connection, "watch"), Is.True);
            Assert.That(TableExists(connection, "bookmark"), Is.True);
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void TryCreateDatabase_親フォルダが無ければ失敗し0kbも残さない()
    {
        string missingRoot = Path.Combine(
            Path.GetTempPath(),
            $"imm-create-db-missing-{Guid.NewGuid():N}"
        );
        string dbPath = Path.Combine(missingRoot, "unc-test.wb");

        bool created = SQLite.TryCreateDatabase(dbPath, out string errorMessage);

        Assert.That(created, Is.False);
        Assert.That(errorMessage, Does.Contain("DB保存先フォルダが見つかりません"));
        Assert.That(File.Exists(dbPath), Is.False);
    }

    private static bool TableExists(SQLiteConnection connection, string tableName)
    {
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name LIMIT 1";
        command.Parameters.AddWithValue("@name", tableName);
        return command.ExecuteScalar() != null;
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
}
