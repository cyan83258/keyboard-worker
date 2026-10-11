# Builds the small setup program (installer\) into dist\KeyboardWorker-Setup.exe.
#   powershell -ExecutionPolicy Bypass -File tools\make-installer.ps1
# The setup program always fetches the newest release from GitHub, so it only needs rebuilding when the installer itself changes.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'installer\bin\setup'
dotnet build (Join-Path $root 'installer\Installer.csproj') -c Release -o $out --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host 'Build failed.'; exit 1 }
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item (Join-Path $out 'KeyboardWorker-Setup.exe') $dist -Force
foreach ($name in 'bin', 'obj') { $dir = Join-Path $root "installer\$name"; if (Test-Path $dir) { [IO.Directory]::Delete($dir, $true) } }
$exe = Get-Item (Join-Path $dist 'KeyboardWorker-Setup.exe')
Write-Host ("Setup ready: {0} ({1:N0} KB)" -f $exe.FullName, ($exe.Length / 1KB))
