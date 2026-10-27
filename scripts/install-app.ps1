# 이미 빌드된 칸칸(SourceDir 안의 KanKan.exe 등)을 설치한다.
#   - %LOCALAPPDATA%\Programs\KanKan 에 복사 (실행 중이면 끄고 교체)
#   - Windows 시작 시 자동 실행 + 시작 메뉴 바로가기 등록
#   - 실행
# 영역 데이터(%APPDATA%\KanKan)는 건드리지 않는다.
# 개발용 scripts\install.ps1 과 배포 패키지의 설치.cmd 가 함께 쓴다.
param([Parameter(Mandatory)][string]$SourceDir)
$ErrorActionPreference = 'Stop'

$dest = Join-Path $env:LOCALAPPDATA 'Programs\KanKan'
$exe = Join-Path $dest 'KanKan.exe'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) '칸칸.lnk'

if (-not (Test-Path (Join-Path $SourceDir 'KanKan.exe'))) { throw "KanKan.exe 를 찾지 못했습니다: $SourceDir" }

Write-Host '실행 중인 칸칸 종료 후 설치'
# 방금 친 메모가 저장되도록 먼저 칸칸에게 종료를 부탁하고, 끝나지 않으면(종료 이벤트가 없는 옛 버전 등) 강제로 끈다
$exitEvent = $null
$asked = [Threading.EventWaitHandle]::TryOpenExisting('KanKan.Exit', [ref]$exitEvent)
if ($asked) { $exitEvent.Set() | Out-Null; $exitEvent.Dispose() }
Get-Process KanKan -ErrorAction SilentlyContinue | ForEach-Object {
    if (-not ($asked -and $_.WaitForExit(5000))) { $_.Kill(); $_.WaitForExit(5000) | Out-Null }
}
# 설치 방식(런타임 포함 단일 파일 ↔ 일반)이 바뀌어도 옛 파일이 섞이지 않도록 프로그램 폴더를 비운다 (영역 데이터는 다른 폴더)
if (Test-Path $dest) { Get-ChildItem $dest | Remove-Item -Recurse -Force }
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $SourceDir '*') $dest -Recurse -Force
# 인터넷에서 받은 zip을 풀면 붙는 '다운로드한 파일' 표시를 지워 실행 경고를 줄인다
Get-ChildItem $dest -Recurse -File | Unblock-File

Write-Host '자동 실행·시작 메뉴 등록'
# Run 키는 등록된 앱을 차례로 실행해서 늦게 뜨므로, 로그온 즉시 실행되는 작업 스케줄러를 쓴다 (앱의 AutoStart.cs와 같은 설정)
Remove-ItemProperty -Path $runKey -Name 'KanKan' -ErrorAction SilentlyContinue
$user = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
Register-ScheduledTask -TaskName 'KanKan' -Force -Description '칸칸 - Windows 로그인 시 자동 실행' `
    -Action (New-ScheduledTaskAction -Execute $exe -Argument '--autostart' -WorkingDirectory $dest) `
    -Trigger (New-ScheduledTaskTrigger -AtLogOn -User $user) `
    -Principal (New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited) `
    -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -Priority 5) | Out-Null
$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut($shortcut)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $dest
$lnk.Description = '바탕화면 영역 정리'
$lnk.Save()

Write-Host '실행'
Start-Process $exe

Write-Host ''
Write-Host "설치 완료: $exe"
