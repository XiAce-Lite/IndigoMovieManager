namespace IndigoReleaseManager.Models;

public sealed class CommandRunResult
{
    public int ExitCode { get; init; }

    public string Output { get; init; } = string.Empty;

    public IReadOnlyList<string> Urls { get; init; } = Array.Empty<string>();

    public bool IsSuccess => ExitCode == 0;
}
