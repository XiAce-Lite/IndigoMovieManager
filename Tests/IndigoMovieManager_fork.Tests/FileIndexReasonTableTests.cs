using IndigoMovieManager.Watcher;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
public sealed class FileIndexReasonTableTests
{
    [Test]
    public void NormalizeByMode_Auto_ConvertsEverythingNotAvailable()
    {
        string normalized = FileIndexReasonTable.NormalizeByMode(
            IntegrationMode.Auto,
            EverythingReasonCodes.EverythingNotAvailable
        );

        Assert.That(normalized, Is.EqualTo(EverythingReasonCodes.AutoNotAvailable));
    }

    [Test]
    public void NormalizeByMode_On_LeavesEverythingNotAvailable()
    {
        string normalized = FileIndexReasonTable.NormalizeByMode(
            IntegrationMode.On,
            EverythingReasonCodes.EverythingNotAvailable
        );

        Assert.That(normalized, Is.EqualTo(EverythingReasonCodes.EverythingNotAvailable));
    }

    [Test]
    public void ToCategory_OkPrefixPayload_ReturnsOkPrefix()
    {
        string category = FileIndexReasonTable.ToCategory(
            "ok:provider=everythinglite count=10 since=2026-03-04T00:00:00.0000000Z"
        );

        Assert.That(category, Is.EqualTo(EverythingReasonCodes.OkPrefix));
    }

    [Test]
    public void ToCategory_ErrorPrefixPayload_ReturnsQueryErrorPrefix()
    {
        string category = FileIndexReasonTable.ToCategory("everything_query_error:IOException");

        Assert.That(category, Is.EqualTo(EverythingReasonCodes.EverythingQueryErrorPrefix));
    }

    [Test]
    public void ToCategory_UnknownReason_ReturnsOriginal()
    {
        const string unknown = "custom_reason:sample";
        string category = FileIndexReasonTable.ToCategory(unknown);

        Assert.That(category, Is.EqualTo(unknown));
    }

    [Test]
    public void ToLogAxis_AdminRequiredはAvailability軸へ分類する()
    {
        string axis = FileIndexReasonTable.ToLogAxis(
            $"{EverythingReasonCodes.AvailabilityErrorPrefix}AdminRequired"
        );

        Assert.That(axis, Is.EqualTo("file-index-availability"));
    }

    [Test]
    public void ToLogAxis_OkPrefixはOk軸へ分類する()
    {
        string axis = FileIndexReasonTable.ToLogAxis(
            "ok:provider=usnmft count=10 since=2026-03-04T00:00:00.0000000Z"
        );

        Assert.That(axis, Is.EqualTo("file-index-ok"));
    }

    [Test]
    public void ToLogAxis_UnknownReasonはUnknown軸へ分類する()
    {
        string axis = FileIndexReasonTable.ToLogAxis("custom_reason:sample");

        Assert.That(axis, Is.EqualTo("file-index-unknown"));
    }
}
