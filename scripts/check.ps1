param([string]$Action = 'test', [string]$Project = 'VoiceTyper.sln', [string]$Filter = '')
$ErrorActionPreference = 'Stop'
$env:DOTNET_ROOT = 'C:/Users/Carl/AppData/Local/Temp/sayinput-dotnet'
$env:DOTNET_CLI_HOME = 'C:/Users/Carl/AppData/Local/Temp/sayinput-cli'
$env:APPDATA = 'C:/Users/Carl/AppData/Local/Temp/sayinput-appdata'
$env:NUGET_PACKAGES = 'C:/Users/Carl/AppData/Local/Temp/sayinput-nuget'
$argsList = @($Action, $Project, '-c', 'Release', '--no-restore')
if ($Filter) { $argsList += @('--filter', $Filter) }
& "$env:DOTNET_ROOT/dotnet.exe" @argsList
exit $LASTEXITCODE
