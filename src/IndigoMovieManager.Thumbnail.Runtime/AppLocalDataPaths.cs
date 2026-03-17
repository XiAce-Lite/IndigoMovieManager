using System.IO;

namespace IndigoMovieManager
{
    // ローカル保存先の規約は queue や worker 固有ではないため、runtime 専用 project に寄せて境界を安定させる。
    public static class AppLocalDataPaths
    {
        public const string RootFolderName = "IndigoMovieManager_fork_workthree";
        public const string LogsFolderName = "logs";
        public const string QueueDbFolderName = "QueueDb";
        public const string FailureDbFolderName = "FailureDb";
        public const string RescueWorkerSessionsFolderName = "RescueWorkerSessions";

        public static string RootPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                RootFolderName
            );

        public static string LogsPath => Path.Combine(RootPath, LogsFolderName);

        public static string QueueDbPath => Path.Combine(RootPath, QueueDbFolderName);

        public static string FailureDbPath => Path.Combine(RootPath, FailureDbFolderName);

        public static string RescueWorkerSessionsPath =>
            Path.Combine(RootPath, RescueWorkerSessionsFolderName);
    }
}
