namespace NeonShield.Models;

public sealed class AppSettings
{
    public string ClamAvDirectory { get; set; } = string.Empty;
    public bool AutoQuarantine { get; set; } = true;
    public bool ScanArchives { get; set; } = true;
    public bool ScanPotentiallyUnwanted { get; set; } = true;
    public bool EnableOnlineReputation { get; set; }
    public string VirusTotalApiKeyProtected { get; set; } = string.Empty;
}
