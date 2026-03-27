using System.Globalization;
using IndigoMovieManager.Converter;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class TagSummaryConverterTests
{
    [Test]
    public void タグが空なら空文字を返す()
    {
        TagSummaryConverter converter = new();

        object actual = converter.Convert(
            Array.Empty<string>(),
            typeof(string),
            null,
            CultureInfo.InvariantCulture
        );

        Assert.That(actual, Is.EqualTo(""));
    }

    [Test]
    public void 指定件数まではそのまま連結する()
    {
        TagSummaryConverter converter = new();

        object actual = converter.Convert(
            new[] { "alpha", "beta", "gamma" },
            typeof(string),
            4,
            CultureInfo.InvariantCulture
        );

        Assert.That(actual, Is.EqualTo("alpha  beta  gamma"));
    }

    [Test]
    public void 指定件数を超えた分は残件数を付けて省略する()
    {
        TagSummaryConverter converter = new();

        object actual = converter.Convert(
            new[] { "alpha", "beta", "gamma", "delta" },
            typeof(string),
            2,
            CultureInfo.InvariantCulture
        );

        Assert.That(actual, Is.EqualTo("alpha  beta  ... (+2)"));
    }
}
