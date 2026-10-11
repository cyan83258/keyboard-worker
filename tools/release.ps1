# 새 버전을 내는 스크립트.
#   powershell -ExecutionPolicy Bypass -File tools\release.ps1 2.1.0
# 하는 일: (1) 프로그램의 버전 번호를 바꾸고 (2) 배포용 ZIP을 만들고 (3) GitHub의 "새 릴리스" 화면과 ZIP이 있는 폴더를 열어 줍니다.
# 그다음 할 일은 열린 화면에 ZIP 파일을 끌어다 놓고 "Publish release"를 누르는 것뿐입니다.
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+(\.\d+)?$') { Write-Host '버전은 2.1.0 같은 숫자로 적어 주세요.'; exit 1 }
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'KeyboardWorker.csproj'
$text = [IO.File]::ReadAllText($project)
if ($text -notmatch '<UpdateRepository>\s*([^<\s]+/[^<\s]+)\s*</UpdateRepository>') { Write-Host 'KeyboardWorker.csproj의 <UpdateRepository>에 GitHub 주소(계정이름/저장소이름)가 아직 없습니다.'; exit 1 }
$repository = $Matches[1]
$text = [regex]::Replace($text, '<Version>[^<]*</Version>', "<Version>$Version</Version>")
[IO.File]::WriteAllText($project, $text, (New-Object Text.UTF8Encoding $false))
Write-Host "버전을 $Version(으)로 바꿨습니다. 배포 파일을 만드는 중..."
& (Join-Path $PSScriptRoot 'make-dist.ps1')
if ($LASTEXITCODE -ne 0) { Write-Host '배포 파일을 만들지 못했습니다.'; exit 1 }
$zip = Join-Path $root 'dist\KeyboardWorker-win-x64.zip'
Write-Host ''
Write-Host '이제 두 가지만 하면 됩니다.'
Write-Host '  1. 바뀐 소스를 GitHub에 올리기 (GitHub Desktop에서 Commit -> Push)'
Write-Host '  2. 지금 열리는 GitHub 화면에 KeyboardWorker-win-x64.zip 을 끌어다 놓고 "Publish release" 누르기'
Start-Process "https://github.com/$repository/releases/new?tag=v$Version&title=$Version"
Start-Process explorer.exe "/select,`"$zip`""