using System.Reflection;
using IndigoMovieManager.Thumbnail.Ipc;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class AdminTelemetryContractsTests
{
    [Test]
    public void AdminTelemetryRequestContext_公開プロパティ名を固定する()
    {
        Assert.That(
            GetPublicPropertyNames(typeof(AdminTelemetryRequestContext)),
            Is.EqualTo(
                new[]
                {
                    "ConsumerKind",
                    "OrchestratorInstanceId",
                    "CallerProcessName",
                    "CallerProcessId",
                    "RequestedAtUtc",
                }
            )
        );
    }

    [Test]
    public void AdminTelemetryConsumerKind_共通サービスの呼び出し元を固定する()
    {
        Assert.That(
            Enum.GetNames<AdminTelemetryConsumerKind>(),
            Is.EqualTo(new[] { "ThumbnailOrchestrator", "WatcherFacade" })
        );
    }

    [Test]
    public void AdminTelemetryServiceCapabilities_公開プロパティ名を固定する()
    {
        Assert.That(
            GetPublicPropertyNames(typeof(AdminTelemetryServiceCapabilities)),
            Is.EqualTo(
                new[]
                {
                    "ServiceVersion",
                    "RequiresElevation",
                    "SupportsSystemLoad",
                    "SupportsDiskThermal",
                    "SupportsUsnMftStatus",
                    "SupportsWatcherIntegration",
                    "CapturedAtUtc",
                }
            )
        );
    }

    [Test]
    public void IAdminTelemetryClient_取得専用メソッドだけを持つ()
    {
        MethodInfo[] methods = typeof(IAdminTelemetryClient)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public);

        Assert.That(
            methods.Select(x => x.Name),
            Is.EqualTo(
                new[]
                {
                    "GetCapabilitiesAsync",
                    "GetSystemLoadSnapshotAsync",
                    "GetDiskThermalSnapshotAsync",
                    "GetUsnMftStatusAsync",
                }
            )
        );
        Assert.That(
            methods.Select(x => x.ReturnType.Name),
            Is.EqualTo(
                new[]
                {
                    "Task`1",
                    "Task`1",
                    "Task`1",
                    "Task`1",
                }
            )
        );
    }

    [Test]
    public void RequestContext_ローカル時刻入力でもUtcへ正規化する()
    {
        DateTime localTime = DateTime.SpecifyKind(
            new DateTime(2026, 3, 7, 10, 30, 0),
            DateTimeKind.Local
        );

        AdminTelemetryRequestContext actual = new() { RequestedAtUtc = localTime };

        Assert.That(actual.RequestedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(actual.RequestedAtUtc, Is.EqualTo(localTime.ToUniversalTime()));
    }

    [Test]
    public void Capabilities_既定値は昇格前提かつ機能オフで始まる()
    {
        AdminTelemetryServiceCapabilities actual = new();

        Assert.That(actual.RequiresElevation, Is.True);
        Assert.That(actual.SupportsSystemLoad, Is.False);
        Assert.That(actual.SupportsDiskThermal, Is.False);
        Assert.That(actual.SupportsUsnMftStatus, Is.False);
        Assert.That(actual.SupportsWatcherIntegration, Is.False);
        Assert.That(actual.CapturedAtUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public void RequestContext_既定値はThumbnailOrchestratorで始まる()
    {
        AdminTelemetryRequestContext actual = new();

        Assert.That(
            actual.ConsumerKind,
            Is.EqualTo(AdminTelemetryConsumerKind.ThumbnailOrchestrator)
        );
    }

    private static string[] GetPublicPropertyNames(Type type)
    {
        return
        [
            .. type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(x => x.Name),
        ];
    }
}
