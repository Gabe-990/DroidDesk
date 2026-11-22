[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$AppRoot = Join-Path $env:LOCALAPPDATA 'DroidDesk'
$SdkRoot = Join-Path $AppRoot 'AndroidSdk'
$ToolsRoot = Join-Path $AppRoot 'tools'
$SdkManager = Join-Path $SdkRoot 'cmdline-tools\latest\bin\sdkmanager.bat'
$AvdManager = Join-Path $SdkRoot 'cmdline-tools\latest\bin\avdmanager.bat'
$AvdName = 'DroidDesk_API_36'
$DownloadDir = Join-Path $AppRoot 'downloads'
New-Item -ItemType Directory -Path $DownloadDir -Force | Out-Null
$SystemImage = 'system-images;android-36;google_apis;x86_64'

function Status([string]$Message) {
    Write-Output "STATUS: $Message"
}

function Download-VerifiedFile {
    param(
        [Parameter(Mandatory=$true)][string]$Uri,
        [Parameter(Mandatory=$true)][string]$Destination,
        [Parameter(Mandatory=$true)][string]$Sha256
    )
    if (Test-Path $Destination) {
        $existingHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($existingHash -eq $Sha256.ToLowerInvariant()) { return }
        Remove-Item -LiteralPath $Destination -Force
    }
    Status "Downloading $([IO.Path]::GetFileName($Destination))..."
    Invoke-WebRequest -Uri $Uri -OutFile $Destination
    $downloadHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($downloadHash -ne $Sha256.ToLowerInvariant()) {
        Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
        throw "Checksum verification failed for $([IO.Path]::GetFileName($Destination)). The download was removed."
    }
}

function Invoke-BatchWithInput {
    param(
        [Parameter(Mandatory=$true)][string]$BatchFile,
        [Parameter(Mandatory=$true)][string]$Arguments,
        [string]$InputLine = 'y',
        [int]$InputCount = 120,
        [int]$TimeoutSeconds = 1800
    )

    $command = '"' + $BatchFile + '" ' + $Arguments
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $env:ComSpec
    $psi.Arguments = '/d /s /c "' + $command + '"'
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    if (-not $process.Start()) { throw "Could not start $BatchFile" }

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    for ($i = 0; $i -lt $InputCount; $i++) {
        try { $process.StandardInput.WriteLine($InputLine) } catch { break }
    }
    try { $process.StandardInput.Close() } catch { }

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill($true) } catch { }
        throw "$([IO.Path]::GetFileName($BatchFile)) timed out."
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($stdout) { Write-Output $stdout }
    if ($stderr) { Write-Output $stderr }
    if ($process.ExitCode -ne 0) {
        throw "$([IO.Path]::GetFileName($BatchFile)) failed with exit code $($process.ExitCode)."
    }
}

try {
    New-Item -ItemType Directory -Path $AppRoot, $SdkRoot, $ToolsRoot -Force | Out-Null

    # Android command-line tools require a Java runtime. Install a user-scoped JDK if needed.
    $javaCommand = Get-Command java.exe -ErrorAction SilentlyContinue
    if (-not $javaCommand) {
        $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
        if ($winget) {
            Status 'Installing Microsoft OpenJDK 17 for Android SDK tools...'
            & $winget.Source install --id Microsoft.OpenJDK.17 --exact --scope user --silent --accept-package-agreements --accept-source-agreements
            if ($LASTEXITCODE -ne 0) {
                & $winget.Source install --id Microsoft.OpenJDK.17 --exact --silent --accept-package-agreements --accept-source-agreements
            }
        }
        $jdkCandidates = @(
            (Join-Path $env:LOCALAPPDATA 'Programs\Microsoft\jdk-17*'),
            'C:\Program Files\Microsoft\jdk-17*'
        )
        foreach ($pattern in $jdkCandidates) {
            $candidate = Get-ChildItem -Path $pattern -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($candidate) {
                $env:JAVA_HOME = $candidate.FullName
                $env:PATH = (Join-Path $candidate.FullName 'bin') + ';' + $env:PATH
                break
            }
        }
        $javaCommand = Get-Command java.exe -ErrorAction SilentlyContinue
        if (-not $javaCommand) {
            throw 'Java 17 or later is required by Android SDK tools. Install Microsoft OpenJDK 17, then run setup again.'
        }
    }
    Status 'Java runtime found.'

    # Install the official Android command-line tools, validating Google's published SHA-256.
    if (-not (Test-Path $SdkManager)) {
        $cmdlineZip = Join-Path $DownloadDir 'commandlinetools-win-15859902_latest.zip'
        Download-VerifiedFile `
            -Uri 'https://dl.google.com/android/repository/commandlinetools-win-15859902_latest.zip' `
            -Destination $cmdlineZip `
            -Sha256 '90ae805d20434428bffcb699c290860f19bb5f66a67e6b330067e3de801fb04a'

        Status 'Extracting Android SDK command-line tools...'
        $extractRoot = Join-Path $DownloadDir 'cmdline-extract'
        if (Test-Path $extractRoot) { Remove-Item $extractRoot -Recurse -Force }
        Expand-Archive -LiteralPath $cmdlineZip -DestinationPath $extractRoot -Force
        $sourceTools = Join-Path $extractRoot 'cmdline-tools'
        $targetTools = Join-Path $SdkRoot 'cmdline-tools\latest'
        New-Item -ItemType Directory -Path (Split-Path $targetTools -Parent) -Force | Out-Null
        if (Test-Path $targetTools) { Remove-Item $targetTools -Recurse -Force }
        Move-Item -LiteralPath $sourceTools -Destination $targetTools
        Remove-Item $extractRoot -Recurse -Force
    }

    $env:ANDROID_HOME = $SdkRoot
    $env:ANDROID_SDK_ROOT = $SdkRoot
    $env:PATH = (Join-Path $SdkRoot 'platform-tools') + ';' + (Join-Path $SdkRoot 'emulator') + ';' + $env:PATH

    Status 'Accepting Android SDK license agreements...'
    Invoke-BatchWithInput -BatchFile $SdkManager -Arguments "--sdk_root=`"$SdkRoot`" --licenses" -InputLine 'y' -InputCount 160 -TimeoutSeconds 600 | Out-Null

    Status 'Downloading Android emulator, platform tools and Android 16 system image...'
    $packages = @(
        'platform-tools',
        'emulator',
        'platforms;android-36',
        'build-tools;36.0.0',
        $SystemImage
    )
    $quotedPackages = ($packages | ForEach-Object { '"' + $_ + '"' }) -join ' '
    Invoke-BatchWithInput -BatchFile $SdkManager -Arguments "--sdk_root=`"$SdkRoot`" $quotedPackages" -InputLine 'y' -InputCount 160 -TimeoutSeconds 3600 | Out-Null

    # Create a persistent virtual Android device once. It runs without the emulator desktop UI.
    $avdDirectory = Join-Path $env:USERPROFILE ".android\avd\$AvdName.avd"
    if (-not (Test-Path (Join-Path $avdDirectory 'config.ini'))) {
        Status 'Creating the DroidDesk Android device...'
        Invoke-BatchWithInput -BatchFile $AvdManager `
            -Arguments "create avd --name $AvdName --package `"$SystemImage`" --device pixel_6 --force" `
            -InputLine 'no' -InputCount 1 -TimeoutSeconds 180 | Out-Null
    }
    $configPath = Join-Path $avdDirectory 'config.ini'
    if (Test-Path $configPath) {
        $config = Get-Content -LiteralPath $configPath -Raw
        foreach ($setting in @('hw.ramSize=1536', 'hw.cpu.ncore=2', 'vm.heapSize=256', 'hw.keyboard=yes')) {
            $key = ($setting -split '=', 2)[0]
            if ($config -match "(?m)^$([regex]::Escape($key))=") {
                $config = [regex]::Replace($config, "(?m)^$([regex]::Escape($key))=.*$", [System.Text.RegularExpressions.MatchEvaluator]{ param($m) $setting })
            } else {
                $config += "`r`n$setting"
            }
        }
        Set-Content -LiteralPath $configPath -Value $config -Encoding ASCII
    }

    # scrcpy opens the app in a clean virtual-display window. Download only from its official release.
    $scrcpyExe = Join-Path $ToolsRoot 'scrcpy\scrcpy.exe'
    if (-not (Test-Path $scrcpyExe)) {
        $scrcpyZip = Join-Path $DownloadDir 'scrcpy-win64-v5.0.1.zip'
        Download-VerifiedFile `
            -Uri 'https://github.com/Genymobile/scrcpy/releases/download/v5.0.1/scrcpy-win64-v5.0.1.zip' `
            -Destination $scrcpyZip `
            -Sha256 'b12a2c4ee8be317422451fc7dcf8ee20a71b5ea7ef9ad73ddd825a25316227a5'
        Status 'Extracting scrcpy...'
        $scrcpyExtract = Join-Path $DownloadDir 'scrcpy-extract'
        if (Test-Path $scrcpyExtract) { Remove-Item $scrcpyExtract -Recurse -Force }
        Expand-Archive -LiteralPath $scrcpyZip -DestinationPath $scrcpyExtract -Force
        $scrcpySource = Get-ChildItem -Path $scrcpyExtract -Filter 'scrcpy.exe' -Recurse | Select-Object -First 1
        if (-not $scrcpySource) { throw 'The official scrcpy archive did not contain scrcpy.exe.' }
        $scrcpySourceDirectory = $scrcpySource.Directory.FullName
        $scrcpyTarget = Join-Path $ToolsRoot 'scrcpy'
        if (Test-Path $scrcpyTarget) { Remove-Item $scrcpyTarget -Recurse -Force }
        New-Item -ItemType Directory -Path $scrcpyTarget -Force | Out-Null
        Copy-Item -Path (Join-Path $scrcpySourceDirectory '*') -Destination $scrcpyTarget -Recurse -Force
        Remove-Item $scrcpyExtract -Recurse -Force
    }

    Status 'Android runtime installation is complete.'
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
