[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')]
    [string[]]$Runtime = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64')
)

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
foreach ($rid in $Runtime) {
    $output = Join-Path $projectDir "dist\$rid"
    dotnet publish (Join-Path $projectDir 'OfficeWebLauncher.csproj') -c Release -r $rid --self-contained true -o $output
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }
    if ($rid.StartsWith('win-')) {
        Copy-Item -LiteralPath (Join-Path $projectDir 'Setup.ps1') -Destination $output -Force
        Copy-Item -LiteralPath (Join-Path $projectDir 'Uninstall.ps1') -Destination $output -Force
    } else {
        Copy-Item -LiteralPath (Join-Path $projectDir 'Setup.sh') -Destination $output -Force
        Copy-Item -LiteralPath (Join-Path $projectDir 'Uninstall.sh') -Destination $output -Force
    }
    Copy-Item -LiteralPath (Join-Path $projectDir 'README.md') -Destination $output -Force
}
