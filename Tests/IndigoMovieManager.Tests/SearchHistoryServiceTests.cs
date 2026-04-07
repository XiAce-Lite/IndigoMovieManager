using System.Data.SQLite;
using IndigoMovieManager.DB;
using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class SearchHistoryServiceTests
{
    [Test]
    public void LoadLatestHistory_同一キーワードは最新1件だけ返す()
    {
        string dbPath = CreateTempMainDb();
        try
        {
            InsertHistoryRow(dbPath, 1, "tokyo", "2026-04-07 10:00:00");
            InsertHistoryRow(dbPath, 2, "osaka", "2026-04-07 10:05:00");
            InsertHistoryRow(dbPath, 3, "tokyo", "2026-04-07 10:10:00");

            History[] actual = SearchHistoryService.LoadLatestHistory(dbPath);

            Assert.Multiple(() =>
            {
                Assert.That(actual.Select(x => x.Find_Text).ToArray(), Is.EqualTo(["tokyo", "osaka"]));
                Assert.That(actual[0].Find_Id, Is.EqualTo(3));
            });
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    [Test]
    public void PersistSuccessfulSearch_ヒット0件では履歴追加せず成功時だけ保存する()
    {
        string dbPath = CreateTempMainDb();
        try
        {
            SearchHistoryService.PersistSuccessfulSearch(dbPath, "tokyo", 0);
            SearchHistoryService.PersistSuccessfulSearch(dbPath, "tokyo", 1);

            string[] actual = ReadHistoryTexts(dbPath);

            Assert.That(actual, Is.EqualTo(["tokyo"]));
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    private static string CreateTempMainDb()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"imm-search-history-{Guid.NewGuid():N}.wb");
        bool created = SQLite.TryCreateDatabase(dbPath, out string errorMessage);
        Assert.That(created, Is.True, errorMessage);
        return dbPath;
    }

    private static void InsertHistoryRow(string dbPath, long findId, string text, string dateText)
    {
        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText =
            "insert into history (find_id, find_text, find_date) values (@find_id, @find_text, @find_date)";
        command.Parameters.AddWithValue("@find_id", findId);
        command.Parameters.AddWithValue("@find_text", text);
        command.Parameters.AddWithValue("@find_date", dateText);
        command.ExecuteNonQuery();
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
                // 一時DB削除失敗はテスト本体の判定を優先する。
            }
        }
    }
}
