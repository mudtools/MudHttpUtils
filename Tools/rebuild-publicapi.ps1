# -----------------------------------------------------------------------
#  Mud.HttpUtils - PublicAPI 契约基线重建脚本（H-4）
#
#  用途：为所有发布库的动态多 TFM 一次性生成/回归 PublicAPI 基线文件
#        （PublicAPI/<tfm>/PublicAPI.Shipped.txt + PublicAPI.Unshipped.txt）。
#
#  原理：
#    1. 以 -p:PublicApiGenerateFiles=true 构建各库各 TFM —— 由 Directory.Build.targets
#       中 GeneratePublicApiBaselines target 调用 Mono.ApiTools.GeneratePublicApiFiles，
#       将当前公共 API 集合写入 PublicAPI.Unshipped.txt（新 API 均视为未发布）。
#    2. 将 Unshipped 的 API 清单并入 Shipped（项目未发布，无历史 Shipped 基线，全部固化进 Shipped）。
#    3. Unshipped 复位为仅含 "#nullable enable" 表头。
#
#  用法：.\tools\rebuild-publicapi.ps1
#       （可选）.\tools\rebuild-publicapi.ps1 -Configuration Release
# -----------------------------------------------------------------------
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$RootDir = Split-Path -Parent $PSScriptRoot
$script:failed = $false
$Header = "#nullable enable"

# ─────────────────────────────────────────────────────────────────────
# 发布库发现（与 Directory.Build.targets 的 EnablePublicApiBaseline 判定对齐）：
#   含 <PackageId> 且 <IsRoslynComponent> != true 且 <PackAsTool> != true
#   且 <IncludeBuildOutput> != false 的工程即为 PublicAPI 基线候选库。逐工程解析其实际多 TFM（TargetFrameworks/TargetFramework），
#   避免硬编码 4 TFM 遗漏单 TFM 库（如 Mud.HttpUtils.Attributes）。
# ─────────────────────────────────────────────────────────────────────

function Get-ProjectXml($projPath) {
    # ReadAllText 正确检测 UTF-8 / BOM，避免 PS5.1 的 Get-Content 对无 BOM 中文文件
    # 按 ANSI 误解码后在 XML 解析时抛 "invalid attribute character" 异常。
    $text = [System.IO.File]::ReadAllText($projPath)
    $doc = New-Object 'System.Xml.XmlDocument'
    $doc.LoadXml($text)
    return $doc
}

function Get-Prop($doc, $name) {
    # csproj 的 <Project> 无 xmlns，子元素属空命名空间，不能用前缀命名空间 XPath
    # （否则 '//m:PackageId' 匹配不到）。用 local-name() 按元素名取首个匹配。
    $node = $doc.SelectSingleNode("//*[local-name()='$name']")
    if ($node) { return $node.InnerText.Trim() }
    return ''
}

# 返回该工程应纳入基线的 TFM 列表；非发布库返回 $null。
function Get-PublishedTfms($projPath) {
    $xml = Get-ProjectXml $projPath
    if (-not (Get-Prop $xml 'PackageId')) { return $null }
    if ((Get-Prop $xml 'IsRoslynComponent') -eq 'true') { return $null }
    if ((Get-Prop $xml 'PackAsTool') -eq 'true') { return $null }
    if ((Get-Prop $xml 'OutputType') -eq 'Exe') { return $null }
    if ((Get-Prop $xml 'IncludeBuildOutput') -eq 'false') { return $null }
    if ((Get-Prop $xml 'IsTestProject') -eq 'true') { return $null }

    $multi = Get-Prop $xml 'TargetFrameworks'
    if ($multi) { return @($multi -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
    $single = Get-Prop $xml 'TargetFramework'
    if ($single) { return @($single) }
    return @()
}

# 递归发现所有发布候选 csproj（含 tests 测试辅助包、子目录库），
# 排除 bin/obj 复制产物，判定条件与 Directory.Build.targets 的 EnablePublicApiBaseline 对齐。
# 注释：不假设 "目录名 == csproj 名"，一律从 csproj 文件本身解析。
$Libs = @()
Get-ChildItem -Path $RootDir -Filter '*.csproj' -Recurse |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|artifacts|\.git)\\' } |
    ForEach-Object {
        try {
            $tfms = Get-PublishedTfms $_.FullName
        } catch {
            Write-Host "  [skip] 无法解析 $($_.FullName)：$($_.Exception.Message)" -ForegroundColor DarkYellow
            return
        }
        if ($null -ne $tfms -and $tfms.Count -gt 0) {
            $script:Libs += [PSCustomObject]@{ Name = $_.BaseName; ProjPath = $_.FullName; Tfms = $tfms }
        }
    }

# UTF-8 无 BOM 写入（PublicApiAnalyzers 表头行忌 BOM）
function Write-Utf8NoBom($path, $lines) {
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllLines($path, $lines, $utf8)
}

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }

function Generate-Baseline($lib, $tfm) {
    $projDir = Split-Path -Parent $lib.ProjPath
    Write-Host "  [build] $($lib.Name) ($tfm) ... " -NoNewline

    # -p:EnablePublicApiBaseline=true 确保分析器与 AdditionalFiles 就绪
    & dotnet build $lib.ProjPath -f $tfm -c $Configuration `
        -p:PublicApiGenerateFiles=true -p:EnablePublicApiBaseline=true `
        -nologo -v:minimal 2>&1 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }

    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED" -ForegroundColor Red
        $script:failed = $true
        return
    }

    $shipPath = Join-Path $projDir "PublicAPI\$tfm\PublicAPI.Shipped.txt"
    $unshipPath = Join-Path $projDir "PublicAPI\$tfm\PublicAPI.Unshipped.txt"

    # 固化：把 Unshipped 的 API 并入 Shipped
    $unshipLines = Get-Content -Path $unshipPath
    $newApis = $unshipLines | Where-Object { ($_ -and (-not ($_ -like "#nullable*"))) }

    $existingShip = @()
    if (Test-Path $shipPath) {
        $existingShip = Get-Content -Path $shipPath | Where-Object { ($_ -and (-not ($_ -like "#nullable*"))) }
    }

    $merged = @($existingShip) + @($newApis) | Select-Object -Unique
    # 写入 Shipped（含 "#nullable enable" 表头）
    Write-Utf8NoBom $shipPath (@($Header) + $merged + "")
    # 复位 Unshipped
    Write-Utf8NoBom $unshipPath @($Header + "")

    Write-Host "OK (Shipped=$($merged.Count) 行)" -ForegroundColor Green
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Mud.HttpUtils PublicAPI 基线重建" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

foreach ($lib in $Libs) {
    $projDir = Split-Path -Parent $lib.ProjPath
    Write-Step "重建 $($lib.Name)"
    # 先确保基线占位文件存在，避免 csc 因 AdditionalFiles 缺失报 CS2001
    foreach ($tfm in $lib.Tfms) {
        $dir = Join-Path $projDir "PublicAPI\$tfm"
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        foreach ($f in @("PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt")) {
            $p = Join-Path $dir $f
            if (-not (Test-Path $p)) { Write-Utf8NoBom $p @($Header + "") }
        }
    }
    foreach ($tfm in $lib.Tfms) {
        Generate-Baseline $lib $tfm
    }
}

if ($script:failed) {
    Write-Host "存在失败项，请检查上述错误输出。" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "基线重建完成。" -ForegroundColor Green
Write-Host "提示：请运行 diff 复核基线，然后执行 strict 校验："
Write-Host "  dotnet build -p:PublicApiStrictMode=true"
exit 0