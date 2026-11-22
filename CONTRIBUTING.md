# Contributing

1. Open an issue describing the bug or proposed change.
2. Keep runtime downloads pinned to official vendor sources and verify SHA-256 checksums.
3. Do not commit Android SDK binaries, system images, APKs, XAPKs, signing keys, or user data.
4. Build with the .NET 10 SDK on Windows:

   ```powershell
   dotnet build .\DroidDesk.sln -c Release
   ```

5. Test APK installs, split XAPK installs, OBB copying, app launch/close, and file associations on a Windows machine with WHPX enabled.
6. Include the Windows version, CPU architecture, RAM, emulator log, and relevant DroidDesk status messages in bug reports. Remove personal paths and package data before sharing logs.
