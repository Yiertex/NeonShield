using System.Text.Json;
using NeonShield.Models;

namespace NeonShield.Services;

public sealed class ScanHistoryService
{
    private const int MaximumReports = 100;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _historyPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ScanHistoryService()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NeonShield");
        Directory.CreateDirectory(dataDirectory);
        _historyPath = Path.Combine(dataDirectory, "scan-history.json");
    }

    public async Task<IReadOnlyList<ScanReport>> GetAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return (await LoadUnsafeAsync())
                .OrderByDescending(report => report.StartedAt)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ScanReport report)
    {
        await _gate.WaitAsync();
        try
        {
            var reports = await LoadUnsafeAsync();
            reports.RemoveAll(existing => existing.Id == report.Id);
            reports.Insert(0, report);

            if (reports.Count > MaximumReports)
            {
                reports.RemoveRange(MaximumReports, reports.Count - MaximumReports);
            }

            await SaveUnsafeAsync(reports);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            var reports = await LoadUnsafeAsync();
            reports.RemoveAll(report => report.Id == id);
            await SaveUnsafeAsync(reports);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<ScanReport>> LoadUnsafeAsync()
    {
        if (!File.Exists(_historyPath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_historyPath);
            var reports = await JsonSerializer.DeserializeAsync<List<ScanReport>>(stream, JsonOptions) ?? [];
            foreach (var report in reports)
            {
                NormalizeFileAccessWarnings(report);
                if (report.Status == ScanReportStatus.Completed &&
                    report.Errors.Any(error =>
                        error.Contains("Code 2", StringComparison.OrdinalIgnoreCase) ||
                        error.Contains("Can't load", StringComparison.OrdinalIgnoreCase) ||
                        error.Contains("Can't verify", StringComparison.OrdinalIgnoreCase) ||
                        error.Contains("Initialization error", StringComparison.OrdinalIgnoreCase)))
                {
                    report.Status = ScanReportStatus.Failed;
                }
            }

            return reports;
        }
        catch
        {
            return [];
        }
    }

    private static void NormalizeFileAccessWarnings(ScanReport report)
    {
        var inaccessible = report.Errors
            .Where(IsFileAccessWarning)
            .ToList();
        if (inaccessible.Count == 0)
        {
            return;
        }

        report.Errors.RemoveAll(IsFileAccessWarning);
        report.SkippedFiles = Math.Max(report.SkippedFiles, inaccessible.Count);
        if (!report.Warnings.Any(warning =>
                warning.Contains("wurden übersprungen", StringComparison.OrdinalIgnoreCase)))
        {
            report.Warnings.Insert(
                0,
                $"{inaccessible.Count:N0} Datei(en) wurden übersprungen, weil sie gesperrt waren " +
                "oder dem aktuellen Windows-Benutzer der Zugriff fehlte.");
        }

        foreach (var line in inaccessible.Take(25))
        {
            report.Warnings.Add(FormatFileAccessWarning(line));
        }

        report.Errors.RemoveAll(error =>
            error.Contains("ClamAV beendete den Scan mit Code 2", StringComparison.OrdinalIgnoreCase));
        if (report.Status == ScanReportStatus.Failed && report.Errors.Count == 0)
        {
            report.Status = ScanReportStatus.Completed;
        }
    }

    private static bool IsFileAccessWarning(string line) =>
        line.Contains("Can't open file ", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Can't access file ", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Permission denied", StringComparison.OrdinalIgnoreCase);

    private static string FormatFileAccessWarning(string line)
    {
        const string openMarker = "Can't open file ";
        var markerIndex = line.IndexOf(openMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return $"Übersprungen (gesperrt oder Zugriff verweigert): {line}";
        }

        var detail = line[(markerIndex + openMarker.Length)..].Trim();
        var errorCodeSeparator = detail.LastIndexOf(": ", StringComparison.Ordinal);
        if (errorCodeSeparator > 2 &&
            int.TryParse(detail[(errorCodeSeparator + 2)..], out _))
        {
            detail = detail[..errorCodeSeparator];
        }

        return $"Übersprungen (gesperrt oder Zugriff verweigert): {detail}";
    }

    private async Task SaveUnsafeAsync(List<ScanReport> reports)
    {
        await using var stream = File.Create(_historyPath);
        await JsonSerializer.SerializeAsync(stream, reports, JsonOptions);
    }
}
