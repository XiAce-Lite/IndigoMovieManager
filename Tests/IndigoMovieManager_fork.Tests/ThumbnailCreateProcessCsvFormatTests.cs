using System.Reflection;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[NonParallelizable]
public class ThumbnailCreateProcessCsvFormatTests
{
    private static readonly string ExpectedHeader =
        "datetime,engine,movie_file_name,codec,length_sec,size_bytes,output_path,status,error_message";

    [Test]
    public void WriteThumbnailCreateProcessLog_HeaderAndColumns_AreCompatible()
    {
        string logPath = GetProcessLogPath();
        string logDir = Path.GetDirectoryName(logPath) ?? string.Empty;
        Directory.CreateDirectory(logDir);

        bool hadOriginal = File.Exists(logPath);
        byte[] originalBytes = hadOriginal ? File.ReadAllBytes(logPath) : [];

        try
        {
            // テスト用に毎回ログを作り直し、ヘッダーと列数を安定検証する。
            if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }

            InvokeWriteProcessLog(
                engineId: "autogen",
                movieFullPath: @"C:\tmp\movie_a.mp4",
                codec: "h264",
                durationSec: 12.345,
                fileSizeBytes: 123456,
                outputPath: @"C:\tmp\out.jpg",
                isSuccess: true,
                errorMessage: string.Empty
            );

            string[] lines = File.ReadAllLines(logPath);
            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(lines[0], Is.EqualTo(ExpectedHeader));

            List<string> cols = ParseCsvLine(lines[1]);
            Assert.That(cols.Count, Is.EqualTo(9));
            Assert.That(cols[1], Is.EqualTo("autogen"));
            Assert.That(cols[2], Is.EqualTo("movie_a.mp4"));
            Assert.That(cols[3], Is.EqualTo("h264"));
            Assert.That(cols[4], Is.EqualTo("12.345"));
            Assert.That(cols[5], Is.EqualTo("123456"));
            Assert.That(cols[6], Is.EqualTo(@"C:\tmp\out.jpg"));
            Assert.That(cols[7], Is.EqualTo("success"));
        }
        finally
        {
            // 既存ログを壊さないよう、テスト後に元へ戻す。
            if (hadOriginal)
            {
                File.WriteAllBytes(logPath, originalBytes);
            }
            else if (File.Exists(logPath))
            {
                File.Delete(logPath);
            }
        }
    }

    [Test]
    public void EscapeCsvValue_CommaAndQuote_AreEscaped()
    {
        MethodInfo? method = typeof(ThumbnailCreationService).GetMethod(
            "EscapeCsvValue",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.That(method, Is.Not.Null);

        object? raw = method!.Invoke(null, ["a,\"b\",c"]);
        Assert.That(raw as string, Is.EqualTo("\"a,\"\"b\"\",c\""));
    }

    private static string GetProcessLogPath()
    {
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IndigoMovieManager_fork",
            "logs"
        );
        return Path.Combine(logDir, "thumbnail-create-process.csv");
    }

    private static void InvokeWriteProcessLog(
        string engineId,
        string movieFullPath,
        string codec,
        double? durationSec,
        long fileSizeBytes,
        string outputPath,
        bool isSuccess,
        string errorMessage
    )
    {
        MethodInfo? method = typeof(ThumbnailCreationService).GetMethod(
            "WriteThumbnailCreateProcessLog",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.That(method, Is.Not.Null);

        _ = method!.Invoke(
            null,
            [engineId, movieFullPath, codec, durationSec, fileSizeBytes, outputPath, isSuccess, errorMessage]
        );
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        result.Add(current.ToString());
        return result;
    }
}
