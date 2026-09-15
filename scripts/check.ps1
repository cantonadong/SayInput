param([string]$Action = 'test', [string]$Project = 'VoiceTyper.sln', [string]$Filter = '',
    [string]$SdkPath = 'D:/Program/dotnet')
$ErrorActionPreference = 'Stop'
$env:DOTNET_ROOT = $SdkPath
$env:DOTNET_CLI_HOME = Join-Path $env:TEMP 'sayinput-cli'
$env:NUGET_PACKAGES = Join-Path $env:TEMP 'sayinput-nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if (!(Test-Path "$SdkPath/dotnet.exe")) { throw "Missing .NET SDK: $SdkPath" }
Set-Location (Split-Path $PSScriptRoot -Parent)
$argsList = @($Action, $Project, '-c', 'Release', '--no-restore')
if ($Action -eq 'restore') { $argsList = @($Action, $Project) }
if ($Filter) { $argsList += @('--filter', $Filter) }
& "$env:DOTNET_ROOT/dotnet.exe" @argsList
exit $LASTEXITCODE
