using System.Diagnostics;
using DroidDesk.Models;

namespace DroidDesk.Services;

public sealed class AndroidRuntimeService
{
    public const string AvdName = "DroidDesk_API_36";
    private const string EmulatorSerial = "emulator-5554";
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DroidDesk");
    private Process? _emulatorProcess;
    private readonly SemaphoreSlim _runtimeLock = new(1, 1);

    public event Action<string>? StatusChanged;

    public string SdkRoot => Path.Combine(_root, "AndroidSdk");
    public string AdbPath => Path.Combine(SdkRoot, "platform-tools", "adb.exe");
    public string EmulatorPath => Path.Combine(SdkRoot, "emulator", "emulator.exe");
    public string AaptPath => Path.Combine(SdkRoot, "build-tools", "36.0.0", "aapt.exe");
    public string ScrcpyPath => Path.Combine(_root, "tools", "scrcpy", "scrcpy.exe");
    public string RootPath => _root;

    public bool IsInstalled => File.Exists(AdbPath)
        && File.Exists(EmulatorPath)
        && File.Exists(AaptPath)
        && File.Exists(ScrcpyPath)
        && File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "avd", $"{AvdName}.avd", "config.ini"));

    public async Task EnsureInstalledAsync(CancellationToken cancellationToken = default)
    {
        if (IsInstalled)
            return;

        await _runtimeLock.WaitAsync(cancellationToken);
        try
        {
            if (IsInstalled)
                return;

            var script = Path.Combine(AppContext.BaseDirectory, "scripts", "Install-Runtime.ps1");
            if (!File.Exists(script))
            {
                var projectRootScript = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "Install-Runtime.ps1"));
                if (File.Exists(projectRootScript)) script = projectRootScript;
                else throw new FileNotFoundException("The Android runtime setup script is missing.", script);
            }

            Report("Preparing Android runtime. This is a one-time setup and downloads several components...");
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(script);

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start()) throw new InvalidOperationException("Could not start the Android runtime setup script.");

            var stdoutTask = PumpOutputAsync(process.StandardOutput, cancellationToken);
            var stderrTask = PumpOutputAsync(process.StandardError, cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdoutTask, stderrTask);

            if (process.ExitCode != 0)
                throw new InvalidOperationException("Android runtime setup failed. Review the status messages and try again.");
            if (!IsInstalled)
                throw new InvalidOperationException("Setup finished, but one or more required runtime files are missing.");

            Report("Android runtime is installed.");
        }
        finally
        {
            _runtimeLock.Release();
        }
    }

    private async Task PumpOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(line)) Report(line.Trim());
        }
    }

    public async Task StartAndroidAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInstalledAsync(cancellationToken);
        Report("Starting Android in the background...");
        await ProcessRunner.RunAsync(AdbPath, new[] { "start-server" }, cancellationToken: cancellationToken, timeoutSeconds: 30);

        if (!await IsEmulatorConnectedAsync(cancellationToken))
        {
            try
            {
                var acceleration = await ProcessRunner.RunAsync(EmulatorPath, new[] { "-accel-check" },
                    cancellationToken: cancellationToken, timeoutSeconds: 20);
                Report(acceleration);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Android hardware acceleration is not available. Enable Intel VT-x or AMD SVM in firmware and enable Windows Hypervisor Platform in Windows Features, then restart Windows.", ex);
            }
            var args = new[]
            {
                $"@{AvdName}", "-no-window", "-no-audio", "-no-boot-anim",
                "-gpu", "auto", "-memory", "1536", "-cores", "2",
                "-accel", "auto", "-no-snapshot-save"
            };
            var emulatorEnvironment = CreateAndroidEnvironment();
            _emulatorProcess = ProcessRunner.StartDetached(EmulatorPath, args, Path.GetDirectoryName(EmulatorPath), emulatorEnvironment);
        }

        var deadline = DateTime.UtcNow.AddMinutes(3);
        var booted = false;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await ProcessRunner.RunAsync(AdbPath,
                    new[] { "-s", EmulatorSerial, "shell", "getprop", "sys.boot_completed" },
                    cancellationToken: cancellationToken, timeoutSeconds: 10);
                if (result.Trim().EndsWith("1", StringComparison.Ordinal))
                {
                    booted = true;
                    break;
                }
            }
            catch
            {
                // The emulator may not have registered with ADB yet.
            }
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        if (!booted)
            throw new TimeoutException("Android did not finish starting within three minutes. Check that CPU virtualization is enabled and Windows Hypervisor Platform is available.");

        Report("Android is ready.");
    }

    private IReadOnlyDictionary<string, string> CreateAndroidEnvironment()
    {
        var avdRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "avd");
        var platformTools = Path.Combine(SdkRoot, "platform-tools");
        return new Dictionary<string, string>
        {
            ["ANDROID_HOME"] = SdkRoot,
            ["ANDROID_SDK_ROOT"] = SdkRoot,
            ["ANDROID_AVD_HOME"] = avdRoot,
            ["ADB"] = AdbPath,
            ["PATH"] = platformTools + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
        };
    }

    private async Task<bool> IsEmulatorConnectedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var devices = await ProcessRunner.RunAsync(AdbPath, new[] { "devices" }, cancellationToken: cancellationToken, timeoutSeconds: 15);
            return devices.Split('\n').Any(line => line.Trim().StartsWith(EmulatorSerial + "\tdevice", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> LaunchAppAsync(InstalledApp app, CancellationToken cancellationToken = default)
    {
        await StartAndroidAsync(cancellationToken);
        var args = new[]
        {
            "--serial", EmulatorSerial,
            "--new-display=1280x800/240",
            "--no-vd-system-decorations",
            $"--start-app={app.PackageName}",
            $"--window-title={app.DisplayName} — DroidDesk",
            "--video-bit-rate=8M",
            "--max-fps=60"
        };
        ProcessRunner.StartDetached(ScrcpyPath, args, Path.GetDirectoryName(ScrcpyPath), CreateAndroidEnvironment());
        Report($"Opened {app.DisplayName} in its own window.");
        return app.DisplayName;
    }

    public async Task StopAndroidAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(AdbPath)) return;
        try
        {
            await ProcessRunner.RunAsync(AdbPath,
                new[] { "-s", EmulatorSerial, "emu", "kill" },
                cancellationToken: cancellationToken, timeoutSeconds: 20);
        }
        catch
        {
            try
            {
                if (_emulatorProcess is { HasExited: false }) _emulatorProcess.Kill(entireProcessTree: true);
            }
            catch { /* The emulator may already have exited. */ }
        }
        Report("Android stopped. All open Android app windows have closed.");
    }

    public async Task UninstallAsync(string packageName, CancellationToken cancellationToken = default)
    {
        await StartAndroidAsync(cancellationToken);
        await ProcessRunner.RunAsync(AdbPath,
            new[] { "-s", EmulatorSerial, "uninstall", packageName },
            cancellationToken: cancellationToken, timeoutSeconds: 90);
        Report($"Removed {packageName} from Android.");
    }

    public void Report(string message) => StatusChanged?.Invoke(message);
}
