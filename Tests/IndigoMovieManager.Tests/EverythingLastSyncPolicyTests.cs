using IndigoMovieManager;
using System.IO;
using System.Reflection;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class EverythingLastSyncPolicyTests
{
    [Test]
    public void BuildEverythingLastSyncAttr_同じ実体パスなら大小文字差を吸収して同じキーになる()
    {
        string basePath = Path.Combine(
            Path.GetTempPath(),
            "EverythingLastSyncPolicyTests",
            "WatchFolder"
        );
        string lowerPath = basePath.ToLowerInvariant();
        string upperPath = basePath.ToUpperInvariant();

        string lower = InvokeBuildEverythingLastSyncAttr(lowerPath, sub: true);
        string upper = InvokeBuildEverythingLastSyncAttr(upperPath, sub: true);

        Assert.That(lower, Is.EqualTo(upper));
    }

    [Test]
    public void BuildEverythingLastSyncAttr_sub有無で別キーになる()
    {
        string watchFolder = Path.Combine(
            Path.GetTempPath(),
            "EverythingLastSyncPolicyTests",
            "WatchFolder"
        );

        string withSub = InvokeBuildEverythingLastSyncAttr(watchFolder, sub: true);
        string withoutSub = InvokeBuildEverythingLastSyncAttr(watchFolder, sub: false);

        Assert.That(withSub, Is.Not.EqualTo(withoutSub));
    }

    private static string InvokeBuildEverythingLastSyncAttr(string watchFolder, bool sub)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "BuildEverythingLastSyncAttr",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null);
        return (string)method.Invoke(null, [watchFolder, sub])!;
    }
}
