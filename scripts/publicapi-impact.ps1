#Requires -Version 5.1
<#
.SYNOPSIS
生成「上游变更 → 下游影响清单」草稿（演进治理 D-1）。

.DESCRIPTION
对比基线 git 引用与当前工作树，输出发布说明草稿供人工裁定：
  ① 各项目 PublicAPI.Unshipped.txt 条目（自上一版发布以来累积的候选新增 API，
     跨 TFM 去重；发布时应由 Tools/rebuild-publicapi.ps1 并入 Shipped）；
  ② 诊断描述符差集（Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs 的 id / title / severity 增改）；
  ③ 按 PR 模板「下游影响」四类（公共 API / 行为变更 / 默认开启诊断 / 传递依赖）组织的裁定骨架。
本脚本只做机械汇总，行为变更与下游自建件替代映射须人工填写（草稿含占位）。

.PARAMETER BaseRef
基线 git 引用。默认 HEAD（对比工作树未提交变更）；传上一版 tag（如 v3.0.8）可对比发布版。

.EXAMPLE
pwsh ./scripts/publicapi-impact.ps1
.EXAMPLE
pwsh ./scripts/publicapi-impact.ps1 -BaseRef v3.0.8
#>
param([string]$BaseRef = "HEAD")

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot/..").Path
$diagnosticsRelPath = "Mud.HttpUtils.Generator/Diagnostics/Diagnostics.cs"

function Get-UnshippedApiEntries {
    $files = Get-ChildItem -Path $repoRoot -Recurse -Filter "PublicAPI.Unshipped.txt" -File |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
    $lines = foreach ($f in $files) {
        $rel = $f.FullName.Substring($repoRoot.Length + 1)
        $project = ($rel -split '\\')[0]
        foreach ($line in (Get-Content -LiteralPath $f.FullName)) {
            if ($line -match '\S' -and $line -notmatch '^#') {
                "  - [{0}] {1}" -f $project, $line.Trim()
            }
        }
    }
    return @($lines | Sort-Object -Unique)
}

function Get-DiagnosticDescriptors {
    param([string]$Content)
    $map = @{}
    foreach ($chunk in ($Content -split 'DiagnosticDescriptor\s+\w+\s*=\s*new\(')) {
        if ($chunk -notmatch 'id:\s*([^,\r\n]+)') { continue }
        $id = $Matches[1].Trim().Trim('"')
        $title = if ($chunk -match 'title:\s*"([^"]+)"') { $Matches[1] } else { "" }
        $severity = if ($chunk -match 'DiagnosticSeverity\.(\w+)') { $Matches[1] } else { "" }
        $map[$id] = ("title={0}; severity={1}" -f $title, $severity)
    }
    return $map
}

# ---- ① PublicAPI.Unshipped 条目 ----
$apiEntries = Get-UnshippedApiEntries

# ---- ② 诊断描述符差集 ----
$oldContent = $null
try {
    $oldContent = & git -C $repoRoot show ("{0}:{1}" -f $BaseRef, $diagnosticsRelPath) 2>$null
}
catch {
    Write-Warning "无法读取基线 $BaseRef 的诊断文件（可能该路径在基线不存在）——诊断差集按「全部视为新增」处理。"
}

$newMap = Get-DiagnosticDescriptors (Get-Content -LiteralPath (Join-Path $repoRoot $diagnosticsRelPath) -Raw)
$oldMap = if ($oldContent) { Get-DiagnosticDescriptors ($oldContent -join "`n") } else { @{} }

$added = @($newMap.Keys | Where-Object { -not $oldMap.ContainsKey($_) } | Sort-Object)
$removed = @($oldMap.Keys | Where-Object { -not $newMap.ContainsKey($_) } | Sort-Object)
$changed = @($newMap.Keys | Where-Object { $oldMap.ContainsKey($_) -and $oldMap[$_] -ne $newMap[$_] } | Sort-Object)

# ---- ③ 输出草稿 ----
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("# 上游变更 → 下游影响清单（草稿，待人工裁定）")
[void]$sb.AppendLine()
[void]$sb.AppendLine("- 基线：``$BaseRef``（工作树对比）")
[void]$sb.AppendLine("- 生成时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$sb.AppendLine()
[void]$sb.AppendLine("## 一、公共 API 变化（PublicAPI.Unshipped.txt，跨 TFM 去重）")
[void]$sb.AppendLine()
if ($apiEntries.Count -gt 0) {
    [void]$sb.AppendLine("以下条目登记于未发布基线；发布时须由 ``Tools/rebuild-publicapi.ps1`` 并入 Shipped（D-2 裁决）：")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine(($apiEntries -join "`n"))
}
else {
    [void]$sb.AppendLine("（无未登记 API 条目）")
}
[void]$sb.AppendLine()
[void]$sb.AppendLine("## 二、诊断描述符差集")
[void]$sb.AppendLine()
if ($added.Count -gt 0) {
    [void]$sb.AppendLine("### 新增诊断（默认开启与否见 isEnabledByDefault；下游或需豁免动作）")
    foreach ($id in $added) { [void]$sb.AppendLine(("- ``{0}`` — {1}" -f $id, $newMap[$id])) }
    [void]$sb.AppendLine()
}
if ($changed.Count -gt 0) {
    [void]$sb.AppendLine("### 变更诊断（消息/级别/标题变化即影响下游屏蔽与 golden）")
    foreach ($id in $changed) {
        [void]$sb.AppendLine(("- ``{0}``：{1} ⇒ {2}" -f $id, $oldMap[$id], $newMap[$id]))
    }
    [void]$sb.AppendLine()
}
if ($removed.Count -gt 0) {
    [void]$sb.AppendLine("### 移除诊断（下游 NoWarn / #pragma 可能失效）")
    foreach ($id in $removed) { [void]$sb.AppendLine(("- ``{0}`` — {1}" -f $id, $oldMap[$id])) }
    [void]$sb.AppendLine()
}
if ($added.Count -eq 0 -and $changed.Count -eq 0 -and $removed.Count -eq 0) {
    [void]$sb.AppendLine("（无诊断描述符增改）")
    [void]$sb.AppendLine()
}
[void]$sb.AppendLine("## 三、发布说明裁定骨架（人工填写后填入 CHANGELOG）")
[void]$sb.AppendLine()
[void]$sb.AppendLine("### 破坏性变更")
[void]$sb.AppendLine("<!-- 行为变更逐条列：旧行为 → 新行为 → 下游适配动作；无写「无」 -->")
[void]$sb.AppendLine()
[void]$sb.AppendLine("### 升级注意")
[void]$sb.AppendLine("<!-- 默认开启的新诊断、传递依赖断裂类型；无写「无」 -->")
[void]$sb.AppendLine()
[void]$sb.AppendLine("### 下游自建件替代映射（D-3）")
[void]$sb.AppendLine("<!-- 上游能力 → 可裁撤/可回迁的下游件 → 验证方式；无写「无」 -->")
[void]$sb.AppendLine()

Write-Output $sb.ToString()
