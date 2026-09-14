[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$classesRoot = 'HKCU:\Software\Classes'
$productRoot = 'HKCU:\Software\OfficeWebLauncher'
$progId = 'OfficeWebLauncher.File'
$backupPath = "$productRoot\PreviousAssociations"

if (Test-Path $backupPath) {
    $backup = Get-Item $backupPath
    foreach ($property in $backup.Property) {
        $extensionPath = "$classesRoot\$property"
        if (Test-Path $extensionPath) {
            Remove-ItemProperty -Path "$extensionPath\OpenWithProgids" -Name $progId -ErrorAction SilentlyContinue
            if ((Get-Item $extensionPath).GetValue('') -eq $progId) {
                $previous = $backup.GetValue($property)
                Set-Item -Path $extensionPath -Value $(if ($previous -eq '<none>') { '' } else { $previous })
            }
        }
    }
}

Remove-Item -LiteralPath "$classesRoot\$progId" -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\RegisteredApplications' -Name 'Office Web Launcher' -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $productRoot -Recurse -Force -ErrorAction SilentlyContinue

Write-Host 'Office Web Launcher file associations were removed. Uploaded OneDrive files and the local executable were left in place.'
Write-Host 'Run OfficeWebLauncher.exe --signout before uninstalling if you also want to remove its saved sign-in.'
