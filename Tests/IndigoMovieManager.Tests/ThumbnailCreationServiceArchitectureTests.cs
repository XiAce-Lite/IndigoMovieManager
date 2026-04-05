using System.Reflection;
using System.Text.RegularExpressions;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class ThumbnailCreationServiceArchitectureTests
{
    [Test]
    public void LegacyApi_完全削除済み()
    {
        Type serviceType = typeof(ThumbnailCreationService);
        Assert.That(serviceType.IsNotPublic, Is.True);
        Assert.That(
            serviceType.GetMethod(
                "Create",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
            ),
            Is.Null
        );

        ConstructorInfo[] publicConstructors = serviceType.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public
        );
        Assert.That(publicConstructors, Is.Empty);
        AssertLegacyMethodMissing(
            serviceType,
            nameof(ThumbnailCreationService.CreateBookmarkThumbAsync),
            typeof(string),
            typeof(string),
            typeof(int)
        );
        AssertLegacyMethodMissing(
            serviceType,
            nameof(ThumbnailCreationService.CreateThumbAsync),
            typeof(QueueObj),
            typeof(string),
            typeof(string),
            typeof(bool),
            typeof(bool),
            typeof(CancellationToken),
            typeof(string),
            typeof(string),
            typeof(ThumbInfo)
        );
        AssertLegacyMethodMissing(
            serviceType,
            nameof(ThumbnailCreationService.CreateThumbAsync),
            typeof(ThumbnailRequest),
            typeof(string),
            typeof(string),
            typeof(bool),
            typeof(bool),
            typeof(CancellationToken),
            typeof(string),
            typeof(string),
            typeof(ThumbInfo)
        );

        MethodInfo canonicalBookmark = RequirePublicInstanceMethod(
            serviceType,
            nameof(ThumbnailCreationService.CreateBookmarkThumbAsync),
            typeof(ThumbnailBookmarkArgs),
            typeof(CancellationToken)
        );
        MethodInfo canonicalCreate = RequirePublicInstanceMethod(
            serviceType,
            nameof(ThumbnailCreationService.CreateThumbAsync),
            typeof(ThumbnailCreateArgs),
            typeof(CancellationToken)
        );

        AssertCanonicalMember(canonicalBookmark);
        AssertCanonicalMember(canonicalCreate);
    }

    [Test]
    public void ThumbnailCreateArgs_公開面はRequest本流だけに絞られている()
    {
        string[] propertyNames = typeof(ThumbnailCreateArgs)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(propertyNames, Does.Contain(nameof(ThumbnailCreateArgs.Request)));
        Assert.That(propertyNames, Does.Not.Contain("QueueObj"));
    }

    [Test]
    public void Service_保持する依存はdelegateだけに絞られている()
    {
        Type serviceType = typeof(ThumbnailCreationService);
        FieldInfo[] fields = serviceType.GetFields(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
        );

        Assert.That(
            fields.Select(field => field.FieldType),
            Is.EquivalentTo(
                new[]
                {
                    typeof(Func<ThumbnailBookmarkArgs, CancellationToken, Task<bool>>),
                    typeof(Func<ThumbnailCreateArgs, CancellationToken, Task<ThumbnailCreateResult>>),
                }
            )
        );
    }

    [Test]
    public void Composition_Serviceへ渡す面もdelegateだけに絞られている()
    {
        PropertyInfo[] properties = typeof(ThumbnailCreationServiceComposition).GetProperties(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
        );

        Assert.That(
            properties.Select(property => property.PropertyType),
            Is.EquivalentTo(
                new[]
                {
                    typeof(Func<ThumbnailBookmarkArgs, CancellationToken, Task<bool>>),
                    typeof(Func<ThumbnailCreateArgs, CancellationToken, Task<ThumbnailCreateResult>>),
                }
            )
        );
        Assert.That(
            properties.All(property => property.GetMethod?.IsAssembly == true),
            Is.True
        );
    }

    [Test]
    public void ComponentFactory_組み立てhelperはinternalなstatic面だけに閉じている()
    {
        Type factoryType = typeof(ThumbnailCreationServiceComponentFactory);
        MethodInfo[] methods = factoryType
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .OrderBy(method => method.Name)
            .ThenBy(method => method.GetParameters().Length)
            .ToArray();

        Assert.That(factoryType.IsNotPublic, Is.True);
        Assert.That(methods.All(method => method.IsAssembly), Is.True);
        Assert.That(
            methods.Select(FormatSignature),
            Is.EquivalentTo(
                new[]
                {
                    "Compose(ThumbnailCreationOptions)",
                    "CreateDefaultEngineSet()",
                    "CreateDefaultOptions()",
                    "CreateEngineSet(IThumbnailGenerationEngine,IThumbnailGenerationEngine,IThumbnailGenerationEngine,IThumbnailGenerationEngine)",
                    "CreateOptions(ThumbnailCreationEngineSet,IVideoMetadataProvider,IThumbnailLogger,IThumbnailCreationHostRuntime,IThumbnailCreateProcessLogWriter)",
                    "CreateTestingOptions(IThumbnailGenerationEngine,IThumbnailGenerationEngine,IThumbnailGenerationEngine,IThumbnailGenerationEngine,ThumbnailCreationOptions)",
                }
            )
        );
    }

    [Test]
    public void Factory_公開面が正規入口だけに絞られている()
    {
        Type factoryType = typeof(ThumbnailCreationServiceFactory);
        MethodInfo[] publicMethods = factoryType.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly
            )
            .OrderBy(method => method.Name)
            .ThenBy(method => method.GetParameters().Length)
            .ToArray();
        MethodInfo[] internalMethods = factoryType
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .OrderBy(method => method.Name)
            .ThenBy(method => method.GetParameters().Length)
            .ToArray();

        Assert.That(publicMethods, Has.Length.EqualTo(2));
        Assert.That(
            publicMethods.All(method => method.ReturnType == typeof(IThumbnailCreationService)),
            Is.True
        );
        Assert.That(
            publicMethods.Select(FormatSignature),
            Is.EquivalentTo(
                new[]
                {
                    "Create(IThumbnailCreationHostRuntime,IThumbnailCreateProcessLogWriter)",
                    "Create(IVideoMetadataProvider,IThumbnailLogger,IThumbnailCreationHostRuntime,IThumbnailCreateProcessLogWriter)",
                }
            )
        );
        Assert.That(
            internalMethods.Select(FormatSignature),
            Has.Member("CreateDefault()")
        );
        Assert.That(
            factoryType.GetMethod(
                "CreateForTesting",
                BindingFlags.Static
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly
            ),
            Is.Null
        );
    }

    [Test]
    public void EntryCoordinator_入口はinternalなArgs一本だけに絞られている()
    {
        Type coordinatorType = typeof(ThumbnailCreateEntryCoordinator);
        ConstructorInfo[] publicConstructors = coordinatorType.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public
        );
        MethodInfo[] createMethods = coordinatorType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "CreateAsync")
            .OrderBy(method => method.GetParameters().Length)
            .ToArray();

        Assert.That(publicConstructors, Is.Empty);
        Assert.That(
            createMethods.Select(FormatSignature),
            Is.EquivalentTo(new[] { "CreateAsync(ThumbnailCreateArgs,CancellationToken)" })
        );
        Assert.That(createMethods.All(method => method.IsAssembly), Is.True);
    }

    [Test]
    public void BookmarkCoordinator_入口はinternalなBookmarkArgs一本だけに絞られている()
    {
        Type coordinatorType = typeof(ThumbnailBookmarkCoordinator);
        ConstructorInfo[] publicConstructors = coordinatorType.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public
        );
        MethodInfo[] createMethods = coordinatorType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(method => method.Name == "CreateAsync")
            .OrderBy(method => method.GetParameters().Length)
            .ToArray();

        Assert.That(publicConstructors, Is.Empty);
        Assert.That(
            createMethods.Select(FormatSignature),
            Is.EquivalentTo(new[] { "CreateAsync(ThumbnailBookmarkArgs,CancellationToken)" })
        );
        Assert.That(createMethods.All(method => method.IsAssembly), Is.True);
    }

    [Test]
    public void RequestArgumentValidator_assembly内helperとして閉じている()
    {
        Type validatorType = typeof(ThumbnailRequestArgumentValidator);
        MethodInfo[] methods = validatorType
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .OrderBy(method => method.Name)
            .ToArray();

        Assert.That(validatorType.IsNotPublic, Is.True);
        Assert.That(
            methods.Select(FormatSignature),
            Is.EquivalentTo(
                new[]
                {
                    "ValidateBookmarkArgs(ThumbnailBookmarkArgs)",
                    "ValidateCreateArgs(ThumbnailCreateArgs)",
                }
            )
        );
        Assert.That(methods.All(method => method.IsAssembly), Is.True);
    }

    [Test]
    public void RequestArgumentValidator_ValidateCreateArgs利用箇所はentryCoordinatorと専用testsに閉じている()
    {
        string root = FindRepositoryRoot();
        var pattern = new Regex(
            $@"\b{Regex.Escape(nameof(ThumbnailRequestArgumentValidator))}\b\s*\.\s*ValidateCreateArgs\s*\(",
            RegexOptions.CultureInvariant
        );

        string[] offenders = EnumerateRepositoryCsFiles(root)
            .Select(path => new { Path = path, RelativePath = ToRelativePath(root, path) })
            .Where(file => !IsAllowedCreateArgumentValidatorCaller(file.RelativePath))
            .Where(file => pattern.IsMatch(File.ReadAllText(file.Path)))
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "create validator 呼び出しは entry coordinator / 専用 tests に閉じる。"
        );
    }

    [Test]
    public void RequestArgumentValidator_ValidateBookmarkArgs利用箇所はbookmarkCoordinatorと専用testsに閉じている()
    {
        string root = FindRepositoryRoot();
        var pattern = new Regex(
            $@"\b{Regex.Escape(nameof(ThumbnailRequestArgumentValidator))}\b\s*\.\s*ValidateBookmarkArgs\s*\(",
            RegexOptions.CultureInvariant
        );

        string[] offenders = EnumerateRepositoryCsFiles(root)
            .Select(path => new { Path = path, RelativePath = ToRelativePath(root, path) })
            .Where(file => !IsAllowedBookmarkArgumentValidatorCaller(file.RelativePath))
            .Where(file => pattern.IsMatch(File.ReadAllText(file.Path)))
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "bookmark validator 呼び出しは bookmark coordinator / 専用 tests に閉じる。"
        );
    }

    [Test]
    public void RequestArgumentValidator_必須メッセージ定義はvalidatorに集約されている()
    {
        string root = FindRepositoryRoot();
        string[] messages =
        [
            "Request は必須です。",
            "MovieFullPath は必須です。",
            "SaveThumbPath は必須です。",
        ];

        string[] offenders = EnumerateRepositoryCsFiles(root)
            .Select(path => new { Path = path, RelativePath = ToRelativePath(root, path) })
            .Where(file =>
                !string.Equals(
                    file.RelativePath,
                    "src/IndigoMovieManager.Thumbnail.Engine/ThumbnailRequestArgumentValidator.cs",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Where(file =>
                !string.Equals(
                    file.RelativePath,
                    "Tests/IndigoMovieManager.Tests/ThumbnailCreationServiceArchitectureTests.cs",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Where(file =>
            {
                string content = File.ReadAllText(file.Path);
                return messages.Any(content.Contains);
            })
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "必須メッセージ文字列は validator に集約する。"
        );
    }

    [Test]
    public void EngineProject_LegacyCompile条件が残っていない()
    {
        string root = FindRepositoryRoot();
        string csprojPath = Path.Combine(
            root,
            "src",
            "IndigoMovieManager.Thumbnail.Engine",
            "IndigoMovieManager.Thumbnail.Engine.csproj"
        );
        string xml = File.ReadAllText(csprojPath);

        Assert.That(xml, Does.Not.Contain("EnableThumbnailCreationServiceLegacyApi"));
        Assert.That(xml, Does.Not.Contain("ThumbnailCreationService.Legacy.cs"));
    }

    [Test]
    public void 旧Autogen回帰テスト資産が残っていない()
    {
        string root = FindRepositoryRoot();
        string legacyTestPath = Path.Combine(root, "Thumbnail", "Test", "AutogenRegressionTests.cs");

        Assert.That(File.Exists(legacyTestPath), Is.False);
    }

    [Test]
    public void Service直newはfactoryとtestHelperに閉じている()
    {
        string root = FindRepositoryRoot();
        string serviceTypeName = nameof(ThumbnailCreationService);
        var directNewPattern = new Regex(
            $@"new\s+{Regex.Escape(serviceTypeName)}\s*\(",
            RegexOptions.CultureInvariant
        );

        string[] offenders = EnumerateRepositoryCsFiles(root)
            .Select(path => new { Path = path, RelativePath = ToRelativePath(root, path) })
            .Where(file => !IsAllowedConcreteConstructorCaller(file.RelativePath))
            .Where(file => directNewPattern.IsMatch(File.ReadAllText(file.Path)))
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "直 new は production factory か tests helper に閉じる。"
        );
    }

    [Test]
    public void TestFactory_利用箇所はテスト領域に閉じている()
    {
        string root = FindRepositoryRoot();
        var pattern = new Regex(
            $@"{Regex.Escape(nameof(ThumbnailCreationServiceTestFactory))}\s*\.\s*CreateForTesting\s*\(",
            RegexOptions.CultureInvariant
        );

        string[] offenders = EnumerateRepositoryCsFiles(root)
            .Select(path => new { Path = path, RelativePath = ToRelativePath(root, path) })
            .Where(file => !IsAllowedTestFactoryCaller(file.RelativePath))
            .Where(file => pattern.IsMatch(File.ReadAllText(file.Path)))
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.That(offenders, Is.Empty, "test factory は production へ漏らさない。");
    }

    [Test]
    public void TestFactory_戻り値もinterfaceに固定されている()
    {
        MethodInfo? method = typeof(ThumbnailCreationServiceTestFactory).GetMethod(
            "CreateForTesting",
            BindingFlags.Static | BindingFlags.NonPublic
        );

        Assert.That(method, Is.Not.Null);
        Assert.That(method!.ReturnType, Is.EqualTo(typeof(IThumbnailCreationService)));
    }

    [Test]
    public void PublicFactory_利用箇所はhost別factoryかテスト領域に閉じている()
    {
        string root = FindRepositoryRoot();
        var pattern = new Regex(
            $@"\b{Regex.Escape(nameof(ThumbnailCreationServiceFactory))}\b\s*\.\s*Create(?:Default)?\s*\(",
            RegexOptions.CultureInvariant
        );

        string[] offenders = EnumerateRepositoryCsFiles(root)
            .Select(path => new { Path = path, RelativePath = ToRelativePath(root, path) })
            .Where(file => !IsAllowedPublicFactoryCaller(file.RelativePath))
            .Where(file => pattern.IsMatch(File.ReadAllText(file.Path)))
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "public factory の直呼びは host 別 factory か tests に閉じる。"
        );
    }

    [Test]
    public void InternalFactory_CreateDefault利用箇所はtestsに閉じている()
    {
        string root = FindRepositoryRoot();
        var pattern = new Regex(
            $@"\b{Regex.Escape(nameof(ThumbnailCreationServiceFactory))}\b\s*\.\s*CreateDefault\s*\(",
            RegexOptions.CultureInvariant
        );

        string[] offenders = EnumerateRepositoryCsFiles(root)
            .Select(path => new { Path = path, RelativePath = ToRelativePath(root, path) })
            .Where(file =>
                !file.RelativePath.StartsWith(
                    "Tests/IndigoMovieManager.Tests/",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .Where(file => pattern.IsMatch(File.ReadAllText(file.Path)))
            .Select(file => file.RelativePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.That(
            offenders,
            Is.Empty,
            "CreateDefault は tests 内の内部入口に閉じる。"
        );
    }

    private static MethodInfo RequirePublicInstanceMethod(
        Type targetType,
        string name,
        params Type[] parameterTypes
    )
    {
        MethodInfo? method = targetType.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: parameterTypes,
            modifiers: null
        );
        Assert.That(method, Is.Not.Null);
        return method!;
    }

    private static void AssertLegacyMethodMissing(
        Type targetType,
        string name,
        params Type[] parameterTypes
    )
    {
        MethodInfo? method = targetType.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: parameterTypes,
            modifiers: null
        );
        Assert.That(method, Is.Null);
    }

    private static void AssertCanonicalMember(MethodInfo method)
    {
        Assert.That(method.GetCustomAttribute<ObsoleteAttribute>(), Is.Null);
    }

    private static string FormatSignature(MethodInfo method)
    {
        string parameters = string.Join(
            ",",
            method.GetParameters().Select(parameter => parameter.ParameterType.Name)
        );
        return $"{method.Name}({parameters})";
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
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

    private static IEnumerable<string> EnumerateRepositoryCsFiles(string root)
    {
        foreach (string filePath in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string relativePath = ToRelativePath(root, filePath);
            if (IsIgnoredPath(relativePath))
            {
                continue;
            }

            yield return filePath;
        }
    }

    private static string ToRelativePath(string root, string path)
    {
        return Path.GetRelativePath(root, path).Replace('\\', '/');
    }

    private static bool IsIgnoredPath(string relativePath)
    {
        string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // 生成物やローカル資産は検査対象から外し、本流コードだけを見る。
        return segments.Any(
            segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
                || segment.Equals(".vs", StringComparison.OrdinalIgnoreCase)
                || segment.Equals(".local", StringComparison.OrdinalIgnoreCase)
                || segment.Equals(".codex_build", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("artifacts", StringComparison.OrdinalIgnoreCase)
        );
    }

    private static bool IsAllowedTestFactoryCaller(string relativePath)
    {
        if (
            string.Equals(
                relativePath,
                "Tests/IndigoMovieManager.Tests/ThumbnailCreationServiceTestFactory.cs",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return true;
        }

        return relativePath.StartsWith(
                "Tests/IndigoMovieManager.Tests/",
                StringComparison.OrdinalIgnoreCase
            )
            || relativePath.StartsWith("Thumbnail/Test/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedConcreteConstructorCaller(string relativePath)
    {
        return string.Equals(
                relativePath,
                "src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreationServiceFactory.cs",
                StringComparison.OrdinalIgnoreCase
            )
            || string.Equals(
                relativePath,
                "Tests/IndigoMovieManager.Tests/ThumbnailCreationServiceTestFactory.cs",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool IsAllowedCreateArgumentValidatorCaller(string relativePath)
    {
        return string.Equals(
                relativePath,
                "src/IndigoMovieManager.Thumbnail.Engine/ThumbnailCreateEntryCoordinator.cs",
                StringComparison.OrdinalIgnoreCase
            )
            || string.Equals(
                relativePath,
                "Tests/IndigoMovieManager.Tests/ThumbnailRequestArgumentValidatorTests.cs",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool IsAllowedBookmarkArgumentValidatorCaller(string relativePath)
    {
        return string.Equals(
                relativePath,
                "src/IndigoMovieManager.Thumbnail.Engine/ThumbnailBookmarkCoordinator.cs",
                StringComparison.OrdinalIgnoreCase
            )
            || string.Equals(
                relativePath,
                "Tests/IndigoMovieManager.Tests/ThumbnailRequestArgumentValidatorTests.cs",
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool IsAllowedPublicFactoryCaller(string relativePath)
    {
        if (
            string.Equals(
                relativePath,
                "Thumbnail/AppThumbnailCreationServiceFactory.cs",
                StringComparison.OrdinalIgnoreCase
            )
            || string.Equals(
                relativePath,
                "src/IndigoMovieManager.Thumbnail.RescueWorker/RescueWorkerThumbnailCreationServiceFactory.cs",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return true;
        }

        return relativePath.StartsWith(
            "Tests/IndigoMovieManager.Tests/",
            StringComparison.OrdinalIgnoreCase
        );
    }
}
