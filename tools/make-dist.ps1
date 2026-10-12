# Makes the package for other people: dist\KeyboardWorker\ and dist\KeyboardWorker-win-x64.zip.
#   powershell -ExecutionPolicy Bypass -File tools\make-dist.ps1
# The package is built from source and contains the program and the .NET runtime only. Your own settings, API keys,
# presets and pictures live in %LOCALAPPDATA%\KeyboardWorker and are never read here. As a last step the program files
# are searched for the words listed in $private; add anything personal that must never ship.
param([string[]]$Private = @('Users\' + $env:USERNAME, 'D:\code'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $root 'dist\KeyboardWorker'
$zip = Join-Path $root 'dist\KeyboardWorker-win-x64.zip'

dotnet publish (Join-Path $root 'KeyboardWorker.csproj') -c Release -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -p:SatelliteResourceLanguages=ko -o $folder --nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host 'Publish failed.'; exit 1 }

# The note for the person who unpacks it.
Copy-Item (Join-Path $PSScriptRoot '읽어주세요.txt') (Join-Path $folder '읽어주세요.txt') -Force

# Only program files may be in the package.
$unexpected = Get-ChildItem $folder -Recurse -File | Where-Object { -not ($_.FullName -like (Join-Path $folder 'backgrounds\*') -and $_.Extension -in '.png', '.jpg', '.jpeg') -and -not ($_.FullName -like (Join-Path $folder 'accessories\*') -and $_.Extension -in '.png', '.jpg', '.jpeg', '.json') -and $_.Extension -notin '.dll', '.exe' -and $_.Name -notin 'KeyboardWorker.deps.json', 'KeyboardWorker.runtimeconfig.json', '읽어주세요.txt' }
if ($unexpected) { Write-Host 'Unexpected files in the package:'; $unexpected | ForEach-Object { Write-Host "  $($_.FullName)" }; exit 1 }

$hits = 0
foreach ($file in Get-ChildItem $folder -File | Where-Object { $_.Name -like 'KeyboardWorker*' }) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName); $latin = [Text.Encoding]::GetEncoding(28591); $text = $latin.GetString($bytes)
    foreach ($word in $Private) {
        foreach ($form in $latin.GetString([Text.Encoding]::UTF8.GetBytes($word)), $latin.GetString([Text.Encoding]::Unicode.GetBytes($word))) {
            if ($text.IndexOf($form, [StringComparison]::OrdinalIgnoreCase) -ge 0) { Write-Host "Private text '$word' found in $($file.Name)"; $hits++; break }
        }
    }
}
if ($hits -gt 0) { Write-Host 'The package was NOT zipped.'; exit 1 }

if (Test-Path $zip) { [IO.File]::Delete($zip) }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($folder, $zip, 'Optimal', $true, [Text.Encoding]::UTF8)
# The publish step leaves a second copy of the runtime under bin and obj; it is only needed while publishing.
foreach ($left in 'bin\Release\net10.0-windows10.0.19041.0\win-x64', 'obj\Release\net10.0-windows10.0.19041.0\win-x64') { $path = Join-Path $root $left; if (Test-Path $path) { [IO.Directory]::Delete($path, $true) } }
Write-Host "Package ready: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
