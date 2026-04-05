using IndigoReleaseManager.Models;
using IndigoReleaseManager.Services;
using IndigoReleaseManager.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Windows;

namespace IndigoReleaseManager;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();
    private readonly string _publicRepoPath = ResolvePublicRepositoryPath();
    private string _privateRepoPath = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "source", "repos", "IndigoMovieEngine"));

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshEnvironmentAsync();
    }

    private async void RefreshEnvironment_Click(object sender, RoutedEventArgs e)
    {
        await RefreshEnvironmentAsync();
    }

    private async Task RefreshEnvironmentAsync()
    {
        await ExecuteGuardedAsync("環境情報を再取得しています。", async () =>
        {
            var snapshot = await LocalEnvironmentService.LoadAsync(_publicRepoPath, _privateRepoPath);
            _viewModel.ApplyEnvironment(snapshot);
            if (!string.IsNullOrWhiteSpace(snapshot.PrivateRepository.LocalPath))
            {
                _privateRepoPath = snapshot.PrivateRepository.LocalPath;
            }
            _viewModel.StatusMessage = "環境情報を更新しました。";
            _viewModel.AppendLog("Public / Private repo の current state を再取得しました。");
        });
    }

    private async void RunPrivateRelease_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.Version))
        {
            MessageBox.Show(this, "Version を入力してください。", "IndigoReleaseManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await ExecuteGuardedAsync("Private local build / publish / pack を実行しています。", async () =>
        {
            var prepareResult = await RunPrivateScriptAsync(
                "invoke_private_engine_prepare.ps1",
                new[]
                {
                    "-Configuration", "Release",
                    "-Runtime", "win-x64",
                    "-VersionLabel", _viewModel.Version,
                    "-PackageVersion", _viewModel.Version
                });
            EnsureSuccess(prepareResult, "Private local prepare に失敗しました。");

            var syncResult = await RunPublicScriptAsync(
                "sync_private_engine_local_outputs.ps1",
                new[]
                {
                    "-PrivateRepoPath", _privateRepoPath
                });
            EnsureSuccess(syncResult, "Private local output 同期に失敗しました。");

            _viewModel.LastPrivateOutputPath = ExtractSummaryPath(syncResult.Output, "workerDestination:")
                ?? Path.Combine(_publicRepoPath, "artifacts", "rescue-worker", "publish", "Release-win-x64");
            _viewModel.StatusMessage = "Private local prepare と Public prepared dir 同期が完了しました。";
        });
    }

    private async void RunPublicPreview_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteGuardedAsync("Public preview を実行しています。", async () =>
        {
            var arguments = new List<string>
            {
                "-Owner", _viewModel.PublicOwnerAccount,
                "-Repository", _viewModel.PublicRepositoryName,
                "-WorkflowFileName", "github-release-package.yml",
                "-Ref", _viewModel.PreviewRef,
                "-Wait"
            };

            if (!string.IsNullOrWhiteSpace(_viewModel.PrivateEngineRunId))
            {
                arguments.Add("-PrivateEngineRunId");
                arguments.Add(_viewModel.PrivateEngineRunId);
            }

            if (!string.IsNullOrWhiteSpace(_viewModel.PrivateEngineReleaseTag))
            {
                arguments.Add("-PrivateEngineReleaseTag");
                arguments.Add(_viewModel.PrivateEngineReleaseTag);
            }

            var result = await RunPublicScriptAsync("invoke_github_release_preview.ps1", arguments);
            EnsureSuccess(result, "Public preview に失敗しました。");

            _viewModel.LastPreviewUrl = result.Urls.FirstOrDefault() ?? _viewModel.LastPreviewUrl;
            _viewModel.StatusMessage = "Public preview が成功しました。";
        });
    }

    private async void RunPublicRelease_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_viewModel.Version))
        {
            MessageBox.Show(this, "Version を入力してください。", "IndigoReleaseManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await ExecuteGuardedAsync("Public release を実行しています。", async () =>
        {
            if (!string.Equals(_viewModel.ReleaseBranch, _viewModel.PublicBranch, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"ReleaseBranch と現在 branch が一致していません。current={_viewModel.PublicBranch} requested={_viewModel.ReleaseBranch}");
            }

            if (string.IsNullOrWhiteSpace(_viewModel.PrivateEngineReleaseTag))
            {
                if (_viewModel.ReleaseDryRun)
                {
                    _viewModel.AppendLog("private_engine_release_tag 未指定時は、public 側 prepared dir を使って dry run します。");
                }

                VerifyPreparedArtifacts();
            }

            if (!string.IsNullOrWhiteSpace(_viewModel.PrivateEngineReleaseTag))
            {
                var syncWorkerResult = await RunPublicScriptAsync(
                    "sync_private_engine_worker_artifact.ps1",
                    new[]
                    {
                        "-ReleaseTag", _viewModel.PrivateEngineReleaseTag
                    });
                EnsureSuccess(syncWorkerResult, "Private worker 同期に失敗しました。");

                var syncPackageResult = await RunPublicScriptAsync(
                    "sync_private_engine_packages.ps1",
                    new[]
                    {
                        "-ReleaseTag", _viewModel.PrivateEngineReleaseTag
                    });
                EnsureSuccess(syncPackageResult, "Private package 同期に失敗しました。");
            }

            var arguments = new List<string>
            {
                "-Version", _viewModel.Version,
                "-PreparedWorkerPublishDir", "artifacts/rescue-worker/publish/Release-win-x64",
                "-PreparedPrivateEnginePackageDir", "artifacts/private-engine-packages/Release",
                "-Remote", "origin"
            };

            if (_viewModel.ReleaseDryRun)
            {
                arguments.Add("-DryRun");
            }

            if (_viewModel.ReleaseAllowDirty)
            {
                arguments.Add("-AllowDirty");
            }

            if (_viewModel.ReleaseSkipBranchPush)
            {
                arguments.Add("-SkipBranchPush");
            }

            if (_viewModel.ReleaseSkipTagPush)
            {
                arguments.Add("-SkipTagPush");
            }

            var result = await RunPublicScriptAsync("invoke_release.ps1", arguments);
            EnsureSuccess(result, "Public release に失敗しました。");

            if (!_viewModel.ReleaseDryRun)
            {
                _viewModel.LastPublicReleaseUrl =
                    $"https://github.com/{_viewModel.PublicOwnerAccount}/{_viewModel.PublicRepositoryName}/releases/tag/v{_viewModel.Version}";
            }

            _viewModel.StatusMessage = _viewModel.ReleaseDryRun
                ? "Public release DryRun が成功しました。"
                : "Public release が完了しました。";
        });
    }

    private void OpenPublicRepoUrl_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_viewModel.PublicOriginUrl);
    }

    private void OpenPrivateRepoUrl_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_viewModel.PrivateOriginUrl);
    }

    private void OpenLastPreviewUrl_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_viewModel.LastPreviewUrl);
    }

    private void OpenLastPublicReleaseUrl_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl(_viewModel.LastPublicReleaseUrl);
    }

    private async Task ExecuteGuardedAsync(string busyMessage, Func<Task> action)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        try
        {
            _viewModel.IsBusy = true;
            _viewModel.StatusMessage = busyMessage;
            await action();
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = "失敗しました。";
            _viewModel.AppendLog(ex.Message);
            MessageBox.Show(this, ex.Message, "IndigoReleaseManager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _viewModel.IsBusy = false;
        }
    }

    private Task<CommandRunResult> RunPublicScriptAsync(string scriptName, IReadOnlyCollection<string> arguments)
    {
        var scriptPath = Path.Combine(_publicRepoPath, "scripts", scriptName);
        return RunScriptAsync(_publicRepoPath, scriptPath, arguments);
    }

    private Task<CommandRunResult> RunPrivateScriptAsync(string scriptName, IReadOnlyCollection<string> arguments)
    {
        var scriptPath = Path.Combine(_privateRepoPath, "scripts", scriptName);
        return RunScriptAsync(_privateRepoPath, scriptPath, arguments);
    }

    private async Task<CommandRunResult> RunScriptAsync(string workingDirectory, string scriptPath, IReadOnlyCollection<string> arguments)
    {
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"script が見つかりません: {scriptPath}", scriptPath);
        }

        var prettyCommand = new StringBuilder(scriptPath);
        foreach (var argument in arguments)
        {
            prettyCommand.Append(' ');
            prettyCommand.Append(argument.Contains(' ') ? $"\"{argument}\"" : argument);
        }

        _viewModel.AppendLog($"実行: {prettyCommand}");

        return await ProcessExecutionService.RunPowerShellScriptAsync(
            workingDirectory,
            scriptPath,
            arguments,
            line => Dispatcher.Invoke(() => _viewModel.AppendLog(line)));
    }

    private static void EnsureSuccess(CommandRunResult result, string message)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"{message}{Environment.NewLine}ExitCode: {result.ExitCode}");
        }
    }

    private static string? ExtractSummaryPath(string output, string prefix)
    {
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var startIndex = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0)
            {
                continue;
            }

            return line[(startIndex + prefix.Length)..].Trim();
        }

        return null;
    }

    private void VerifyPreparedArtifacts()
    {
        var workerMetadataPath = Path.Combine(
            _publicRepoPath,
            "artifacts",
            "rescue-worker",
            "publish",
            "Release-win-x64",
            "rescue-worker-sync-source.json");

        var packageMetadataPath = Path.Combine(
            _publicRepoPath,
            "artifacts",
            "private-engine-packages",
            "Release",
            "private-engine-packages-source.json");

        EnsurePreparedMetadata(workerMetadataPath, packageMetadataPath);
    }

    private void EnsurePreparedMetadata(string workerMetadataPath, string packageMetadataPath)
    {
        if (!File.Exists(workerMetadataPath))
        {
            throw new InvalidOperationException("rescue-worker-sync-source.json が見つかりません。事前に Private release を実行してください。");
        }

        if (!File.Exists(packageMetadataPath))
        {
            throw new InvalidOperationException("private-engine-packages-source.json が見つかりません。事前に Private release を実行してください。");
        }

        var workerVersion = ReadMetadataValue(workerMetadataPath, "version");
        var packageVersion = ReadMetadataValue(packageMetadataPath, "packageVersion");

        if (string.IsNullOrWhiteSpace(workerVersion) || string.IsNullOrWhiteSpace(packageVersion))
        {
            throw new InvalidOperationException("prepared metadata の version 情報を読み取れません。private release または同期結果を再実行してください。");
        }

        if (!string.Equals(workerVersion, packageVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"prepared version が不整合です。worker={workerVersion} package={packageVersion}");
        }

        if (!string.IsNullOrWhiteSpace(_viewModel.Version)
            && !string.Equals(workerVersion, _viewModel.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"prepared version({workerVersion}) と入力 Version({_viewModel.Version}) が一致しません。");
        }
    }

    private static string? ReadMetadataValue(string path, string propertyName)
    {
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }

    private static void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var normalized = url.Replace(".git", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        Process.Start(new ProcessStartInfo
        {
            FileName = normalized,
            UseShellExecute = true
        });
    }

    private static string ResolvePublicRepositoryPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "IndigoMovieManager.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
