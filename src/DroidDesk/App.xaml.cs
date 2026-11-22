using System.Windows;

namespace DroidDesk;

public partial class App : Application
{
    public IReadOnlyList<string> StartupFiles { get; private set; } = Array.Empty<string>();

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        StartupFiles = e.Args
            .Where(File.Exists)
            .Where(path => Path.GetExtension(path).Equals(".apk", StringComparison.OrdinalIgnoreCase)
                        || Path.GetExtension(path).Equals(".xapk", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if (StartupFiles.Count > 0)
        {
            window.Dispatcher.BeginInvoke(new Action(async () => await window.InstallFilesFromCommandLineAsync(StartupFiles)));
        }
    }
}
