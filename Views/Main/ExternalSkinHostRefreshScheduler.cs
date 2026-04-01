using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace IndigoMovieManager
{
    /// <summary>
    /// 外部 skin host の refresh 要求を 1 本の列へ畳み、古い途中経過より最新要求を優先する。
    /// MainWindow 側は「何を refresh するか」だけに集中し、直列化の責務をここへ寄せる。
    /// </summary>
    internal sealed class ExternalSkinHostRefreshScheduler
    {
        private readonly Dispatcher dispatcher;
        private readonly Func<int, string, Task> refreshAsync;
        private readonly Action<Exception> onDrainFailed;
        private bool isRefreshRunning;
        private bool isRefreshPending;
        private int currentGeneration;
        private string pendingReason = "";

        internal ExternalSkinHostRefreshScheduler(
            Dispatcher dispatcher,
            Func<int, string, Task> refreshAsync,
            Action<Exception> onDrainFailed
        )
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.refreshAsync = refreshAsync ?? throw new ArgumentNullException(nameof(refreshAsync));
            this.onDrainFailed = onDrainFailed ?? (_ => { });
        }

        internal int CurrentGeneration => currentGeneration;

        internal void Queue(string reason)
        {
            isRefreshPending = true;
            pendingReason = reason ?? "";
            currentGeneration++;
            if (isRefreshRunning)
            {
                return;
            }

            isRefreshRunning = true;
            _ = dispatcher.BeginInvoke(
                new Action(async () => await DrainAsync()),
                DispatcherPriority.Background
            );
        }

        private async Task DrainAsync()
        {
            try
            {
                while (isRefreshPending)
                {
                    isRefreshPending = false;

                    int generation = currentGeneration;
                    string reason = pendingReason;
                    await refreshAsync(generation, reason);
                }
            }
            catch (Exception ex)
            {
                onDrainFailed(ex);
            }
            finally
            {
                isRefreshRunning = false;
                if (isRefreshPending)
                {
                    Queue(pendingReason);
                }
            }
        }
    }
}
