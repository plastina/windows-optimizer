using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using Velopack;

namespace WindowsOptimizer.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must come first. The installer, the updater and the uninstaller start the exe with their own
        // arguments; Velopack handles those here and exits. It also installs an update that was downloaded
        // last time but not applied yet.
        VelopackApp.Build().Run();

#if !DEBUG
        // The exe is "asInvoker" so the installer and the updater can start it (a requireAdministrator exe
        // can't be started by a normal process). The tweaks write to HKLM, so ask for admin rights here instead.
        if (!IsElevated() && RelaunchElevated(args)) return 0;
#endif

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static bool IsElevated() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Starts the exe again through UAC. False when the user said no, so the app opens without admin rights and says so.</summary>
    private static bool RelaunchElevated(string[] args)
    {
        try
        {
            var arguments = string.Join(" ", args.Select(a => $"\"{a.Replace("\"", "\\\"")}\""));
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, arguments) { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
