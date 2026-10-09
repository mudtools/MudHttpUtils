# -----------------------------------------------------------------------
#  F7：依赖版本一致性校验（CI 作业 dependency-consistency 的唯一入口）
#
#  目的：把"同一 TFM 组内同一包出现多个版本"这类漂移在 CI 阶段拦下
#        （既有实例：某测试工程单点使用 9.0.0，而其余工程同为 10.0.9 —— 全仓唯一 9.x 离群项）。
#
#  规则（按"包名 × TFM 组"聚合，组内版本必须唯一）：
#    · TFM 组由 PackageReference 所在 ItemGroup 的 Condition 推导：
#        Condition 含 net8.0 / net10.0  → modern
#        Condition 含 net6.0 / netstandard2.0 → legacy
#        无 Condition                   → any
#    · 同一 (包名, 组) 出现 >1 个不同版本 ⇒ 失败并列出全部出现位置。
#
#  退出码：0 = 一致；1 = 发现漂移。
#
#  说明：本脚本刻意只做"静态声明一致性"校验（不 restore、不联网），
#        故可在任意 CI 阶段与本地快速执行。传递依赖的版本一致性由 restore 阶段
#        （NU1605 / 包降级告警）与本作业分工覆盖。
# -----------------------------------------------------------------------

[CmdletBinding()]
param(
    [string]$RepoRoot
)

$ErrorActionPreference = 'Stop'

# 注意：不能在 param() 默认值里用 $PSScriptRoot —— Windows PowerShell 5.1 在参数绑定期该变量为空。
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..'
}

$root = (Resolve-Path -LiteralPath $RepoRoot).Path

# 待扫描的项目/属性文件（排除 bin / obj 产物目录）。
# 注意：-Include 与 -LiteralPath 组合会失效（PowerShell 已知行为），故按扩展名显式过滤。
$files = Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and
        ($_.Extension -eq '.csproj' -or $_.Extension -eq '.props')
    }

$entries = New-Object System.Collections.Generic.List[object]

foreach ($file in $files) {
    try {
        [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    }
    catch {
        Write-Warning ("跳过无法解析的文件：{0}（{1}）" -f $file.FullName, $_.Exception.Message)
        continue
    }

    # 依次遍历 ItemGroup，把每个 PackageReference 归入其所属 Condition 推导出的 TFM 组。
    foreach ($itemGroup in $xml.SelectNodes('//ItemGroup')) {
        $condition = [string]$itemGroup.GetAttribute('Condition')

        $group = 'any'
        if ($condition -match "!=\s*'(net[0-9]|netstandard|netcoreapp)") {
            # 形如 '$(TargetFramework)' != 'net6.0' 的**排除式**条件：其覆盖集合无法静态判定，
            # 单独成组（不参与与其他组的一致性比较），避免把"非 net6 的那一支"误判为 legacy。
            $group = 'exclusion'
        }
        elseif ($condition -match 'net8\.0|net10\.0') { $group = 'modern' }
        elseif ($condition -match 'net6\.0|netstandard2\.0') { $group = 'legacy' }

        foreach ($reference in $itemGroup.SelectNodes('PackageReference')) {
            $include = [string]$reference.GetAttribute('Include')
            if ([string]::IsNullOrWhiteSpace($include)) { continue }
            # 版本由 Directory.Packages.props 集中提供的引用（CPM）不含版本，跳过。
            $version = [string]$reference.GetAttribute('Version')
            if ([string]::IsNullOrWhiteSpace($version)) {
                $versionNode = $reference.SelectSingleNode('Version')
                if ($null -ne $versionNode) { $version = $versionNode.InnerText.Trim() }
            }
            if ([string]::IsNullOrWhiteSpace($version)) { continue }
            # MSBuild 变量版本（如 $(Version)）非字面量，不参与一致性判定。
            if ($version.Contains('$')) { continue }
            # VersionOverride 表示"刻意偏离集中版本"，不参与一致性判定。
            if (-not [string]::IsNullOrWhiteSpace([string]$reference.GetAttribute('VersionOverride'))) { continue }

            $relative = $file.FullName.Substring($root.Length).TrimStart('\', '/')

            $entries.Add([pscustomobject]@{
                    Package = $include.Trim()
                    Version = $version.Trim()
                    Group   = $group
                    File    = $relative
                })
        }
    }
}

$drift = $entries |
    Group-Object Package, Group |
    Where-Object { ($_.Group | Select-Object -ExpandProperty Version -Unique).Count -gt 1 }

if ($drift) {
    Write-Host ''
    Write-Host '依赖版本一致性问题（同一 TFM 组内同一包出现多个版本）：' -ForegroundColor Red
    foreach ($group in $drift) {
        Write-Host ("  {0}" -f $group.Name) -ForegroundColor Yellow
        foreach ($entry in $group.Group) {
            Write-Host ("      {0}  ← {1}" -f $entry.Version, $entry.File)
        }
    }
    Write-Host ''
    Write-Host '修复建议：统一为同一 TFM 组内的单一版本；确需偏离请显式使用 VersionOverride 并注明理由。' -ForegroundColor Yellow
    exit 1
}

Write-Host ("依赖版本一致性校验通过（扫描 {0} 个文件、{1} 条包引用）。" -f $files.Count, $entries.Count) -ForegroundColor Green
exit 0
