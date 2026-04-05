using IndigoReleaseManager.Models;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace IndigoReleaseManager.Services;

public static class LocalEnvironmentService
{
    private static readonly Regex RemoteRegex = new(@"^(?<name>\S+)\s+(?<url>\S+)\s+\((fetch|push)\)$", RegexOptions.Compiled);

    public static async Task<EnvironmentSnapshot> LoadAsync(string publicRepoPath, string privateRepoPath, CancellationToken cancellationToken = default)
    {
        var publicRepository = await LoadRepositoryInfoAsync(
            name: "IndigoMovieManager",
            localPath: publicRepoPath,
            ownerAccount: "T-Hamada0101",
            repositoryName: "IndigoMovieManager_fork",
            cancellationToken: cancellationToken);

        var privateRepository = await LoadRepositoryInfoAsync(
            name: "IndigoMovieEngine",
            localPath: privateRepoPath,
            ownerAccount: "T-Hamada0101",
            repositoryName: "IndigoMovieEngine",
            cancellationToken: cancellationToken);

        return new EnvironmentSnapshot
        {
            PublicRepository = publicRepository,
            PrivateRepository = privateRepository,
            PublicVersion = await ReadProjectVersionAsync(Path.Combine(publicRepoPath, "IndigoMovieManager.csproj"), cancellationToken),
            GhAuthState = await GetGhAuthStateAsync(publicRepoPath, cancellationToken),
            GitHubTokenState = GetEnvironmentState("GH_TOKEN", "GITHUB_TOKEN"),
            PrivateEngineTokenState = GetEnvironmentState("INDIGO_ENGINE_REPO_TOKEN")
        };
    }

    private static async Task<RepositoryInfo> LoadRepositoryInfoAsync(
        string name,
        string localPath,
        string ownerAccount,
        string repositoryName,
        CancellationToken cancellationToken)
    {
        var branch = await RunCaptureAsync("git", new[] { "-C", localPath, "branch", "--show-current" }, localPath, cancellationToken);
        var remotes = await RunCaptureAsync("git", new[] { "-C", localPath, "remote", "-v" }, localPath, cancellationToken);
        var remoteMap = ParseRemotes(remotes);
        var originUrl = remoteMap.TryGetValue("origin", out var origin) ? origin : string.Empty;
        var identity = ParseGitHubIdentity(originUrl);

        return new RepositoryInfo
        {
            Name = name,
            LocalPath = localPath,
            Branch = branch.Trim(),
            OwnerAccount = identity.OwnerAccount ?? ownerAccount,
            RepositoryName = identity.RepositoryName ?? repositoryName,
            OriginUrl = originUrl,
            UpstreamUrl = remoteMap.TryGetValue("upstream", out var upstream) ? upstream : string.Empty,
            PrivateProbeUrl = remoteMap.TryGetValue("private-probe", out var probe) ? probe : string.Empty
        };
    }

    private static (string? OwnerAccount, string? RepositoryName) ParseGitHubIdentity(string originUrl)
    {
        if (string.IsNullOrWhiteSpace(originUrl))
        {
            return (null, null);
        }

        var normalized = originUrl.Trim();
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        const string httpsPrefix = "https://github.com/";
        if (normalized.StartsWith(httpsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var segments = normalized.Substring(httpsPrefix.Length).Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2)
            {
                return (segments[0], segments[1]);
            }
        }

        return (null, null);
    }

    private static Dictionary<string, string> ParseRemotes(string remoteOutput)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in remoteOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = RemoteRegex.Match(line.Trim());
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value.Trim();
            var url = match.Groups["url"].Value.Trim();
            if (!result.ContainsKey(name))
            {
                result[name] = url;
            }
        }

        return result;
    }

    private static async Task<string> ReadProjectVersionAsync(string projectPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(projectPath))
        {
            return string.Empty;
        }

        var content = await File.ReadAllTextAsync(projectPath, cancellationToken);
        var match = Regex.Match(content, "<Version>([^<]+)</Version>");
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static async Task<string> GetGhAuthStateAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        try
        {
            await RunCaptureAsync("gh", new[] { "auth", "status" }, workingDirectory, cancellationToken);
            return "Configured";
        }
        catch
        {
            return "Missing";
        }
    }

    private static string GetEnvironmentState(params string[] variableNames)
    {
        foreach (var variableName in variableNames)
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variableName)))
            {
                return "Configured";
            }
        }

        return "Missing";
    }

    private static async Task<string> RunCaptureAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {string.Join(' ', arguments)} に失敗しました。{Environment.NewLine}{standardError}");
        }

        return standardOutput.Trim();
    }
}
