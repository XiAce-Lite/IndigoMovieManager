namespace IndigoMovieManager.Tests;

public class JapaneseKanaProviderTests
{
    [Test]
    public void GetKana_ひらがな入力でも安定してカタカナになる()
    {
        string actual = JapaneseKanaProvider.GetKana("あいうえお");

        Assert.That(actual, Is.EqualTo("アイウエオ"));
    }

    [Test]
    public void GetKana_名前が空でもパスからかなを作る()
    {
        string actual = JapaneseKanaProvider.GetKana("", @"C:\Movies\かきくけこ.mp4");

        Assert.That(actual, Is.EqualTo("カキクケコ"));
    }

    [Test]
    public void GetKana_空入力は空を返す()
    {
        string actual = JapaneseKanaProvider.GetKana("", "");

        Assert.That(actual, Is.EqualTo(""));
    }
}
