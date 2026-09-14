[CmdletBinding()]
param(
    [string]$ClientId,
    [string]$Tenant = 'common',
    [switch]$OpenDefaultApps
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exePath = Join-Path $scriptDir 'OfficeWebLauncher.exe'

if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "OfficeWebLauncher.exe must be in the same folder as Setup.ps1. Run Build.ps1 first when installing from source."
}

if ([string]::IsNullOrWhiteSpace($ClientId)) {
    $ClientId = Read-Host 'Microsoft Entra Application (client) ID'
}
$parsedClientId = [Guid]::Empty
if (-not [Guid]::TryParse($ClientId, [ref]$parsedClientId)) {
    throw 'ClientId must be a GUID from a Microsoft Entra app registration.'
}
if ($Tenant -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]*$') {
    throw 'Tenant must be common, organizations, consumers, a Directory tenant ID, or a verified tenant domain.'
}

$config = [ordered]@{ clientId = $ClientId; tenant = $Tenant }
$config | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $scriptDir 'OfficeWebLauncher.json') -Encoding utf8

$extensions = @(
    '.doc', '.docx', '.docm', '.dot', '.dotx', '.dotm', '.odt', '.rtf',
    '.xls', '.xlsx', '.xlsm', '.xlsb', '.xlt', '.xltx', '.xltm', '.csv', '.ods',
    '.ppt', '.pptx', '.pptm', '.pps', '.ppsx', '.ppsm', '.pot', '.potx', '.potm', '.odp',
    '.vsd', '.vsdx'
)
$classesRoot = 'HKCU:\Software\Classes'
$productRoot = 'HKCU:\Software\OfficeWebLauncher'
$progId = 'OfficeWebLauncher.File'

New-Item -Path "$classesRoot\$progId\shell\open\command" -Force | Out-Null
Set-Item -Path "$classesRoot\$progId" -Value 'Microsoft Office file (web)'
Set-Item -Path "$classesRoot\$progId\shell\open\command" -Value ('"{0}" "%1"' -f $exePath)
New-Item -Path "$classesRoot\$progId\DefaultIcon" -Force | Out-Null
Set-Item -Path "$classesRoot\$progId\DefaultIcon" -Value ('"{0}",0' -f $exePath)

$backupPath = "$productRoot\PreviousAssociations"
New-Item -Path $backupPath -Force | Out-Null
foreach ($extension in $extensions) {
    $extensionPath = "$classesRoot\$extension"
    $previous = if (Test-Path $extensionPath) { (Get-Item $extensionPath).GetValue('') } else { $null }
    if ((Get-Item $backupPath).GetValue($extension, $null) -eq $null) {
        New-ItemProperty -Path $backupPath -Name $extension -Value $(if ($null -eq $previous) { '<none>' } else { [string]$previous }) -PropertyType String -Force | Out-Null
    }
    New-Item -Path "$extensionPath\OpenWithProgids" -Force | Out-Null
    New-ItemProperty -Path "$extensionPath\OpenWithProgids" -Name $progId -Value '' -PropertyType String -Force | Out-Null
    Set-Item -Path $extensionPath -Value $progId
}

$capabilities = "$productRoot\Capabilities"
New-Item -Path "$capabilities\FileAssociations" -Force | Out-Null
New-ItemProperty -Path $capabilities -Name ApplicationName -Value 'Office Web Launcher' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $capabilities -Name ApplicationDescription -Value 'Uploads Office files to a private OneDrive app folder and opens Microsoft 365 for the web.' -PropertyType String -Force | Out-Null
foreach ($extension in $extensions) {
    New-ItemProperty -Path "$capabilities\FileAssociations" -Name $extension -Value $progId -PropertyType String -Force | Out-Null
}
New-Item -Path 'HKCU:\Software\RegisteredApplications' -Force | Out-Null
New-ItemProperty -Path 'HKCU:\Software\RegisteredApplications' -Name 'Office Web Launcher' -Value 'Software\OfficeWebLauncher\Capabilities' -PropertyType String -Force | Out-Null

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ShellRefresh {
  [DllImport("shell32.dll")] public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
'@
[ShellRefresh]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host "Office Web Launcher is registered for $($extensions.Count) file extensions."
Write-Host 'Windows may preserve an existing default app. If double-click still uses another app, choose Office Web Launcher in Default Apps.'
if ($OpenDefaultApps) { Start-Process 'ms-settings:defaultapps' }
