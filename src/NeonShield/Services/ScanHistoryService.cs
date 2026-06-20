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
            return await JsonSerializer.DeserializeAsync<List<ScanReport>>(stream, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task SaveUnsafeAsync(List<ScanReport> reports)
    {
        await using var stream = File.Create(_historyPath);
        await JsonSerializer.SerializeAsync(stream, reports, JsonOptions);
    }
}
