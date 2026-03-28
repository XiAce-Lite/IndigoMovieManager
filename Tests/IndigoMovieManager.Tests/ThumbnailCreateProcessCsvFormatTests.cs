using IndigoMovieManager.Thumbnail;
using System.Globalization;

namespace IndigoMovieManager.Tests;

[TestFixture]
[NonParallelizable]
public class ThumbnailCreateProcessCsvFormatTests
{
    private static readonly string ExpectedHeader = DefaultThumbnailCreateProcessLogWriter.CsvHeader;
    private static readonly string[] ExpectedColumns =
    [
        "datetime",
        "engine",
        "movie_file_name",
        "codec",
        "length_sec",
        "size_bytes",
        "output_path",
        "status",
        "error_message",
    ];

    [Test]
    public void CsvContract_HeaderAndColumnOrder_ArePublic()
    {
        string actualHeader = DefaultThumbnailCreateProcessLogWriter.CsvHeader;

        Assert.That(
            DefaultThumbnailCreateProcessLogWriter.CsvColumns,
            Is.EqualTo(ExpectedColumns)
        );
        Assert.That(actualHeader, Is.EqualTo(string.Join(",", ExpectedColumns)));
        Assert.That(
            DefaultThumbnailCreateProcessLogWriter.TimestampFormat,
            Is.EqualTo("yyyy-MM-dd HH:mm:ss.fff")
        );
        Assert.That(
            DefaultThumbnailCreateProcessLogWriter.StatusSuccess,
            Is.EqualTo("success")
        );
        Assert.That(
            DefaultThumbnailCreateProcessLogWriter.StatusFailed,
            Is.EqualTo("failed")
        );
        Assert.That(
            DefaultThumbnailCreateProcessLogWriter.DurationFormat,
            Is.EqualTo("0.###")
        );
        Assert.That(
            DefaultThumbnailCreateProcessLogWriter.ZeroFileSizeText,
            Is.EqualTo("0")
        );
    }

    [Test]
    public void CsvContract_DurationAndSizeFormatting_ArePublic()
    {
        Assert.That(
            DefaultThumbnailCreateProcessLogWriter.FormatDurationValue(12.3456),
            Is.EqualTo("12.346")
        );
        Assert.That(
            DefaultThumbnailCreateProcessLogWriter.FormatDurationValue(null),
            Is.EqualTo(string.Empty)
        );
        Assert.That(
            DefaultThumbnailCreateProcessLogWriter.FormatFileSizeValue(123456),
            Is.EqualTo("123456")
        );
        Assert.That(
            DefaultThumbnailCreateProcessLogWriter.FormatFileSizeValue(0),
            Is.EqualTo(DefaultThumbnailCreateProcessLogWriter.ZeroFileSizeText)
        );
    }

    [Test]
    public void CsvContract_EscapeCsvValue_IsPublic()
    {
        Assert.That(
            DefaultThumbnailCreateProcessLogWriter.EscapeCsvValue("a,\"b\",c"),
            Is.EqualTo("\"a,\"\"b\"\",c\"")
        );
    }

    [Test]
    public void CsvContract_BuildCsvLine_IsPublicAndDeterministic()
    {
        string line = DefaultThumbnailCreateProcessLogWriter.BuildCsvLine(
            new ThumbnailCreateProcessLogEntry
            {
                EngineId = "autogen",
                MovieFullPath = @"C:\tmp\movie_a.mp4",
                Codec = "h264",
                DurationSec = 12.3456,
                FileSizeBytes = 123456,
                OutputPath = @"C:\tmp\out path.jpg",
                IsSuccess = true,
                ErrorMessage = "a,\"b\",c",
            },
            new DateTime(2026, 3, 17, 12, 34, 56, 789)
        );

        Assert.That(
            line,
            Is.EqualTo(
                "2026-03-17 12:34:56.789,autogen,movie_a.mp4,h264,12.346,123456,C:\\tmp\\out path.jpg,success,\"a,\"\"b\"\",c\""
            )
        );
    }

    [Test]
    public void DefaultWriter_HeaderAndColumns_AreCompatible()
    {
        string tempRoot = CreateTempRoot();

        try
        {
            string logPath = Path.Combine(
                tempRoot,
                "logs",
                DefaultThumbnailCreateProcessLogWriter.FileName
            );
            var writer = new DefaultThumbnailCreateProcessLogWriter(
                new TestThumbnailCreationHostRuntime(logPath)
            );
            writer.Write(
                new ThumbnailCreateProcessLogEntry
                {
                    EngineId = "autogen",
                    MovieFullPath = @"C:\tmp\movie_a.mp4",
                    Codec = "h264",
                    DurationSec = 12.345,
                    FileSizeBytes = 123456,
                    OutputPath = @"C:\tmp\out.jpg",
                    IsSuccess = true,
                    ErrorMessage = string.Empty,
                }
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
            Assert.That(cols[7], Is.EqualTo(DefaultThumbnailCreateProcessLogWriter.StatusSuccess));
            Assert.That(
                DateTime.TryParseExact(
                    cols[0],
                    DefaultThumbnailCreateProcessLogWriter.TimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _
                ),
                Is.True
            );
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void DefaultWriter_Write_AppendsOnceAndUsesUtf8WithoutBom()
    {
        string tempRoot = CreateTempRoot();

        try
        {
            string logPath = Path.Combine(
                tempRoot,
                "logs",
                DefaultThumbnailCreateProcessLogWriter.FileName
            );
            string logDir = Path.GetDirectoryName(logPath) ?? string.Empty;
            Assert.That(Directory.Exists(logDir), Is.False);

            var writer = new DefaultThumbnailCreateProcessLogWriter(
                new TestThumbnailCreationHostRuntime(logPath)
            );

            writer.Write(
                new ThumbnailCreateProcessLogEntry
                {
                    EngineId = "autogen",
                    MovieFullPath = @"C:\tmp\movie_a.mp4",
                    Codec = "h264",
                    DurationSec = 1.5,
                    FileSizeBytes = 111,
                    OutputPath = @"C:\tmp\out1.jpg",
                    IsSuccess = true,
                    ErrorMessage = string.Empty,
                }
            );
            writer.Write(
                new ThumbnailCreateProcessLogEntry
                {
                    EngineId = "ffmpeg1pass",
                    MovieFullPath = @"C:\tmp\movie_b.mp4",
                    Codec = "mpeg4",
                    DurationSec = 2.5,
                    FileSizeBytes = 222,
                    OutputPath = @"C:\tmp\out2.jpg",
                    IsSuccess = false,
                    ErrorMessage = "failed",
                }
            );

            Assert.That(Directory.Exists(logDir), Is.True);

            byte[] bytes = File.ReadAllBytes(logPath);
            Assert.That(bytes.Length, Is.GreaterThan(3));
            Assert.That(bytes[0], Is.Not.EqualTo(0xEF));

            string[] lines = File.ReadAllLines(logPath);
            Assert.That(lines.Length, Is.EqualTo(3));
            Assert.That(
                lines.Count(line => line == DefaultThumbnailCreateProcessLogWriter.CsvHeader),
                Is.EqualTo(1)
            );

            List<string> secondCols = ParseCsvLine(lines[2]);
            Assert.That(secondCols[1], Is.EqualTo("ffmpeg1pass"));
            Assert.That(
                secondCols[7],
                Is.EqualTo(DefaultThumbnailCreateProcessLogWriter.StatusFailed)
            );
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void DefaultWriter_ErrorMessageWithCommaAndQuote_IsEscaped()
    {
        string tempRoot = CreateTempRoot();

        try
        {
            string logPath = Path.Combine(
                tempRoot,
                "logs",
                DefaultThumbnailCreateProcessLogWriter.FileName
            );
            var writer = new DefaultThumbnailCreateProcessLogWriter(
                new TestThumbnailCreationHostRuntime(logPath)
            );
            writer.Write(
                new ThumbnailCreateProcessLogEntry
                {
                    EngineId = "autogen",
                    MovieFullPath = @"C:\tmp\movie_a.mp4",
                    Codec = "h264",
                    DurationSec = 12.345,
                    FileSizeBytes = 123456,
                    OutputPath = @"C:\tmp\out.jpg",
                    IsSuccess = false,
                    ErrorMessage = "a,\"b\",c",
                }
            );

            string[] lines = File.ReadAllLines(logPath);
            Assert.That(
                lines[1],
                Does.Contain(DefaultThumbnailCreateProcessLogWriter.EscapeCsvValue("a,\"b\",c"))
            );

            List<string> cols = ParseCsvLine(lines[1]);
            Assert.That(cols[7], Is.EqualTo(DefaultThumbnailCreateProcessLogWriter.StatusFailed));
            Assert.That(cols[8], Is.EqualTo("a,\"b\",c"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void DefaultWriter_Write_例外を外へ投げない()
    {
        string tempRoot = CreateTempRoot();

        try
        {
            string invalidPath = Path.Combine(
                tempRoot,
                "bad>dir",
                DefaultThumbnailCreateProcessLogWriter.FileName
            );
            var writer = new DefaultThumbnailCreateProcessLogWriter(
                new TestThumbnailCreationHostRuntime(invalidPath)
            );

            Assert.DoesNotThrow(() =>
                writer.Write(
                    new ThumbnailCreateProcessLogEntry
                    {
                        EngineId = "autogen",
                        MovieFullPath = @"C:\tmp\movie_a.mp4",
                        Codec = "h264",
                        DurationSec = 1.0,
                        FileSizeBytes = 1,
                        OutputPath = @"C:\tmp\out.jpg",
                        IsSuccess = true,
                        ErrorMessage = string.Empty,
                    }
                )
            );
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class TestThumbnailCreationHostRuntime : IThumbnailCreationHostRuntime
    {
        private readonly string logPath;

        public TestThumbnailCreationHostRuntime(string logPath)
        {
            this.logPath = logPath;
        }

        public string ResolveMissingMoviePlaceholderPath(int tabIndex)
        {
            return string.Empty;
        }

        public string ResolveProcessLogPath(string fileName)
        {
            return logPath;
        }
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
