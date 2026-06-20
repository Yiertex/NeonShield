using System.Windows;
using NeonShield.Services;

namespace NeonShield;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(argument => argument.Equals("--install-engine", StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await InstallEngineForSetupAsync();
            Shutdown(exitCode);
            return;
        }

        var previewMode = e.Args.Any(argument =>
            argument.Equals("--ui-preview", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(previewMode);
        MainWindow = window;
        window.Show();
    }

    private static async Task<int> InstallEngineForSetupAsync()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NeonShield",
            "engine-install.log");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            var engineManager = new EngineManagerService();
            var result = await engineManager.EnsureLatestEngineAsync(progress: null, CancellationToken.None);

            var clamAv = new ClamAvService();
            var signatureResult = await clamAv.UpdateSignaturesAsync(
                result.EngineDirectory,
                CancellationToken.None);

            await File.WriteAllTextAsync(
                logPath,
                $"{DateTimeOffset.Now:O}{Environment.NewLine}" +
                $"Engine: {result.VersionTag} ({result.Status}){Environment.NewLine}" +
                signatureResult);
            return 0;
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(logPath, $"{DateTimeOffset.Now:O}{Environment.NewLine}{ex}");
            return 1;
        }
    }
}
