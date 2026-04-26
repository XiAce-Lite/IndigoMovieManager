using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using IndigoMovieManager;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MainWindowReloadButtonTests
{
    [Test]
    public async Task ExecuteHeaderReloadAsync_watch抑止下でfilter完了後にmanual_scanを直列実行する()
    {
        MainWindow window = CreateWindow();
        SetPrivateField(window, "_watchUiSuppressionSync", new object());

        List<string> steps = [];
        TaskCompletionSource filterGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        window.ReloadBookmarkTabDataForTesting = () => steps.Add("bookmark");
        window.FilterAndSortAsyncForTesting = async (sortId, isGetNew) =>
        {
            steps.Add($"filter:{sortId}:{isGetNew}");
            Assert.That(GetPrivateField<int>(window, "_watchUiSuppressionCount"), Is.EqualTo(1));
            await filterGate.Task;
        };
        window.QueueCheckFolderAsyncForTesting = (mode, trigger) =>
        {
            steps.Add($"queue:{mode}:{trigger}");
            Assert.That(GetPrivateField<int>(window, "_watchUiSuppressionCount"), Is.EqualTo(1));
            return Task.CompletedTask;
        };

        Task reloadTask = window.ExecuteHeaderReloadAsync("1", "Header.ReloadButton");
        await Task.Yield();

        Assert.That(
            steps,
            Is.EqualTo(new[] { "bookmark", "filter:1:True" })
        );
        Assert.That(GetPrivateField<int>(window, "_watchUiSuppressionCount"), Is.EqualTo(1));

        filterGate.SetResult();
        await reloadTask;

        Assert.That(
            steps,
            Is.EqualTo(
                new[]
                {
                    "bookmark",
                    "filter:1:True",
                    "queue:Manual:Header.ReloadButton",
                }
            )
        );
        Assert.That(GetPrivateField<int>(window, "_watchUiSuppressionCount"), Is.EqualTo(0));
    }

    private static MainWindow CreateWindow()
    {
        return (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
    }

    private static void SetPrivateField(MainWindow window, string fieldName, object value)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        field.SetValue(window, value);
    }

    private static T GetPrivateField<T>(MainWindow window, string fieldName)
    {
        FieldInfo field = typeof(MainWindow).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        return (T)field.GetValue(window)!;
    }
}
