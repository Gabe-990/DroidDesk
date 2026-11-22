Write-Host 'Checking Windows virtualization features...'
try {
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    Write-Host "CPU: $($cpu.Name)"
    Write-Host "Virtualization firmware enabled: $($cpu.VirtualizationFirmwareEnabled)"
    Write-Host "Second-level address translation: $($cpu.SecondLevelAddressTranslationExtensions)"
} catch {
    Write-Warning "Could not query CPU virtualization details: $_"
}
try {
    Get-WindowsOptionalFeature -Online -FeatureName HypervisorPlatform | Select-Object FeatureName, State | Format-Table -AutoSize
} catch {
    Write-Warning 'Could not query Windows Hypervisor Platform. Enable it from Windows Features if the Android emulator cannot start.'
}
