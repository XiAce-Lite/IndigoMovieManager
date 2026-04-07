using System.Data.SQLite;
using IndigoMovieManager.BottomTabs.SavedSearch;
using IndigoMovieManager.DB;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class SavedSearchServiceTests
{
    [Test]
    public void LoadItems_tagbarを表示順で読み込み実行可否も返す()
    {
        string dbPath = CreateTempMainDb();
        try
        {
            InsertTagbarRow(dbPath, 10, 0, 2, 0, "B", "beta");
            InsertTagbarRow(dbPath, 5, 0, 1, 0, "A", "alpha");
            InsertTagbarRow(dbPath, 20, 1, 1, 0, "Group", "");

            SavedSearchItem[] actual = SavedSearchService.LoadItems(dbPath);

            Assert.Multiple(() =>
            {
                Assert.That(actual.Select(x => x.DisplayTitle).ToArray(), Is.EqualTo(["A", "B", "Group"]));
                Assert.That(actual.Select(x => x.Contents).ToArray(), Is.EqualTo(["alpha", "beta", ""]));
                Assert.That(actual.Select(x => x.CanExecute).ToArray(), Is.EqualTo([true, true, false]));
            });
        }
        finally
        {
            TryDeleteFile(dbPath);
        }
    }

    private static string CreateTempMainDb()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"imm-saved-search-{Guid.NewGuid():N}.wb");
        bool created = SQLite.TryCreateDatabase(dbPath, out string errorMessage);
        Assert.That(created, Is.True, errorMessage);
        return dbPath;
    }

    private static void InsertTagbarRow(
        string dbPath,
        long itemId,
        long parentId,
        long orderId,
        long groupId,
        string title,
        string contents
    )
    {
        using SQLiteConnection connection = new($"Data Source={dbPath}");
        connection.Open();
        using SQLiteCommand command = connection.CreateCommand();
        command.CommandText =
            @"insert into tagbar (item_id, parent_id, order_id, group_id, title, contents)
              values (@item_id, @parent_id, @order_id, @group_id, @title, @contents)";
        command.Parameters.AddWithValue("@item_id", itemId);
        command.Parameters.AddWithValue("@parent_id", parentId);
        command.Parameters.AddWithValue("@order_id", orderId);
        command.Parameters.AddWithValue("@group_id", groupId);
        command.Parameters.AddWithValue("@title", title);
        command.Parameters.AddWithValue("@contents", contents);
        command.ExecuteNonQuery();
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
