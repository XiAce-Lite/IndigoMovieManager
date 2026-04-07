namespace IndigoMovieManager.Tests;

public class JapaneseKanaProviderTests
{
    [Test]
    public void GetKana_ひらがな入力でも安定してひらがなになる()
    {
        string actual = JapaneseKanaProvider.GetKana("あいうえお");

        Assert.That(actual, Is.EqualTo("あいうえお"));
    }

    [Test]
    public void GetKana_名前が空でもパスからかなを作る()
    {
        string actual = JapaneseKanaProvider.GetKana("", @"C:\Movies\かきくけこ.mp4");

        Assert.That(actual, Is.EqualTo("かきくけこ"));
    }

    [Test]
    public void GetKana_空入力は空を返す()
    {
        string actual = JapaneseKanaProvider.GetKana("", "");

        Assert.That(actual, Is.EqualTo(""));
    }

    [Test]
    public void GetRoma_かな入力からローマ字検索文字列を作れる()
    {
        string actual = JapaneseKanaProvider.GetRoma("かな", "");

        Assert.That(actual, Does.Contain("kana"));
    }

    [Test]
    public void GetRoma_長音を縮めた別表記も検索用に含める()
    {
        string actual = JapaneseKanaProvider.GetRoma("とうきょう", "");

        Assert.That(actual, Does.Contain("toukyou"));
        Assert.That(actual, Does.Contain("tokyo"));
    }

    [Test]
    public void GetKanaForPersistence_かな主体の文字列だけを保存用かなにする()
    {
        string actual = JapaneseKanaProvider.GetKanaForPersistence("けものフレンズ01-02");

        Assert.That(actual, Is.EqualTo("けものふれんず01-02"));
    }

    [Test]
    public void GetKanaForPersistence_カタカナ入力をひらがなへ寄せる()
    {
        string actual = JapaneseKanaProvider.GetKanaForPersistence("カナテスト");

        Assert.That(actual, Is.EqualTo("かなてすと"));
    }

    [Test]
    public void GetKanaForPersistence_漢字混じりの題名をひらがなへ変換する()
    {
        string actual = JapaneseKanaProvider.GetKanaForPersistence("東京ラブストーリー");

        Assert.That(actual, Is.EqualTo("とうきょうらぶすとーりー"));
    }

    [Test]
    public void GetKana_英語副題混在の実パスでも日本語読みを抽出する()
    {
        string actual = JapaneseKanaProvider.GetKana(
            "",
            @"E:\copy1\【公式】新・エースをねらえ！ 第1話「ひろみとお蝶と鬼コーチ」”AIM FOR THE BEST THE REMAKE VERSION” EP011978.mp4"
        );

        Assert.That(actual, Is.EqualTo("こうしきしんえーすをねらえだい1わひろみとおちょうとおにこーち"));
    }

    [Test]
    public void GetKanaForPersistence_英語副題混在の実パスでも日本語部分だけ保存する()
    {
        string actual = JapaneseKanaProvider.GetKanaForPersistence(
            "",
            @"E:\copy1\【公式】新・エースをねらえ！ 第1話「ひろみとお蝶と鬼コーチ」”AIM FOR THE BEST THE REMAKE VERSION” EP011978.mp4"
        );

        Assert.That(actual, Is.EqualTo("こうしきしんえーすをねらえだい1わひろみとおちょうとおにこーち"));
    }

    [Test]
    public void GetRomaForPersistence_英語副題混在の実パスでも日本語部分からローマ字を作る()
    {
        string actual = JapaneseKanaProvider.GetRomaForPersistence(
            "",
            @"E:\copy1\【公式】新・エースをねらえ！ 第1話「ひろみとお蝶と鬼コーチ」”AIM FOR THE BEST THE REMAKE VERSION” EP011978.mp4"
        );

        Assert.That(actual, Does.Contain("koushikishineesuoneraedai1wa"));
        Assert.That(actual, Does.Contain("hiromitoochoutoonikoochi"));
    }

    [Test]
    public void GetKana_英数主体の題名も検索用には元文字を使う()
    {
        string actual = JapaneseKanaProvider.GetKana("one piece film red");

        Assert.That(actual, Is.EqualTo("one piece film red"));
    }

    [Test]
    public void GetKanaForPersistence_英数主体の題名は保存しない()
    {
        string actual = JapaneseKanaProvider.GetKanaForPersistence("one piece film red");

        Assert.That(actual, Is.EqualTo(""));
    }
}
