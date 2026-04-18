using IndigoMovieManager;
using System.Reflection;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class EverythingEligibilityPolicyTests
{
    [Test]
    public void IsEverythingEligiblePath_空文字は対象外になる()
    {
        bool result = InvokeIsEverythingEligiblePath("", out string reason);

        Assert.That(result, Is.False);
        Assert.That(reason, Is.EqualTo("empty_path"));
    }

    [Test]
    public void IsEverythingEligiblePath_UNCパスは対象外になる()
    {
        bool result = InvokeIsEverythingEligiblePath(@"\\server\share\Movies", out string reason);

        Assert.That(result, Is.False);
        Assert.That(reason, Is.EqualTo("unc_path"));
    }

    private static bool InvokeIsEverythingEligiblePath(string watchFolder, out string reason)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            "IsEverythingEligiblePath",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null);

        object?[] args = [watchFolder, null];
        bool result = (bool)method.Invoke(null, args)!;
        reason = (string?)args[1] ?? "";
        return result;
    }
}
