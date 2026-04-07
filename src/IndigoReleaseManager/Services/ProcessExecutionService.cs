using IndigoReleaseManager.Models;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace IndigoReleaseManager.Services;

public static class ProcessExecutionService
{
    private static readonly Regex UrlRegex = new(@"https?://[^\s""]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static async Task<CommandRunResult> RunPowerShellScriptAsync(
        string workingDirectory,
        string scriptPath,
        IEnumerable<string> arguments,
        Action<string>? onOutput,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        var buffer = new StringBuilder();

        void AppendLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            lock (buffer)
            {
                buffer.AppendLine(line);
            }

            onOutput?.Invoke(line);
        }

        process.OutputDataReceived += (_, eventArgs) => AppendLine(eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => AppendLine(eventArgs.Data);

        // 既存 script の stdout / stderr を UI へそのまま流し、どこで止まったかを見やすくする。
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cancellationToken);

        string output;
        lock (buffer)
        {
            output = buffer.ToString();
        }

        var urls = UrlRegex.Matches(output)
            .Select(match => match.Value.TrimEnd('.', ',', ';'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CommandRunResult
        {
            ExitCode = process.ExitCode,
            Output = output,
            Urls = urls
        };
    }
}
