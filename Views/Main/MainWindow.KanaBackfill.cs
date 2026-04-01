using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using IndigoMovieManager.DB;

namespace IndigoMovieManager
{
    public partial class MainWindow
    {
        private const int KanaBackfillBatchSize = 100;
        private const int KanaBackfillInterBatchDelayMs = 50;

        private CancellationTokenSource _kanaBackfillCts = new();
        private Task _kanaBackfillTask;

        private void StartKanaBackfillIfNeeded(string trigger)
        {
            string dbPath = MainVM?.DbInfo?.DBFullPath ?? "";
            if (string.IsNullOrWhiteSpace(dbPath))
            {
                return;
            }

            if (_kanaBackfillTask != null && !_kanaBackfillTask.IsCompleted)
            {
                return;
            }

            _kanaBackfillCts.Dispose();
            _kanaBackfillCts = new CancellationTokenSource();
            DebugRuntimeLog.TaskStart(nameof(RunKanaBackfillAsync), $"trigger={trigger} db='{dbPath}'");
            _kanaBackfillTask = RunKanaBackfillAsync(dbPath, _kanaBackfillCts.Token);
        }

        private void CancelKanaBackfill(string reason)
        {
            try
            {
                if (_kanaBackfillCts.IsCancellationRequested)
                {
                    return;
                }

                DebugRuntimeLog.Write("kana", $"backfill cancel requested: reason={reason}");
                _kanaBackfillCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 終了処理中の多重停止は静かに吸収する。
            }
        }

        private async Task RunKanaBackfillAsync(string dbPath, CancellationToken cancellationToken)
        {
            int totalMovieUpdated = 0;
            int totalBookmarkUpdated = 0;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    List<KanaBackfillTarget> movieTargets = await Task.Run(
                        () => SQLite.ReadMovieKanaBackfillTargets(dbPath, KanaBackfillBatchSize),
                        cancellationToken
                    );
                    List<KanaBackfillTarget> bookmarkTargets = await Task.Run(
                        () => SQLite.ReadBookmarkKanaBackfillTargets(dbPath, KanaBackfillBatchSize),
                        cancellationToken
                    );

                    if (movieTargets.Count < 1 && bookmarkTargets.Count < 1)
                    {
                        break;
                    }

                    List<KanaBackfillUpdate> movieUpdates = BuildKanaUpdates(movieTargets);
                    List<KanaBackfillUpdate> bookmarkUpdates = BuildKanaUpdates(bookmarkTargets);

                    if (movieUpdates.Count > 0)
                    {
                        totalMovieUpdated += await Task.Run(
                            () => SQLite.UpdateMovieKanaBatch(dbPath, movieUpdates),
                            cancellationToken
                        );
                    }

                    if (bookmarkUpdates.Count > 0)
                    {
                        totalBookmarkUpdated += await Task.Run(
                            () => SQLite.UpdateBookmarkKanaBatch(dbPath, bookmarkUpdates),
                            cancellationToken
                        );
                    }

                    await Dispatcher.InvokeAsync(
                        () => ApplyKanaBackfillToUi(movieUpdates, bookmarkUpdates),
                        DispatcherPriority.Background,
                        cancellationToken
                    );

                    await Task.Delay(KanaBackfillInterBatchDelayMs, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                DebugRuntimeLog.Write("kana", $"backfill canceled: db='{dbPath}'");
            }
            catch (Exception ex)
            {
                DebugRuntimeLog.Write(
                    "kana",
                    $"backfill failed: db='{dbPath}' err='{ex.GetType().Name}: {ex.Message}'"
                );
            }
            finally
            {
                DebugRuntimeLog.TaskEnd(
                    nameof(RunKanaBackfillAsync),
                    $"db='{dbPath}' movie_updated={totalMovieUpdated} bookmark_updated={totalBookmarkUpdated}"
                );
            }
        }

        private static List<KanaBackfillUpdate> BuildKanaUpdates(
            IReadOnlyList<KanaBackfillTarget> targets
        )
        {
            List<KanaBackfillUpdate> updates = [];
            if (targets == null || targets.Count < 1)
            {
                return updates;
            }

            foreach (KanaBackfillTarget target in targets)
            {
                string kana = JapaneseKanaProvider.GetKana(target.MovieName, target.MoviePath);
                if (string.IsNullOrWhiteSpace(kana))
                {
                    continue;
                }

                updates.Add(new KanaBackfillUpdate(target.MovieId, kana));
            }

            return updates;
        }

        private void ApplyKanaBackfillToUi(
            IReadOnlyList<KanaBackfillUpdate> movieUpdates,
            IReadOnlyList<KanaBackfillUpdate> bookmarkUpdates
        )
        {
            Dictionary<long, string> movieKanaMap = movieUpdates?
                .GroupBy(x => x.MovieId)
                .ToDictionary(x => x.Key, x => x.Last().Kana) ?? new Dictionary<long, string>();
            Dictionary<long, string> bookmarkKanaMap = bookmarkUpdates?
                .GroupBy(x => x.MovieId)
                .ToDictionary(x => x.Key, x => x.Last().Kana) ?? new Dictionary<long, string>();

            if (movieKanaMap.Count > 0)
            {
                foreach (MovieRecords item in MainVM.MovieRecs)
                {
                    if (movieKanaMap.TryGetValue(item.Movie_Id, out string kana))
                    {
                        item.Kana = kana;
                    }
                }

                foreach (MovieRecords item in MainVM.FilteredMovieRecs)
                {
                    if (movieKanaMap.TryGetValue(item.Movie_Id, out string kana))
                    {
                        item.Kana = kana;
                    }
                }
            }

            if (bookmarkKanaMap.Count > 0)
            {
                foreach (MovieRecords item in MainVM.BookmarkRecs)
                {
                    if (bookmarkKanaMap.TryGetValue(item.Movie_Id, out string kana))
                    {
                        item.Kana = kana;
                    }
                }
            }

            if (MainVM?.DbInfo?.Sort is "10" or "11")
            {
                FilterAndSort(MainVM.DbInfo.Sort, true);
            }
        }
    }
}
