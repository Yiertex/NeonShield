using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NeonShield.Models;

namespace NeonShield.Services;

public sealed class ApplicationUpdateService
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/Yiertex/NeonShield/releases/latest";
    private const string InstallerPrefix = "NeonShield-Setup-";
    private const string ChecksumFileName = "SHA256SUMS.txt";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public ApplicationUpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("NeonShield-Updater");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public string CurrentVersion => GetCurrentVersionText();

    public async Task<ApplicationUpdateInfo?> CheckForUpdateAsync(
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseUrl, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "Der öffentliche GitHub-Releasekanal ist nicht erreichbar. " +
                "Das Repository muss öffentlich sein und mindestens ein stabiles Release enthalten.");
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                          stream,
                          JsonOptions,
                          cancellationToken)
                      ?? throw new InvalidDataException(
                          "Die GitHub-Releaseinformationen konnten nicht gelesen werden.");

        var currentVersionText = GetCurrentVersionText();
        var currentVersion = ParseVersion(currentVersionText);
        var latestVersionText = release.TagName.Trim().TrimStart('v', 'V');
        var latestVersion = ParseVersion(latestVersionText);
        if (latestVersion <= currentVersion)
        {
            return null;
        }

        var installer = release.Assets
            .Where(asset =>
                asset.Name.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith("-win-x64.exe", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(asset => asset.UpdatedAt)
            .FirstOrDefault()
            ?? throw new InvalidDataException(
                "Das aktuelle GitHub Release enthält keinen NeonShield-Windows-Installer.");

        var checksum = await GetInstallerChecksumAsync(release.Assets, installer, cancellationToken);
        return new ApplicationUpdateInfo
        {
            CurrentVersion = currentVersionText,
            LatestVersion = latestVersionText,
            InstallerFileName = Path.GetFileName(installer.Name),
            InstallerDownloadUrl = installer.BrowserDownloadUrl,
            InstallerSha256 = checksum,
            ReleasePageUrl = release.HtmlUrl,
            ReleaseNotes = release.Body
        };
    }

    public async Task<string> DownloadInstallerAsync(
        ApplicationUpdateInfo update,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NeonShield",
            "Updates");
        Directory.CreateDirectory(updateDirectory);

        var installerPath = Path.Combine(updateDirectory, Path.GetFileName(update.InstallerFileName));
        var temporaryPath = installerPath + ".download";

        try
        {
            using var response = await _httpClient.GetAsync(
                update.InstallerDownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);

            var buffer = new byte[81920];
            long downloadedBytes = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloadedBytes += read;
                if (totalBytes > 0)
                {
                    progress?.Report((int)Math.Clamp(
                        downloadedBytes * 100 / totalBytes.Value,
                        0,
                        100));
                }
            }

            await destination.FlushAsync(cancellationToken);
            var actualChecksum = await CalculateSha256Async(temporaryPath, cancellationToken);
            if (!actualChecksum.Equals(update.InstallerSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Die SHA-256-Prüfsumme des heruntergeladenen Installers stimmt nicht.");
            }

            File.Move(temporaryPath, installerPath, overwrite: true);
            progress?.Report(100);
            return installerPath;
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private async Task<string> GetInstallerChecksumAsync(
        IReadOnlyCollection<GitHubAsset> assets,
        GitHubAsset installer,
        CancellationToken cancellationToken)
    {
        var checksumAsset = assets.FirstOrDefault(asset =>
            asset.Name.Equals(ChecksumFileName, StringComparison.OrdinalIgnoreCase));
        if (checksumAsset is not null)
        {
            var content = await _httpClient.GetStringAsync(
                checksumAsset.BrowserDownloadUrl,
                cancellationToken);
            foreach (var line in content.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split(
                    [' ', '\t'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 2 &&
                    Path.GetFileName(parts[^1]).Equals(
                        installer.Name,
                        StringComparison.OrdinalIgnoreCase) &&
                    IsSha256(parts[0]))
                {
                    return parts[0].ToUpperInvariant();
                }
            }
        }

        const string digestPrefix = "sha256:";
        if (installer.Digest.StartsWith(digestPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var digest = installer.Digest[digestPrefix.Length..];
            if (IsSha256(digest))
            {
                return digest.ToUpperInvariant();
            }
        }

        throw new InvalidDataException(
            "Für den GitHub-Installer wurde keine gültige SHA-256-Prüfsumme veröffentlicht.");
    }

    private static string GetCurrentVersionText()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var value = string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly.GetName().Version?.ToString(3) ?? "0.0.0"
            : informationalVersion;
        return value.Split('+')[0].Trim();
    }

    private static Version ParseVersion(string value)
    {
        var numericPart = value.Split('-', '+')[0];
        return Version.TryParse(numericPart, out var version)
            ? version
            : throw new InvalidDataException($"Ungültige Versionsnummer im Updatekanal: {value}");
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static async Task<string> CalculateSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // A failed partial download is removed best-effort.
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; init; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;

        [JsonPropertyName("digest")]
        public string Digest { get; init; } = string.Empty;

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; init; }
    }
}
