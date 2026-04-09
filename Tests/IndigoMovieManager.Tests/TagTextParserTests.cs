using IndigoMovieManager.Infrastructure;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class TagTextParserTests
{
    [Test]
    public void SplitDistinct_改行揺れと前後空白を吸収して重複を除去する()
    {
        string source = " 猫 \r\n犬\n猫\r鳥 ";

        string[] actual = TagTextParser.SplitDistinct(
            source,
            StringComparer.CurrentCultureIgnoreCase
        );

        Assert.That(actual, Is.EqualTo(["猫", "犬", "鳥"]));
    }

    [Test]
    public void SplitDistinct_列挙入力でも空要素を落として重複を除去する()
    {
        string[] actual = TagTextParser.SplitDistinct(
            [" idol ", "", "anime", "IDOL", " ", "live"],
            StringComparer.CurrentCultureIgnoreCase
        );

        Assert.That(actual, Is.EqualTo(["idol", "anime", "live"]));
    }
}
