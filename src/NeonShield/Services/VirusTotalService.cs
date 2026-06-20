using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using NeonShield.Models;

namespace NeonShield.Services;

public sealed class VirusTotalService
{
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("https://www.virustotal.com/api/v3/"),
        Timeout = TimeSpan.FromSeconds(45)
    };

    public async Task<List<OnlineReputationResult>> CheckFilesAsync(
        IReadOnlyList<string> files,
        string apiKey,
        IProgress<ScanProgress> progress,
        AsyncPauseGate pauseGate,
        CancellationToken cancellationToken)
    {
        var results = new List<OnlineReputationResult>();
        var validFiles = files
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var index = 0; index < validFiles.Count; index++)
        {
            await pauseGate.WaitIfPausedAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var path = validFiles[index];
            progress.Report(new ScanProgress
            {
                CurrentPath = $"Online-Abgleich: {path}",
                FilesScanned = index,
                TargetIndex = index + 1,
                TargetCount = validFiles.Count
            });

            var result = await CheckFileAsync(path, apiKey, cancellationToken);
            results.Add(result);

            if (index < validFiles.Count - 1)
            {
                await DelayWithPauseAsync(
                    TimeSpan.FromSeconds(16),
                    pauseGate,
                    cancellationToken);
            }
        }

        return results;
    }

    private async Task<OnlineReputationResult> CheckFileAsync(
        string filePath,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var result = new OnlineReputationResult { FilePath = filePath };

        try
        {
            await using var stream = File.OpenRead(filePath);
            result.Sha256 = Convert.ToHexStringLower(
                await SHA256.HashDataAsync(stream, cancellationToken));

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"files/{result.Sha256}");
            request.Headers.Add("x-apikey", apiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                result.IsKnown = false;
                return result;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                result.Error = "API-Limit erreicht";
                return result;
            }

            response.EnsureSuccessStatusCode();
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            var stats = document.RootElement
                .GetProperty("data")
                .GetProperty("attributes")
                .GetProperty("last_analysis_stats");

            result.IsKnown = true;
            result.Malicious = GetInt(stats, "malicious");
            result.Suspicious = GetInt(stats, "suspicious");
            result.Harmless = GetInt(stats, "harmless");
            result.Undetected = GetInt(stats, "undetected");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Error = ex.Message.ReplaceLineEndings(" ");
        }

        return result;
    }

    private static int GetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) ? value.GetInt32() : 0;

    private static async Task DelayWithPauseAsync(
        TimeSpan duration,
        AsyncPauseGate pauseGate,
        CancellationToken cancellationToken)
    {
        var remaining = duration;
        while (remaining > TimeSpan.Zero)
        {
            await pauseGate.WaitIfPausedAsync(cancellationToken);
            var slice = remaining > TimeSpan.FromSeconds(1)
                ? TimeSpan.FromSeconds(1)
                : remaining;
            await Task.Delay(slice, cancellationToken);
            remaining -= slice;
        }
    }
}
