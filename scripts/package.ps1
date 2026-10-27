# 다른 PC용 배포 zip을 만든다: dist\KanKan-<버전>.zip
#   - .NET 런타임을 포함한 단일 실행 파일이라 받는 PC에 아무것도 설치할 필요가 없다 (Windows 10/11 64비트)
#   - 받는 PC에서는 압축을 풀고 '설치.cmd' 더블클릭
#   사용: powershell -ExecutionPolicy Bypass -File scripts\package.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\KanKan\KanKan.csproj'
$version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$name = "KanKan-$version"
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist $name
$zip = Join-Path $dist "$name.zip"

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnet = if ($dotnet) { $dotnet.Source } else { 'C:\Program Files\dotnet\dotnet.exe' }

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Force (Join-Path $stage 'setup') | Out-Null

Write-Host "1/3 단일 실행 파일 빌드 (버전 $version)"
& $dotnet publish $project -c Release -r win-x64 --self-contained true -o (Join-Path $stage 'app') --nologo -v quiet `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "빌드 실패 (exit $LASTEXITCODE)" }

Write-Host '2/3 설치 파일 구성'
Copy-Item (Join-Path $PSScriptRoot 'install-app.ps1'), (Join-Path $PSScriptRoot 'uninstall.ps1') (Join-Path $stage 'setup')

# .cmd 는 한글이 깨지지 않도록 ASCII 로만 쓴다
$installCmd = "@echo off`r`npowershell -NoProfile -ExecutionPolicy Bypass -File `"%~dp0setup\install-app.ps1`" -SourceDir `"%~dp0app`"`r`npause`r`n"
$uninstallCmd = "@echo off`r`npowershell -NoProfile -ExecutionPolicy Bypass -File `"%~dp0setup\uninstall.ps1`"`r`npause`r`n"
[IO.File]::WriteAllText((Join-Path $stage '설치.cmd'), $installCmd, [Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $stage '제거.cmd'), $uninstallCmd, [Text.Encoding]::ASCII)

$readme = @"
칸칸 $version - 바탕화면 영역 정리

[설치]
1. 이 zip 파일의 압축을 풉니다. (zip 안에서 바로 실행하지 마세요)
2. '설치.cmd' 를 더블클릭합니다.
   - Windows 가 '알 수 없는 앱' 경고를 띄우면 [추가 정보] → [실행] 을 누르세요.
3. 설치가 끝나면 칸칸이 실행되고, 다음부터는 Windows 시작 시 자동으로 켜집니다.
   시작 메뉴에서 '칸칸' 으로도 실행할 수 있습니다.

[사용]
- 작업 표시줄 오른쪽 알림 영역의 칸칸 아이콘 → 새 영역 만들기
- 영역 제목 바: 드래그로 이동, 테두리로 크기 조절, + 옆에 새 영역, 핀 고정, ... 메뉴
- 영역은 바탕화면에 붙어 있어서 다른 창 아래에 깔립니다. (Win+D 로 바탕화면 보기)

[업데이트]
새 버전 zip 의 '설치.cmd' 를 실행하면 덮어씁니다. 영역·노트는 그대로 남습니다.

[제거]
'제거.cmd' 를 더블클릭합니다. 영역·노트 데이터(%APPDATA%\KanKan)는 남겨 둡니다.

[요구 사항]
Windows 10/11 64비트. 별도 설치 프로그램은 필요 없습니다.
"@
[IO.File]::WriteAllText((Join-Path $stage '읽어주세요.txt'), $readme.Replace("`n", "`r`n"), (New-Object Text.UTF8Encoding $true))

Write-Host "3/3 압축 → $zip"
# PowerShell 5.1의 Compress-Archive·ZipFile 은 zip 안 경로를 '\'로 저장해 압축 프로그램에 따라 깨지므로,
# 항목 이름을 표준대로 '/'로 직접 넣는다 (한글 이름은 UTF-8 표시가 붙는다)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $stage -Recurse -File | ForEach-Object {
        $entry = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally {
    $archive.Dispose()
}
Remove-Item $stage -Recurse -Force

$size = [Math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "완료: $zip ($size MB)"
