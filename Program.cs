namespace DelayedClipboardApp;

/// <summary>
/// Entry point for the Delayed Clipboard App.
///
/// This application demonstrates Windows delayed clipboard rendering,
/// where clipboard data is not generated at copy time but instead
/// generated on-demand when another application requests it via paste.
/// </summary>
static class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// [STAThread] is required for clipboard operations and COM interop.
    /// </summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
