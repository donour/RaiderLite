namespace RaiderLite
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application. Image paths on the command line are opened.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // An unexpected error in an event handler is reported instead of ending the program,
            // so open images (and unsaved edits) survive it.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ErrorReporter.Report(e.Exception, fatal: false);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorReporter.Report(e.ExceptionObject as Exception, fatal: true);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                ErrorReporter.Log(e.Exception);
                e.SetObserved();
            };

            ApplicationConfiguration.Initialize();
            AppSettings.Load();
            Application.Run(new MainForm(args));
        }
    }

    /// <summary>Logs unexpected errors to %APPDATA%\RaiderLite\error.log and tells the user.</summary>
    internal static class ErrorReporter
    {
        public static string LogPath { get; } = Path.Combine(AppSettings.SettingsDirectory, "error.log");

        public static void Log(Exception? ex)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.SettingsDirectory);
                File.AppendAllText(LogPath, $"[{DateTime.Now:O}] {ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
            {
                // Nowhere to log; the message box still shows.
            }
        }

        public static void Report(Exception? ex, bool fatal)
        {
            Log(ex);
            var message = fatal
                ? "RaiderLite hit an unexpected error and has to close."
                : "RaiderLite hit an unexpected error and the last operation may not have finished. Your images are still open: check the last change (Undo is available) and save your work.";
            try
            {
                MessageBox.Show($"{message}\n\n{ex?.GetType().Name}: {ex?.Message}\n\nDetails were written to {LogPath}",
                    "Unexpected Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (InvalidOperationException)
            {
                // No UI available (e.g. during shutdown).
            }
        }
    }
}
