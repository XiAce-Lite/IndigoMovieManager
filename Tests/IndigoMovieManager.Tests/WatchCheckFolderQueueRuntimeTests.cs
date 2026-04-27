using IndigoMovieManager;
using System.Reflection;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchCheckFolderQueueRuntimeTests
{
    [Test]
    public void BuildWatchCheckFolderQueueTraceSummary_path由来と圧縮回数を短く返す()
    {
        string result = InvokePrivateStaticString(
            "BuildWatchCheckFolderQueueTraceSummary",
            @"created:E:\Movies\A\a.mp4",
            @"renamed-untracked:E:\Movies\B\b.mp4",
            2
        );

        Assert.That(
            result,
            Is.EqualTo(
                "compressed=2 first=created path='a.mp4' last=renamed-untracked path='b.mp4'"
            )
        );
    }

    [Test]
    public void BuildWatchCheckFolderQueueTraceSummary_resume系は値由来として返す()
    {
        string result = InvokePrivateStaticString(
            "BuildWatchCheckFolderQueueTraceSummary",
            "EverythingPoll",
            "ui-resume:left-drawer",
            1
        );

        Assert.That(
            result,
            Is.EqualTo("compressed=1 first=EverythingPoll last=ui-resume value='left-drawer'")
        );
    }

    private static string InvokePrivateStaticString(string methodName, params object[] args)
    {
        MethodInfo method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        Assert.That(method, Is.Not.Null, methodName);
        return (string)method.Invoke(null, args)!;
    }
}
