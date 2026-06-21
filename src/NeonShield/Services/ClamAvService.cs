using System.Diagnostics;
using System.IO;
using System.Text;
using NeonShield.Models;

namespace NeonShield.Services;

public sealed class ClamAvService
{
    private Process? _activeProcess;
    private readonly object _processGate = new();
    private bool _isPaused;

    public bool IsPaused
    {
        get
        {
            lock (_processGate)
            {
                return _isPaused;
            }
        }
    }

    public string? FindClamAvDirectory(string configuredDirectory)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            candidates.Add(configuredDirectory);
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Engine"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "clamav"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClamAV"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ClamAV"));

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        return candidates
            .Select(candidate => candidate.Trim().Trim('"'))
            .FirstOrDefault(IsUsableDirectory);
    }

    public bool IsUsableDirectory(string? directory) =>
        !string.IsNullOrWhiteSpace(directory) &&
        File.Exists(Path.Combine(directory, "clamscan.exe")) &&
        File.Exists(Path.Combine(directory, "freshclam.exe")) &&
        File.Exists(Path.Combine(directory, "certs", "clamav.crt"));

    public async Task<string> GetVersionAsync(string clamAvDirectory)
    {
        var executable = Path.Combine(clamAvDirectory, "clamscan.exe");
        var result = await RunUtilityAsync(executable, ["--version"], CancellationToken.None);
        return result.Trim();
    }

    public async Task<string> UpdateSignaturesAsync(string clamAvDirectory, CancellationToken cancellationToken)
    {
        var executable = Path.Combine(clamAvDirectory, "freshclam.exe");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("freshclam.exe wurde im gewählten ClamAV-Ordner nicht gefunden.");
        }

        var configPath = EngineManagerService.EnsureFreshClamConfiguration(clamAvDirectory);
        var databaseDirectory = EngineManagerService.GetDatabaseDirectory();
        var certificateDirectory = EngineManagerService.GetCvdCertificateDirectory(clamAvDirectory);
        return await RunUtilityAsync(
            executable,
            [
                $"--config-file={configPath}",
                $"--datadir={databaseDirectory}",
                $"--cvdcertsdir={certificateDirectory}",
                "--stdout"
            ],
            cancellationToken);
    }

    public async Task<ScanResult> ScanAsync(
        string clamAvDirectory,
        IReadOnlyList<string> targets,
        AppSettings settings,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken)
    {
        var executable = Path.Combine(clamAvDirectory, "clamscan.exe");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("clamscan.exe wurde im gewählten ClamAV-Ordner nicht gefunden.");
        }

        var validTargets = targets
            .Where(target => Directory.Exists(target) || File.Exists(target))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        string? fileListPath = null;
        try
        {
            if (validTargets.Count > 20)
            {
                fileListPath = Path.Combine(
                    Path.GetTempPath(),
                    $"neonshield-targets-{Guid.NewGuid():N}.txt");
                await File.WriteAllLinesAsync(fileListPath, validTargets, new UTF8Encoding(false), cancellationToken);
            }

            var startInfo = CreateScanStartInfo(executable, validTargets, fileListPath, settings);
            return await RunScanProcessAsync(
                startInfo,
                validTargets.FirstOrDefault() ?? "Dateisystem",
                progress,
                cancellationToken);
        }
        finally
        {
            if (fileListPath is not null)
            {
                try
                {
                    File.Delete(fileListPath);
                }
                catch
                {
                    // Temporary target lists contain paths only and are cleaned up best-effort.
                }
            }
        }
    }

    public async Task<ScanResult> ScanMemoryAsync(
        string clamAvDirectory,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken)
    {
        var executable = Path.Combine(clamAvDirectory, "clamscan.exe");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("clamscan.exe wurde im gewählten ClamAV-Ordner nicht gefunden.");
        }

        var startInfo = CreateBaseStartInfo(executable);
        startInfo.ArgumentList.Add($"--database={EngineManagerService.GetDatabaseDirectory()}");
        startInfo.ArgumentList.Add($"--cvdcertsdir={EngineManagerService.GetCvdCertificateDirectory(clamAvDirectory)}");
        startInfo.ArgumentList.Add("--memory");
        return await RunScanProcessAsync(
            startInfo,
            "Arbeitsspeicher laufender Prozesse",
            progress,
            cancellationToken);
    }

    public void CancelScan() => TryStopActiveProcess();

    public bool TogglePause()
    {
        lock (_processGate)
        {
            if (_activeProcess is null || _activeProcess.HasExited)
            {
                return false;
            }

            if (_isPaused)
            {
                ProcessPauseService.Resume(_activeProcess);
                _isPaused = false;
            }
            else
            {
                ProcessPauseService.Suspend(_activeProcess);
                _isPaused = true;
            }

            return _isPaused;
        }
    }

    private async Task<ScanResult> RunScanProcessAsync(
        ProcessStartInfo startInfo,
        string fallbackPath,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        var threats = new List<ThreatDetection>();
        var errors = new List<string>();
        long filesScanned = 0;
        var exitCode = 0;

        try
        {
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            lock (_processGate)
            {
                _activeProcess = process;
                _isPaused = false;
            }

            process.Start();
            var stdoutTask = ConsumeOutputAsync(
                process.StandardOutput,
                line =>
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        return;
                    }

                    if (TryParseThreat(line, out var threat))
                    {
                        threats.Add(threat);
                    }

                    if (line.EndsWith(" OK", StringComparison.OrdinalIgnoreCase) ||
                        line.EndsWith(" FOUND", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains(": Empty file", StringComparison.OrdinalIgnoreCase))
                    {
                        Interlocked.Increment(ref filesScanned);
                    }

                    progress.Report(new ScanProgress
                    {
                        CurrentPath = ExtractDisplayPath(line, fallbackPath),
                        FilesScanned = Interlocked.Read(ref filesScanned),
                        ThreatsFound = threats.Count,
                        TargetIndex = 1,
                        TargetCount = 1
                    });
                },
                cancellationToken);

            var stderrTask = ConsumeOutputAsync(
                process.StandardError,
                line =>
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        errors.Add(line);
                    }
                },
                cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask);
            exitCode = process.ExitCode;

            if (exitCode > 1)
            {
                errors.Add($"ClamAV beendete den Scan mit Code {exitCode}.");
            }
        }
        catch (OperationCanceledException)
        {
            TryStopActiveProcess();
            return new ScanResult
            {
                StartedAt = startedAt,
                FinishedAt = DateTimeOffset.Now,
                FilesScanned = filesScanned,
                Threats = threats,
                Errors = errors,
                WasCancelled = true,
                ExitCode = -1
            };
        }
        finally
        {
            lock (_processGate)
            {
                _activeProcess = null;
                _isPaused = false;
            }
        }

        return new ScanResult
        {
            StartedAt = startedAt,
            FinishedAt = DateTimeOffset.Now,
            FilesScanned = filesScanned,
            Threats = threats,
            Errors = errors,
            ExitCode = exitCode
        };
    }

    private static ProcessStartInfo CreateScanStartInfo(
        string executable,
        IReadOnlyList<string> targets,
        string? fileListPath,
        AppSettings settings)
    {
        var startInfo = CreateBaseStartInfo(executable);
        startInfo.ArgumentList.Add("--recursive=yes");
        startInfo.ArgumentList.Add("--max-filesize=100M");
        startInfo.ArgumentList.Add("--max-scansize=300M");
        startInfo.ArgumentList.Add("--max-recursion=30");
        startInfo.ArgumentList.Add($"--database={EngineManagerService.GetDatabaseDirectory()}");
        startInfo.ArgumentList.Add(
            $"--cvdcertsdir={EngineManagerService.GetCvdCertificateDirectory(Path.GetDirectoryName(executable)!)}");
        startInfo.ArgumentList.Add(settings.ScanArchives ? "--scan-archive=yes" : "--scan-archive=no");
        startInfo.ArgumentList.Add(settings.ScanPotentiallyUnwanted ? "--detect-pua=yes" : "--detect-pua=no");
        startInfo.ArgumentList.Add(@"--exclude-dir=\\System Volume Information$");
        startInfo.ArgumentList.Add(@"--exclude-dir=\\$Recycle\.Bin$");
        if (fileListPath is not null)
        {
            startInfo.ArgumentList.Add($"--file-list={fileListPath}");
        }
        else
        {
            foreach (var target in targets)
            {
                startInfo.ArgumentList.Add(target);
            }
        }

        return startInfo;
    }

    private static ProcessStartInfo CreateBaseStartInfo(string executable)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("--verbose");
        startInfo.ArgumentList.Add("--stdout");
        startInfo.ArgumentList.Add("--no-summary");
        return startInfo;
    }

    private static async Task ConsumeOutputAsync(
        StreamReader reader,
        Action<string> consume,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            consume(line);
        }
    }

    private static bool TryParseThreat(string line, out ThreatDetection detection)
    {
        detection = new ThreatDetection(string.Empty, string.Empty);
        const string suffix = " FOUND";
        if (!line.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var content = line[..^suffix.Length];
        var separator = content.LastIndexOf(": ", StringComparison.Ordinal);
        if (separator < 0)
        {
            return false;
        }

        var filePath = content[..separator].Trim();
        var signature = content[(separator + 2)..].Trim();
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        detection = new ThreatDetection(filePath, signature);
        return true;
    }

    private static string ExtractDisplayPath(string line, string fallback)
    {
        var separator = line.LastIndexOf(": ", StringComparison.Ordinal);
        return separator > 1 ? line[..separator] : fallback;
    }

    private static async Task<string> RunUtilityAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"\"{executable}\" konnte nicht gestartet werden.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode > 1)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);
        }

        return string.IsNullOrWhiteSpace(output) ? error : output;
    }

    private void TryStopActiveProcess()
    {
        try
        {
            lock (_processGate)
            {
                if (_activeProcess is { HasExited: false })
                {
                    if (_isPaused)
                    {
                        ProcessPauseService.Resume(_activeProcess);
                        _isPaused = false;
                    }

                    _activeProcess.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            // The process may have ended between checking and stopping it.
        }
    }
}
