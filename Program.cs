namespace TrayInfo;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // Single instance: if another copy is already running, exit so we don't
        // stack duplicate icons in the tray.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\TrayInfo", out var isNewInstance);
        if (!isNewInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());

        GC.KeepAlive(mutex); // hold the mutex for the whole app lifetime
    }
}
