param([string]$SdkPath = 'D:/Program/dotnet', [string]$Output = 'artifacts/VoiceTyper-1.0.0-win-x64')
$ErrorActionPreference = 'Stop'
$env:DOTNET_ROOT = $SdkPath
$env:DOTNET_CLI_HOME = Join-Path $env:TEMP 'sayinput-cli'
$env:NUGET_PACKAGES = Join-Path $env:TEMP 'sayinput-nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Set-Location (Split-Path $PSScriptRoot -Parent)
if (Test-Path -LiteralPath $Output) {
    $existing = @(Get-ChildItem -LiteralPath $Output -Force)
    if ($existing.Count -gt 0) { throw "Refusing to publish into non-empty directory: $Output" }
}
& "$SdkPath/dotnet.exe" publish src/VoiceTyper.App/VoiceTyper.App.csproj -c Release -r win-x64 --self-contained true -o $Output -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$private = @(Get-ChildItem -LiteralPath $Output -Recurse -Force | Where-Object {
    $_.Name -eq 'key.txt' -or $_.FullName -match '[\\/](data|recordings)([\\/]|$)'
})
if ($private.Count -gt 0) { throw "Release output contains runtime user data: $($private[0].FullName)" }
Write-Host "Runnable application: $Output/VoiceTyper.App.exe (keep the entire folder)."
