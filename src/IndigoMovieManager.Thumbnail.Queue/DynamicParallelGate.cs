namespace IndigoMovieManager.Thumbnail
{
    // 並列数の上限を動的に調整する軽量ゲート。
    // ForEach自体は最大プールで回し、実行許可数だけここで制御する。
    internal sealed class DynamicParallelGate
    {
        private readonly object syncRoot = new();
        private readonly SemaphoreSlim semaphore;
        private readonly int maxLimit;
        private int targetLimit;
        private int pendingReduction;

        public DynamicParallelGate(int initialLimit, int maxLimit)
        {
            this.maxLimit = maxLimit < 1 ? 1 : maxLimit;
            int clampedInitial = initialLimit;
            if (clampedInitial < 1)
            {
                clampedInitial = 1;
            }

            if (clampedInitial > this.maxLimit)
            {
                clampedInitial = this.maxLimit;
            }

            targetLimit = clampedInitial;
            semaphore = new SemaphoreSlim(clampedInitial, this.maxLimit);
        }

        public int CurrentLimit
        {
            get
            {
                lock (syncRoot)
                {
                    return targetLimit;
                }
            }
        }

        public async Task WaitAsync(CancellationToken cts)
        {
            await semaphore.WaitAsync(cts).ConfigureAwait(false);
        }

        public void Release()
        {
            lock (syncRoot)
            {
                if (pendingReduction > 0)
                {
                    pendingReduction--;
                    return;
                }
            }

            semaphore.Release();
        }

        public void SetLimit(int requestedLimit)
        {
            int clamped = requestedLimit;
            if (clamped < 1)
            {
                clamped = 1;
            }

            if (clamped > maxLimit)
            {
                clamped = maxLimit;
            }

            lock (syncRoot)
            {
                if (clamped == targetLimit)
                {
                    return;
                }

                if (clamped > targetLimit)
                {
                    int deltaUp = clamped - targetLimit;
                    targetLimit = clamped;

                    int consumePending = Math.Min(deltaUp, pendingReduction);
                    pendingReduction -= consumePending;
                    int releaseCount = deltaUp - consumePending;
                    if (releaseCount > 0)
                    {
                        semaphore.Release(releaseCount);
                    }
                    return;
                }

                int deltaDown = targetLimit - clamped;
                targetLimit = clamped;
                pendingReduction += deltaDown;
                while (pendingReduction > 0 && semaphore.Wait(0))
                {
                    pendingReduction--;
                }
            }
        }
    }
}
