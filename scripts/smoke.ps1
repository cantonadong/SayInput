param([string]$Exe = 'artifacts/voice-test/VoiceTyper.App.exe', [int]$IdleSeconds = 300)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$target = (Resolve-Path (Join-Path (Split-Path $PSScriptRoot -Parent) $Exe)).Path
$process = Start-Process -FilePath $target -PassThru -WindowStyle Hidden
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 200
        $process.Refresh()
        if ($process.HasExited) { throw "Application exited during startup: $($process.ExitCode)" }
        $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
    } while (!$window -and [DateTime]::UtcNow -lt $deadline)
    if (!$window) { throw 'Settings window did not appear.' }
    Write-Output "Window created: $($window.Current.Name)"
    Start-Sleep -Seconds 3
    $process.Refresh()
    $cpuStart = $process.TotalProcessorTime.TotalSeconds
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $samples = @()
    while ($watch.Elapsed.TotalSeconds -lt $IdleSeconds) {
        Start-Sleep -Seconds 10
        $process.Refresh()
        if ($process.HasExited) { throw 'Application exited while idle.' }
        $samples += [pscustomobject]@{Seconds=[Math]::Round($watch.Elapsed.TotalSeconds,1);WorkingSetMB=[Math]::Round($process.WorkingSet64/1MB,2);PrivateMB=[Math]::Round($process.PrivateMemorySize64/1MB,2)}
        Write-Output ($samples[-1] | ConvertTo-Json -Compress)
    }
    $cpu = 100 * ($process.TotalProcessorTime.TotalSeconds - $cpuStart) / $watch.Elapsed.TotalSeconds / [Environment]::ProcessorCount
    Write-Output "Idle average total-machine CPU: $([Math]::Round($cpu,4))%"
    $exitLabel = -join ([char[]]@(0x9000,0x51FA,0x7A0B,0x5E8F))
    $exitCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $exitLabel)
    $exitButton = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $exitCondition)
    if (!$exitButton) { throw 'Exit button not found.' }
    $exitButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    if (!$process.WaitForExit(10000)) { throw 'Application failed to exit within 10 seconds.' }
    if ($process.ExitCode -ne 0) { throw "Application exit code: $($process.ExitCode)" }
    Write-Output 'Clean exit: 0'
} finally {
    if (!$process.HasExited) { Stop-Process -Id $process.Id -Force }
    $process.Dispose()
}
