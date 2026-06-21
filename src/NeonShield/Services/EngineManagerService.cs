using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NeonShield.Models;

namespace NeonShield.Services;

public sealed class EngineManagerService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Cisco-Talos/clamav/releases/latest";
    private const string MetadataFileName = "neonshield-engine.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient _httpClient;

    public EngineManagerService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("NeonShield", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public string ManagedEngineDirectory => Path.Combine(AppContext.BaseDirectory, "Engine");

    public bool IsManagedDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return true;
        }

        return string.Equals(
            Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(ManagedEngineDirectory).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    public async Task<EngineUpdateResult> EnsureLatestEngineAsync(
        IProgress<EngineUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new EngineUpdateProgress { Message = "Prüfe die aktuelle ClamAV-Version …" });
        var release = await GetLatestReleaseAsync(cancellationToken);
        var asset = SelectWindowsAsset(release);
        var currentMetadata = await LoadMetadataAsync();
        var engineExecutable = Path.Combine(ManagedEngineDirectory, "clamscan.exe");
        var engineCertificate = Path.Combine(ManagedEngineDirectory, "certs", "clamav.crt");

        if (File.Exists(engineExecutable) &&
            File.Exists(engineCertificate) &&
            string.Equals(currentMetadata?.VersionTag, release.TagName, StringComparison.OrdinalIgnoreCase))
        {
            EnsureDataDirectories(ManagedEngineDirectory);
            return new EngineUpdateResult
            {
                EngineDirectory = ManagedEngineDirectory,
                VersionTag = release.TagName,
                Status = EngineUpdateStatus.Current
            };
        }

        var wasInstalled = File.Exists(engineExecutable);
        var tempRoot = Path.Combine(Path.GetTempPath(), "NeonShield", Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(tempRoot, asset.Name);
        var extractPath = Path.Combine(tempRoot, "extracted");

        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(extractPath);

        try
        {
            progress?.Report(new EngineUpdateProgress
            {
                Message = $"ClamAV {release.TagName.Replace("clamav-", string.Empty)} wird heruntergeladen …",
                Percentage = 0
            });

            await DownloadAssetAsync(asset, archivePath, progress, cancellationToken);
            await VerifyDigestAsync(archivePath, asset.Digest, cancellationToken);

            progress?.Report(new EngineUpdateProgress
            {
                Message = "Download geprüft. Engine wird installiert …",
                Percentage = 100
            });

            ZipFile.ExtractToDirectory(archivePath, extractPath);
            var clamscanPath = Directory
                .EnumerateFiles(extractPath, "clamscan.exe", SearchOption.AllDirectories)
                .FirstOrDefault()
                ?? throw new InvalidDataException("Das offizielle ClamAV-Archiv enthält keine clamscan.exe.");

            var extractedEngineRoot = Path.GetDirectoryName(clamscanPath)
                                      ?? throw new InvalidDataException("Der Engine-Ordner konnte nicht ermittelt werden.");
            var extractedCertificate = Path.Combine(extractedEngineRoot, "certs", "clamav.crt");
            if (!File.Exists(extractedCertificate))
            {
                throw new InvalidDataException(
                    "Das offizielle ClamAV-Archiv enthält das benötigte Zertifikat certs\\clamav.crt nicht.");
            }

            ReplaceManagedEngine(extractedEngineRoot, release.TagName, asset);
            EnsureDataDirectories(ManagedEngineDirectory);

            return new EngineUpdateResult
            {
                EngineDirectory = ManagedEngineDirectory,
                VersionTag = release.TagName,
                Status = wasInstalled ? EngineUpdateStatus.Updated : EngineUpdateStatus.Installed
            };
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    public static string GetDatabaseDirectory()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NeonShield",
            "Database");
        Directory.CreateDirectory(dataDirectory);
        return dataDirectory;
    }

    public static string GetCvdCertificateDirectory(string clamAvDirectory)
    {
        var certificateDirectory = Path.Combine(clamAvDirectory, "certs");
        var certificatePath = Path.Combine(certificateDirectory, "clamav.crt");
        if (!File.Exists(certificatePath))
        {
            throw new FileNotFoundException(
                "Das ClamAV-Datenbankzertifikat certs\\clamav.crt fehlt. " +
                "Die verwaltete Engine muss erneut installiert werden.",
                certificatePath);
        }

        return certificateDirectory;
    }

    public static string EnsureFreshClamConfiguration(string clamAvDirectory)
    {
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NeonShield");
        var databaseDirectory = GetDatabaseDirectory();
        var certificateDirectory = GetCvdCertificateDirectory(clamAvDirectory);
        var configPath = Path.Combine(dataRoot, "freshclam.conf");
        Directory.CreateDirectory(dataRoot);

        var configuration =
            $"DatabaseDirectory \"{databaseDirectory}\"{Environment.NewLine}" +
            $"CVDCertsDirectory \"{certificateDirectory}\"{Environment.NewLine}" +
            $"DatabaseMirror database.clamav.net{Environment.NewLine}" +
            $"Checks 12{Environment.NewLine}" +
            $"ReceiveTimeout 300{Environment.NewLine}" +
            $"TestDatabases yes{Environment.NewLine}";

        File.WriteAllText(configPath, configuration);
        return configPath;
    }

    private async Task<GitHubRelease> GetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken)
               ?? throw new InvalidDataException("Die ClamAV-Releaseinformationen konnten nicht gelesen werden.");
    }

    private static GitHubAsset SelectWindowsAsset(GitHubRelease release)
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            _ => throw new PlatformNotSupportedException(
                $"Die Windows-Architektur {RuntimeInformation.ProcessArchitecture} wird nicht unterstützt.")
        };

        return release.Assets.FirstOrDefault(asset =>
                   asset.Name.EndsWith($".win.{architecture}.zip", StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidDataException(
                   $"Im offiziellen ClamAV-Release wurde kein Windows-{architecture}-ZIP gefunden.");
    }

    private async Task DownloadAssetAsync(
        GitHubAsset asset,
        string destination,
        IProgress<EngineUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            asset.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destinationStream = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 128,
            useAsync: true);

        var buffer = new byte[1024 * 128];
        long downloaded = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;

            double? percentage = totalBytes > 0 ? downloaded * 100d / totalBytes : null;
            progress?.Report(new EngineUpdateProgress
            {
                Message = totalBytes > 0
                    ? $"Engine-Download: {downloaded / 1024d / 1024d:N0} von {totalBytes / 1024d / 1024d:N0} MB"
                    : $"Engine-Download: {downloaded / 1024d / 1024d:N0} MB",
                Percentage = percentage
            });
        }
    }

    private static async Task VerifyDigestAsync(
        string archivePath,
        string? digest,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(digest) ||
            !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Das offizielle Release enthält keine verwendbare SHA-256-Prüfsumme.");
        }

        await using var stream = File.OpenRead(archivePath);
        var actualBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        var actual = Convert.ToHexStringLower(actualBytes);
        var expected = digest["sha256:".Length..].Trim().ToLowerInvariant();

        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(expected)))
        {
            throw new InvalidDataException(
                "Die SHA-256-Prüfsumme des ClamAV-Downloads ist ungültig.");
        }
    }

    private void ReplaceManagedEngine(
        string extractedEngineRoot,
        string versionTag,
        GitHubAsset asset)
    {
        var backupDirectory = ManagedEngineDirectory + ".previous";
        TryDeleteDirectory(backupDirectory);

        try
        {
            if (Directory.Exists(ManagedEngineDirectory))
            {
                Directory.Move(ManagedEngineDirectory, backupDirectory);
            }

            Directory.Move(extractedEngineRoot, ManagedEngineDirectory);
            var metadata = new EngineMetadata
            {
                VersionTag = versionTag,
                AssetName = asset.Name,
                Digest = asset.Digest,
                InstalledAt = DateTimeOffset.Now
            };
            File.WriteAllText(
                Path.Combine(ManagedEngineDirectory, MetadataFileName),
                JsonSerializer.Serialize(metadata, JsonOptions));
            TryDeleteDirectory(backupDirectory);
        }
        catch
        {
            TryDeleteDirectory(ManagedEngineDirectory);
            if (Directory.Exists(backupDirectory))
            {
                Directory.Move(backupDirectory, ManagedEngineDirectory);
            }

            throw;
        }
    }

    private async Task<EngineMetadata?> LoadMetadataAsync()
    {
        var path = Path.Combine(ManagedEngineDirectory, MetadataFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<EngineMetadata>(stream, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureDataDirectories(string clamAvDirectory)
    {
        GetDatabaseDirectory();
        GetCvdCertificateDirectory(clamAvDirectory);
        EnsureFreshClamConfiguration(clamAvDirectory);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Cleanup is best-effort; install rollback still protects the active engine.
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; init; } = string.Empty;

        [JsonPropertyName("digest")]
        public string? Digest { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }
    }

    private sealed class EngineMetadata
    {
        public string VersionTag { get; init; } = string.Empty;
        public string AssetName { get; init; } = string.Empty;
        public string? Digest { get; init; }
        public DateTimeOffset InstalledAt { get; init; }
    }
}
