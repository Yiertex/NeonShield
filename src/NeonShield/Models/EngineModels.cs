namespace NeonShield.Models;

public enum EngineUpdateStatus
{
    Current,
    Installed,
    Updated
}

public sealed class EngineUpdateProgress
{
    public string Message { get; init; } = string.Empty;
    public double? Percentage { get; init; }
}

public sealed class EngineUpdateResult
{
    public required string EngineDirectory { get; init; }
    public required string VersionTag { get; init; }
    public required EngineUpdateStatus Status { get; init; }
}
