using IndigoMovieManager;
using System.Reflection;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchCheckProbePolicyTests
{
    [Test]
    public void IsWatchCheckProbeTargetMovie_含有一致ならTrueを返す()
    {
        bool result = InvokePrivateStaticBool(
            "IsWatchCheckProbeTargetMovie",
            @"E:\movies\MH922SNIgTs_gggggggggg.mkv"
        );

        Assert.That(result, Is.True);
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase(@"E:\movies\other_movie.mkv")]
    public void IsWatchCheckProbeTargetMovie_空白や非一致はFalseを返す(string movieFullPath)
    {
        bool result = InvokePrivateStaticBool("IsWatchCheckProbeTargetMovie", movieFullPath);

        Assert.That(result, Is.False);
    }

    private static bool InvokePrivateStaticBool(string methodName, params object[] args)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null, methodName);
        return (bool)method.Invoke(null, args)!;
    }
}
