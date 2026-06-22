namespace NeonShield.Models;

public sealed class ApplicationUpdateInfo
{
    public required string CurrentVersion { get; init; }
    public required string LatestVersion { get; init; }
    public required string InstallerFileName { get; init; }
    public required string InstallerDownloadUrl { get; init; }
    public required string InstallerSha256 { get; init; }
    public required string ReleasePageUrl { get; init; }
    public string ReleaseNotes { get; init; } = string.Empty;
}
