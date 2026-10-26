using System.Runtime.InteropServices;

namespace WindowsOptimizer.Core;

internal static class PowerCapabilities
{
    private const int SystemPowerCapabilities = 4;

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(
        int informationLevel, IntPtr inputBuffer, uint inputBufferLength, IntPtr outputBuffer, uint outputBufferLength);

    /// <summary>
    /// Whether the firmware offers the hibernate (S4) sleep state. Virtual machines often don't, and there
    /// <c>powercfg /hibernate on</c> fails, so the hibernation tweak would be one-way.
    /// </summary>
    public static bool SupportsHibernate()
    {
        const int size = 512; // SYSTEM_POWER_CAPABILITIES is ~76 bytes; we only read the first nine flags.
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (CallNtPowerInformation(SystemPowerCapabilities, IntPtr.Zero, 0, buffer, size) != 0) return true;

            // Layout: PowerButton, SleepButton, Lid, S1, S2, S3, S4, S5, HiberFilePresent (one byte each).
            return Marshal.ReadByte(buffer, 6) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
