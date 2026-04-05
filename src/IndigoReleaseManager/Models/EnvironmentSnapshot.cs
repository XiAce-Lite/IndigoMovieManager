namespace IndigoReleaseManager.Models;

public sealed class EnvironmentSnapshot
{
    public RepositoryInfo PublicRepository { get; init; } = new();

    public RepositoryInfo PrivateRepository { get; init; } = new();

    public string PublicVersion { get; init; } = string.Empty;

    public string GhAuthState { get; init; } = string.Empty;

    public string GitHubTokenState { get; init; } = string.Empty;

    public string PrivateEngineTokenState { get; init; } = string.Empty;
}
