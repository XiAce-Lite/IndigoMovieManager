using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class ThumbnailIpcTransportPolicyTests
{
    [Test]
    public void 固定値_採用したIPC方式とタイムアウトを返す()
    {
        Assert.That(ThumbnailIpcTransportPolicy.TransportKind, Is.EqualTo("named-pipe"));
        Assert.That(
            ThumbnailIpcTransportPolicy.MessageFormat,
            Is.EqualTo("length-prefixed-json")
        );
        Assert.That(
            ThumbnailIpcTransportPolicy.SerializationKind,
            Is.EqualTo("system-text-json-utf8")
        );
        Assert.That(ThumbnailIpcTransportPolicy.ConnectTimeoutMs, Is.EqualTo(1000));
        Assert.That(ThumbnailIpcTransportPolicy.RequestTimeoutMs, Is.EqualTo(2000));
        Assert.That(ThumbnailIpcTransportPolicy.HealthCheckTimeoutMs, Is.EqualTo(500));
        Assert.That(ThumbnailIpcTransportPolicy.ReconnectDelayMs, Is.EqualTo(5000));
    }

    [Test]
    public void ResolveThumbnailEnginePipeName_インスタンス単位のpipe名を返す()
    {
        string actual = ThumbnailIpcTransportPolicy.ResolveThumbnailEnginePipeName("main-01");

        Assert.That(
            actual,
            Is.EqualTo("IndigoMovieManager.Thumbnail.Engine.v1.main-01")
        );
    }

    [Test]
    public void ResolveThumbnailEnginePipeName_危険文字は安全なpipe名へ丸める()
    {
        string actual = ThumbnailIpcTransportPolicy.ResolveThumbnailEnginePipeName(
            " main:01/東京 "
        );

        Assert.That(
            actual,
            Is.EqualTo("IndigoMovieManager.Thumbnail.Engine.v1.main-01")
        );
    }

    [Test]
    public void ResolveThumbnailEnginePipeName_空値はdefaultへ寄せる()
    {
        string actual = ThumbnailIpcTransportPolicy.ResolveThumbnailEnginePipeName("   ");

        Assert.That(
            actual,
            Is.EqualTo("IndigoMovieManager.Thumbnail.Engine.v1.default")
        );
    }
}
