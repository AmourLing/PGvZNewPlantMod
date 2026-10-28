param(
    [string]$GameRoot = "D:\植物大战僵尸\WP_PGVZ\PlantGirlsVsZombies"
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false

$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

function Stop-WithMessage([string]$message) {
    Write-Host ''
    Write-Host ('[失败] ' + $message) -ForegroundColor Red
    Write-Host ''
    Read-Host '按 Enter 关闭'
    exit 1
}

# 部署会覆盖被游戏加载的 DLL，先挡住正在运行的实例（不替你结束游戏）。
foreach ($name in 'Lawn', 'Lawn.Console') {
    $running = @(Get-Process -Name $name -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        $ids = ($running | ForEach-Object { $_.Id }) -join ','
        Stop-WithMessage "检测到 $name.exe 正在运行（PID $ids）。请先完全退出游戏，再运行本脚本。"
    }
}

Write-Host '== 1/2 编译 Release ==' -ForegroundColor Cyan
& dotnet build 'PGvZNewPlantMod.slnx' -c Release
if ($LASTEXITCODE -ne 0) {
    Stop-WithMessage '编译没过，本次没有部署。'
}

Write-Host ''
Write-Host '== 2/2 部署到游戏 mods 目录 ==' -ForegroundColor Cyan
$shell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { 'pwsh' } else { 'powershell' }
& $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot '部署.ps1') -GameRoot $GameRoot
if ($LASTEXITCODE -ne 0) {
    Stop-WithMessage '部署出错。若提示文件被占用，请完全退出游戏后重跑。'
}

Write-Host ''
Write-Host '[完成] 已编译并部署，启动游戏即可生效。' -ForegroundColor Green
Write-Host ''
Read-Host '按 Enter 关闭'
