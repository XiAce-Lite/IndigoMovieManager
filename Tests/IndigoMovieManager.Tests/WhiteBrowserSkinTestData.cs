using System.Linq;
using System.Text;

namespace IndigoMovieManager.Tests;

internal static class WhiteBrowserSkinTestData
{
    private static readonly string FixtureRootPath = Path.Combine(
        AppContext.BaseDirectory,
        "TestData",
        "WhiteBrowserSkins"
    );

    static WhiteBrowserSkinTestData()
    {
        // 実物由来 fixture を Shift_JIS へ戻して扱うので、テスト側でもコードページを有効化する。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    internal static string CreateSkinRootCopy(
        IEnumerable<string> fixtureNames,
        bool rewriteHtmlAsShiftJis
    )
    {
        string destinationRootPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-wbskin-fixtures-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(destinationRootPath);

        foreach (string fixtureName in fixtureNames ?? [])
        {
            CopyFixtureDirectory(fixtureName, destinationRootPath, rewriteHtmlAsShiftJis);
        }

        return destinationRootPath;
    }

    internal static string CreateSkinRootCopyWithCompat(
        IEnumerable<string> fixtureNames,
        bool rewriteHtmlAsShiftJis
    )
    {
        string skinRootPath = CreateSkinRootCopy(fixtureNames, rewriteHtmlAsShiftJis);
        string compatSourcePath = FindRepositoryDirectory("skin", "Compat");
        if (string.IsNullOrWhiteSpace(compatSourcePath) || !Directory.Exists(compatSourcePath))
        {
            throw new DirectoryNotFoundException(
                $"Compat フォルダが見つかりません: {compatSourcePath}"
            );
        }

        CopyDirectory(compatSourcePath, Path.Combine(skinRootPath, "Compat"));
        return skinRootPath;
    }

    internal static string CreateRepositorySkinRootCopyWithCompat(IEnumerable<string> skinNames)
    {
        string skinRootPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-wbskin-repo-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(skinRootPath);

        foreach (string skinName in skinNames ?? [])
        {
            CopyRepositorySkinDirectory(skinName, skinRootPath);
        }

        string compatSourcePath = FindRepositoryDirectory("skin", "Compat");
        if (string.IsNullOrWhiteSpace(compatSourcePath) || !Directory.Exists(compatSourcePath))
        {
            throw new DirectoryNotFoundException(
                $"Compat フォルダが見つかりません: {compatSourcePath}"
            );
        }

        CopyDirectory(compatSourcePath, Path.Combine(skinRootPath, "Compat"));
        return skinRootPath;
    }

    internal static string CreateBuildOutputSkinRootCopyWithCompat(IEnumerable<string> skinNames)
    {
        string skinRootPath = Path.Combine(
            Path.GetTempPath(),
            $"imm-wbskin-build-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(skinRootPath);

        string buildSkinRootPath = FindRepositoryDirectory(
            "bin",
            "x64",
            "Debug",
            "net8.0-windows10.0.19041.0",
            "skin"
        );
        if (string.IsNullOrWhiteSpace(buildSkinRootPath) || !Directory.Exists(buildSkinRootPath))
        {
            throw new DirectoryNotFoundException(
                $"build output skin フォルダが見つかりません: {buildSkinRootPath}"
            );
        }

        foreach (string skinName in skinNames ?? [])
        {
            string normalizedSkinName = skinName ?? "";
            string sourceRootPath = Path.Combine(buildSkinRootPath, normalizedSkinName);
            if (!Directory.Exists(sourceRootPath))
            {
                throw new DirectoryNotFoundException(
                    $"build output skin が見つかりません: {sourceRootPath}"
                );
            }

            // 実行中 build 出力の skin をそのまま複製して、real skin 読込を確認する。
            CopyDirectory(sourceRootPath, Path.Combine(skinRootPath, normalizedSkinName));
        }

        string compatSourcePath = FindRepositoryDirectory("skin", "Compat");
        if (string.IsNullOrWhiteSpace(compatSourcePath) || !Directory.Exists(compatSourcePath))
        {
            throw new DirectoryNotFoundException(
                $"Compat フォルダが見つかりません: {compatSourcePath}"
            );
        }

        CopyDirectory(compatSourcePath, Path.Combine(skinRootPath, "Compat"));
        return skinRootPath;
    }

    internal static string GetFixtureHtmlPath(string skinRootPath, string fixtureName)
    {
        string fixtureDirectoryPath = Path.Combine(skinRootPath, fixtureName);
        string[] htmlPaths = Directory
            .EnumerateFiles(fixtureDirectoryPath, "*.htm")
            .Concat(Directory.EnumerateFiles(fixtureDirectoryPath, "*.html"))
            .ToArray();
        if (htmlPaths.Length == 1)
        {
            return htmlPaths[0];
        }

        string normalizedFixtureName = (fixtureName ?? "").TrimStart('#');
        string preferredHtmPath = Path.Combine(fixtureDirectoryPath, normalizedFixtureName + ".htm");
        if (File.Exists(preferredHtmPath))
        {
            return preferredHtmPath;
        }

        string preferredHtmlPath = Path.Combine(fixtureDirectoryPath, normalizedFixtureName + ".html");
        if (File.Exists(preferredHtmlPath))
        {
            return preferredHtmlPath;
        }

        throw new InvalidOperationException(
            $"html が一意に決まりません: {fixtureDirectoryPath} ({string.Join(", ", htmlPaths.Select(Path.GetFileName))})"
        );
    }

    internal static void DeleteDirectorySafe(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return;
        }

        try
        {
            Directory.Delete(directoryPath, recursive: true);
        }
        catch
        {
            // 一時ディレクトリの後始末失敗は、契約テスト本体より優先しない。
        }
    }

    private static void CopyFixtureDirectory(
        string fixtureName,
        string destinationRootPath,
        bool rewriteHtmlAsShiftJis
    )
    {
        string normalizedFixtureName = fixtureName ?? "";
        string sourceRootPath = Path.Combine(FixtureRootPath, normalizedFixtureName);
        if (!Directory.Exists(sourceRootPath))
        {
            throw new DirectoryNotFoundException($"fixture が見つかりません: {sourceRootPath}");
        }

        string destinationFixtureRootPath = Path.Combine(
            destinationRootPath,
            normalizedFixtureName
        );
        Directory.CreateDirectory(destinationFixtureRootPath);

        foreach (
            string sourceFilePath in Directory.EnumerateFiles(
                sourceRootPath,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            string relativePath = Path.GetRelativePath(sourceRootPath, sourceFilePath);
            string destinationFilePath = Path.Combine(destinationFixtureRootPath, relativePath);
            string destinationDirectoryPath = Path.GetDirectoryName(destinationFilePath) ?? "";
            if (!string.IsNullOrWhiteSpace(destinationDirectoryPath))
            {
                Directory.CreateDirectory(destinationDirectoryPath);
            }

            string extension = Path.GetExtension(sourceFilePath);
            bool isHtml =
                extension.Equals(".htm", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".html", StringComparison.OrdinalIgnoreCase);
            if (rewriteHtmlAsShiftJis && isHtml)
            {
                // repo では UTF-8 で保持し、実行時だけ Shift_JIS へ戻して実物寄りの読込を確認する。
                string html = File.ReadAllText(sourceFilePath, Encoding.UTF8);
                File.WriteAllBytes(
                    destinationFilePath,
                    Encoding.GetEncoding(932).GetBytes(html)
                );
                continue;
            }

            File.Copy(sourceFilePath, destinationFilePath, overwrite: true);
        }
    }

    private static void CopyRepositorySkinDirectory(string skinName, string destinationRootPath)
    {
        string normalizedSkinName = skinName ?? "";
        string sourceRootPath = FindRepositoryDirectory("skin", normalizedSkinName);
        if (string.IsNullOrWhiteSpace(sourceRootPath) || !Directory.Exists(sourceRootPath))
        {
            throw new DirectoryNotFoundException($"repo skin が見つかりません: {sourceRootPath}");
        }

        CopyDirectory(sourceRootPath, Path.Combine(destinationRootPath, normalizedSkinName));
    }

    private static string FindRepositoryDirectory(params string[] relativeSegments)
    {
        string current = TestContext.CurrentContext.TestDirectory;
        string latestMatch = "";
        while (!string.IsNullOrWhiteSpace(current))
        {
            string candidate = Path.Combine([current, .. relativeSegments]);
            if (Directory.Exists(candidate))
            {
                // Tests/bin 配下にも同形の候補があるため、一番上の repo 側候補を優先する。
                latestMatch = candidate;
            }

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }

            current = parent.FullName;
        }

        return latestMatch;
    }

    private static void CopyDirectory(string sourceDirectoryPath, string destinationDirectoryPath)
    {
        Directory.CreateDirectory(destinationDirectoryPath);
        foreach (
            string sourceFilePath in Directory.EnumerateFiles(
                sourceDirectoryPath,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            string relativePath = Path.GetRelativePath(sourceDirectoryPath, sourceFilePath);
            string destinationFilePath = Path.Combine(destinationDirectoryPath, relativePath);
            string destinationParentPath = Path.GetDirectoryName(destinationFilePath) ?? "";
            if (!string.IsNullOrWhiteSpace(destinationParentPath))
            {
                Directory.CreateDirectory(destinationParentPath);
            }

            File.Copy(sourceFilePath, destinationFilePath, overwrite: true);
        }
    }
}
