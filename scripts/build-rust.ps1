param(
    [string]$ToolchainPath = 'D:/Dev/MindLite/target/toolchain-download/llvm-mingw-20261006-ucrt-x86_64',
    [switch]$CheckOnly,
    [switch]$Lint,
    [switch]$BuildOnly
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location (Join-Path $repo 'rust')
$bin = Join-Path $ToolchainPath 'bin'
if (-not (Test-Path -LiteralPath (Join-Path $bin 'ld.lld.exe'))) { throw 'LLVM-MinGW linker not found. Supply -ToolchainPath.' }
$env:PATH = $bin + ';' + $env:PATH
$env:CARGO_TARGET_X86_64_PC_WINDOWS_GNU_LINKER = Join-Path $bin 'ld.lld.exe'
$env:RUSTFLAGS = '-C linker-flavor=ld.lld -C link-self-contained=yes'
$env:SAYINPUT_WINDRES = Join-Path $bin 'x86_64-w64-mingw32-windres.exe'
if ($Lint) {
    & cargo +stable-x86_64-pc-windows-gnu fmt --check
    if ($LASTEXITCODE -ne 0) { throw 'Rust formatting failed.' }
    & cargo +stable-x86_64-pc-windows-gnu clippy --release --locked --offline -- -D warnings
} elseif ($CheckOnly) {
    & cargo +stable-x86_64-pc-windows-gnu check --release --locked --offline
} else {
    & cargo +stable-x86_64-pc-windows-gnu build --release --locked --offline
}
if ($LASTEXITCODE -ne 0) { throw 'Rust compilation failed.' }
if ($CheckOnly -or $BuildOnly -or $Lint) { exit 0 }
$source = Join-Path $repo 'rust/target/release/SayInput.exe'
$destination = Join-Path $repo 'artifacts/SayInput.exe'
$running = @(Get-Process -Name SayInput -ErrorAction SilentlyContinue)
foreach ($appProcess in $running) {
    if (-not $appProcess.HasExited) {
        Stop-Process -InputObject $appProcess -Force -ErrorAction Stop
        if (-not $appProcess.WaitForExit(5000)) { throw 'SayInput did not exit before EXE replacement.' }
    }
}
Copy-Item -LiteralPath $source -Destination $destination -Force -ErrorAction Stop
if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { throw 'Deployed EXE hash mismatch.' }
Write-Host "EXE: $destination"
Write-Host "Bytes: $((Get-Item -LiteralPath $destination).Length)"
Write-Host "SHA256: $((Get-FileHash -LiteralPath $destination).Hash)"
