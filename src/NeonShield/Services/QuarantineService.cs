using System.Text.Json;
using NeonShield.Models;

namespace NeonShield.Services;

public sealed class QuarantineService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _quarantineDirectory;
    private readonly string _indexPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public QuarantineService()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NeonShield");
        _quarantineDirectory = Path.Combine(dataDirectory, "Quarantine");
        _indexPath = Path.Combine(dataDirectory, "quarantine.json");
        Directory.CreateDirectory(_quarantineDirectory);
    }

    public async Task<IReadOnlyList<QuarantineRecord>> GetAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await LoadUnsafeAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<QuarantineRecord?> QuarantineAsync(ThreatDetection detection)
    {
        if (!File.Exists(detection.FilePath))
        {
            return null;
        }

        await _gate.WaitAsync();
        try
        {
            var records = await LoadUnsafeAsync();
            var id = Guid.NewGuid();
            var destination = Path.Combine(_quarantineDirectory, id.ToString("N") + ".qtn");
            var info = new FileInfo(detection.FilePath);

            File.Move(detection.FilePath, destination);

            var record = new QuarantineRecord
            {
                Id = id,
                OriginalPath = detection.FilePath,
                QuarantinedPath = destination,
                ThreatName = detection.Signature,
                QuarantinedAt = DateTimeOffset.Now,
                OriginalSize = info.Length
            };

            records.Add(record);
            await SaveUnsafeAsync(records);
            return record;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestoreAsync(Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            var records = await LoadUnsafeAsync();
            var record = records.FirstOrDefault(item => item.Id == id)
                         ?? throw new InvalidOperationException("Der Quarantäne-Eintrag wurde nicht gefunden.");

            if (!File.Exists(record.QuarantinedPath))
            {
                records.Remove(record);
                await SaveUnsafeAsync(records);
                throw new FileNotFoundException("Die isolierte Datei existiert nicht mehr.");
            }

            var directory = Path.GetDirectoryName(record.OriginalPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var restorePath = GetAvailableRestorePath(record.OriginalPath);
            File.Move(record.QuarantinedPath, restorePath);
            records.Remove(record);
            await SaveUnsafeAsync(records);
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
            var records = await LoadUnsafeAsync();
            var record = records.FirstOrDefault(item => item.Id == id);
            if (record is null)
            {
                return;
            }

            if (File.Exists(record.QuarantinedPath))
            {
                File.Delete(record.QuarantinedPath);
            }

            records.Remove(record);
            await SaveUnsafeAsync(records);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<QuarantineRecord>> LoadUnsafeAsync()
    {
        if (!File.Exists(_indexPath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_indexPath);
            return await JsonSerializer.DeserializeAsync<List<QuarantineRecord>>(stream, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task SaveUnsafeAsync(List<QuarantineRecord> records)
    {
        await using var stream = File.Create(_indexPath);
        await JsonSerializer.SerializeAsync(stream, records, JsonOptions);
    }

    private static string GetAvailableRestorePath(string originalPath)
    {
        if (!File.Exists(originalPath))
        {
            return originalPath;
        }

        var directory = Path.GetDirectoryName(originalPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(originalPath);
        var extension = Path.GetExtension(originalPath);
        return Path.Combine(directory, $"{name}.wiederhergestellt-{DateTime.Now:yyyyMMdd-HHmmss}{extension}");
    }
}
