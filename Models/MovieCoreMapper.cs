using System.Globalization;
using System.IO;
using System.Linq;

namespace IndigoMovieManager
{
    /// <summary>
    /// 既存の複数のデータモデル（MovieInfo, MovieRecords）間で共通コアである MovieCore に
    /// 値を安全に相互変換・適用する変換器（Mapper）
    /// </summary>
    public static class MovieCoreMapper
    {
        /// <summary>
        /// MovieInfo（ファイル直読みデータ）から共通コア(MovieCore)への変換を行う。
        /// データ構造が基本的に同じため、プロパティのディープコピー（CloneFromCore）を実行する。
        /// </summary>
        public static MovieCore ToMovieCore(this MovieInfo source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return CloneFromCore(source);
        }

        /// <summary>
        /// MovieRecords（UI用の表示特化データ）から共通コア(MovieCore)への変換を行う。
        /// 文字列型の時間や日付、サイズ(KB等)の単位など、UI向けに整形されているデータを
        /// DB保存や内部処理に適したネイティブな型（long, DateTime等）へ逆変換しながら詰め直す。
        /// </summary>
        public static MovieCore ToMovieCore(this MovieRecords source)
        {
            ArgumentNullException.ThrowIfNull(source);

            var movieName = source.Movie_Body;
            if (string.IsNullOrWhiteSpace(movieName))
            {
                movieName = Path.GetFileNameWithoutExtension(source.Movie_Name ?? "");
            }

            var tags = source.Tags;
            // 3. 配列形式のタグ(Tag)しかデータがない場合は、改行区切りの文字列(Tags)に結合する
            if (string.IsNullOrWhiteSpace(tags) && source.Tag is { Count: > 0 })
            {
                tags = string.Join(Environment.NewLine, source.Tag);
            }

            // 4. メインの詰め替え処理
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
                Comment3 = source.Comment3 ?? "",
            };
        }

        /// <summary>
        /// 共通コア(MovieCore)から、新しくUI用データ(MovieRecords)を生成して返す。
        /// 内部で ApplyMovieCore(詰め替え処理)を呼び出している。
        /// </summary>
        public static MovieRecords ToMovieRecords(this MovieCore source)
        {
            ArgumentNullException.ThrowIfNull(source);

            var record = new MovieRecords();
            record.ApplyMovieCore(source);
            return record;
        }

        /// <summary>
        /// 既存のUI用データ(MovieRecords)に対して、共通コア(MovieCore)の値を上書き適用する。
        /// DBに保存されている「コアな情報」のみを更新し、サムネイル画像のパスなど
        /// UI層専用の表示情報は破壊せずにそのまま維持する。
        /// </summary>
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
                TotalFrames = source.TotalFrames,
            };
        }

        /// <summary>
        /// 文字列型の再生時間（00:00:00 形式）を解析し、総秒数（long）へ変換する内部ヘルパー
        /// </summary>
        private static long ParseLengthToSeconds(string lengthText)
        {
            if (string.IsNullOrWhiteSpace(lengthText))
            {
                return 0;
            }

            if (
                TimeSpan.TryParseExact(
                    lengthText,
                    @"hh\:mm\:ss",
                    CultureInfo.InvariantCulture,
                    out var fixedFormat
                )
            )
            {
                return (long)fixedFormat.TotalSeconds;
            }

            if (TimeSpan.TryParse(lengthText, out var parsed))
            {
                return (long)parsed.TotalSeconds;
            }

            return 0;
        }

        /// <summary>
        /// 文字列型の日付を解析し、DateTime型へ変換する内部ヘルパー。
        /// 失敗した場合はフェールセーフとして現在時刻を返す。
        /// </summary>
        private static DateTime ParseDateTimeOrNow(string value)
        {
            if (DateTime.TryParse(value, out var parsed))
            {
                return parsed;
            }

            return DateTime.Now;
        }

        /// <summary>
        /// 総秒数（long）から、UI表示用の文字列型の再生時間（00:00:00 形式）へ変換する内部ヘルパー
        /// </summary>
        private static string FormatLengthFromSeconds(long seconds)
        {
            if (seconds <= 0)
            {
                return "00:00:00";
            }

            return TimeSpan
                .FromSeconds(seconds)
                .ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// DateTime型から、UI表示用の標準的な日付文字列フォーマットへ変換する内部ヘルパー
        /// </summary>
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

            return tags.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x != "")
                .Distinct()
                .ToList();
        }
    }
}
