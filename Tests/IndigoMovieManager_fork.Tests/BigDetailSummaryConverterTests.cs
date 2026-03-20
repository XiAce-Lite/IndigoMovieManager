using System.Globalization;
using IndigoMovieManager.Converter;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class BigDetailSummaryConverterTests
{
    [Test]
    public void スコアサイズ時間を1行へまとめる()
    {
        BigDetailSummaryConverter converter = new();

        object actual = converter.Convert(
            [12, 1024L * 2L, "00:10:00"],
            typeof(string),
            null,
            CultureInfo.InvariantCulture
        );

        Assert.That(actual, Is.EqualTo("S:12 / 2.0 MB / 00:10:00"));
    }

    [Test]
    public void 情報が薄い時も文字列化は継続する()
    {
        BigDetailSummaryConverter converter = new();

        object actual = converter.Convert(
            ["", 0L, "00:00:03"],
            typeof(string),
            null,
            CultureInfo.InvariantCulture
        );

        Assert.That(actual, Is.EqualTo("S: / 0 B / 00:00:03"));
    }
}
