using System.Text.RegularExpressions;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class LaneBFacadeGuardArchitectureTests
{
    [Test]
    public void MainWindow_movieReadFacade配線が維持されている()
    {
        string root = FindRepositoryRoot();
        string mainWindowPath = Path.Combine(root, "Views", "Main", "MainWindow.xaml.cs");
        string startupPath = Path.Combine(root, "Views", "Main", "MainWindow.Startup.cs");
        string legacyStartupReaderPath = Path.Combine(root, "Startup", "StartupDbPageReader.cs");

        string mainWindowSource = File.ReadAllText(mainWindowPath);
        string startupSource = File.ReadAllText(startupPath);

        // MainWindow 本体は read facade を握り、対象4口をそこ経由へ閉じる。
        Assert.That(
            mainWindowSource,
            Does.Contain("private readonly IMainDbMovieReadFacade _mainDbMovieReadFacade =")
        );
        Assert.That(mainWindowSource, Does.Contain("new MainDbMovieReadFacade();"));

        string registeredMovieCountBody = ExtractMethodBody(
            mainWindowSource,
            "private async Task RefreshRegisteredMovieCountAsync("
        );
        AssertMethodUsesFacadeOnly(
            registeredMovieCountBody,
            @"_mainDbMovieReadFacade\s*\.\s*ReadRegisteredMovieCount\s*\(",
            "登録件数更新"
        );

        string systemTableBody = ExtractMethodBody(
            mainWindowSource,
            "private void GetSystemTable("
        );
        AssertMethodUsesFacadeOnly(
            systemTableBody,
            @"_mainDbMovieReadFacade\s*\.\s*LoadSystemTable\s*\(",
            "system テーブル読取"
        );

        string filterAndSortBody = ExtractMethodBody(
            mainWindowSource,
            "private async Task FilterAndSortAsync("
        );
        AssertMethodUsesFacadeOnly(
            filterAndSortBody,
            @"_mainDbMovieReadFacade\s*\.\s*LoadMovieTableForSort\s*\(",
            "movie full reload"
        );

        string startupPageBody = ExtractMethodBody(
            startupSource,
            "private async Task<StartupFeedPage> LoadStartupFeedPageAsync("
        );
        AssertMethodUsesFacadeOnly(
            startupPageBody,
            @"_mainDbMovieReadFacade\s*\.\s*ReadStartupPage\s*\(",
            "startup page 読取"
        );

        Assert.That(startupSource, Does.Not.Contain("StartupDbPageReader"));
        Assert.That(File.Exists(legacyStartupReaderPath), Is.False);
    }

    [Test]
    public void UpdateMovieSingleColumn直叩きはDataFacadeに閉じている()
    {
        string root = FindRepositoryRoot();
        Regex pattern = new(@"\bUpdateMovieSingleColumn\s*\(", RegexOptions.CultureInvariant);

        string[] offenders = EnumerateLaneBMutationCallerFiles(root)
            .Where(file => pattern.IsMatch(File.ReadAllText(file.Path)))
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "単一 movie 更新の直叩きは mutation facade 以外へ戻さない。"
        );
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IndigoMovieManager.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        Assert.Fail("リポジトリルートを特定できませんでした。");
        return "";
    }

    private static IEnumerable<(string Path, string RelativePath)> EnumerateLaneBMutationCallerFiles(
        string root
    )
    {
        string[] relativePaths =
        {
            "Views/Main/MainWindow.Tag.cs",
            "Views/Main/MainWindow.Player.cs",
            "Views/Main/MainWindow.MenuActions.cs",
            "UserControls/TagControl.xaml.cs",
            "Thumbnail/MainWindow.ThumbnailCreation.cs",
            "Watcher/MainWindow.WatcherRenameBridge.cs",
        };

        foreach (string relativePath in relativePaths)
        {
            yield return (Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)), relativePath);
        }
    }

    private static void AssertMethodUsesFacadeOnly(
        string methodBody,
        string facadePattern,
        string operationName
    )
    {
        Assert.That(
            Regex.IsMatch(methodBody, facadePattern),
            Is.True,
            $"{operationName} は facade 経由で呼ばれる必要があります。"
        );

        string[] forbiddenTokens =
        [
            "GetData(",
            "CreateReadOnlyConnection",
            "SQLiteConnection",
            "SQLiteCommand",
            "SQLiteDataAdapter",
            "SQLiteDataReader",
            "ExecuteReader(",
            "ExecuteScalar(",
        ];

        foreach (string forbiddenToken in forbiddenTokens)
        {
            Assert.That(
                methodBody,
                Does.Not.Contain(forbiddenToken),
                $"{operationName} に旧 direct DB read が再混入しています。"
            );
        }
    }

    private static string ExtractMethodBody(string source, string signaturePrefix)
    {
        int signatureIndex = source.IndexOf(signaturePrefix, StringComparison.Ordinal);
        Assert.That(signatureIndex, Is.GreaterThanOrEqualTo(0), $"{signaturePrefix} が見つかりません。");

        int bodyStart = source.IndexOf('{', signatureIndex);
        Assert.That(bodyStart, Is.GreaterThanOrEqualTo(0), $"{signaturePrefix} の本体開始が見つかりません。");

        int depth = 0;
        for (int index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[(bodyStart + 1)..index];
                }
            }
        }

        Assert.Fail($"{signaturePrefix} の本体終端が見つかりません。");
        return "";
    }
}
