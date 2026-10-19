#Requires -Version 5.1
<#
.SYNOPSIS
CHANGELOG「新增」声明与 PublicAPI 基线一致性校验（演进治理 D-2）。

.DESCRIPTION
解析 CHANGELOG.md 顶部（最新）版本节中反引号标注的公共 API 形名字
（以 Mud.HttpUtils 开头的限定名，且带调用签名或三段及以上「命名空间.类型.成员」形状），
要求其必须出现在某个 PublicAPI.Shipped.txt 或 PublicAPI.Unshipped.txt 中：

  - 完全不存在  ==> 失败（exit 1）：声明了不存在的 API（笔误 / 改名 / 漏登记基线）；
  - 仅 Unshipped ==> 警告（exit 0）：发布提示，应由 Tools/rebuild-publicapi.ps1
    在发布时并入 Shipped，杜绝「声明已发布 / 基线未发布」脱节。

仅检查顶部版本节（最新版本），历史版本以基线快照为准不再回溯。
包名 / 工程名（两段，如 Mud.HttpUtils.Client）与下游仓库名（MudFeishu / Mud.Wechat）不视为 API 声明。
#>
param()

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot/..").Path
$changelogPath = Join-Path $repoRoot "CHANGELOG.md"

# ---- 定位顶部版本节 ----
$lines = Get-Content -LiteralPath $changelogPath
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s+\d+\.\d+\.\d+') { $start = $i; break }
}
if ($start -lt 0) {
    Write-Error "CHANGELOG.md 中找不到版本节标题（## x.y.z）。"
    exit 2
}
$end = $lines.Count
for ($i = $start + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^##\s') { $end = $i; break }
}
$section = $lines[$start..($end - 1)] -join "`n"
$version = ($lines[$start] -replace '^##\s+', '').Trim()
Write-Host "校验 CHANGELOG 顶部版本节：$version"

# ---- 汇总 PublicAPI 基线 ----
function Get-BaselineText {
    param([string]$Filter)
    $text = @()
    Get-ChildItem -Path $repoRoot -Recurse -Filter $Filter -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        ForEach-Object { $text += (Get-Content -LiteralPath $_.FullName -Raw) }
    return ($text -join "`n")
}

$allText = Get-BaselineText "PublicAPI.*.txt"
$shippedText = Get-BaselineText "PublicAPI.Shipped.txt"

# 源码兜底：内部类型（如分析器）不出现在 PublicAPI 基线，但可在 CHANGELOG 中提及；
# 聚合全部 .cs 文本供「既非基线、亦非源码」的判定，避免把内部实现误报为不存在。
$sourceText = @()
Get-ChildItem -Path $repoRoot -Recurse -Filter "*.cs" -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.FullName -notmatch '\\Demos\\' } |
    ForEach-Object { $sourceText += (Get-Content -LiteralPath $_.FullName -Raw) }
$sourceAll = $sourceText -join "`n"

# ---- 候选 API 形名字 ----
# ① 以 Mud.HttpUtils 开头的限定名（本仓公共 API 命名空间，精确判定）；
# ② 带调用签名的反引号 span（取方法名主干判定；路径/配置键除外）。
$rawSpans = [regex]::Matches($section, '`([^`]+)`') | ForEach-Object { $_.Groups[1].Value.Trim() }
$candidates = $rawSpans |
    Where-Object { $_ -notmatch '[/\\:]' } |
    Where-Object { $_ -match '^Mud\.HttpUtils' -or $_ -match '\(' } |
    Sort-Object -Unique

if (-not $candidates) {
    Write-Host "（顶部版本节未发现公共 API 形声明，跳过）"
    exit 0
}

$missing = @()
$unshippedOnly = @()
foreach ($c in $candidates) {
    $inBaseline = $false
    $inShipped = $false

    if ($c -match '^Mud\.HttpUtils') {
        # 限定名：主干（去泛型实参表）做基线包含判定
        $name = ($c -split '<')[0].Trim()
        if ($name.EndsWith(")")) { $name = ($name -split '\(')[0].Trim() }
        $inBaseline = $allText -match [regex]::Escape($name)
        $inShipped = $shippedText -match [regex]::Escape($name)
        if (-not $inBaseline) { $missing += $c; continue }
        if (-not $inShipped) { $unshippedOnly += $c }
        continue
    }

    # 调用签名形：取方法名主干（最后一段点分名），要求为合法标识符
    $head = ($c -split '\(')[0].Trim()
    $method = ($head -split '\.')[-1].Trim()
    if ($method -notmatch '^[A-Za-z_]\w*$') { continue }
    $methodPattern = "\." + [regex]::Escape($method) + "\("
    $inBaseline = $allText -match $methodPattern
    if (-not $inBaseline) {
        # 基线未登记：源码中存在（内部/分析器类型）则放过，否则判定为不存在
        if ($sourceAll -match ("\b" + [regex]::Escape($method) + "\b")) { continue }
        $missing += $c
        continue
    }
    $inShipped = $shippedText -match $methodPattern
    if (-not $inShipped) { $unshippedOnly += $c }
}

$exitCode = 0
if ($missing.Count -gt 0) {
    Write-Host "::error:: CHANGELOG 声明的以下 API 不存在于任何 PublicAPI 基线（笔误 / 改名 / 漏登记）："
    $missing | ForEach-Object { Write-Host "  - $_" }
    $exitCode = 1
}
if ($unshippedOnly.Count -gt 0) {
    Write-Host "::warning:: 以下声明的 API 仅在 Unshipped 基线（发布时须 rebuild-publicapi 并入 Shipped，否则脱节）："
    $unshippedOnly | ForEach-Object { Write-Host "  - $_" }
}
if ($exitCode -eq 0) {
    Write-Host "一致性校验通过（候选 $($candidates.Count) 条）。"
}
exit $exitCode
