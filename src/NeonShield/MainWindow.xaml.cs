using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NeonShield.Models;
using NeonShield.Services;
using Forms = System.Windows.Forms;

namespace NeonShield;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private readonly QuarantineService _quarantineService = new();
    private readonly ScanHistoryService _scanHistoryService = new();
    private readonly ClamAvService _clamAvService = new();
    private readonly EngineManagerService _engineManager = new();
    private readonly VirusTotalService _virusTotalService = new();
    private readonly AsyncPauseGate _pauseGate = new();
    private readonly DispatcherTimer _scanTimer;
    private readonly bool _skipStartupUpdates;
    private AppSettings _settings = new();
    private CancellationTokenSource? _scanCancellation;
    private DateTimeOffset _scanStartedAt;
    private string? _clamAvDirectory;
    private long _lastFilesScanned;
    private bool _isUpdating;
    private bool _isScanPaused;
    private DateTimeOffset? _pauseStartedAt;
    private TimeSpan _totalPausedDuration;
    private string _engineVersion = string.Empty;

    public MainWindow(bool skipStartupUpdates = false)
    {
        InitializeComponent();
        _skipStartupUpdates = skipStartupUpdates;
        _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _scanTimer.Tick += (_, _) => UpdateElapsedTime();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await _settingsService.LoadAsync();
        ClamAvPathTextBox.Text = _settings.ClamAvDirectory;
        AutoQuarantineCheckBox.IsChecked = _settings.AutoQuarantine;
        ScanArchivesCheckBox.IsChecked = _settings.ScanArchives;
        ScanPuaCheckBox.IsChecked = _settings.ScanPotentiallyUnwanted;
        OnlineReputationCheckBox.IsChecked = _settings.EnableOnlineReputation;
        VirusTotalApiKeyBox.Password =
            SecretProtectionService.Unprotect(_settings.VirusTotalApiKeyProtected);

        await RefreshQuarantineAsync();
        await RefreshReportsAsync();
        if (_skipStartupUpdates)
        {
            ApplyPreviewState();
        }
        else
        {
            await CheckForUpdatesAsync();
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _scanCancellation?.Cancel();
        Close();
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void DashboardNav_Click(object sender, RoutedEventArgs e) => ShowPage(DashboardPage, DashboardNav);
    private void ScanNav_Click(object sender, RoutedEventArgs e) => ShowPage(ScanPage, ScanNav);
    private void ReportsNav_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(ReportsPage, ReportsNav);
        _ = RefreshReportsAsync();
    }
    private void QuarantineNav_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(QuarantinePage, QuarantineNav);
        _ = RefreshQuarantineAsync();
    }
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage, SettingsNav);

    private void ShowPage(UIElement page, FrameworkElement navButton)
    {
        DashboardPage.Visibility = Visibility.Collapsed;
        ScanPage.Visibility = Visibility.Collapsed;
        ReportsPage.Visibility = Visibility.Collapsed;
        QuarantinePage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;

        DashboardNav.Tag = null;
        ScanNav.Tag = null;
        ReportsNav.Tag = null;
        QuarantineNav.Tag = null;
        SettingsNav.Tag = null;
        navButton.Tag = "Active";
    }

    private async Task DetectEngineAsync()
    {
        _clamAvDirectory = _clamAvService.FindClamAvDirectory(_settings.ClamAvDirectory);
        if (_clamAvDirectory is null)
        {
            SetEngineState(false, "Engine nicht verbunden", "Automatischer Download fehlgeschlagen");
            ProtectionTitle.Text = "Engine konnte nicht geladen werden";
            ProtectionSubtitle.Text = "Prüfe die Internetverbindung oder wähle einen vorhandenen ClamAV-Ordner aus.";
            DatabaseStatusText.Text = "ClamAV ist nicht verfügbar.";
            SignatureStatusDashboardText.Text = "Offline";
            QuickScanDashboardButton.IsEnabled = false;
            UpdateButton.IsEnabled = false;
            return;
        }

        try
        {
            var version = await _clamAvService.GetVersionAsync(_clamAvDirectory);
            _engineVersion = version;
            SetEngineState(true, "System geschützt", version);
            ProtectionTitle.Text = "Schutz ist einsatzbereit";
            ProtectionSubtitle.Text = "Die ClamAV-Engine ist verbunden. Starte einen Scan, um dein System zu prüfen.";
            DatabaseStatusText.Text = version;
            SignatureStatusDashboardText.Text = "Bereit";
            QuickScanDashboardButton.IsEnabled = true;
            UpdateButton.IsEnabled = true;
            if (string.IsNullOrWhiteSpace(_settings.ClamAvDirectory))
            {
                ClamAvPathTextBox.Text = _clamAvDirectory;
            }
        }
        catch (Exception ex)
        {
            SetEngineState(false, "Engine-Fehler", Shorten(ex.Message, 60));
            QuickScanDashboardButton.IsEnabled = false;
            UpdateButton.IsEnabled = false;
        }
    }

    private void ApplyPreviewState()
    {
        _clamAvDirectory = AppContext.BaseDirectory;
        _engineVersion = "ClamAV Vorschau";
        SetEngineState(true, "System geschützt", "ClamAV Engine bereit");
        ProtectionTitle.Text = "Schutz ist einsatzbereit";
        ProtectionSubtitle.Text = "Engine und Signaturen sind aktuell. Starte einen Scan, um dein System zu prüfen.";
        DatabaseStatusText.Text = "Signaturen aktuell · heute 17:45";
        SignatureStatusDashboardText.Text = "Aktuell";
        QuickScanDashboardButton.IsEnabled = true;
        UpdateButton.IsEnabled = true;
    }

    private void SetEngineState(bool connected, string title, string subtitle)
    {
        var color = connected ? Color.FromRgb(80, 235, 178) : Color.FromRgb(255, 106, 141);
        var brush = new SolidColorBrush(color);
        EngineDot.Fill = brush;
        HeaderStatusDot.Fill = brush;
        EngineStatusText.Text = title;
        EngineVersionText.Text = subtitle;
        HeaderStatusText.Text = connected ? "Schutz aktiv" : "Einrichtung erforderlich";
    }

    private async void QuickScan_Click(object sender, RoutedEventArgs e) =>
        await StartScanAsync(ScanKind.Quick, GetQuickScanTargets());

    private async void DeepScan_Click(object sender, RoutedEventArgs e) =>
        await StartScanAsync(ScanKind.Deep, GetDeepScanTargets());

    private async void QuickScanCard_Click(object sender, MouseButtonEventArgs e) =>
        await StartScanAsync(ScanKind.Quick, GetQuickScanTargets());

    private async void DeepScanCard_Click(object sender, MouseButtonEventArgs e) =>
        await StartScanAsync(ScanKind.Deep, GetDeepScanTargets());

    private async void CustomScanCard_Click(object sender, MouseButtonEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Ordner für den Malware-Scan auswählen",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            await StartScanAsync(ScanKind.Custom, [dialog.SelectedPath]);
        }
    }

    private async void MemoryScanCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (!IsRunningAsAdministrator())
        {
            ShowMessage(
                "Der ClamAV-Arbeitsspeicher-Scan benötigt Administratorrechte. " +
                "Starte NeonShield über „Als Administrator ausführen“ erneut.",
                isError: true);
            return;
        }

        await StartScanAsync(ScanKind.Memory, ["Arbeitsspeicher laufender Prozesse"]);
    }

    private async void ProcessScanCard_Click(object sender, MouseButtonEventArgs e)
    {
        var targets = GetRunningProcessTargets();
        if (targets.Count == 0)
        {
            ShowMessage("Es konnten keine ausführbaren Dateien laufender Prozesse gelesen werden.", isError: true);
            return;
        }

        await StartScanAsync(ScanKind.Processes, targets);
    }

    private async Task StartScanAsync(ScanKind kind, IReadOnlyList<string> targets)
    {
        if (_scanCancellation is not null)
        {
            return;
        }

        if (_clamAvDirectory is null)
        {
            ShowPage(SettingsPage, SettingsNav);
            SettingsFeedbackText.Text = "Bitte zuerst den ClamAV-Ordner auswählen.";
            return;
        }

        var validTargets = kind == ScanKind.Memory
            ? targets.ToList()
            : targets.Where(path => Directory.Exists(path) || File.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (validTargets.Count == 0)
        {
            ShowMessage("Keine gültigen Scan-Ziele gefunden.", isError: true);
            return;
        }

        ShowPage(ScanPage, ScanNav);
        _scanCancellation = new CancellationTokenSource();
        _scanStartedAt = DateTimeOffset.Now;
        _totalPausedDuration = TimeSpan.Zero;
        _pauseStartedAt = null;
        _isScanPaused = false;
        _pauseGate.Resume();
        _scanTimer.Start();
        SetScanningUi(true);
        ScanStatusTitle.Text = kind switch
        {
            ScanKind.Quick => "Schnellscan läuft",
            ScanKind.Deep => "Tiefenscan läuft",
            ScanKind.Custom => "Benutzerdefinierter Scan läuft",
            ScanKind.Memory => "Arbeitsspeicher-Scan läuft",
            _ => "Laufende Prozesse werden geprüft"
        };
        ScanStatusSubtitle.Text = $"{validTargets.Count} Bereich(e) werden mit ClamAV geprüft.";
        FilesScannedText.Text = "0";
        ThreatsFoundText.Text = "0";
        ScanDurationText.Text = "00:00";
        ScanProgressBar.Value = 0;
        ScanProgressBar.IsIndeterminate = false;
        var report = new ScanReport
        {
            Kind = kind,
            Status = ScanReportStatus.Completed,
            StartedAt = _scanStartedAt,
            Targets = validTargets.ToList(),
            EngineVersion = _engineVersion
        };

        var progress = new Progress<ScanProgress>(scanProgress =>
        {
            if (!scanProgress.CurrentPath.StartsWith("Online-Abgleich:", StringComparison.Ordinal))
            {
                _lastFilesScanned = Math.Max(_lastFilesScanned, scanProgress.FilesScanned);
            }

            FilesScannedText.Text = _lastFilesScanned.ToString("N0");
            ThreatsFoundText.Text = scanProgress.ThreatsFound.ToString("N0");
            CurrentFileText.Text = scanProgress.CurrentPath;
            ScanProgressBar.Value = scanProgress.TargetCount == 0
                ? 0
                : Math.Clamp((double)(scanProgress.TargetIndex - 1) / scanProgress.TargetCount * 100 + 5, 0, 95);
        });

        try
        {
            _lastFilesScanned = 0;
            var result = kind == ScanKind.Memory
                ? await _clamAvService.ScanMemoryAsync(
                    _clamAvDirectory,
                    progress,
                    _scanCancellation.Token)
                : await _clamAvService.ScanAsync(
                    _clamAvDirectory,
                    validTargets,
                    _settings,
                    progress,
                    _scanCancellation.Token);

            var quarantineFailures = new List<string>();
            var quarantinedCount = 0;
            if (_settings.AutoQuarantine &&
                !result.WasCancelled &&
                kind is ScanKind.Quick or ScanKind.Deep or ScanKind.Custom)
            {
                foreach (var threat in result.Threats)
                {
                    if (!File.Exists(threat.FilePath))
                    {
                        quarantineFailures.Add($"{threat.FilePath}: Datei ist nicht direkt isolierbar.");
                        continue;
                    }

                    try
                    {
                        if (await _quarantineService.QuarantineAsync(threat) is not null)
                        {
                            quarantinedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        quarantineFailures.Add($"{threat.FilePath}: {ex.Message}");
                    }
                }
            }

            report.FilesScanned = result.FilesScanned;
            report.Threats = result.Threats;
            report.Errors = result.Errors;
            report.QuarantinedCount = quarantinedCount;
            report.QuarantineFailures = quarantineFailures;

            var onlineResults = new List<OnlineReputationResult>();
            var apiKey = VirusTotalApiKeyBox.Password.Trim();
            if (kind == ScanKind.Processes &&
                _settings.EnableOnlineReputation &&
                !string.IsNullOrWhiteSpace(apiKey) &&
                !result.WasCancelled)
            {
                ScanStatusTitle.Text = "Online-Reputation wird geprüft";
                ScanStatusSubtitle.Text = "Es werden ausschließlich SHA-256-Hashes an VirusTotal übertragen.";
                onlineResults = await _virusTotalService.CheckFilesAsync(
                    validTargets,
                    apiKey,
                    progress,
                    _pauseGate,
                    _scanCancellation.Token);
            }

            _lastFilesScanned = result.FilesScanned;
            report.Status = result.WasCancelled
                ? ScanReportStatus.Cancelled
                : ScanReportStatus.Completed;
            report.FinishedAt = DateTimeOffset.Now;
            report.PausedDuration = GetTotalPausedDuration();
            report.OnlineResults = onlineResults;
            await _scanHistoryService.SaveAsync(report);

            UpdateScanResultUi(result, quarantineFailures);
            await RefreshQuarantineAsync();
            await RefreshReportsAsync(report.Id);
            ShowPage(ReportsPage, ReportsNav);
        }
        catch (OperationCanceledException)
        {
            report.Status = ScanReportStatus.Cancelled;
            report.FinishedAt = DateTimeOffset.Now;
            report.PausedDuration = GetTotalPausedDuration();
            report.FilesScanned = _lastFilesScanned;
            report.Errors.Add("Der Scan wurde vom Benutzer abgebrochen.");
            await _scanHistoryService.SaveAsync(report);
            ScanStatusTitle.Text = "Scan abgebrochen";
            ScanStatusSubtitle.Text = $"{_lastFilesScanned:N0} Dateien wurden bis zum Abbruch geprüft.";
            CurrentFileText.Text = "Scan wurde vom Benutzer abgebrochen.";
            await RefreshReportsAsync(report.Id);
            ShowPage(ReportsPage, ReportsNav);
        }
        catch (Exception ex)
        {
            report.Status = ScanReportStatus.Failed;
            report.FinishedAt = DateTimeOffset.Now;
            report.PausedDuration = GetTotalPausedDuration();
            report.FilesScanned = _lastFilesScanned;
            report.Errors.Add(ex.Message);
            await _scanHistoryService.SaveAsync(report);
            ScanStatusTitle.Text = "Scan konnte nicht gestartet werden";
            ScanStatusSubtitle.Text = Shorten(ex.Message, 180);
            CurrentFileText.Text = "Prüfe die ClamAV-Konfiguration in den Einstellungen.";
            await RefreshReportsAsync(report.Id);
            ShowPage(ReportsPage, ReportsNav);
        }
        finally
        {
            if (_isScanPaused)
            {
                _pauseGate.Resume();
            }

            _isScanPaused = false;
            _pauseStartedAt = null;
            _scanTimer.Stop();
            _scanCancellation.Dispose();
            _scanCancellation = null;
            SetScanningUi(false);
        }
    }

    private void UpdateScanResultUi(ScanResult result, IReadOnlyCollection<string> quarantineFailures)
    {
        ScanProgressBar.Value = result.WasCancelled ? ScanProgressBar.Value : 100;
        FilesScannedText.Text = result.FilesScanned.ToString("N0");
        ThreatsFoundText.Text = result.Threats.Count.ToString("N0");
        FilesScannedDashboardText.Text = result.FilesScanned.ToString("N0");
        LastScanThreatsText.Text = result.Threats.Count.ToString();
        LastScanTimeText.Text = result.FinishedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm");
        CurrentFileText.Text = result.WasCancelled ? "Scan wurde vom Benutzer abgebrochen." : "Scan abgeschlossen.";

        if (result.WasCancelled)
        {
            ScanStatusTitle.Text = "Scan abgebrochen";
            ScanStatusSubtitle.Text = $"{result.FilesScanned:N0} Dateien wurden bis zum Abbruch geprüft.";
        }
        else if (result.Threats.Count == 0)
        {
            ScanStatusTitle.Text = "Keine Bedrohungen gefunden";
            ScanStatusSubtitle.Text = $"{result.FilesScanned:N0} Dateien wurden erfolgreich geprüft.";
            ProtectionTitle.Text = "Keine aktiven Bedrohungen";
            ProtectionSubtitle.Text = $"Der letzte Scan am {result.FinishedAt.LocalDateTime:dd.MM.yyyy} war sauber.";
        }
        else
        {
            ScanStatusTitle.Text = $"{result.Threats.Count} Bedrohung(en) erkannt";
            ScanStatusSubtitle.Text = _settings.AutoQuarantine
                ? $"{result.Threats.Count - quarantineFailures.Count} Fund/Funde wurden isoliert."
                : "Die Funde wurden protokolliert und nicht verändert.";
            ProtectionTitle.Text = "Bedrohungen wurden erkannt";
            ProtectionSubtitle.Text = _settings.AutoQuarantine
                ? "Die erkannten Dateien wurden in die Quarantäne verschoben."
                : "Prüfe die Scanergebnisse und aktiviere bei Bedarf die automatische Quarantäne.";
        }

        if (result.Errors.Count > 0 || quarantineFailures.Count > 0)
        {
            ScanStatusSubtitle.Text += $" {result.Errors.Count + quarantineFailures.Count} Hinweis(e) wurden protokolliert.";
        }
    }

    private void SetScanningUi(bool isScanning)
    {
        PauseScanButton.Visibility = isScanning ? Visibility.Visible : Visibility.Collapsed;
        CancelScanButton.Visibility = isScanning ? Visibility.Visible : Visibility.Collapsed;
        DashboardNav.IsEnabled = !isScanning && !_isUpdating;
        ReportsNav.IsEnabled = !isScanning && !_isUpdating;
        QuarantineNav.IsEnabled = !isScanning && !_isUpdating;
        SettingsNav.IsEnabled = !isScanning && !_isUpdating;
        QuickScanDashboardButton.IsEnabled = !isScanning && !_isUpdating && _clamAvDirectory is not null;
        UpdateButton.IsEnabled = !isScanning && !_isUpdating;
    }

    private void PauseScan_Click(object sender, RoutedEventArgs e)
    {
        if (_scanCancellation is null)
        {
            return;
        }

        try
        {
            if (_isScanPaused)
            {
                if (_clamAvService.IsPaused)
                {
                    _clamAvService.TogglePause();
                }

                _pauseGate.Resume();
                _isScanPaused = false;
                if (_pauseStartedAt is not null)
                {
                    _totalPausedDuration += DateTimeOffset.Now - _pauseStartedAt.Value;
                    _pauseStartedAt = null;
                }

                PauseScanButton.Content = "Pausieren";
                ScanStatusSubtitle.Text = "Der Scan wurde fortgesetzt.";
            }
            else
            {
                _clamAvService.TogglePause();
                _pauseGate.Pause();
                _isScanPaused = true;
                _pauseStartedAt = DateTimeOffset.Now;
                PauseScanButton.Content = "Fortsetzen";
                ScanStatusSubtitle.Text = "Scan pausiert. Es werden momentan keine Daten geprüft.";
            }
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, isError: true);
        }
    }

    private void CancelScan_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanPaused)
        {
            _pauseGate.Resume();
        }

        _scanCancellation?.Cancel();
        _clamAvService.CancelScan();
        ScanStatusSubtitle.Text = "Der laufende Prozess wird beendet …";
    }

    private void UpdateElapsedTime()
    {
        var elapsed = DateTimeOffset.Now - _scanStartedAt - GetTotalPausedDuration();
        ScanDurationText.Text = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
    }

    private TimeSpan GetTotalPausedDuration() =>
        _totalPausedDuration +
        (_pauseStartedAt is null ? TimeSpan.Zero : DateTimeOffset.Now - _pauseStartedAt.Value);

    private static IReadOnlyList<string> GetQuickScanTargets()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(userProfile, "Downloads"),
            Path.GetTempPath()
        };
    }

    private static IReadOnlyList<string> GetDeepScanTargets() =>
        DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
            .Select(drive => drive.RootDirectory.FullName)
            .ToList();

    private static IReadOnlyList<string> GetRunningProcessTargets()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        paths.Add(path);
                    }
                }
                catch
                {
                    // Protected system processes are expected to reject access.
                }
            }
        }

        return paths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private async void UpdateSignatures_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_isUpdating || _scanCancellation is not null)
        {
            return;
        }

        _isUpdating = true;
        UpdateButton.IsEnabled = false;
        QuickScanDashboardButton.IsEnabled = false;
        SignatureStatusDashboardText.Text = "Prüfung";
        DatabaseStatusText.Text = "Engine-Updates werden geprüft …";

        try
        {
            string? engineUpdateWarning = null;
            if (_engineManager.IsManagedDirectory(_settings.ClamAvDirectory))
            {
                try
                {
                    var progress = new Progress<EngineUpdateProgress>(update =>
                    {
                        DatabaseStatusText.Text = update.Message;
                    });

                    var engineResult = await _engineManager.EnsureLatestEngineAsync(
                        progress,
                        CancellationToken.None);
                    _settings.ClamAvDirectory = engineResult.EngineDirectory;
                    ClamAvPathTextBox.Text = engineResult.EngineDirectory;
                    await _settingsService.SaveAsync(_settings);

                    DatabaseStatusText.Text = engineResult.Status switch
                    {
                        EngineUpdateStatus.Installed => "ClamAV wurde installiert. Signaturen werden geladen …",
                        EngineUpdateStatus.Updated => "ClamAV wurde aktualisiert. Signaturen werden geprüft …",
                        _ => "Engine ist aktuell. Signaturen werden geprüft …"
                    };
                }
                catch (Exception ex)
                {
                    engineUpdateWarning = Shorten(ex.Message, 70);
                }
            }

            await DetectEngineAsync();
            if (_clamAvDirectory is null)
            {
                throw new InvalidOperationException(
                    engineUpdateWarning ?? "Die ClamAV-Engine ist nicht verfügbar.");
            }

            var output = await _clamAvService.UpdateSignaturesAsync(
                _clamAvDirectory,
                CancellationToken.None);
            var signatureStatus = output.Contains("up-to-date", StringComparison.OrdinalIgnoreCase)
                ? "Datenbank ist bereits aktuell."
                : "Signaturen wurden aktualisiert.";
            DatabaseStatusText.Text = engineUpdateWarning is null
                ? signatureStatus
                : $"{signatureStatus} Engine-Prüfung fehlgeschlagen: {engineUpdateWarning}";
            SignatureStatusDashboardText.Text = "Aktuell";
        }
        catch (Exception ex)
        {
            await DetectEngineAsync();
            DatabaseStatusText.Text = $"Update nicht möglich: {Shorten(ex.Message, 82)}";
            SignatureStatusDashboardText.Text = "Fehler";
        }
        finally
        {
            _isUpdating = false;
            SetScanningUi(isScanning: false);
        }
    }

    private async Task RefreshReportsAsync(Guid? selectReportId = null)
    {
        var reports = await _scanHistoryService.GetAllAsync();
        ReportsList.ItemsSource = reports;
        ReportsEmptyState.Visibility = reports.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (reports.Count > 0)
        {
            var latest = reports[0];
            LastScanThreatsText.Text = latest.Threats.Count.ToString();
            LastScanTimeText.Text = latest.FinishedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm");
            FilesScannedDashboardText.Text = latest.FilesScanned.ToString("N0");

            var selected = selectReportId is null
                ? ReportsList.SelectedItem as ScanReport ?? latest
                : reports.FirstOrDefault(report => report.Id == selectReportId) ?? latest;
            ReportsList.SelectedItem = selected;
            ReportsList.ScrollIntoView(selected);
            ShowReportDetails(selected);
        }
        else
        {
            ReportDetailPanel.Visibility = Visibility.Collapsed;
            ReportDetailEmptyState.Visibility = Visibility.Visible;
        }
    }

    private async void RefreshReports_Click(object sender, RoutedEventArgs e) =>
        await RefreshReportsAsync();

    private void ReportsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ReportsList.SelectedItem is ScanReport report)
        {
            ShowReportDetails(report);
        }
    }

    private void ShowReportDetails(ScanReport report)
    {
        ReportDetailEmptyState.Visibility = Visibility.Collapsed;
        ReportDetailPanel.Visibility = Visibility.Visible;
        ReportTitleText.Text = report.KindDisplay;
        ReportDateText.Text =
            $"{report.StartedAt.LocalDateTime:dd.MM.yyyy HH:mm:ss} bis {report.FinishedAt.LocalDateTime:HH:mm:ss}";
        ReportStatusText.Text = report.StatusDisplay;
        ReportFilesText.Text = report.FilesScanned.ToString("N0");
        ReportThreatsText.Text = report.Threats.Count.ToString("N0");
        ReportQuarantinedText.Text = report.QuarantinedCount.ToString("N0");
        ReportDurationText.Text = report.DurationDisplay;
        ReportThreatList.ItemsSource = report.Threats;
        ReportTargetsList.ItemsSource = report.Targets;
        ReportErrorsList.ItemsSource = report.Errors
            .Concat(report.QuarantineFailures)
            .DefaultIfEmpty("Keine Hinweise oder Fehler.")
            .ToList();
        ReportOnlineList.ItemsSource = report.OnlineResults.Count > 0
            ? report.OnlineResults
            : [new OnlineReputationResult { Error = "Für diesen Scan wurden keine Online-Abfragen durchgeführt." }];
        ReportEngineText.Text =
            $"{report.EngineVersion} · Pausenzeit {FormatDuration(report.PausedDuration)}";

        var statusColor = report.Status switch
        {
            ScanReportStatus.Completed when report.Threats.Count == 0 => Color.FromRgb(80, 235, 178),
            ScanReportStatus.Completed => Color.FromRgb(255, 127, 171),
            ScanReportStatus.Cancelled => Color.FromRgb(245, 190, 80),
            _ => Color.FromRgb(255, 106, 141)
        };
        ReportStatusText.Foreground = new SolidColorBrush(statusColor);
        ReportStatusBadge.BorderBrush = new SolidColorBrush(statusColor);
    }

    private async void DeleteReport_Click(object sender, RoutedEventArgs e)
    {
        if (ReportsList.SelectedItem is not ScanReport report)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Den Scanbericht vom {report.StartedAt.LocalDateTime:dd.MM.yyyy HH:mm} löschen?",
            "Scanbericht löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await _scanHistoryService.DeleteAsync(report.Id);
        await RefreshReportsAsync();
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss")
            : duration.ToString(@"mm\:ss");

    private async Task RefreshQuarantineAsync()
    {
        var records = await _quarantineService.GetAllAsync();
        QuarantineList.ItemsSource = records.OrderByDescending(item => item.QuarantinedAt).ToList();
        QuarantineEmptyState.Visibility = records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        QuarantineCountDashboardText.Text = records.Count.ToString();
    }

    private async void RefreshQuarantine_Click(object sender, RoutedEventArgs e) => await RefreshQuarantineAsync();

    private async void RestoreQuarantine_Click(object sender, RoutedEventArgs e)
    {
        if (QuarantineList.SelectedItem is not QuarantineRecord record)
        {
            ShowMessage("Bitte zuerst einen Quarantäne-Eintrag auswählen.", isError: true);
            return;
        }

        var answer = MessageBox.Show(
            $"Die Datei wird an ihren ursprünglichen Ort zurückverschoben:\n\n{record.OriginalPath}\n\nNur wiederherstellen, wenn du der Datei vertraust.",
            "Datei wiederherstellen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _quarantineService.RestoreAsync(record.Id);
            await RefreshQuarantineAsync();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, isError: true);
        }
    }

    private async void DeleteQuarantine_Click(object sender, RoutedEventArgs e)
    {
        if (QuarantineList.SelectedItem is not QuarantineRecord record)
        {
            ShowMessage("Bitte zuerst einen Quarantäne-Eintrag auswählen.", isError: true);
            return;
        }

        var answer = MessageBox.Show(
            $"\"{record.ThreatName}\" endgültig löschen?\n\nDieser Vorgang kann nicht rückgängig gemacht werden.",
            "Bedrohung endgültig löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _quarantineService.DeleteAsync(record.Id);
            await RefreshQuarantineAsync();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, isError: true);
        }
    }

    private void BrowseClamAv_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "ClamAV-Installationsordner auswählen",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(ClamAvPathTextBox.Text) ? ClamAvPathTextBox.Text : string.Empty
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            ClamAvPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var selectedDirectory = ClamAvPathTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(selectedDirectory) &&
            !File.Exists(Path.Combine(selectedDirectory, "clamscan.exe")))
        {
            SettingsFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(255, 106, 141));
            SettingsFeedbackText.Text = "clamscan.exe wurde dort nicht gefunden.";
            return;
        }

        if (OnlineReputationCheckBox.IsChecked == true &&
            string.IsNullOrWhiteSpace(VirusTotalApiKeyBox.Password))
        {
            SettingsFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(255, 106, 141));
            SettingsFeedbackText.Text = "Für den Online-Abgleich wird ein VirusTotal API-Schlüssel benötigt.";
            return;
        }

        _settings.ClamAvDirectory = selectedDirectory;
        _settings.AutoQuarantine = AutoQuarantineCheckBox.IsChecked == true;
        _settings.ScanArchives = ScanArchivesCheckBox.IsChecked == true;
        _settings.ScanPotentiallyUnwanted = ScanPuaCheckBox.IsChecked == true;
        _settings.EnableOnlineReputation = OnlineReputationCheckBox.IsChecked == true;
        _settings.VirusTotalApiKeyProtected =
            SecretProtectionService.Protect(VirusTotalApiKeyBox.Password);
        await _settingsService.SaveAsync(_settings);

        SettingsFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(98, 233, 189));
        SettingsFeedbackText.Text = "Gespeichert.";
        await DetectEngineAsync();
    }

    private static string Shorten(string value, int maxLength)
    {
        var singleLine = value.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= maxLength ? singleLine : singleLine[..(maxLength - 1)] + "…";
    }

    private void ShowMessage(string message, bool isError)
    {
        MessageBox.Show(
            this,
            message,
            "NeonShield",
            MessageBoxButton.OK,
            isError ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }
}
