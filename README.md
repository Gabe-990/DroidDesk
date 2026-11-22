# DroidDesk

**Run Android APK and XAPK packages on Windows in individual app windows.**

DroidDesk is a Windows desktop launcher that manages an Android virtual device in the background. It installs APKs and XAPK packages, keeps a small library of installed apps, and opens each app in a separate resizable desktop window.

> DroidDesk does not make Android apps run natively as Windows programs. Android applications need an Android runtime. DroidDesk manages the Android Emulator without showing its usual device window, then uses scrcpy virtual displays to present each app in its own window. It does not require BlueStacks or LDPlayer.

## Current preview status

This repository contains the first implementation and Windows build pipeline. It has not yet been compiled or exercised on a Windows machine in this environment. Treat it as an early preview, not a finished or compatibility-tested product.

## Features

- Windows desktop interface built with WPF and .NET 10.
- Drag-and-drop or browse for `.apk` and `.xapk` files.
- Installs ordinary APKs and multi-APK XAPK packages.
- Copies XAPK OBB data into the Android virtual device when present.
- Stores an installed-app library between launches.
- Opens apps in individual windows using scrcpy virtual displays.
- Can register `.apk` and `.xapk` file associations for the current Windows user.
- First-run setup downloads the Android command-line tools, Android Emulator, Android 16 Google APIs x86_64 system image, build-tools, and scrcpy. Downloads are SHA-256 checked against the published hashes used by the installer.
- GitHub Actions builds a self-contained Windows x64 publish output.

## Requirements

- Windows 10 (version 1809 or later) or Windows 11, 64-bit.
- An x64 Intel or AMD processor with hardware virtualization enabled in firmware.
- Windows Hypervisor Platform enabled in Windows Features.
- A graphics driver supported by Android Emulator.
- A stable internet connection for first-run setup.
- Several gigabytes of free disk space for Android SDK components and the virtual device.
- **8 GB RAM is a practical starting point; 16 GB is recommended.** The emulator is memory-intensive. Systems with 4 GB RAM are unlikely to provide a usable experience.

Android Emulator acceleration on Windows uses Windows Hypervisor Platform (WHPX). If Android does not start, check that CPU virtualization is enabled in firmware and that Windows Hypervisor Platform is enabled. See the [Android Emulator hardware acceleration guide](https://developer.android.com/studio/run/emulator-acceleration).

## Build from source

Install the .NET 10 SDK, then run:

```powershell
dotnet restore .\src\DroidDesk\DroidDesk.csproj
dotnet run --project .\src\DroidDesk\DroidDesk.csproj
```

To publish a self-contained Windows x64 app:

```powershell
.\scripts\Build.ps1
```

The publish output is written to `artifacts\publish`.

## First launch

1. Start DroidDesk.
2. Choose **Set up Android runtime** (or add a package and approve setup when prompted).
3. Review the [Android SDK license terms](https://developer.android.com/studio/terms). The setup process accepts SDK licenses only after the user confirms the prompt in DroidDesk.
4. First-run setup downloads and configures the Android SDK tools, emulator, Android 16 Google APIs x86_64 image, and scrcpy. This can take some time and several gigabytes of disk space.
5. Drop an APK/XAPK onto the window or choose **Browse files**. DroidDesk installs it and automatically opens the app in a separate window.
6. To make Explorer double-click work, choose **Register APK / XAPK files**. Select DroidDesk as the Windows default app if Windows asks.
7. Later, select an installed app and choose **Open selected app** to launch it again.

To open packages by double-clicking them in Explorer, choose **Register APK / XAPK files**. Windows may still require you to choose DroidDesk as the default handler in Windows Settings.

## How the runtime works

1. Android Emulator starts with `-no-window` and stays in the background.
2. DroidDesk installs the package through Android Debug Bridge (ADB).
3. For each app launch, DroidDesk starts scrcpy with `--new-display` and `--start-app`.
4. scrcpy presents that virtual Android display in its own Windows window. Closing that window closes that virtual display; the app remains installed in the Android virtual device.

References:

- [Android Emulator command-line options](https://developer.android.com/studio/run/emulator-commandline)
- [scrcpy virtual display guide](https://github.com/Genymobile/scrcpy/blob/master/doc/virtual-display.md)
- [scrcpy Windows installation and releases](https://github.com/Genymobile/scrcpy/blob/master/doc/windows.md)

## Compatibility notes

DroidDesk is an emulator-based launcher, not a universal Android compatibility layer. Some apps may refuse to run or may not behave like they do on a physical phone. Common reasons include:

- Apps that require the Google Play Store, Play Integrity, DRM, a physical SIM, device-specific sensors, or hardware-backed security.
- Apps that only ship native libraries for ARM/ARM64 or detect virtual devices.
- Apps that depend on camera, GPS, Bluetooth, telephony, or other device hardware that is not fully represented by the virtual device.
- Games with demanding GPU requirements or emulator detection.

The configured system image includes Google APIs but does not provide a normal Google Play Store installation. The default image is x86_64; ARM-only apps may not work.

XAPK is a third-party packaging convention rather than a universal Android package standard. The installer handles archives containing APK files and optional `Android/obb/<package>` data. Unusual XAPK layouts may need additional support.

## Project layout

```text
DroidDesk/
├── .github/workflows/build-windows.yml
├── assets/DroidDesk.ico
├── scripts/
│   ├── Build.ps1
│   ├── Check-Virtualization.ps1
│   └── Install-Runtime.ps1
├── src/DroidDesk/
│   ├── Models/
│   ├── Services/
│   ├── App.xaml
│   ├── MainWindow.xaml
│   └── DroidDesk.csproj
├── DroidDesk.sln
└── README.md
```

## Security and licensing

Only install APK/XAPK files from sources you trust. Android apps can request access to files, contacts, location, microphone, camera, and network data within the virtual device.

DroidDesk source code is licensed under MIT. The runtime installer downloads Android SDK components from Google and scrcpy from its official Genymobile GitHub release. Those components remain subject to their respective licenses; see [Android SDK terms](https://developer.android.com/studio/terms) and the [scrcpy project](https://github.com/Genymobile/scrcpy).

## Roadmap

- Package icon extraction and richer app cards.
- Better detection for non-standard XAPK manifests and split ordering.
- Runtime diagnostics and hardware acceleration checks in the UI.
- Per-app display size and scaling settings.
- Installer/portable release packaging and more Windows compatibility testing.

## License

MIT. See [LICENSE](LICENSE).

## Create the GitHub repository and push

The connected GitHub integration used to prepare this project can edit existing repositories but does not expose a create-repository action. To create the new repository from your Windows PC, install and sign in to GitHub CLI once:

```powershell
winget install --id GitHub.cli --exact
gh auth login
```

Then run this from the extracted project folder:

```powershell
.\scripts\Publish-To-GitHub.ps1
```

The script creates `Gabe-990/DroidDesk` as a **private** repository by default, initializes Git, commits the source, and pushes `main`. To create it as public instead:

```powershell
.\scripts\Publish-To-GitHub.ps1 -Public
```
