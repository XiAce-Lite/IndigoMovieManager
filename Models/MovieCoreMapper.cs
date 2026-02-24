using System.Globalization;
using System.IO;
using System.Linq;

namespace IndigoMovieManager
{
    /// <summary>
    /// 既存モデルを MovieCore に寄せる変換口。
    /// 呼び出し側は段階的に MovieCore ベースへ移行できる。
    /// </summary>
    public static class MovieCoreMapper
    {
        public static MovieCore ToMovieCore(this MovieInfo source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return CloneFromCore(source);
        }

        public static MovieCore ToMovieCore(this MovieRecords source)
        {
            ArgumentNullException.ThrowIfNull(source);

            var movieName = source.Movie_Body;
            if (string.IsNullOrWhiteSpace(movieName))
            {
                movieName = Path.GetFileNameWithoutExtension(source.Movie_Name ?? "");
            }

            var tags = source.Tags;
            if (string.IsNullOrWhiteSpace(tags) && source.Tag is { Count: > 0 })
            {
                tags = string.Join(Environment.NewLine, source.Tag);
            }

            return new MovieCore
            {
                MovieId = source.Movie_Id,
                MovieName = movieName ?? "",
                // MoviePath は生パスを保持。MovieCore 側で正規化パスも同時更新される。
                MoviePath = source.Movie_Path ?? "",
                MovieLength = ParseLengthToSeconds(source.Movie_Length),
                // MovieRecords の Movie_Size はKB想定なので、コアではbyteに戻す。
                MovieSize = source.Movie_Size * 1024,
                LastDate = ParseDateTimeOrNow(source.Last_Date),
                FileDate = ParseDateTimeOrNow(source.File_Date),
                RegistDate = ParseDateTimeOrNow(source.Regist_Date),
                Score = source.Score,
                ViewCount = source.View_Count,
                Hash = source.Hash ?? "",
                Container = source.Container ?? "",
                Video = source.Video ?? "",
                Audio = source.Audio ?? "",
                Extra = source.Extra ?? "",
                Title = source.Title ?? "",
                Artist = source.Artist ?? "",
                Album = source.Album ?? "",
                Grouping = source.Grouping ?? "",
                Writer = source.Writer ?? "",
                Genre = source.Genre ?? "",
                Track = source.Track ?? "",
                Camera = source.Camera ?? "",
                CreateTime = source.Create_Time ?? "",
                Kana = source.Kana ?? "",
                Roma = source.Roma ?? "",
                Tags = tags ?? "",
                Comment1 = source.Comment1 ?? "",
                Comment2 = source.Comment2 ?? "",
                Comment3 = source.Comment3 ?? ""
            };
        }

        public static MovieRecords ToMovieRecords(this MovieCore source)
        {
            ArgumentNullException.ThrowIfNull(source);

            var record = new MovieRecords();
            record.ApplyMovieCore(source);
            return record;
        }

        public static void ApplyMovieCore(this MovieRecords target, MovieCore source)
        {
            // DB共通項目のみを更新する。
            // サムネイルパスや存在フラグなどのUI専用項目は変更しない。
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(source);

            target.Movie_Id = source.MovieId;
            target.Movie_Name = source.MovieName ?? "";
            target.Movie_Body = source.MovieName ?? "";
            // 生パスを復元しつつ、MovieRecords 側で正規化パスも同時更新する。
            target.Movie_Path = source.MoviePath ?? "";
            target.Movie_Length = FormatLengthFromSeconds(source.MovieLength);
            target.Movie_Size = source.MovieSize / 1024;
            target.Last_Date = FormatDateTime(source.LastDate);
            target.File_Date = FormatDateTime(source.FileDate);
            target.Regist_Date = FormatDateTime(source.RegistDate);
            target.Score = source.Score;
            target.View_Count = source.ViewCount;
            target.Hash = source.Hash ?? "";
            target.Container = source.Container ?? "";
            target.Video = source.Video ?? "";
            target.Audio = source.Audio ?? "";
            target.Extra = source.Extra ?? "";
            target.Title = source.Title ?? "";
            target.Artist = source.Artist ?? "";
            target.Album = source.Album ?? "";
            target.Grouping = source.Grouping ?? "";
            target.Writer = source.Writer ?? "";
            target.Genre = source.Genre ?? "";
            target.Track = source.Track ?? "";
            target.Camera = source.Camera ?? "";
            target.Create_Time = source.CreateTime ?? "";
            target.Kana = source.Kana ?? "";
            target.Roma = source.Roma ?? "";
            target.Tags = source.Tags ?? "";
            target.Tag = SplitTags(target.Tags);
            target.Comment1 = source.Comment1 ?? "";
            target.Comment2 = source.Comment2 ?? "";
            target.Comment3 = source.Comment3 ?? "";
        }

        private static MovieCore CloneFromCore(MovieCore source)
        {
            return new MovieCore
            {
                MovieId = source.MovieId,
                MovieName = source.MovieName,
                MoviePath = source.MoviePath,
                MovieLength = source.MovieLength,
                MovieSize = source.MovieSize,
                LastDate = source.LastDate,
                FileDate = source.FileDate,
                RegistDate = source.RegistDate,
                Score = source.Score,
                ViewCount = source.ViewCount,
                Hash = source.Hash,
                Container = source.Container,
                Video = source.Video,
                Audio = source.Audio,
                Extra = source.Extra,
                Title = source.Title,
                Artist = source.Artist,
                Album = source.Album,
                Grouping = source.Grouping,
                Writer = source.Writer,
                Genre = source.Genre,
                Track = source.Track,
                Camera = source.Camera,
                CreateTime = source.CreateTime,
                Kana = source.Kana,
                Roma = source.Roma,
                Tags = source.Tags,
                Comment1 = source.Comment1,
                Comment2 = source.Comment2,
                Comment3 = source.Comment3,
                FPS = source.FPS,
                TotalFrames = source.TotalFrames
            };
        }

        private static long ParseLengthToSeconds(string lengthText)
        {
            if (string.IsNullOrWhiteSpace(lengthText))
            {
                return 0;
            }

            if (TimeSpan.TryParseExact(lengthText, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out var fixedFormat))
            {
                return (long)fixedFormat.TotalSeconds;
            }

            if (TimeSpan.TryParse(lengthText, out var parsed))
            {
                return (long)parsed.TotalSeconds;
            }

            return 0;
        }

        private static DateTime ParseDateTimeOrNow(string value)
        {
            if (DateTime.TryParse(value, out var parsed))
            {
                return parsed;
            }

            return DateTime.Now;
        }

        private static string FormatLengthFromSeconds(long seconds)
        {
            if (seconds <= 0)
            {
                return "00:00:00";
            }

            return TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        private static string FormatDateTime(DateTime value)
        {
            return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static List<string> SplitTags(string tags)
        {
            if (string.IsNullOrWhiteSpace(tags))
            {
                return [];
            }

            return tags
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x != "")
                .Distinct()
                .ToList();
        }
    }
}
