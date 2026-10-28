param(
    [string]$GameRoot = "D:\植物大战僵尸\WP_PGVZ\PlantGirlsVsZombies"
)

$utf8 = New-Object System.Text.UTF8Encoding $false
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath (Join-Path $GameRoot 'Lawn.exe'))) {
    throw "Lawn.exe not found under: $GameRoot"
}

$dataRoot = Join-Path $env:APPDATA 'ZBC\PlantGirlsVsZombies'
$mods = Join-Path $dataRoot 'mods'
$source = Join-Path $PSScriptRoot '..\src\PGvZNewPlantMod\bin\Release\net6.0'
$hostDll = Join-Path $source 'PGvZNewPlantMod.dll'
$kitDll = Join-Path $source 'PGvZPlantKit.dll'
$sourceMod = Join-Path $source 'PGvZNewPlantMod'

if (-not (Test-Path -LiteralPath $hostDll) -or
    -not (Test-Path -LiteralPath $kitDll) -or
    -not (Test-Path -LiteralPath $sourceMod)) {
    throw "Build first: dotnet build PGvZNewPlantMod.slnx -c Release (missing host DLL, kit DLL or data folder)"
}

New-Item -ItemType Directory -Path $mods -Force | Out-Null

# 顶层只放一个 mod DLL
$targetHost = Join-Path $mods 'PGvZNewPlantMod.dll'
if (Test-Path -LiteralPath $targetHost) {
    Copy-Item -LiteralPath $targetHost -Destination "$targetHost.bak" -Force
}
Copy-Item -LiteralPath $hostDll -Destination $targetHost -Force

$modFolder = Join-Path $mods 'PGvZNewPlantMod'
New-Item -ItemType Directory -Path $modFolder,
    (Join-Path $modFolder 'plants'),
    (Join-Path $modFolder 'assets') -Force | Out-Null

# 框架库放到同名文件夹里，由宿主解析
$targetKit = Join-Path $modFolder 'PGvZPlantKit.dll'
if (Test-Path -LiteralPath $targetKit) {
    Copy-Item -LiteralPath $targetKit -Destination "$targetKit.bak" -Force
}
Copy-Item -LiteralPath $kitDll -Destination $targetKit -Force

# Spine 已内嵌在各植物插件 DLL 中，不再复制到 mod 目录或游戏 Content。

# 说明文件可更新；用户植物/卡图/清单不覆盖
$readme = Join-Path $sourceMod 'README.md'
if (Test-Path -LiteralPath $readme) {
    Copy-Item -LiteralPath $readme -Destination (Join-Path $modFolder 'README.md') -Force
}
$assetsReadme = Join-Path $sourceMod 'assets\README.md'
if (Test-Path -LiteralPath $assetsReadme) {
    Copy-Item -LiteralPath $assetsReadme -Destination (Join-Path $modFolder 'assets\README.md') -Force
}

$manifest = Join-Path $modFolder 'plant_packs.json'
if (-not (Test-Path -LiteralPath $manifest)) {
    Copy-Item -LiteralPath (Join-Path $sourceMod 'plant_packs.json') -Destination $manifest -Force
} else {
    Write-Host 'NOTE: existing plant_packs.json kept (edit it to change load order).' -ForegroundColor DarkGray
}
$template = Join-Path $modFolder 'plant_packs.template.json'
if (-not (Test-Path -LiteralPath $template)) {
    Copy-Item -LiteralPath (Join-Path $sourceMod 'plant_packs.template.json') -Destination $template -Force
}

# C# 植物插件是构建产物，始终覆盖；Python 植物只补缺失，保留用户修改
Get-ChildItem -LiteralPath (Join-Path $sourceMod 'plants') -Filter '*.dll' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $modFolder 'plants') -Force
}
# 已废弃/移走的工程不再产出 DLL；目标目录里的旧副本必须清掉，否则删掉的植物还会被加载。
$keep = @{}
Get-ChildItem -LiteralPath (Join-Path $sourceMod 'plants') -Filter '*.dll' | ForEach-Object {
    $keep[$_.Name.ToLowerInvariant()] = $true
}
Get-ChildItem -LiteralPath (Join-Path $modFolder 'plants') -Filter 'PGvZNewPlantMod.*.dll' | ForEach-Object {
    if (-not $keep.ContainsKey($_.Name.ToLowerInvariant())) {
        $archiveDir = Join-Path $modFolder '_archived_plants'
        New-Item -ItemType Directory -Path $archiveDir -Force | Out-Null
        Move-Item -LiteralPath $_.FullName -Destination (Join-Path $archiveDir $_.Name) -Force
        Write-Host ('Moved stale plant plugin out of load path: ' + $_.Name) -ForegroundColor DarkGray
    }
}
Get-ChildItem -LiteralPath (Join-Path $sourceMod 'plants') -Filter '*.py' | ForEach-Object {
    $target = Join-Path (Join-Path $modFolder 'plants') $_.Name
    if (-not (Test-Path -LiteralPath $target)) {
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    } else {
        Write-Host ("NOTE: kept existing plant " + $_.Name) -ForegroundColor DarkGray
    }
}
Get-ChildItem -LiteralPath (Join-Path $sourceMod 'assets') -File |
    Where-Object { $_.Name -ne 'README.md' } | ForEach-Object {
    $target = Join-Path (Join-Path $modFolder 'assets') $_.Name
    if (-not (Test-Path -LiteralPath $target)) {
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    }
}

# 迁移旧的顶层布局：旧 kit、旧顶层示例、旧顶层清单都不再使用
foreach ($legacyName in 'PGvZPlantKit.dll', '星尘花示例.py', 'plant_packs.json') {
    $legacy = Join-Path $mods $legacyName
    if (-not (Test-Path -LiteralPath $legacy)) {
        continue
    }
    try {
        Move-Item -LiteralPath $legacy -Destination "$legacy.bak" -Force
        Write-Host ("Moved legacy top-level " + $legacyName + " to .bak") -ForegroundColor DarkGray
    } catch {
        throw ("Cannot move " + $legacy + " (game still running?). Close the game and rerun.")
    }
}

# 旧 SaveData 迁移到新数据文件夹（只在目标不存在时复制）
$oldSave = Join-Path $mods 'SaveData'
$newSave = Join-Path $modFolder 'SaveData'
if ((Test-Path -LiteralPath $oldSave) -and -not (Test-Path -LiteralPath $newSave)) {
    Copy-Item -LiteralPath $oldSave -Destination $newSave -Recurse -Force
    Write-Host 'Migrated old SaveData into PGvZNewPlantMod/SaveData' -ForegroundColor DarkGray
}

$pythonTool = Join-Path $mods 'pgvztool'
if (Test-Path -LiteralPath $pythonTool) {
    Write-Host ''
    Write-Host 'WARNING: mods\pgvztool (Python 九植物) is still installed.' -ForegroundColor Yellow
    Write-Host 'It registers the same seed IDs 65-67 / 87-93. Disable or remove it.' -ForegroundColor Yellow
}
$pythonApi = Join-Path $mods 'pgvz'
if (Test-Path -LiteralPath $pythonApi) {
    Write-Host 'NOTE: mods\pgvz is the Python API used by other Python mods; left untouched.' -ForegroundColor DarkGray
}

Write-Host ''
Write-Host 'Installed (one top-level DLL + one data folder).' -ForegroundColor Green
Write-Host "  DLL    : $targetHost"
Write-Host "  Folder : $modFolder"
Write-Host "  Kit    : $targetKit"
Write-Host "  Plants : $(Join-Path $modFolder 'plants')"
Write-Host "  Order  : $manifest"
Write-Host 'Fully close and restart the game.'
