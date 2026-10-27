# 칸칸 제거: 프로그램 파일·자동 실행·시작 메뉴 바로가기를 지운다.
# 영역 데이터(%APPDATA%\KanKan)는 남긴다. 함께 지우려면 -RemoveData.
#   사용: powershell -ExecutionPolicy Bypass -File scripts\uninstall.ps1 [-RemoveData]
param([switch]$RemoveData)
$ErrorActionPreference = 'Stop'

$dest = Join-Path $env:LOCALAPPDATA 'Programs\KanKan'
$data = Join-Path $env:APPDATA 'KanKan'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) '칸칸.lnk'

# 방금 친 메모가 저장되도록 먼저 칸칸에게 종료를 부탁하고, 끝나지 않으면(종료 이벤트가 없는 옛 버전 등) 강제로 끈다
$exitEvent = $null
$asked = [Threading.EventWaitHandle]::TryOpenExisting('KanKan.Exit', [ref]$exitEvent)
if ($asked) { $exitEvent.Set() | Out-Null; $exitEvent.Dispose() }
Get-Process KanKan -ErrorAction SilentlyContinue | ForEach-Object {
    if (-not ($asked -and $_.WaitForExit(5000))) { $_.Kill(); $_.WaitForExit(5000) | Out-Null }
}

Remove-ItemProperty -Path $runKey -Name 'KanKan' -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName 'KanKan' -Confirm:$false -ErrorAction SilentlyContinue
if (Test-Path $shortcut) { Remove-Item $shortcut }
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }

if ($RemoveData) {
    if (Test-Path $data) { Remove-Item $data -Recurse -Force }
    Write-Host "제거 완료 (영역 데이터까지 삭제)"
} else {
    Write-Host "제거 완료. 영역 데이터는 남겨 둠: $data"
}
