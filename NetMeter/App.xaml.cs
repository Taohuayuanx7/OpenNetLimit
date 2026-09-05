using System.Windows;

namespace NetMeter;

public partial class App : Application
{
    private static readonly string DiagPath = StoragePaths.DiagPath;

    protected override void OnStartup(StartupEventArgs e)
    {
        StoragePaths.Initialize();
        L.Init();
        LogLifecycle("APP STARTED");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogLifecycle($"APP EXIT code={e.ApplicationExitCode}");
        base.OnExit(e);
    }

    private static void LogLifecycle(string message)
    {
        try
        {
            StoragePaths.SafeAppend(DiagPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch
        {
        }
    }
}