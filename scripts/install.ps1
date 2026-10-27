# 개발 PC용 칸칸 설치: 소스에서 Release 빌드한 뒤 install-app.ps1 로 설치한다.
# 다시 실행하면 최신 코드로 업데이트된다. 영역 데이터(%APPDATA%\KanKan)는 건드리지 않는다.
#   사용: powershell -ExecutionPolicy Bypass -File scripts\install.ps1
# 다른 PC에 설치하려면 scripts\package.ps1 로 만든 배포 zip을 쓴다.
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($dotnet) { $dotnet.Source } else { 'C:\Program Files\dotnet\dotnet.exe' }
if (-not (Test-Path $dotnet)) { throw '.NET SDK를 찾지 못했습니다. (winget install Microsoft.DotNet.SDK.8)' }

# 빌드가 실패하면 실행 중인 칸칸을 끄지 않도록, 임시 폴더에 먼저 빌드한 뒤 설치한다
$staging = Join-Path ([IO.Path]::GetTempPath()) 'KanKan-publish'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }

Write-Host '빌드'
& $dotnet publish (Join-Path $root 'src\KanKan\KanKan.csproj') -c Release -o $staging --nologo -v quiet -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "빌드 실패 (exit $LASTEXITCODE) - 설치된 칸칸은 그대로 둡니다" }

& (Join-Path $PSScriptRoot 'install-app.ps1') -SourceDir $staging
Remove-Item $staging -Recurse -Force
Write-Host '제거하려면: powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1'
