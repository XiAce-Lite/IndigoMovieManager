using System.Data.SQLite;
using System.Drawing;
using System.Globalization;
using IndigoMovieManager;
using IndigoMovieManager.Thumbnail.FailureDb;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    internal sealed partial class RescueWorkerApplication
    {
        // MainDBへは書き込まず、systemテーブルの読み取りだけを許容する。
        private static MainDbContext ResolveMainDbContext(
            string mainDbFullPath,
            string thumbFolderOverride
        )
        {
            string dbName = Path.GetFileNameWithoutExtension(mainDbFullPath) ?? "";
            string thumbFolder = NormalizeThumbFolderPath(thumbFolderOverride);

            if (!string.IsNullOrWhiteSpace(thumbFolder))
            {
                return new MainDbContext(dbName, thumbFolder);
            }

            using SQLiteConnection connection = CreateReadOnlyMainDbConnection(mainDbFullPath);
            connection.Open();
            using SQLiteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM system WHERE attr = 'thum' LIMIT 1;";
            object value = command.ExecuteScalar();
            thumbFolder = NormalizeThumbFolderPath(Convert.ToString(value) ?? "");

            if (string.IsNullOrWhiteSpace(thumbFolder))
            {
                thumbFolder = NormalizeThumbFolderPath(
                    Path.Combine(AppContext.BaseDirectory, "Thumb", dbName)
                );
            }

            return new MainDbContext(dbName, thumbFolder);
        }

        // rescue worker は mainDB を読むだけなので、読取専用で開いて余計なロックを増やさない。
        private static SQLiteConnection CreateReadOnlyMainDbConnection(string mainDbFullPath)
        {
            SQLiteConnectionStringBuilder builder = new()
            {
                DataSource = mainDbFullPath,
                FailIfMissing = true,
                ReadOnly = true,
            };

            return new SQLiteConnection(builder.ToString());
        }

        // launcher から渡された絶対パスを優先し、相対パスも worker 側で固定化してぶらさない。
        private static string NormalizeThumbFolderPath(string thumbFolder)
        {
            if (string.IsNullOrWhiteSpace(thumbFolder))
            {
                return "";
            }

            string normalized = thumbFolder.Trim();
            if (Path.IsPathRooted(normalized))
            {
                return Path.GetFullPath(normalized);
            }

            return Path.GetFullPath(normalized, AppContext.BaseDirectory);
        }

        private static ThumbnailLayoutProfile ResolveLayoutProfile(int tabIndex)
        {
            // 詳細タブの実行時モードまで含めて、worker 側のレイアウト依存をここへ集約する。
            return ThumbnailLayoutProfileResolver.Resolve(
                tabIndex,
                ThumbnailDetailModeRuntime.ReadRuntimeMode()
            );
        }

        private static string ResolveOutPath(int tabIndex, string dbName, string thumbFolder)
        {
            ThumbnailLayoutProfile layoutProfile = ResolveLayoutProfile(tabIndex);
            string thumbRoot = string.IsNullOrWhiteSpace(thumbFolder)
                ? ThumbRootResolver.GetDefaultThumbRoot(dbName)
                : thumbFolder;
            return layoutProfile.BuildOutPath(thumbRoot);
        }

        private static void DeleteStaleErrorMarker(string thumbFolder, int tabIndex, string moviePath)
        {
            try
            {
                string errorMarkerPath = ThumbnailPathResolver.BuildErrorMarkerPath(
                    ResolveOutPath(tabIndex, "", thumbFolder),
                    moviePath
                );
                if (File.Exists(errorMarkerPath))
                {
                    File.Delete(errorMarkerPath);
                }
            }
            catch
            {
                // エラーマーカー掃除に失敗しても救済本体を優先する。
            }
        }

        // 既に正常jpgがある個体は再救済せず、古い pending_rescue を整理する。
        internal static bool TryFindExistingSuccessThumbnailPath(
            string thumbFolder,
            int tabIndex,
            string moviePath,
            out string successThumbnailPath
        )
        {
            successThumbnailPath = "";
            if (string.IsNullOrWhiteSpace(thumbFolder) || string.IsNullOrWhiteSpace(moviePath))
            {
                return false;
            }

            string outPath = ResolveOutPath(tabIndex, "", thumbFolder);
            return ThumbnailPathResolver.TryFindExistingSuccessThumbnailPath(
                outPath,
                moviePath,
                out successThumbnailPath
            );
        }

        // 明示救済フラグ付きかつWB互換メタ欠落の時だけ、既存jpgありでも再生成へ進める。
        internal static bool ShouldReplaceExistingSuccessThumbnailWhenMetadataMissing(
            string extraJson,
            string existingSuccessThumbnailPath
        )
        {
            return ShouldReplaceThumbnailWhenMetadataMissing(extraJson)
                && !HasWhiteBrowserThumbnailMetadata(existingSuccessThumbnailPath);
        }

        // manual 差し替えやクリック再生が依存するWB互換メタの有無だけを軽く判定する。
        internal static bool HasWhiteBrowserThumbnailMetadata(string thumbnailPath)
        {
            return WhiteBrowserThumbInfoSerializer.TryReadFromJpeg(
                    thumbnailPath,
                    out ThumbnailSheetSpec spec
                )
                && spec?.CaptureSeconds != null
                && spec.CaptureSeconds.Count > 0;
        }

        // 救済workerは最後の失敗文言から repair 入口を決めるため、語彙漏れはここで吸収する。
        private static long TryGetMovieFileLength(string moviePath)
        {
            try
            {
                return new FileInfo(moviePath).Length;
            }
            catch
            {
                return 0;
            }
        }

        private static string BuildRepairOutputPath(string moviePath, bool preserveNamedCopy = false)
        {
            string repairRoot = preserveNamedCopy
                ? Path.GetDirectoryName(moviePath ?? "") ?? ""
                : Path.Combine(
                    Path.GetTempPath(),
                    AppIdentityRuntime.ResolveStorageRootName(),
                    "thumbnail-repair"
                );
            if (string.IsNullOrWhiteSpace(repairRoot))
            {
                repairRoot = Path.Combine(
                    Path.GetTempPath(),
                    AppIdentityRuntime.ResolveStorageRootName(),
                    "thumbnail-repair"
                );
            }
            Directory.CreateDirectory(repairRoot);

            string extension = Path.GetExtension(moviePath ?? "");
            string normalizedExtension =
                string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".mkv", StringComparison.OrdinalIgnoreCase)
                    ? extension
                    : ".mkv";
            if (!preserveNamedCopy)
            {
                return Path.Combine(repairRoot, $"{Guid.NewGuid():N}_repair{normalizedExtension}");
            }

            string movieBody = Path.GetFileNameWithoutExtension(moviePath ?? "") ?? "repair";
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff", CultureInfo.InvariantCulture);
            return Path.Combine(repairRoot, $"{movieBody}_repair_{stamp}{normalizedExtension}");
        }

        // 救済worker でも真っ黒jpgは成功扱いにせず、次の勝ち筋へ進める。
        internal static bool TryRejectNearBlackOutput(
            string outputThumbPath,
            out string failureReason
        )
        {
            failureReason = "";
            if (string.IsNullOrWhiteSpace(outputThumbPath) || !File.Exists(outputThumbPath))
            {
                return false;
            }

            if (!IsNearBlackImageFile(outputThumbPath, out double averageLuma))
            {
                return false;
            }

            failureReason = $"near-black thumbnail rejected: avg_luma={averageLuma:0.##}";
            try
            {
                File.Delete(outputThumbPath);
            }
            catch
            {
                // 黒jpgの削除失敗よりも、次のengineへ進めることを優先する。
            }

            return true;
        }

        internal static bool IsNearBlackFailureReason(string failureReason)
        {
            return !string.IsNullOrWhiteSpace(failureReason)
                && failureReason.IndexOf(
                    "near-black thumbnail rejected",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
        }

        internal static bool IsNearBlackImageFile(string imagePath, out double averageLuma)
        {
            averageLuma = 0d;
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                return false;
            }

            try
            {
                using Bitmap bitmap = new(imagePath);
                return IsNearBlackBitmap(bitmap, out averageLuma);
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsNearBlackBitmap(Bitmap source, out double averageLuma)
        {
            averageLuma = 0d;
            if (source == null || source.Width < 1 || source.Height < 1)
            {
                return false;
            }

            double sum = 0d;
            int count = 0;
            for (int y = 0; y < source.Height; y += NearBlackThumbnailSampleStep)
            {
                for (int x = 0; x < source.Width; x += NearBlackThumbnailSampleStep)
                {
                    Color pixel = source.GetPixel(x, y);
                    sum += (0.2126d * pixel.R) + (0.7152d * pixel.G) + (0.0722d * pixel.B);
                    count++;
                }
            }

            if (count < 1)
            {
                return false;
            }

            averageLuma = sum / count;
            return averageLuma <= NearBlackThumbnailLumaThreshold;
        }

        internal static bool IsFailurePlaceholderSuccess(ThumbnailCreateResult result)
        {
            if (result == null || !result.IsSuccess)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(result.ProcessEngineId)
                && result.ProcessEngineId.StartsWith("placeholder-", StringComparison.OrdinalIgnoreCase);
        }

        // attempt_failed の kind が Unknown に寄りすぎると束読みが鈍るため、
        // rescue worker 側でよく出る文言はここで先に failure kind へ寄せる。
        internal static ThumbnailFailureKind ResolveFailureKind(
            Exception ex,
            string moviePath,
            string failureReasonOverride = ""
        )
        {
            return ThumbnailRescueHandoffPolicy.ResolveFailureKind(
                ex,
                moviePath,
                failureReasonOverride
            );
        }
    }
}
