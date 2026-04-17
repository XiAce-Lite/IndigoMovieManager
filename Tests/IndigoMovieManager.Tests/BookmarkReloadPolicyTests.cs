using System.Data;
using NUnit.Framework;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class BookmarkReloadPolicyTests
{
    [Test]
    public void BuildBookmarkRecordsForReload_bookmark行をMovieRecordsへ変換できる()
    {
        DataTable bookmarkData = new();
        bookmarkData.Columns.Add("movie_id", typeof(long));
        bookmarkData.Columns.Add("movie_name", typeof(string));
        bookmarkData.Columns.Add("movie_path", typeof(string));
        bookmarkData.Columns.Add("last_date", typeof(string));
        bookmarkData.Columns.Add("file_date", typeof(string));
        bookmarkData.Columns.Add("regist_date", typeof(string));
        bookmarkData.Columns.Add("view_count", typeof(long));
        bookmarkData.Columns.Add("kana", typeof(string));
        bookmarkData.Columns.Add("roma", typeof(string));

        bookmarkData.Rows.Add(
            10L,
            "sample",
            "sample[(123)12-34-56].jpg",
            "2026/04/17 10:11:12",
            "2026/04/16 09:08:07",
            "2026/04/15 08:07:06",
            5L,
            "さむぷる",
            "sample"
        );

        MovieRecords[] items = MainWindow.BuildBookmarkRecordsForReload(
            bookmarkData,
            @"C:\bookmark-root"
        );

        Assert.That(items, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(items[0].Movie_Id, Is.EqualTo(10L));
            Assert.That(items[0].Movie_Name, Is.EqualTo("sample.jpg"));
            Assert.That(items[0].Movie_Body, Is.EqualTo("sample"));
            Assert.That(items[0].Score, Is.EqualTo(123L));
            Assert.That(
                items[0].ThumbDetail,
                Is.EqualTo(@"C:\bookmark-root\sample[(123)12-34-56].jpg")
            );
            Assert.That(items[0].Kana, Is.EqualTo("さむぷる"));
            Assert.That(items[0].Roma, Is.EqualTo("sample"));
        });
    }
}
