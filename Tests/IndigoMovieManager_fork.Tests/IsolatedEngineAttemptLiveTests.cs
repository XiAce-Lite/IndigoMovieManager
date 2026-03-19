using System.Diagnostics;
using System.Text.Json;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[NonParallelizable]
public sealed class IsolatedEngineAttemptLiveTests
{
    private const string InputMovieEnvName = "IMM_ISOLATED_ATTEMPT_LIVE_INPUT";
    private const string EngineEnvName = "IMM_ISOLATED_ATTEMPT_LIVE_ENGINE";
    private const string ThumbSecCsvEnvName = "IMM_ISOLATED_ATTEMPT_LIVE_THUMB_SEC_CSV";
    private const string TimeoutSecEnvName = "IMM_ISOLATED_ATTEMPT_LIVE_TIMEOUT_SEC";
    private const string TabIndexEnvName = "IMM_ISOLATED_ATTEMPT_LIVE_TAB_INDEX";
    private const string WorkerExeEnvName = "IMM_ISOLATED_ATTEMPT_LIVE_WORKER_EXE";

    [Test]
    public async Task Live_attempt_childで秒位置固定の単一engine実行を確認する()
    {
        string moviePath = Environment.GetEnvironmentVariable(InputMovieEnvName)?.Trim().Trim('"') ?? "";
        if (string.IsNullOrWhiteSpace(moviePath) || !File.Exists(moviePath))
        {
            Assert.Ignore($"{InputMovieEnvName} に存在する動画ファイルを設定してください。");
            return;
        }

        string engineId = Environment.GetEnvironmentVariable(EngineEnvName)?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(engineId))
        {
            Assert.Ignore($"{EngineEnvName} に engine id を設定してください。");
            return;
        }

        string thumbSecCsv = Environment.GetEnvironmentVariable(ThumbSecCsvEnvName)?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(thumbSecCsv))
        {
            Assert.Ignore($"{ThumbSecCsvEnvName} に capture 秒を設定してください。例: 2159");
            return;
        }

        string workerExePath = ResolveWorkerExePath();
        if (string.IsNullOrWhiteSpace(workerExePath) || !File.Exists(workerExePath))
        {
            Assert.Ignore("RescueWorker exe が見つかりません。");
            return;
        }

        int tabIndex = ResolveTabIndex();
        int timeoutSec = ResolveTimeoutSec();
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_fork_isolated_attempt_live",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(tempRoot);

        try
        {
            string thumbRoot = Path.Combine(tempRoot, "thumb");
            string resultJsonPath = Path.Combine(tempRoot, "result.json");
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IndigoMovieManager_fork_workthree",
                "logs"
            );
            Directory.CreateDirectory(thumbRoot);
            Directory.CreateDirectory(logDir);

            string[] args =
            [
                "--attempt-child",
                "--engine",
                engineId,
                "--movie",
                moviePath,
                "--db-name",
                "isolated-live",
                "--thumb-folder",
                thumbRoot,
                "--tab-index",
                tabIndex.ToString(),
                "--movie-size-bytes",
                new FileInfo(moviePath).Length.ToString(),
                "--thumb-sec-csv",
                thumbSecCsv,
                "--log-dir",
                logDir,
                "--result-json",
                resultJsonPath,
            ];

            using Process process = new();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = workerExePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string arg in args)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            Stopwatch sw = Stopwatch.StartNew();
            _ = process.Start();
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            Task waitTask = process.WaitForExitAsync();
            Task completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(timeoutSec)));
            if (completed != waitTask)
            {
                TryKillProcessTree(process);
                string timeoutMessage =
                    $"isolated live attempt timeout: engine={engineId} thumb_sec_csv='{thumbSecCsv}' timeout_sec={timeoutSec}";
                TestContext.Out.WriteLine(timeoutMessage);
                Assert.Fail(timeoutMessage);
            }

            sw.Stop();
            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            IsolatedAttemptResultPayload? payload = TryReadResult(resultJsonPath);

            TestContext.Out.WriteLine(
                $"isolated live attempt: engine={engineId} tab={tabIndex} thumb_sec_csv='{thumbSecCsv}' elapsed_ms={sw.ElapsedMilliseconds} exit_code={process.ExitCode}"
            );
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                TestContext.Out.WriteLine($"stdout: {stdout.Trim()}");
            }
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                TestContext.Out.WriteLine($"stderr: {stderr.Trim()}");
            }
            if (payload != null)
            {
                TestContext.Out.WriteLine(
                    $"result: success={payload.IsSuccess} output='{payload.SaveThumbFileName}' error='{payload.ErrorMessage}' duration_sec={payload.DurationSec}"
                );
            }

            Assert.That(payload, Is.Not.Null, "result.json が生成されていません。");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch
                {
                    // live 実行の後始末は best effort でよい。
                }
            }
        }
    }

    private static string ResolveWorkerExePath()
    {
        string explicitPath = Environment.GetEnvironmentVariable(WorkerExeEnvName)?.Trim().Trim('"') ?? "";
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        string root = TestContext.CurrentContext.TestDirectory;
        string[] candidates =
        [
            Path.GetFullPath(
                Path.Combine(
                    root,
                    "..",
                    "..",
                    "..",
                    "..",
                    "artifacts",
                    "rescue-worker",
                    "publish",
                    "Release-win-x64",
                    "IndigoMovieManager.Thumbnail.RescueWorker.exe"
                )
            ),
            Path.GetFullPath(
                Path.Combine(
                    root,
                    "..",
                    "..",
                    "..",
                    "..",
                    "src",
                    "IndigoMovieManager.Thumbnail.RescueWorker",
                    "bin",
                    "x64",
                    "Debug",
                    "net8.0-windows",
                    "IndigoMovieManager.Thumbnail.RescueWorker.exe"
                )
            ),
        ];

        return candidates.FirstOrDefault(File.Exists) ?? "";
    }

    private static int ResolveTabIndex()
    {
        string raw = Environment.GetEnvironmentVariable(TabIndexEnvName)?.Trim() ?? "";
        if (int.TryParse(raw, out int parsed))
        {
            return parsed;
        }

        // 秒位置固定の単発検証では 1x1 を既定にする。
        return 99;
    }

    private static int ResolveTimeoutSec()
    {
        string raw = Environment.GetEnvironmentVariable(TimeoutSecEnvName)?.Trim() ?? "";
        if (int.TryParse(raw, out int parsed))
        {
            return Math.Clamp(parsed, 15, 1800);
        }

        return 120;
    }

    private static IsolatedAttemptResultPayload? TryReadResult(string resultJsonPath)
    {
        if (!File.Exists(resultJsonPath))
        {
            return null;
        }

        string json = File.ReadAllText(resultJsonPath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<IsolatedAttemptResultPayload>(json);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // timeout 時は best effort で十分。
        }
    }

    private sealed class IsolatedAttemptResultPayload
    {
        public string SaveThumbFileName { get; set; } = "";
        public double? DurationSec { get; set; }
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; } = "";
    }
}
