using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using DroidDesk.Models;
using DroidDesk.Services;

namespace DroidDesk;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AndroidRuntimeService _runtime;
    private readonly AppInstallerService _installer;
    private InstalledApp? _selectedApp;
    private bool _working;

    public ObservableCollection<InstalledApp> Apps { get; } = new();

    public InstalledApp? SelectedApp
    {
        get => _selectedApp;
        set
        {
            if (_selectedApp == value) return;
            _selectedApp = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _runtime = new AndroidRuntimeService();
        _installer = new AppInstallerService(_runtime);
        _runtime.StatusChanged += message => Dispatcher.Invoke(() => SetStatus(message));
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAppsAsync();
        if (Application.Current is App app && app.StartupFiles.Count > 0)
            SetStatus("Files received from Windows. Installing...");
        else
            SetStatus("Ready. Add an APK or XAPK to begin.");
    }

    public async Task InstallFilesFromCommandLineAsync(IEnumerable<string> files)
    {
        await InstallFilesAsync(files);
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose an Android package",
            Filter = "Android packages (*.apk;*.xapk)|*.apk;*.xapk|APK files (*.apk)|*.apk|XAPK files (*.xapk)|*.xapk",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            await InstallFilesAsync(dialog.FileNames);
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            await InstallFilesAsync(files);
    }

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async Task InstallFilesAsync(IEnumerable<string> files)
    {
        var validFiles = files.Where(File.Exists)
            .Where(path => Path.GetExtension(path).Equals(".apk", StringComparison.OrdinalIgnoreCase)
                        || Path.GetExtension(path).Equals(".xapk", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (validFiles.Count == 0)
        {
            SetStatus("Choose an APK or XAPK file.");
            return;
        }

        if (!BeginWork()) return;
        try
        {
            if (!await EnsureRuntimeConsentAsync()) return;
            foreach (var file in validFiles)
            {
                SetStatus($"Installing {Path.GetFileName(file)}...");
                try
                {
                    var installedApp = await _installer.InstallAsync(file);
                    var existing = Apps.FirstOrDefault(item => item.PackageName.Equals(installedApp.PackageName, StringComparison.OrdinalIgnoreCase));
                    if (existing is not null) Apps.Remove(existing);
                    Apps.Add(installedApp);
                    SelectedApp = installedApp;
                    await _runtime.LaunchAppAsync(installedApp);
                }
                catch (Exception ex)
                {
                    SetStatus($"Could not install {Path.GetFileName(file)}: {ex.Message}");
                    MessageBox.Show(this, ex.Message, $"Install failed: {Path.GetFileName(file)}", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            UpdateEmptyState();
            SetStatus("Installation task finished.");
        }
        catch (Exception ex)
        {
            ShowError("Android runtime setup failed", ex);
        }
        finally
        {
            EndWork();
        }
    }

    private async void SetupRuntime_Click(object sender, RoutedEventArgs e)
    {
        if (!BeginWork()) return;
        try
        {
            if (!await EnsureRuntimeConsentAsync()) return;
            SetStatus("Android runtime setup is complete.");
            MessageBox.Show(this, "Android runtime is ready. Add an APK or XAPK to install your first app.", "DroidDesk setup", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError("Android runtime setup failed", ex);
        }
        finally { EndWork(); }
    }

    private async void StartAndroid_Click(object sender, RoutedEventArgs e)
    {
        if (!BeginWork()) return;
        try
        {
            if (!await EnsureRuntimeConsentAsync()) return;
            await _runtime.StartAndroidAsync();
            SetStatus("Android is running in the background.");
        }
        catch (Exception ex) { ShowError("Could not start Android", ex); }
        finally { EndWork(); }
    }

    private async void StopAndroid_Click(object sender, RoutedEventArgs e)
    {
        if (!BeginWork()) return;
        try
        {
            await _runtime.StopAndroidAsync();
            SetStatus("Android stopped. All Android app windows are closed.");
        }
        catch (Exception ex) { ShowError("Could not stop Android", ex); }
        finally { EndWork(); }
    }

    private async void LaunchSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedApp is null)
        {
            SetStatus("Select an app from your library first.");
            return;
        }
        if (!BeginWork()) return;
        try
        {
            if (!await EnsureRuntimeConsentAsync()) return;
            await _runtime.LaunchAppAsync(SelectedApp);
        }
        catch (Exception ex) { ShowError("Could not open app", ex); }
        finally { EndWork(); }
    }

    private async void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedApp is null)
        {
            SetStatus("Select an app to remove.");
            return;
        }
        var app = SelectedApp;
        var answer = MessageBox.Show(this, $"Remove {app.DisplayName} from the Android runtime?", "Remove app", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes || !BeginWork()) return;

        try
        {
            if (!await EnsureRuntimeConsentAsync()) return;
            await _installer.RemoveAsync(app.PackageName);
            Apps.Remove(app);
            SelectedApp = null;
            UpdateEmptyState();
            SetStatus($"Removed {app.DisplayName}.");
        }
        catch (Exception ex) { ShowError("Could not remove app", ex); }
        finally { EndWork(); }
    }

    private async Task<bool> EnsureRuntimeConsentAsync()
    {
        if (_runtime.IsInstalled) return true;
        var answer = MessageBox.Show(this,
            "DroidDesk needs the Android SDK, an Android system image and scrcpy. Setup will download these components and accept the Android SDK license agreements on your behalf. Review the terms first at:\nhttps://developer.android.com/studio/terms\n\nContinue with setup?",
            "Android SDK license and runtime setup", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes)
        {
            SetStatus("Runtime setup cancelled. Review the Android SDK terms before continuing.");
            return false;
        }
        await _runtime.EnsureInstalledAsync();
        return true;
    }

    private void RegisterFiles_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (executable is null || !File.Exists(executable))
                throw new InvalidOperationException("Could not locate the DroidDesk executable. Publish or install the app first.");

            RegisterAssociation(".apk", "DroidDesk.APK", executable);
            RegisterAssociation(".xapk", "DroidDesk.XAPK", executable);
            MessageBox.Show(this,
                "APK and XAPK file types have been registered for your Windows user.\n\nWindows may still ask you to choose DroidDesk as the default app in Settings.",
                "File types registered", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus("APK and XAPK file types registered for this Windows account.");
        }
        catch (Exception ex) { ShowError("Could not register file types", ex); }
    }

    private static void RegisterAssociation(string extension, string programId, string executable)
    {
        using var extensionKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{extension}");
        extensionKey?.SetValue("", programId);
        using var programKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{programId}");
        programKey?.SetValue("", "Android package - DroidDesk");
        using var iconKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{programId}\DefaultIcon");
        iconKey?.SetValue("", $"\"{executable}\",0");
        using var commandKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{programId}\shell\open\command");
        commandKey?.SetValue("", $"\"{executable}\" \"%1\"");
    }

    private async Task RefreshAppsAsync()
    {
        var storedApps = await _installer.LoadAsync();
        Apps.Clear();
        foreach (var app in storedApps.OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            Apps.Add(app);
        UpdateEmptyState();
    }

    private void UpdateEmptyState() => EmptyState.Visibility = Apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private bool BeginWork()
    {
        if (_working) return false;
        _working = true;
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        StatusDot.Opacity = 0.5;
        return true;
    }

    private void EndWork()
    {
        _working = false;
        Mouse.OverrideCursor = null;
        StatusDot.Opacity = 1;
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private void ShowError(string title, Exception exception)
    {
        SetStatus(exception.Message);
        MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
