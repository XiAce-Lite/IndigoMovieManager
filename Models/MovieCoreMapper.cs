using System.Globalization;
using System.IO;
using System.Linq;
using static IndigoMovieManager.DB.SQLite;

namespace IndigoMovieManager
{
    /// <summary>
    /// 古き良きモデル(MovieInfo, MovieRecords)と新世代コア(MovieCore)の架け橋！🌈
    /// データの形を自在に操る、安全第一の超絶トランスフォーマー（変換器）だ！🤖✨
    /// </summary>
    public static class MovieCoreMapper
    {
        /// <summary>
        /// 獲れたて新鮮なファイル直読みデータ(MovieInfo)を、洗練された共通コア(MovieCore)へと昇華させるぜ！✨
        /// 構造が似てるから、得意のディープコピー魔法（CloneFromCore）で一瞬にして複製だ！🪄
        /// </summary>
        public static MovieCore ToMovieCore(this MovieInfo source)
        {
            ArgumentNullException.ThrowIfNull(source);

            return CloneFromCore(source);
        }

        /// <summary>
        /// 見た目重視のUI用データ(MovieRecords)を、中身重視の共通コア(MovieCore)へ逆変換（リバース）！🔄
        /// 文字列化された時間やKB単位のサイズを、DBが喜ぶネイティブ型（long, DateTime）へとゴリゴリ詰め直すぜ！💪
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
        /// 共通コア(MovieCore)の素材から、画面を彩るUI用データ(MovieRecords)を新規爆誕させるファクトリー！🏭✨
        /// 内部ではApplyMovieCore職人がバッチリ仕事をしてるぜ！
        /// </summary>
        public static MovieRecords ToMovieRecords(this MovieCore source)
        {
            ArgumentNullException.ThrowIfNull(source);

            var record = new MovieRecords();
            record.ApplyMovieCore(source);
            return record;
        }

        /// <summary>
        /// 既存のUIデータ(MovieRecords)に、最新の共通コア(MovieCore)の魂を上書き注入！💉🔥
        /// 大事なUI専用情報（サムネパス等）は一切傷つけず、コアな情報だけを的確にアップデートする神がかりな職人技！
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
        /// 「00:00:00」形式の文字列を、絶対的な総秒数（long）へと還元してやる内部の裏方マシーン！⚙️
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
        /// ただの文字列を本物のDateTime型に覚醒させる！もし失敗しても「今（現在時刻）」を返す無敵のフェールセーフ付きだ！🛡️
        /// </summary>
        private static DateTime ParseDateTimeOrNow(string value)
        {
            if (TryParseDbDateTimeText(value, out var parsed))
            {
                return parsed;
            }

            return DateTime.Now;
        }

        /// <summary>
        /// 無骨な総秒数（long）を着飾って、画面映えする「00:00:00」形式のドレスに変えるぜ！👗✨
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
        /// 生硬なDateTime型を、UI表示にピッタリな美しい標準フォーマットへと整形するスタイリスト！✂️
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
