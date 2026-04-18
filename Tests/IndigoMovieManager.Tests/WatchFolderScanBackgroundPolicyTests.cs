using IndigoMovieManager;
using System.Reflection;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class WatchFolderScanBackgroundPolicyTests
{
    [Test]
    public void ScanFolderInBackground_拡張子に一致したファイルだけ返す()
    {
        string root = CreateTempRoot();
        try
        {
            string movie1 = Path.Combine(root, "movie1.mp4");
            string movie2 = Path.Combine(root, "movie2.mkv");
            string text = Path.Combine(root, "memo.txt");
            File.WriteAllBytes(movie1, [1]);
            File.WriteAllBytes(movie2, [1]);
            File.WriteAllBytes(text, [1]);

            object result = InvokePrivateStatic(
                "ScanFolderInBackground",
                root,
                false,
                "*.mp4, *.mkv"
            );

            Assert.That(GetIntProperty(result, "ScannedCount"), Is.EqualTo(2));
            Assert.That(
                GetStringListProperty(result, "NewMoviePaths"),
                Is.EquivalentTo(new[] { movie1, movie2 })
            );
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Test]
    public void ScanFolderInBackground_ゴミ箱配下を除外する()
    {
        string root = CreateTempRoot();
        try
        {
            string normal = Path.Combine(root, "movie1.mp4");
            string recycleDir = Path.Combine(root, "$RECYCLE.BIN");
            Directory.CreateDirectory(recycleDir);
            string recycled = Path.Combine(recycleDir, "movie2.mp4");
            File.WriteAllBytes(normal, [1]);
            File.WriteAllBytes(recycled, [1]);

            object result = InvokePrivateStatic(
                "ScanFolderInBackground",
                root,
                true,
                "*.mp4"
            );

            Assert.That(GetIntProperty(result, "ScannedCount"), Is.EqualTo(1));
            Assert.That(
                GetStringListProperty(result, "NewMoviePaths"),
                Is.EquivalentTo(new[] { normal })
            );
        }
        finally
        {
            DeleteTempRoot(root);
        }
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

    private static int GetIntProperty(object instance, string propertyName)
    {
        PropertyInfo property = instance.GetType().GetProperty(propertyName)!;
        Assert.That(property, Is.Not.Null, propertyName);
        return (int)property.GetValue(instance)!;
    }

    private static List<string> GetStringListProperty(object instance, string propertyName)
    {
        PropertyInfo property = instance.GetType().GetProperty(propertyName)!;
        Assert.That(property, Is.Not.Null, propertyName);
        return ((IEnumerable<string>)property.GetValue(instance)!).ToList();
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"imm-watch-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
