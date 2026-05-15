using System.Diagnostics;
using System.Runtime.Versioning;

namespace Shared;

/// <summary>
/// Wraps Process.ProcessorAffinity so callers don't have to deal with the platform check.
/// Affinity is Windows-only in .NET; on macOS/Linux this is a no-op (logged, not silent).
/// </summary>
public static class CpuPinning
{
    /// <summary>
    /// Pins the current process to a single CPU core if the OS supports it.
    /// </summary>
    /// <param name="coreIndex">Zero-based core index.</param>
    /// <returns>True if pinning actually happened, false if it was skipped or failed.</returns>
    public static bool TryPinCurrentProcessToCore(int coreIndex)
    {
        if (coreIndex < 0 || coreIndex >= Environment.ProcessorCount)
        {
            Console.WriteLine($"[CpuPinning] Core index {coreIndex} out of range (machine has {Environment.ProcessorCount} cores). Skipping.");
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            // ProcessorAffinity throws PlatformNotSupportedException on macOS/Linux for setters.
            // Document the skip so the testing report is honest about what ran.
            Console.WriteLine($"[CpuPinning] Not running on Windows ({RuntimeInformation()}). Skipping core pin (would have been core {coreIndex}).");
            return false;
        }

        return PinWindows(coreIndex);
    }

    [SupportedOSPlatform("windows")]
    private static bool PinWindows(int coreIndex)
    {
        try
        {
            var mask = (IntPtr)(1L << coreIndex);
            Process.GetCurrentProcess().ProcessorAffinity = mask;
            Console.WriteLine($"[CpuPinning] Pinned to core {coreIndex} (mask 0x{mask.ToInt64():X}).");
            return true;
        }
        catch (Exception ex)
        {
            // Some Windows configurations need admin to change affinity.
            Console.WriteLine($"[CpuPinning] Failed to set affinity: {ex.Message}");
            return false;
        }
    }

    private static string RuntimeInformation()
    {
        if (OperatingSystem.IsMacOS()) return "macOS";
        if (OperatingSystem.IsLinux()) return "Linux";
        return "unknown OS";
    }
}
