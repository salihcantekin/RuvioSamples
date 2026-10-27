namespace RuvioExamples.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Scripted runs (--autostart) have nobody to click "Continue": write the error down and stop.
        if (Environment.GetCommandLineArgs().Contains("--autostart"))
        {
            Application.ThreadException += (_, e) =>
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "error.log"), e.Exception.ToString());
                Environment.Exit(1);
            };
        }

        Application.Run(new MainForm());
    }
}
