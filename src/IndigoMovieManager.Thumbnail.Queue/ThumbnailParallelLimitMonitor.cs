namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 実行中バッチでも設定変更へ追従できるよう、並列上限ゲートを周期更新する。
    /// </summary>
    internal static class ThumbnailParallelLimitMonitor
    {
        public static async Task RunAsync(
            DynamicParallelGate parallelGate,
            Func<int> resolveConfiguredParallelism,
            ThumbnailParallelController parallelController,
            Action<string> log,
            CancellationToken cts
        )
        {
            if (
                parallelGate == null
                || resolveConfiguredParallelism == null
                || parallelController == null
            )
            {
                return;
            }

            int lastApplied = parallelGate.CurrentLimit;
            while (!cts.IsCancellationRequested)
            {
                int configured = resolveConfiguredParallelism();
                int next = parallelController.EnsureWithinConfigured(configured);
                parallelGate.SetLimit(next);
                int applied = parallelGate.CurrentLimit;
                if (applied != lastApplied)
                {
                    log?.Invoke($"parallel apply: {lastApplied} -> {applied} configured={configured}");
                    lastApplied = applied;
                }

                await Task.Delay(200, cts).ConfigureAwait(false);
            }
        }
    }
}
