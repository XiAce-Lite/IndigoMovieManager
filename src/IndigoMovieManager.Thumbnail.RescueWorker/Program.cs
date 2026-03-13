namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            RescueWorkerApplication app = new();
            return await app.RunAsync(args).ConfigureAwait(false);
        }
    }
}
