using System.IO;
using IndigoMovieManager.Thumbnail.QueueDb;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private readonly record struct MainDbSwitchContext(
            string CurrentDbFullPath,
            string TargetDbFullPath,
            MainDbSwitchSource Source
        );

        internal enum MainDbSwitchSource
        {
            New,
            OpenDialog,
            RecentMenu,
            StartupAutoOpen,
        }

        // MainDB切り替えの入口を1か所へ寄せ、保存順と成功後処理を揃える。
        private bool TrySwitchMainDb(string dbFullPath, MainDbSwitchSource source)
        {
            MainDbSwitchContext context = BuildMainDbSwitchContext(dbFullPath, source);
            if (string.IsNullOrWhiteSpace(context.TargetDbFullPath))
            {
                return false;
            }

            bool switchSucceeded = false;
            RunMainDbPreSwitch(context);
            try
            {
                if (!TryActivateMainDbSession(context))
                {
                    return false;
                }

                // 切替成功時だけセッション印を進め、旧DB向けQueueRequestをstale化する。
                _ = AdvanceCurrentMainDbQueueRequestSessionStamp();
                RunMainDbPostSwitch(context);
                switchSucceeded = true;
                return true;
            }
            finally
            {
                CompleteMainDbSwitchTransition(switchSucceeded);
            }
        }

        // 切り替え前の見た目保存とメニュー状態調整をまとめて扱う。
        private void RunMainDbPreSwitch(MainDbSwitchContext context)
        {
            if (ShouldCloseMainMenuBeforeDbSwitch(context.Source))
            {
                MenuToggleButton.IsChecked = false;
            }

            // 切替中は旧DB由来の投入を止め、成功後にだけ新セッションを再開する。
            SetThumbnailQueueInputEnabled(false);
            TryPersistCurrentDbViewStateBeforeSwitch(context);
        }

        // DB本体の切り替えはここでだけ行い、失敗時は後段へ進ませない。
        private bool TryActivateMainDbSession(MainDbSwitchContext context)
        {
            return OpenDatafile(context.TargetDbFullPath);
        }

        // open成功後のRecent/LastDoc更新をここへ集約する。
        private void RunMainDbPostSwitch(MainDbSwitchContext context)
        {
            TryDiscardPreviousDbPendingThumbnailQueueItems(context);

            if (ShouldUpdateRecentFilesOnSuccessfulDbSwitch(context.Source))
            {
                ReStackRecentTree(context.TargetDbFullPath);
            }

            if (ShouldRememberLastDocOnSuccessfulDbSwitch(context.Source))
            {
                Properties.Settings.Default.LastDoc = context.TargetDbFullPath;
                Properties.Settings.Default.Save();
            }
        }

        // 別DBへ切り替えた後は、旧QueueDBに積みっぱなしだった未着手pendingを掃除する。
        private void TryDiscardPreviousDbPendingThumbnailQueueItems(MainDbSwitchContext context)
        {
            if (
                !ShouldDiscardPreviousDbPendingThumbnailQueueItemsOnSuccessfulSwitch(
                    context.CurrentDbFullPath,
                    context.TargetDbFullPath
                )
            )
            {
                return;
            }

            string oldQueueDbPath = QueueDbPathResolver.ResolveQueueDbPath(context.CurrentDbFullPath);
            if (!Path.Exists(oldQueueDbPath))
            {
                return;
            }

            try
            {
                QueueDbService queueDbService = new(context.CurrentDbFullPath);
                int deleted = queueDbService.DeletePending();
                DebugRuntimeLog.Write(
                    "queue-ops",
                    $"switch pending discard: deleted={deleted} current='{context.CurrentDbFullPath}' target='{context.TargetDbFullPath}'"
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "queue-ops",
                    $"switch pending discard failed: current='{context.CurrentDbFullPath}' target='{context.TargetDbFullPath}' err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        // 切替成否に関わらず最後に投入を戻す。失敗時は旧セッションをそのまま継続する。
        private void CompleteMainDbSwitchTransition(bool switchSucceeded)
        {
            SetThumbnailQueueInputEnabled(true);
            if (!switchSucceeded)
            {
                RequestThumbnailProgressSnapshotRefresh();
            }
        }

        // UI起点の切り替えだけ、旧DBの見た目状態を切り替え前に保存する。
        private void TryPersistCurrentDbViewStateBeforeSwitch(MainDbSwitchContext context)
        {
            if (
                !ShouldPersistCurrentDbViewStateBeforeSwitch(
                    context.CurrentDbFullPath,
                    context.TargetDbFullPath,
                    context.Source
                )
            )
            {
                return;
            }

            try
            {
                UpdateSkin(context.CurrentDbFullPath);
                UpdateSort(context.CurrentDbFullPath);
                DebugRuntimeLog.Write(
                    "db",
                    $"pre-switch view state saved: current='{context.CurrentDbFullPath}' target='{context.TargetDbFullPath}' source={context.Source}"
                );
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "db",
                    $"pre-switch view state save failed: current='{context.CurrentDbFullPath}' target='{context.TargetDbFullPath}' source={context.Source} err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
        }

        private MainDbSwitchContext BuildMainDbSwitchContext(
            string targetDbFullPath,
            MainDbSwitchSource source
        )
        {
            return new MainDbSwitchContext(
                NormalizeMainDbPath(MainVM?.DbInfo?.DBFullPath ?? ""),
                NormalizeMainDbPath(targetDbFullPath),
                source
            );
        }

        internal static bool ShouldPersistCurrentDbViewStateBeforeSwitch(
            string currentDbFullPath,
            string targetDbFullPath,
            MainDbSwitchSource source
        )
        {
            if (
                source != MainDbSwitchSource.New
                && source != MainDbSwitchSource.OpenDialog
                && source != MainDbSwitchSource.RecentMenu
            )
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(currentDbFullPath))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetDbFullPath))
            {
                return false;
            }

            return !AreSameMainDbPath(currentDbFullPath, targetDbFullPath);
        }

        internal static bool ShouldUpdateRecentFilesOnSuccessfulDbSwitch(MainDbSwitchSource source)
        {
            return source != MainDbSwitchSource.StartupAutoOpen;
        }

        internal static bool ShouldRememberLastDocOnSuccessfulDbSwitch(MainDbSwitchSource source)
        {
            return source != MainDbSwitchSource.StartupAutoOpen;
        }

        internal static bool ShouldCloseMainMenuBeforeDbSwitch(MainDbSwitchSource source)
        {
            return source != MainDbSwitchSource.StartupAutoOpen;
        }

        internal static bool ShouldDiscardPreviousDbPendingThumbnailQueueItemsOnSuccessfulSwitch(
            string currentDbFullPath,
            string targetDbFullPath
        )
        {
            if (string.IsNullOrWhiteSpace(currentDbFullPath))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetDbFullPath))
            {
                return false;
            }

            return !AreSameMainDbPath(currentDbFullPath, targetDbFullPath);
        }

        // パス表記の揺れを吸収し、同じMainDBかどうかを安全側で判定する。
        internal static bool AreSameMainDbPath(string left, string right)
        {
            string normalizedLeft = NormalizeMainDbPath(left);
            string normalizedRight = NormalizeMainDbPath(right);
            if (string.IsNullOrWhiteSpace(normalizedLeft) || string.IsNullOrWhiteSpace(normalizedRight))
            {
                return false;
            }

            return string.Equals(
                normalizedLeft,
                normalizedRight,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static string NormalizeMainDbPath(string dbFullPath)
        {
            if (string.IsNullOrWhiteSpace(dbFullPath))
            {
                return "";
            }

            string normalized = dbFullPath.Trim().Trim('"');
            try
            {
                normalized = Path.GetFullPath(normalized);
            }
            catch
            {
                // 不正な文字を含む場合は元文字列比較へ落とす。
            }

            return normalized.Replace('/', '\\');
        }
    }
}
