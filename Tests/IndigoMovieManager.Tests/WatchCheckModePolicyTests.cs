using IndigoMovieManager;
using System.Reflection;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchCheckModePolicyTests
{
    [Test]
    public void MergeCheckMode_AutoへWatchが来たらWatchを優先する()
    {
        object autoMode = ParseCheckMode("Auto");
        object watchMode = ParseCheckMode("Watch");

        object result = InvokePrivateStatic("MergeCheckMode", autoMode, watchMode);

        Assert.That(result.ToString(), Is.EqualTo("Watch"));
    }

    [Test]
    public void MergeCheckMode_WatchへManualが来たらManualを優先する()
    {
        object watchMode = ParseCheckMode("Watch");
        object manualMode = ParseCheckMode("Manual");

        object result = InvokePrivateStatic("MergeCheckMode", watchMode, manualMode);

        Assert.That(result.ToString(), Is.EqualTo("Manual"));
    }

    [Test]
    public void MergeCheckMode_強いモードが先ならそのまま維持する()
    {
        object manualMode = ParseCheckMode("Manual");
        object autoMode = ParseCheckMode("Auto");

        object result = InvokePrivateStatic("MergeCheckMode", manualMode, autoMode);

        Assert.That(result.ToString(), Is.EqualTo("Manual"));
    }

    [TestCase("Auto", 1)]
    [TestCase("Watch", 2)]
    [TestCase("Manual", 3)]
    public void GetCheckModePriority_優先度順を返す(string modeName, int expected)
    {
        object mode = ParseCheckMode(modeName);

        object result = InvokePrivateStatic("GetCheckModePriority", mode);

        Assert.That(result, Is.EqualTo(expected));
    }

    private static object ParseCheckMode(string name)
    {
        Type enumType = typeof(MainWindow).GetNestedType(
            "CheckMode",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        Assert.That(enumType, Is.Not.Null);
        return Enum.Parse(enumType, name);
    }

    private static object InvokePrivateStatic(string methodName, params object[] args)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null, methodName);
        return method.Invoke(null, args)!;
    }
}
