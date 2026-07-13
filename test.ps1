# -----------------------------------------------------------------------
#  Mud.HttpUtils 测试脚本
#  用法: .\test.ps1 [配置] [过滤器] [-AOT]
#  示例: .\test.ps1 Debug
#         .\test.ps1 Debug "Resilience"
#         .\test.ps1 -AOT          # 包含 AOT 发布验证
# -----------------------------------------------------------------------

param(
    [string]$Configuration = "Debug",
    [string]$Filter = "",
    [switch]$AOT
)

$ErrorActionPreference = "Stop"
$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Mud.HttpUtils 测试脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
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

# 构建项目
Write-Host "构建项目..." -ForegroundColor Yellow
dotnet build $RootDir\Mud.HttpUtils.slnx -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    Write-Host "构建失败！" -ForegroundColor Red
    exit 1
}
Write-Host "构建完成" -ForegroundColor Green
Write-Host ""

# 测试项目列表
$TestProjects = @(
    "Tests/Mud.HttpUtils.Client.Tests",
    "Tests/Mud.HttpUtils.Resilience.Tests",
    "Tests/Mud.HttpUtils.Generator.Tests"
)

$TotalPassed = 0
$TotalFailed = 0
$FailedProjects = @()

foreach ($testProject in $TestProjects) {
    $csproj = Join-Path $RootDir "$testProject/*.csproj"

    if (-not (Test-Path (Join-Path $RootDir $testProject))) {
        Write-Host "跳过 (未找到测试项目): $testProject" -ForegroundColor DarkGray
        continue
    }

    Write-Host "测试: $testProject" -ForegroundColor Yellow

    $filterArg = ""
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $filterArg = "--filter `"$Filter`""
    }

    $command = "dotnet test `"$RootDir\$testProject`" -c $Configuration --no-build --verbosity normal $filterArg"
    Invoke-Expression $command

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  失败: $testProject" -ForegroundColor Red
        $FailedProjects += $testProject
    } else {
        Write-Host "  通过: $testProject" -ForegroundColor Green
    }
    Write-Host ""
}

Write-Host "========================================" -ForegroundColor Cyan

if ($FailedProjects.Count -gt 0) {
    Write-Host "  测试完成，以下项目失败:" -ForegroundColor Red
    foreach ($p in $FailedProjects) {
        Write-Host "    - $p" -ForegroundColor Red
    }
    exit 1
} else {
    Write-Host "  所有测试通过！" -ForegroundColor Green
}

Write-Host "========================================" -ForegroundColor Cyan

# ──────────────────────────────────────────────
# Native AOT 发布验证（可选，通过 -AOT 参数触发）
# ──────────────────────────────────────────────

if ($AOT) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  Native AOT 发布验证" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    $AotDemo = Join-Path $RootDir "Demos/AotVerificationDemo/AotVerificationDemo.csproj"
    $Rid = if ($IsLinux) { "linux-x64" } elseif ($IsMacOS) { "osx-x64" } else { "win-x64" }

    foreach ($tfm in @("net8.0", "net10.0")) {
        Write-Host "发布 AOT ($tfm)..." -ForegroundColor Yellow
        dotnet publish $AotDemo -c Release -f $tfm -r $Rid
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  AOT 发布失败 ($tfm)！" -ForegroundColor Red
            exit 1
        }

        $binPath = Join-Path $RootDir "Demos/AotVerificationDemo/bin/Release/$tfm/$Rid/publish/AotVerificationDemo"
        if ($Rid -eq "win-x64") {
            $binPath += ".exe"
        }

        if (Test-Path $binPath) {
            Write-Host "运行 AOT 二进制 ($tfm)..." -ForegroundColor Yellow
            $output = & $binPath 2>&1
            $outputStr = $output -join "`n"
            Write-Host $outputStr

            if ($outputStr -match "AOT_OK") {
                Write-Host "  AOT 运行时验证通过 ($tfm)" -ForegroundColor Green
            } else {
                Write-Host "  AOT 运行时验证失败 ($tfm) - 未找到 AOT_OK" -ForegroundColor Red
                exit 1
            }
        } else {
            Write-Host "  二进制未找到: $binPath" -ForegroundColor Red
            exit 1
        }
        Write-Host ""
    }

    Write-Host "  AOT 验证全部通过！" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
}
