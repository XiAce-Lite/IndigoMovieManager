namespace IndigoReleaseManager.Models;

public sealed class RepositoryInfo
{
    public string Name { get; init; } = string.Empty;

    public string LocalPath { get; init; } = string.Empty;

    public string Branch { get; init; } = string.Empty;

    public string OwnerAccount { get; init; } = string.Empty;

    public string RepositoryName { get; init; } = string.Empty;

    public string OriginUrl { get; init; } = string.Empty;

    public string UpstreamUrl { get; init; } = string.Empty;

    public string PrivateProbeUrl { get; init; } = string.Empty;
}
