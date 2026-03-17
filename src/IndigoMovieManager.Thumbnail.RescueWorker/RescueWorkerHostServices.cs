using System.Globalization;
using System.IO;
using System.Text;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    // worker 側は host 基盤へ依存せず、必要最小限の path 解決だけを自前で持つ。
    internal sealed class RescueWorkerHostRuntime : IThumbnailCreationHostRuntime
    {
        private readonly string logDirectoryPath;

        public RescueWorkerHostRuntime(string logDirectoryPath)
        {
            this.logDirectoryPath = logDirectoryPath?.Trim() ?? "";
        }

        public string ResolveMissingMoviePlaceholderPath(int tabIndex)
        {
            string[] fileNames = tabIndex switch
            {
                1 => ["noFileBig.jpg"],
                2 => ["noFileGrid.jpg"],
                3 => ["noFileList.jpg", "nofileList.jpg"],
                4 => ["noFileBig.jpg"],
                99 => ["noFileGrid.jpg"],
                _ => ["noFileSmall.jpg"],
            };

            return ResolveBundledImagePath(fileNames);
        }

        public string ResolveProcessLogPath(string fileName)
        {
            return Path.Combine(ResolveProcessLogDirectoryPath(), fileName ?? "");
        }

        private string ResolveProcessLogDirectoryPath()
        {
            if (!string.IsNullOrWhiteSpace(logDirectoryPath))
            {
                return logDirectoryPath;
            }

            string baseDir = string.IsNullOrWhiteSpace(AppContext.BaseDirectory)
                ? Directory.GetCurrentDirectory()
                : AppContext.BaseDirectory;
            return Path.Combine(baseDir, "logs");
        }

        private static string ResolveBundledImagePath(string[] fileNames)
        {
            string[] baseDirs = [AppContext.BaseDirectory, Directory.GetCurrentDirectory()];
            for (int i = 0; i < baseDirs.Length; i++)
            {
                string baseDir = baseDirs[i];
                if (string.IsNullOrWhiteSpace(baseDir))
                {
                    continue;
                }

                string imagesDir = Path.Combine(baseDir, "Images");
                for (int j = 0; j < fileNames.Length; j++)
                {
                    string candidate = Path.Combine(imagesDir, fileNames[j]);
                    if (Path.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            string fallbackBaseDir = string.IsNullOrWhiteSpace(AppContext.BaseDirectory)
                ? Directory.GetCurrentDirectory()
                : AppContext.BaseDirectory;
            return Path.Combine(fallbackBaseDir, "Images", fileNames[0]);
        }
    }

    // worker の process log は app 側既定 writer と同じ CSV 互換を保ちつつ、project 依存だけ切る。
    internal sealed class RescueWorkerProcessLogWriter : IThumbnailCreateProcessLogWriter
    {
        private const string FileName = "thumbnail-create-process.csv";
        private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
        private const string StatusSuccess = "success";
        private const string StatusFailed = "failed";
        private const string DurationFormat = "0.###";
        private const string ZeroFileSizeText = "0";
        private const string CsvHeader =
            "datetime,engine,movie_file_name,codec,length_sec,size_bytes,output_path,status,error_message";

        private static readonly object SyncRoot = new();
        private readonly IThumbnailCreationHostRuntime hostRuntime;

        public RescueWorkerProcessLogWriter(IThumbnailCreationHostRuntime hostRuntime)
        {
            this.hostRuntime = hostRuntime ?? throw new ArgumentNullException(nameof(hostRuntime));
        }

        public void Write(ThumbnailCreateProcessLogEntry entry)
        {
            try
            {
                string logPath = LogFileTimeWindowSeparator.PrepareForWrite(
                    hostRuntime.ResolveProcessLogPath(FileName)
                );
                string logDir = Path.GetDirectoryName(logPath) ?? "";
                Directory.CreateDirectory(logDir);

                lock (SyncRoot)
                {
                    bool needsHeader = !Path.Exists(logPath) || new FileInfo(logPath).Length == 0;
                    using StreamWriter writer = new(logPath, append: true, new UTF8Encoding(false));
                    if (needsHeader)
                    {
                        writer.WriteLine(CsvHeader);
                    }

                    writer.WriteLine(BuildCsvLine(entry));
                }
            }
            catch
            {
                // worker ログ失敗で救済本体は止めない。
            }
        }

        private static string BuildCsvLine(ThumbnailCreateProcessLogEntry entry)
        {
            entry ??= new ThumbnailCreateProcessLogEntry();

            string durationText = FormatDurationValue(entry.DurationSec);
            string sizeText = FormatFileSizeValue(entry.FileSizeBytes);
            string movieFileName = Path.GetFileName(entry.MovieFullPath) ?? "";

            return string.Join(
                ",",
                EscapeCsvValue(DateTime.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture)),
                EscapeCsvValue(entry.EngineId ?? ""),
                EscapeCsvValue(movieFileName),
                EscapeCsvValue(entry.Codec ?? ""),
                EscapeCsvValue(durationText),
                EscapeCsvValue(sizeText),
                EscapeCsvValue(entry.OutputPath ?? ""),
                EscapeCsvValue(entry.IsSuccess ? StatusSuccess : StatusFailed),
                EscapeCsvValue(entry.ErrorMessage ?? "")
            );
        }

        private static string FormatDurationValue(double? durationSec)
        {
            return durationSec.HasValue && durationSec.Value > 0
                ? durationSec.Value.ToString(DurationFormat, CultureInfo.InvariantCulture)
                : "";
        }

        private static string FormatFileSizeValue(long fileSizeBytes)
        {
            return fileSizeBytes > 0
                ? fileSizeBytes.ToString(CultureInfo.InvariantCulture)
                : ZeroFileSizeText;
        }

        private static string EscapeCsvValue(string value)
        {
            value ??= "";
            if (
                !value.Contains(',')
                && !value.Contains('"')
                && !value.Contains('\n')
                && !value.Contains('\r')
            )
            {
                return value;
            }

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}
