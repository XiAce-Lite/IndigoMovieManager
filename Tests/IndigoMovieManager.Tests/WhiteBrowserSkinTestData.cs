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

    internal static string GetFixtureHtmlPath(string skinRootPath, string fixtureName)
    {
        string fixtureDirectoryPath = Path.Combine(skinRootPath, fixtureName);
        string htmlPath = Directory
            .EnumerateFiles(fixtureDirectoryPath, "*.htm")
            .Concat(Directory.EnumerateFiles(fixtureDirectoryPath, "*.html"))
            .Single();
        return htmlPath;
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
}
