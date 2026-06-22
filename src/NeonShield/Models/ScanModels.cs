using System.Text.Json.Serialization;

namespace NeonShield.Models;

public enum ScanKind
{
    Quick,
    Deep,
    Custom,
    Memory,
    Processes
}

public sealed record ThreatDetection(string FilePath, string Signature);

public sealed class OnlineReputationResult
{
    public string FilePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public bool IsKnown { get; set; }
    public int Malicious { get; set; }
    public int Suspicious { get; set; }
    public int Harmless { get; set; }
    public int Undetected { get; set; }
    public string Error { get; set; } = string.Empty;

    [JsonIgnore]
    public string VerdictDisplay => !string.IsNullOrWhiteSpace(Error)
        ? $"Nicht geprüft: {Error}"
        : !IsKnown
            ? "Unbekannt"
            : Malicious > 0 || Suspicious > 0
                ? $"{Malicious} schädlich · {Suspicious} verdächtig"
                : "Keine Online-Erkennung";
}

public sealed class ScanProgress
{
    public string CurrentPath { get; init; } = string.Empty;
    public long FilesScanned { get; init; }
    public int ThreatsFound { get; init; }
    public int TargetIndex { get; init; }
    public int TargetCount { get; init; }
}

public sealed class ScanResult
{
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; init; }
    public long FilesScanned { get; init; }
    public int SkippedFiles { get; init; }
    public List<ThreatDetection> Threats { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    public bool WasCancelled { get; init; }
    public int ExitCode { get; init; }
    public bool HasFatalError { get; init; }
}

public enum ScanReportStatus
{
    Completed,
    Cancelled,
    Failed
}

public sealed class ScanReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ScanKind Kind { get; set; }
    public ScanReportStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public TimeSpan PausedDuration { get; set; }
    public long FilesScanned { get; set; }
    public int SkippedFiles { get; set; }
    public List<string> Targets { get; set; } = [];
    public List<ThreatDetection> Threats { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public int QuarantinedCount { get; set; }
    public List<string> QuarantineFailures { get; set; } = [];
    public List<OnlineReputationResult> OnlineResults { get; set; } = [];
    public string EngineVersion { get; set; } = string.Empty;

    [JsonIgnore]
    public TimeSpan Duration => FinishedAt - StartedAt;

    [JsonIgnore]
    public string KindDisplay => Kind switch
    {
        ScanKind.Quick => "Schnellscan",
        ScanKind.Deep => "Tiefenscan",
        ScanKind.Custom => "Eigener Scan",
        ScanKind.Memory => "Arbeitsspeicher",
        _ => "Prozess-Scan"
    };

    [JsonIgnore]
    public string StatusDisplay => Status switch
    {
        ScanReportStatus.Completed when Threats.Count == 0 => "Sauber",
        ScanReportStatus.Completed => "Funde erkannt",
        ScanReportStatus.Cancelled => "Abgebrochen",
        _ => "Fehlgeschlagen"
    };

    [JsonIgnore]
    public string DurationDisplay => Duration.TotalHours >= 1
        ? Duration.ToString(@"hh\:mm\:ss")
        : Duration.ToString(@"mm\:ss");

    [JsonIgnore]
    public string Summary => SkippedFiles > 0
        ? $"{FilesScanned:N0} Dateien · {Threats.Count:N0} Funde · {SkippedFiles:N0} übersprungen · {DurationDisplay}"
        : $"{FilesScanned:N0} Dateien · {Threats.Count:N0} Funde · {DurationDisplay}";
}

public sealed class QuarantineRecord
{
    public Guid Id { get; set; }
    public string OriginalPath { get; set; } = string.Empty;
    public string QuarantinedPath { get; set; } = string.Empty;
    public string ThreatName { get; set; } = string.Empty;
    public DateTimeOffset QuarantinedAt { get; set; }
    public long OriginalSize { get; set; }
}
