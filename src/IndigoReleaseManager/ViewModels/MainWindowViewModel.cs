using IndigoReleaseManager.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndigoReleaseManager.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private string _publicRepoPath = string.Empty;
    private string _publicBranch = string.Empty;
    private string _publicOriginUrl = string.Empty;
    private string _publicUpstreamUrl = string.Empty;
    private string _publicPrivateProbeUrl = string.Empty;
    private string _privateRepoPath = string.Empty;
    private string _privateBranch = string.Empty;
    private string _privateOriginUrl = string.Empty;
    private string _version = string.Empty;
    private string _privateEngineReleaseTag = string.Empty;
    private string _privateEngineRunId = string.Empty;
    private string _previewRef = "workthree";
    private string _releaseBranch = "workthree";
    private bool _releaseDryRun = true;
    private bool _releaseAllowDirty;
    private bool _releaseSkipBranchPush;
    private bool _releaseSkipTagPush;
    private string _ghAuthState = "Unknown";
    private string _gitHubTokenState = "Unknown";
    private string _privateEngineTokenState = "Unknown";
    private string _statusMessage = "初期化待ち";
    private string _logText = string.Empty;
    private string _lastPreviewUrl = string.Empty;
    private string _lastPublicReleaseUrl = string.Empty;
    private string _lastPrivateOutputPath = string.Empty;
    private bool _isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    private string _publicOwnerAccount = "T-Hamada0101";
    private string _publicRepositoryName = "IndigoMovieManager_fork";
    private string _privateOwnerAccount = "T-Hamada0101";
    private string _privateRepositoryName = "IndigoMovieEngine";

    public string PublicRepoName { get; } = "IndigoMovieManager";

    public string PublicOwnerAccount
    {
        get => _publicOwnerAccount;
        set => SetProperty(ref _publicOwnerAccount, value);
    }

    public string PublicRepositoryName
    {
        get => _publicRepositoryName;
        set => SetProperty(ref _publicRepositoryName, value);
    }

    public string PrivateRepoName { get; } = "IndigoMovieEngine";

    public string PrivateOwnerAccount
    {
        get => _privateOwnerAccount;
        set => SetProperty(ref _privateOwnerAccount, value);
    }

    public string PrivateRepositoryName
    {
        get => _privateRepositoryName;
        set => SetProperty(ref _privateRepositoryName, value);
    }

    public string PublicRepoPath
    {
        get => _publicRepoPath;
        set => SetProperty(ref _publicRepoPath, value);
    }

    public string PublicBranch
    {
        get => _publicBranch;
        set => SetProperty(ref _publicBranch, value);
    }

    public string PublicOriginUrl
    {
        get => _publicOriginUrl;
        set => SetProperty(ref _publicOriginUrl, value);
    }

    public string PublicUpstreamUrl
    {
        get => _publicUpstreamUrl;
        set => SetProperty(ref _publicUpstreamUrl, value);
    }

    public string PublicPrivateProbeUrl
    {
        get => _publicPrivateProbeUrl;
        set => SetProperty(ref _publicPrivateProbeUrl, value);
    }

    public string PrivateRepoPath
    {
        get => _privateRepoPath;
        set => SetProperty(ref _privateRepoPath, value);
    }

    public string PrivateBranch
    {
        get => _privateBranch;
        set => SetProperty(ref _privateBranch, value);
    }

    public string PrivateOriginUrl
    {
        get => _privateOriginUrl;
        set => SetProperty(ref _privateOriginUrl, value);
    }

    public string Version
    {
        get => _version;
        set => SetProperty(ref _version, value);
    }

    public string PrivateEngineReleaseTag
    {
        get => _privateEngineReleaseTag;
        set => SetProperty(ref _privateEngineReleaseTag, value);
    }

    public string PrivateEngineRunId
    {
        get => _privateEngineRunId;
        set => SetProperty(ref _privateEngineRunId, value);
    }

    public string PreviewRef
    {
        get => _previewRef;
        set => SetProperty(ref _previewRef, value);
    }

    public string ReleaseBranch
    {
        get => _releaseBranch;
        set => SetProperty(ref _releaseBranch, value);
    }

    public bool ReleaseDryRun
    {
        get => _releaseDryRun;
        set => SetProperty(ref _releaseDryRun, value);
    }

    public bool ReleaseAllowDirty
    {
        get => _releaseAllowDirty;
        set => SetProperty(ref _releaseAllowDirty, value);
    }

    public bool ReleaseSkipBranchPush
    {
        get => _releaseSkipBranchPush;
        set => SetProperty(ref _releaseSkipBranchPush, value);
    }

    public bool ReleaseSkipTagPush
    {
        get => _releaseSkipTagPush;
        set => SetProperty(ref _releaseSkipTagPush, value);
    }

    public string GhAuthState
    {
        get => _ghAuthState;
        set => SetProperty(ref _ghAuthState, value);
    }

    public string GitHubTokenState
    {
        get => _gitHubTokenState;
        set => SetProperty(ref _gitHubTokenState, value);
    }

    public string PrivateEngineTokenState
    {
        get => _privateEngineTokenState;
        set => SetProperty(ref _privateEngineTokenState, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string LogText
    {
        get => _logText;
        set => SetProperty(ref _logText, value);
    }

    public string LastPreviewUrl
    {
        get => _lastPreviewUrl;
        set => SetProperty(ref _lastPreviewUrl, value);
    }

    public string LastPublicReleaseUrl
    {
        get => _lastPublicReleaseUrl;
        set => SetProperty(ref _lastPublicReleaseUrl, value);
    }

    public string LastPrivateOutputPath
    {
        get => _lastPrivateOutputPath;
        set => SetProperty(ref _lastPrivateOutputPath, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRunActions));
            }
        }
    }

    public bool CanRunActions => !IsBusy;

    public void ApplyEnvironment(EnvironmentSnapshot snapshot)
    {
        PublicOwnerAccount = snapshot.PublicRepository.OwnerAccount;
        PublicRepositoryName = snapshot.PublicRepository.RepositoryName;
        PublicRepoPath = snapshot.PublicRepository.LocalPath;
        PublicBranch = snapshot.PublicRepository.Branch;
        PublicOriginUrl = snapshot.PublicRepository.OriginUrl;
        PublicUpstreamUrl = snapshot.PublicRepository.UpstreamUrl;
        PublicPrivateProbeUrl = snapshot.PublicRepository.PrivateProbeUrl;
        PrivateOwnerAccount = snapshot.PrivateRepository.OwnerAccount;
        PrivateRepositoryName = snapshot.PrivateRepository.RepositoryName;
        PrivateRepoPath = snapshot.PrivateRepository.LocalPath;
        PrivateBranch = snapshot.PrivateRepository.Branch;
        PrivateOriginUrl = snapshot.PrivateRepository.OriginUrl;
        GhAuthState = snapshot.GhAuthState;
        GitHubTokenState = snapshot.GitHubTokenState;
        PrivateEngineTokenState = snapshot.PrivateEngineTokenState;

        if (string.IsNullOrWhiteSpace(Version))
        {
            Version = snapshot.PublicVersion;
        }

        if (string.IsNullOrWhiteSpace(PreviewRef))
        {
            PreviewRef = snapshot.PublicRepository.Branch;
        }

        if (string.IsNullOrWhiteSpace(ReleaseBranch))
        {
            ReleaseBranch = snapshot.PublicRepository.Branch;
        }
    }

    public void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogText = string.IsNullOrWhiteSpace(LogText)
            ? line
            : $"{LogText}{Environment.NewLine}{line}";
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
