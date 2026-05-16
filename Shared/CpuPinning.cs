using System.Diagnostics;
using System.Runtime.Versioning;

namespace Shared;

/// <summary>
/// Pins the current process to a specific physical CPU core via
/// <see cref="Process.ProcessorAffinity"/>. Only meaningful on Windows;
/// on macOS / Linux it logs a notice and returns.
/// </summary>
public static class CpuPinning
{
    public static void PinCurrentProcessToCore(int coreIndex)
    {
        if (coreIndex < 0)
        {
            Console.WriteLine($"[CpuPinning] Negative core index ({coreIndex}); skipping pin.");
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[CpuPinning] Not running on Windows ({GetOsLabel()}). Skipping core pin (would have been core {coreIndex}).");
            return;
        }

        PinOnWindows(coreIndex);
    }

    [SupportedOSPlatform("windows")]
    private static void PinOnWindows(int coreIndex)
    {
        try
        {
            int total = Environment.ProcessorCount;
            int wrapped = coreIndex % total;
            nint mask = (nint)(1L << wrapped);

            var proc = Process.GetCurrentProcess();
            proc.ProcessorAffinity = mask;

            Console.WriteLine($"[CpuPinning] Pinned PID {proc.Id} to core {wrapped} (mask 0x{mask:X}).");
        }
        catch (Exception ex)
        {
            // Most common failure: insufficient privileges. Log and continue;
            // the assignment still works, just without true core pinning.
            Console.WriteLine($"[CpuPinning] Failed to pin to core {coreIndex}: {ex.Message}");
        }
    }

    private static string GetOsLabel()
    {
        if (OperatingSystem.IsMacOS())   return "macOS";
        if (OperatingSystem.IsLinux())   return "Linux";
        if (OperatingSystem.IsWindows()) return "Windows";
        return "unknown OS";
    }
}
