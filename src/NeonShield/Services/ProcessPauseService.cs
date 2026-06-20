using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NeonShield.Services;

internal static partial class ProcessPauseService
{
    [LibraryImport("ntdll.dll")]
    private static partial int NtSuspendProcess(nint processHandle);

    [LibraryImport("ntdll.dll")]
    private static partial int NtResumeProcess(nint processHandle);

    public static void Suspend(Process process)
    {
        if (process.HasExited)
        {
            throw new InvalidOperationException("Der Scanprozess ist bereits beendet.");
        }

        var status = NtSuspendProcess(process.Handle);
        if (status != 0)
        {
            throw new InvalidOperationException($"Der Scan konnte nicht pausiert werden (NTSTATUS 0x{status:X8}).");
        }
    }

    public static void Resume(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        var status = NtResumeProcess(process.Handle);
        if (status != 0)
        {
            throw new InvalidOperationException($"Der Scan konnte nicht fortgesetzt werden (NTSTATUS 0x{status:X8}).");
        }
    }
}
