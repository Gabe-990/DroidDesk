using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using DroidDesk.Models;

namespace DroidDesk.Services;

public sealed class AppInstallerService
{
    private readonly AndroidRuntimeService _runtime;
    private readonly string _metadataPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public AppInstallerService(AndroidRuntimeService runtime)
    {
        _runtime = runtime;
        Directory.CreateDirectory(runtime.RootPath);
        _metadataPath = Path.Combine(runtime.RootPath, "installed-apps.json");
    }

    public async Task<List<InstalledApp>> LoadAsync()
    {
        if (!File.Exists(_metadataPath)) return new List<InstalledApp>();
        try
        {
            var json = await File.ReadAllTextAsync(_metadataPath);
            return JsonSerializer.Deserialize<List<InstalledApp>>(json, JsonOptions) ?? new List<InstalledApp>();
        }
        catch (JsonException)
        {
            return new List<InstalledApp>();
        }
    }

    public async Task<InstalledApp> InstallAsync(string inputFile, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(inputFile);
        if (!extension.Equals(".apk", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".xapk", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Choose an .apk or .xapk file.");

        await _runtime.StartAndroidAsync(cancellationToken);
        var apks = new List<string>();
        var obbDirectories = new List<string>();
        string? manifestPackage = null;
        string? preferredBaseApk = null;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "DroidDesk", Guid.NewGuid().ToString("N"));

        try
        {
            if (extension.Equals(".apk", StringComparison.OrdinalIgnoreCase))
            {
                apks.Add(Path.GetFullPath(inputFile));
            }
            else
            {
                Directory.CreateDirectory(temporaryDirectory);
                using var archive = ZipFile.OpenRead(inputFile);
                var manifestEntry = archive.GetEntry("manifest.json");
                if (manifestEntry is not null)
                {
                    await using var manifestStream = manifestEntry.Open();
                    using var document = await JsonDocument.ParseAsync(manifestStream, cancellationToken: cancellationToken);
                    var root = document.RootElement;
                    manifestPackage = GetJsonString(root, "package_name") ?? GetJsonString(root, "packageName");
                    preferredBaseApk = GetJsonString(root, "base_apk") ?? GetJsonString(root, "baseApk") ?? GetJsonString(root, "main_apk");
                }

                archive.ExtractToDirectory(temporaryDirectory, overwriteFiles: true);
                apks.AddRange(Directory.EnumerateFiles(temporaryDirectory, "*.apk", SearchOption.AllDirectories));
                obbDirectories.AddRange(Directory.EnumerateDirectories(temporaryDirectory, "obb", SearchOption.AllDirectories));
                if (apks.Count == 0) throw new InvalidDataException("This XAPK archive does not contain any APK files.");
                var namedBaseApk = !string.IsNullOrWhiteSpace(preferredBaseApk)
                    ? apks.FirstOrDefault(path => Path.GetFileName(path).Equals(Path.GetFileName(preferredBaseApk), StringComparison.OrdinalIgnoreCase))
                    : null;
                var standardBaseApk = apks.FirstOrDefault(path => Path.GetFileName(path).Equals("base.apk", StringComparison.OrdinalIgnoreCase));
                var selectedBaseApk = namedBaseApk ?? standardBaseApk ?? apks[0];
                apks = apks.OrderBy(path => path.Equals(selectedBaseApk, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
            }

            var baseApk = apks[0];
            var packageInfo = await ReadPackageInfoAsync(baseApk, cancellationToken);
            var packageName = manifestPackage ?? packageInfo.PackageName;
            if (string.IsNullOrWhiteSpace(packageName))
                throw new InvalidDataException("Could not read the package name from this APK. The file may be damaged or unsupported.");

            if (apks.Count == 1)
            {
                await ProcessRunner.RunAsync(_runtime.AdbPath,
                    new[] { "-s", "emulator-5554", "install", "-r", baseApk },
                    cancellationToken: cancellationToken, timeoutSeconds: 180);
            }
            else
            {
                var args = new List<string> { "-s", "emulator-5554", "install-multiple", "-r" };
                args.AddRange(apks);
                await ProcessRunner.RunAsync(_runtime.AdbPath, args, cancellationToken: cancellationToken, timeoutSeconds: 240);
            }

            foreach (var obbDirectory in obbDirectories)
            {
                var packageDirectory = Path.Combine(obbDirectory, packageName);
                if (!Directory.Exists(packageDirectory)) continue;
                await ProcessRunner.RunAsync(_runtime.AdbPath,
                    new[] { "-s", "emulator-5554", "shell", "mkdir", "-p", $"/sdcard/Android/obb/{packageName}" },
                    cancellationToken: cancellationToken, timeoutSeconds: 30);
                await ProcessRunner.RunAsync(_runtime.AdbPath,
                    new[] { "-s", "emulator-5554", "push", packageDirectory, "/sdcard/Android/obb/" },
                    cancellationToken: cancellationToken, timeoutSeconds: 120);
            }

            var app = new InstalledApp
            {
                PackageName = packageName,
                DisplayName = string.IsNullOrWhiteSpace(packageInfo.DisplayName) ? packageName : packageInfo.DisplayName,
                SourceFile = Path.GetFullPath(inputFile)
            };
            var installed = await LoadAsync();
            installed.RemoveAll(item => item.PackageName.Equals(packageName, StringComparison.OrdinalIgnoreCase));
            installed.Add(app);
            await SaveAsync(installed);
            _runtime.Report($"Installed {app.DisplayName}.");
            return app;
        }
        finally
        {
            try { if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true); }
            catch { /* Temporary extraction cleanup is best-effort. */ }
        }
    }

    private async Task<(string PackageName, string DisplayName)> ReadPackageInfoAsync(string apkPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(_runtime.AaptPath))
            throw new FileNotFoundException("Android build-tools (aapt) were not installed. Run runtime setup again.", _runtime.AaptPath);

        var output = await ProcessRunner.RunAsync(_runtime.AaptPath,
            new[] { "dump", "badging", apkPath },
            cancellationToken: cancellationToken, timeoutSeconds: 60);
        var packageMatch = Regex.Match(output, "package: name='([^']+)'", RegexOptions.CultureInvariant);
        var labelMatch = Regex.Match(output, "application-label(?:-[^:]+)?:'([^']*)'", RegexOptions.CultureInvariant);
        return (packageMatch.Success ? packageMatch.Groups[1].Value : string.Empty,
            labelMatch.Success ? labelMatch.Groups[1].Value : string.Empty);
    }

    private static string? GetJsonString(JsonElement root, string property)
    {
        return root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    public async Task RemoveAsync(string packageName, CancellationToken cancellationToken = default)
    {
        await _runtime.UninstallAsync(packageName, cancellationToken);
        var installed = await LoadAsync();
        installed.RemoveAll(item => item.PackageName.Equals(packageName, StringComparison.OrdinalIgnoreCase));
        await SaveAsync(installed);
    }

    private async Task SaveAsync(List<InstalledApp> apps)
    {
        var json = JsonSerializer.Serialize(apps.OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase), JsonOptions);
        await File.WriteAllTextAsync(_metadataPath, json);
    }
}
