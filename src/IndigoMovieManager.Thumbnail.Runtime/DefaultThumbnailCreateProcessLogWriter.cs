using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using IndigoMovieManager.Thumbnail;

namespace IndigoMovieManager.Thumbnail
{
    // 既定の CSV 保存は host 側の責務として runtime project に置く。
    public sealed class DefaultThumbnailCreateProcessLogWriter : IThumbnailCreateProcessLogWriter
    {
        public const string FileName = "thumbnail-create-process.csv";
        public const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
        public const string StatusSuccess = "success";
        public const string StatusFailed = "failed";
        public const string DurationFormat = "0.###";
        public const string ZeroFileSizeText = "0";
        public const string CsvHeader =
            "datetime,engine,movie_file_name,codec,length_sec,size_bytes,output_path,status,error_message";
        public static IReadOnlyList<string> CsvColumns { get; } =
            new ReadOnlyCollection<string>(
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
                ]
            );

        private static readonly object SyncRoot = new();
        private readonly IThumbnailCreationHostRuntime hostRuntime;

        public DefaultThumbnailCreateProcessLogWriter(IThumbnailCreationHostRuntime hostRuntime)
        {
            this.hostRuntime = hostRuntime ?? DefaultThumbnailCreationHostRuntime.Instance;
        }

        // host 既定の I/O 契約は、ディレクトリ自動作成・UTF-8(BOMなし)追記・空ファイル時のみヘッダー出力・例外非伝播とする。
        public void Write(ThumbnailCreateProcessLogEntry entry)
        {
            try
            {
                string logPath = global::IndigoMovieManager.Thumbnail.LogFileTimeWindowSeparator.PrepareForWrite(
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
                // ログ失敗で本体処理を止めない。
            }
        }

        public static string BuildCsvLine(ThumbnailCreateProcessLogEntry entry)
        {
            return BuildCsvLine(entry, DateTime.Now);
        }

        public static string BuildCsvLine(
            ThumbnailCreateProcessLogEntry entry,
            DateTime timestampLocal
        )
        {
            entry ??= new ThumbnailCreateProcessLogEntry();

            string durationText = FormatDurationValue(entry.DurationSec);
            string sizeText = FormatFileSizeValue(entry.FileSizeBytes);
            string movieFileName = Path.GetFileName(entry.MovieFullPath) ?? "";

            return string.Join(
                ",",
                EscapeCsvValue(
                    timestampLocal.ToString(TimestampFormat, CultureInfo.InvariantCulture)
                ),
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

        public static string FormatDurationValue(double? durationSec)
        {
            return durationSec.HasValue && durationSec.Value > 0
                ? durationSec.Value.ToString(DurationFormat, CultureInfo.InvariantCulture)
                : "";
        }

        public static string FormatFileSizeValue(long fileSizeBytes)
        {
            return fileSizeBytes > 0
                ? fileSizeBytes.ToString(CultureInfo.InvariantCulture)
                : ZeroFileSizeText;
        }

        public static string EscapeCsvValue(string value)
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
