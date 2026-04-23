# -----------------------------------------------------------------------
#  Mud.HttpUtils 打包脚本
#  用法: .\pack.ps1 [配置] [版本号]
#  示例: .\pack.ps1 Release 1.7.0
# -----------------------------------------------------------------------

param(
    [string]$Configuration = "Release",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Mud.HttpUtils 打包脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 如果指定了版本号，更新 Directory.Build.props
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "更新版本号: $Version" -ForegroundColor Yellow
    $propsPath = Join-Path $RootDir "Directory.Build.props"
    $propsContent = Get-Content $propsPath -Raw
    $propsContent = $propsContent -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>"
    Set-Content $propsPath $propsContent -NoNewline
    Write-Host "版本号已更新" -ForegroundColor Green
    Write-Host ""
}

# 要打包的项目列表
$Projects = @(
    "Mud.HttpUtils.Abstractions",
    "Mud.HttpUtils.Attributes",
    "Mud.HttpUtils.Client",
    "Mud.HttpUtils.Resilience",
    "Mud.HttpUtils",
    "Mud.HttpUtils.Generator"
)

# 清理旧的包
$ArtifactsDir = Join-Path $RootDir "artifacts"
if (Test-Path $ArtifactsDir) {
    Write-Host "清理旧的包文件..." -ForegroundColor Yellow
    Remove-Item $ArtifactsDir -Recurse -Force
}
New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
Write-Host ""

# 还原依赖
Write-Host "还原依赖..." -ForegroundColor Yellow
dotnet restore $RootDir\Mud.HttpUtils.slnx
if ($LASTEXITCODE -ne 0) {
    Write-Host "还原依赖失败！" -ForegroundColor Red
    exit 1
}
Write-Host "还原完成" -ForegroundColor Green
Write-Host ""

# 逐项目打包
$FailedProjects = @()

foreach ($project in $Projects) {
    $projectDir = Join-Path $RootDir $project
    $csproj = Join-Path $projectDir "$project.csproj"

    if (-not (Test-Path $csproj)) {
        Write-Host "跳过 (未找到项目文件): $project" -ForegroundColor DarkGray
        continue
    }

    Write-Host "打包: $project" -ForegroundColor Yellow

    dotnet pack $csproj -c $Configuration -o $ArtifactsDir --no-restore

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  失败: $project" -ForegroundColor Red
        $FailedProjects += $project
    } else {
        Write-Host "  成功: $project" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan

if ($FailedProjects.Count -gt 0) {
    Write-Host "  打包完成，以下项目失败:" -ForegroundColor Red
    foreach ($p in $FailedProjects) {
        Write-Host "    - $p" -ForegroundColor Red
    }
    exit 1
} else {
    $packageCount = (Get-ChildItem $ArtifactsDir -Filter "*.nupkg").Count
    Write-Host "  打包完成！共生成 $packageCount 个包" -ForegroundColor Green
    Write-Host "  输出目录: $ArtifactsDir" -ForegroundColor Cyan
}

Write-Host "========================================" -ForegroundColor Cyan