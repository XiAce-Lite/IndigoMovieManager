using System.Text;

namespace IndigoMovieManager.Thumbnail.RescueWorker
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            // launcher 側は UTF-8 で stdout/stderr を読む前提なので、worker 側も明示的に揃える。
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            RescueWorkerApplication app = new();
            return await app.RunAsync(args).ConfigureAwait(false);
        }
    }
}
