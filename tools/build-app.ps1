# Builds the program, runs the unit tests and copies the result into app\ (the copy you actually run).
#   powershell -ExecutionPolicy Bypass -File tools\build-app.ps1            build + tests + copy
#   powershell -ExecutionPolicy Bypass -File tools\build-app.ps1 -Smoke     also open every window once (screenshots land in bin\)
# Nothing is copied when the build or a test fails, or while Keyboard Worker is running.
param([switch]$Smoke)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'bin\Release\net10.0-windows10.0.19041.0'
$programFiles = 'KeyboardWorker.deps.json', 'KeyboardWorker.dll', 'KeyboardWorker.exe', 'KeyboardWorker.pdb', 'KeyboardWorker.runtimeconfig.json', 'Microsoft.Windows.SDK.NET.dll', 'WinRT.Runtime.dll'

if (Get-Process KeyboardWorker -ErrorAction SilentlyContinue) { Write-Host 'Keyboard Worker is running. Close it (tray icon > 종료) and run this again.'; exit 1 }

dotnet build (Join-Path $root 'KeyboardWorker.csproj') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host 'Build failed.'; exit 1 }
dotnet build (Join-Path $root 'tests\KeyboardWorker.Tests.csproj') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host 'Test project build failed.'; exit 1 }

& (Join-Path $root 'tests\bin\Release\net10.0-windows10.0.19041.0\KeyboardWorker.Tests.exe')
if ($LASTEXITCODE -ne 0) { Write-Host 'Unit tests failed. app\ was not changed.'; exit 1 }
# The tests' scratch folder is only useful while they run.
$scratch = Join-Path $root 'tests\bin\Release\net10.0-windows10.0.19041.0\test-data'; if (Test-Path $scratch) { [IO.Directory]::Delete($scratch, $true) }

if ($Smoke) {
    # Screenshots of an earlier run would hide a window that no longer opens.
    Get-ChildItem $out -File | Where-Object { $_.Name -match '^(ui-|preview-).*\.png$' } | ForEach-Object { [IO.File]::Delete($_.FullName) }
    $old = Join-Path $out 'ui-test-data'; if (Test-Path $old) { [IO.Directory]::Delete($old, $true) }
    $failure = Join-Path $out 'smoke-test-failure.txt'
    if (Test-Path $failure) { [IO.File]::Delete($failure) }
    $run = Start-Process (Join-Path $out 'KeyboardWorker.exe') -ArgumentList '--smoke-test' -PassThru
    if (-not $run.WaitForExit(60000)) { $run.Kill(); Write-Host 'Smoke test did not finish. app\ was not changed.'; exit 1 }
    if ($run.ExitCode -ne 0 -or (Test-Path $failure)) { Write-Host 'Smoke test failed:'; if (Test-Path $failure) { Get-Content $failure -Encoding UTF8 | Select-Object -First 5 }; exit 1 }
    Write-Host 'Smoke test passed.'
}

$app = Join-Path $root 'app'
New-Item -ItemType Directory -Force $app | Out-Null
foreach ($name in $programFiles) { Copy-Item (Join-Path $out $name) (Join-Path $app $name) -Force }
# Backgrounds that come with the program (Assets\Backgrounds): the app copy holds exactly what the build holds.
$pictures = Join-Path $app 'backgrounds'; if (Test-Path $pictures) { [IO.Directory]::Delete($pictures, $true) }
if (Test-Path (Join-Path $out 'backgrounds')) { Copy-Item (Join-Path $out 'backgrounds') $pictures -Recurse -Force }
Write-Host "app\ updated: $((Get-Item (Join-Path $app 'KeyboardWorker.dll')).LastWriteTime)"
