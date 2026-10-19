# -----------------------------------------------------------------------
#  Mud.HttpUtils 打包脚本
#  用法: .\pack.ps1 [配置] [版本号] [输出目录]
#  示例: .\pack.ps1 Release 2.0.5
#        .\pack.ps1 Debug   2.0.5 artifacts-debug   # Debug 包必须落到独立目录
# -----------------------------------------------------------------------

param(
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$OutputDir = "artifacts",
    # U-13：逃生开关 —— 允许同版本重打包（正常发布流程禁止，防 NuGet 全局缓存同版本键控导致的下游污染）
    [switch]$ForceSameVersion
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
    $propsContent = Get-Content $propsPath -Raw -Encoding utf8
    $propsContent = $propsContent -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>"
    Set-Content $propsPath $propsContent -NoNewline -Encoding utf8
    Write-Host "版本号已更新" -ForegroundColor Green
    Write-Host ""
}

# 要打包的项目列表（唯一真相源：pack_debug.ps1 只是本脚本的薄封装，禁止各自维护清单）
$Projects = @(
    "Mud.HttpUtils.Abstractions",
    "Mud.HttpUtils.Attributes",
    "Mud.HttpUtils.Client",
    "Mud.HttpUtils.Resilience",
    "Mud.HttpUtils",
    "Mud.HttpUtils.Generator",
    "Mud.HttpUtils.Newtonsoft.Json",
    "Mud.HttpUtils.Xml",
    "Mud.HttpUtils.Redis",
    "Tools/Mud.HttpUtils.JsonContextScaffolder",
    "Mud.HttpUtils.OpenTelemetry"
)

# 预期产出包（C2 修复：清单缺失会被下面的校验拦住，而不是静默少发一个包）
$ExpectedPackages = @(
    "Mud.HttpUtils",
    "Mud.HttpUtils.Abstractions",
    "Mud.HttpUtils.Attributes",
    "Mud.HttpUtils.Client",
    "Mud.HttpUtils.Generator",
    "Mud.HttpUtils.JsonContextScaffolder",
    "Mud.HttpUtils.Newtonsoft.Json",
    "Mud.HttpUtils.OpenTelemetry",
    "Mud.HttpUtils.Redis",
    "Mud.HttpUtils.Resilience",
    "Mud.HttpUtils.Xml"
)

# -------------------------------------------------------------------------
# U-13 版本递增强制校验（在清理 artifacts / 还原 / 打包之前执行）
#   根因：NuGet 全局缓存按 id + version 键控，同版本重打包**不失效**下游缓存 ——
#   2.0.4 期间三次同版本重打包每次污染全部下游（CS1750，需三步手工清缓存）。
#   本仓 artifacts 目录每次运行都会清空重建，不构成污染向量；真正的向量是 global-packages。
#   故在打包前解析目标版本（-Version 参数优先，否则读 Directory.Build.props），
#   对 $ExpectedPackages 逐包探测全局缓存：同版本已存在即拒绝打包（-ForceSameVersion 逃生）。
# -------------------------------------------------------------------------
function Get-NormalizedVersionForCache([string]$v) {
    # 对齐 NuGet 版本规范化：去构建元数据、去前导零、4 段版本末尾 .0 收敛为 3 段（与 pack_debug.ps1 同口径）
    $v = $v.Split('+')[0].ToLowerInvariant()
    $parts = $v -split '-'
    $numeric = @($parts[0] -split '\.' | ForEach-Object { [string][int]$_ })
    while ($numeric.Count -gt 3 -and $numeric[-1] -eq '0') {
        $numeric = $numeric[0..($numeric.Count - 2)]
    }
    $prerelease = if ($parts.Count -gt 1) { '-' + ($parts[1..($parts.Count - 1)] -join '-') } else { '' }
    return ($numeric -join '.') + $prerelease
}

$targetVersion = $Version
if ([string]::IsNullOrWhiteSpace($targetVersion)) {
    $propsRaw = [System.IO.File]::ReadAllText((Join-Path $RootDir "Directory.Build.props"))
    if ($propsRaw -match '<Version>([^<]+)</Version>') {
        $targetVersion = $Matches[1]
    }
}

if (-not [string]::IsNullOrWhiteSpace($targetVersion)) {
    # 版本纪律告警：目标版本不高于 CHANGELOG 已发布的最新版本时提示（仅警告，不阻断）
    $changelogPath = Join-Path $RootDir "CHANGELOG.md"
    if (Test-Path $changelogPath) {
        $changelogLines = [System.IO.File]::ReadAllLines($changelogPath)
        $latestPublished = $null
        foreach ($line in $changelogLines) {
            # 跳过标注「未发布」的在研版本节：其版本号不构成"已发布版本"基线，
            # 否则首次打包该版本会误报"不高于最新发布版本"的版本纪律告警。
            if ($line -match '^##\s+(\d+\.\d+\.\d+)' -and $line -notmatch '未发布') {
                $latestPublished = $Matches[1]
                break   # 版本段自新到旧排列，首个未标注「未发布」的数字版本号即最新发布版本
            }
        }
        if ($latestPublished -and ([version]$targetVersion -le [version]$latestPublished)) {
            Write-Host "!!! 版本纪律告警：目标版本 $targetVersion 不高于 CHANGELOG 最新发布版本 $latestPublished。" -ForegroundColor Yellow
            Write-Host "    同版本重打包不会使下游 NuGet 缓存失效，请确认这是有意为之（否则请递增版本号）。" -ForegroundColor Yellow
            Write-Host ""
        }
    }

    $normalizedTarget = Get-NormalizedVersionForCache $targetVersion

    # 解析全局缓存目录：NUGET_PACKAGES 环境变量优先（亦为测试注入点），其次 dotnet 配置，最后默认路径
    $GlobalPackages = $env:NUGET_PACKAGES
    if ([string]::IsNullOrWhiteSpace($GlobalPackages)) {
        $localsOutput = (& dotnet nuget locals global-packages --list) -join "`n"
        if ($LASTEXITCODE -eq 0 -and $localsOutput -match 'global-packages:\s*(.+)') {
            $GlobalPackages = $Matches[1].Trim()
        } else {
            $GlobalPackages = Join-Path $env:USERPROFILE ".nuget\packages"
        }
    }

    $cacheCollisions = @()
    foreach ($id in $ExpectedPackages) {
        $cacheDir = Join-Path $GlobalPackages ("{0}\{1}" -f $id.ToLowerInvariant(), $normalizedTarget)
        if (Test-Path $cacheDir) {
            $cacheCollisions += "$id $normalizedTarget"
        }
    }

    if ($cacheCollisions.Count -gt 0) {
        if (-not $ForceSameVersion) {
            Write-Host "========================================" -ForegroundColor Red
            Write-Host "  打包被拒绝（U-13 版本递增强制校验）：" -ForegroundColor Red
            Write-Host "  目标版本 $normalizedTarget 已存在于 NuGet 全局缓存：" -ForegroundColor Red
            foreach ($c in $cacheCollisions) {
                Write-Host "    - $c" -ForegroundColor Red
            }
            Write-Host ""
            Write-Host "  同版本重打包不会使下游缓存失效（NuGet 按 id+version 键控），将导致下游继续使用旧产物。" -ForegroundColor Yellow
            Write-Host "  处置三步指引：" -ForegroundColor Yellow
            Write-Host "    1) 递增 Directory.Build.props 中的版本号（推荐），或以 -Version 传入新版本；" -ForegroundColor Yellow
            Write-Host "    2) 如确需同版本覆盖：清缓存 dotnet nuget locals global-packages --clear（或逐包删除上述目录）；" -ForegroundColor Yellow
            Write-Host "    3) 确认必须同版本重打时，使用 -ForceSameVersion 逃生开关重跑本脚本。" -ForegroundColor Yellow
            exit 1
        }
        Write-Host "版本递增校验：目标版本 $normalizedTarget 已存在于全局缓存，-ForceSameVersion 已放行（后果自负）。" -ForegroundColor DarkYellow
        Write-Host ""
    } else {
        Write-Host "版本递增校验通过：全局缓存中不存在 $normalizedTarget 的同版本包。" -ForegroundColor DarkGray
        Write-Host ""
    }
}


if ($Configuration -ne "Release") {
    Write-Host "!!! 警告：以 $Configuration 配置打包。Debug 程序集未做优化，不得作为发布包（BC-1）！！！" -ForegroundColor Red
    Write-Host "!!!        请确保输出目录与发布目录分离（-OutputDir）。                                  ！！！" -ForegroundColor Red
    Write-Host ""
}

# 清理旧的包
$ArtifactsDir = Join-Path $RootDir $OutputDir
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

# -------------------------------------------------------------------------
# 打包前**显式构建一次**（下游反馈①根因修复）
#   原实现每个 `dotnet pack` 各自触发增量构建：排在被引用方（如 Client）之后的引用方工程
#   打包时若触发重建，会改写 bin\<Configuration> 中**已被打进包**的同名 DLL，
#   使「包内 DLL ↔ 校验时现场读取的 bin 产物」比对失配 —— 是否重建由增量 up-to-date 判定，
#   故为**非确定性误报**（下游连续两次复现）。
#   现改为：构建一次定型 → pack 全程 --no-build（打包期间不再改动 bin）→ 校验基准取构建后快照，
#   使「包内容 = 本次 <Configuration> 构建输出」这一命题与打包循环的执行顺序解耦。
#   原防线（BC-1「以 Debug 产物冒充 Release」/ 陈旧错版打包）不削弱：
#   非本次 <Configuration> 构建输出的 DLL 不会进入快照，仍会被校验拦下。
# -------------------------------------------------------------------------
Write-Host "构建 ($Configuration)..." -ForegroundColor Yellow
dotnet build $RootDir\Mud.HttpUtils.slnx -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "构建失败！" -ForegroundColor Red
    exit 1
}
Write-Host "构建完成" -ForegroundColor Green
Write-Host ""

# 逐项目打包
$FailedProjects = @()

foreach ($project in $Projects) {
    $projectDir = Join-Path $RootDir $project
    $projectName = Split-Path $project -Leaf
    $csproj = Join-Path $projectDir "$projectName.csproj"

    if (-not (Test-Path $csproj)) {
        Write-Host "跳过 (未找到项目文件): $project" -ForegroundColor DarkGray
        continue
    }

    Write-Host "打包: $project" -ForegroundColor Yellow

    # --no-build：打包期间不得再触发构建（否则会改写 bin 内已被打进包的产物，造成校验基准漂移）
    dotnet pack $csproj -c $Configuration -o $ArtifactsDir --no-restore --no-build

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  失败: $project" -ForegroundColor Red
        $FailedProjects += $project
    } else {
        Write-Host "  成功: $project" -ForegroundColor Green
    }
}

Write-Host ""

# -------------------------------------------------------------------------
# 打包后校验（C1/C2 修复）
#   ① 包集合必须与 $ExpectedPackages 一致 —— 防"打包清单漂移导致漏发包"；
#   ② 包内每个 DLL 必须与 bin/<Configuration>/<tfm>/ 下的同名产物**字节一致** ——
#      防"以 Debug 产物冒充 Release 发布"与"缓存陈旧导致的错版打包"。
# -------------------------------------------------------------------------
$ValidationFailures = @()
$producedPackages = @()
$nupkgs = @(Get-ChildItem $ArtifactsDir -Filter "*.nupkg" -File -ErrorAction SilentlyContinue)
$producedPackages = @($nupkgs | ForEach-Object { ($_.BaseName -replace '\.\d+\.\d+\.\d+(-[^.]+)?$', '') })

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  打包后校验（配置=$Configuration；基准=打包前构建产物快照，与打包循环时序解耦）" -ForegroundColor Cyan

foreach ($expected in $ExpectedPackages) {
    if ($producedPackages -notcontains $expected) {
        $ValidationFailures += "缺少预期包: $expected"
        Write-Host "  [FAIL] 缺少预期包: $expected" -ForegroundColor Red
    }
}
foreach ($produced in $producedPackages) {
    if ($ExpectedPackages -notcontains $produced) {
        $ValidationFailures += "产出未预期包: $produced"
        Write-Host "  [FAIL] 产出未预期包: $produced" -ForegroundColor Red
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$configBinRoot = Join-Path $RootDir "bin\$Configuration"
$checkedDllCount = 0

# 校验基准 = **打包前构建完成后的一次性快照**（而非打包循环内现场读取 bin）：
#   下游反馈①实证——循环内 `dotnet pack` 的增量重建会改写 bin 中已被打进包的产物，
#   现场读取必然产生非确定性误报。快照令校验与打包循环的时序/并行解耦。
$binSnapshot = @{}          # key: 文件名（小写） → 候选列表（Directory=TFM 目录名 / Hash / LastWrite）
if (Test-Path $configBinRoot) {
    $snapshotFiles = @(Get-ChildItem -Path $configBinRoot -Recurse -Filter "*.dll" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|ref)\\' })
    foreach ($snapshotFile in $snapshotFiles) {
        $key = $snapshotFile.Name.ToLowerInvariant()
        if (-not $binSnapshot.ContainsKey($key)) {
            $binSnapshot[$key] = New-Object System.Collections.ArrayList
        }
        [void]$binSnapshot[$key].Add([pscustomobject]@{
                Directory = $snapshotFile.Directory.Name
                Hash      = (Get-FileHash -LiteralPath $snapshotFile.FullName -Algorithm SHA256).Hash
                LastWrite = $snapshotFile.LastWriteTime
            })
    }
    Write-Host "  校验基准快照：bin\$Configuration 下 $($snapshotFiles.Count) 个 DLL（打包前构建产物）" -ForegroundColor DarkGray
} else {
    Write-Host "  [WARN] 未找到 bin\$Configuration（快照为空，后续比对将全部报缺）" -ForegroundColor Yellow
}

foreach ($pkg in $nupkgs) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($pkg.FullName)
    try {
        $dllEntries = $zip.Entries | Where-Object {
            $_.FullName -match '^(lib|analyzers|runtimes)/.*\.dll$'
        }
        foreach ($entry in $dllEntries) {
            $fileName = [System.IO.Path]::GetFileName($entry.FullName)
            # 包内 TFM 段（lib/net8.0/x.dll → net8.0；analyzers 无 TFM 段）
            $tfmSegment = if ($entry.FullName -match '^lib/([^/]+)/') { $Matches[1] } else { $null }

            $candidates = @()
            $snapshotKey = $fileName.ToLowerInvariant()
            if ($binSnapshot.ContainsKey($snapshotKey)) {
                $candidates = @($binSnapshot[$snapshotKey])
            }
            if ($tfmSegment) {
                $tfmMatched = @($candidates | Where-Object { $_.Directory -eq $tfmSegment })
                if ($tfmMatched.Count -gt 0) { $candidates = $tfmMatched }
            }

            if ($candidates.Count -eq 0) {
                $ValidationFailures += "$($pkg.Name) 中的 $fileName 在 bin\$Configuration 下不存在"
                Write-Host "  [FAIL] $($pkg.Name) → $fileName 不在 bin\$Configuration 产物中" -ForegroundColor Red
                continue
            }

            $stream = $entry.Open()
            $sha = [System.Security.Cryptography.SHA256]::Create()
            $entryHash = ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '')
            $sha.Dispose()
            $stream.Dispose()

            # 同一程序集可能被多个 TFM 的构建输出目录同时包含（例如 Generator 作为分析器被 CopyLocal 进
            # 各 TFM 的输出目录），故只要有**任一**候选与包内条目字节一致即通过 ——
            # 语义是「包内容确实来自本次 <Configuration> 的构建输出」，而不是「等于某一条特定路径」。
            $matched = @($candidates | Where-Object { $_.Hash -eq $entryHash })
            $candidateNames = ($candidates | ForEach-Object { $_.Directory } | Select-Object -Unique) -join '/'
            $checkedDllCount++
            if ($matched.Count -eq 0) {
                # 诊断增强（下游反馈①）：列出候选的 TFM/哈希前缀/写入时刻，便于区分
                # 「错版・陈旧打包」（哈希全不同且时刻早于本次构建）与「基准被外部构建污染」（时刻晚于本次构建）。
                $candidateDetail = ($candidates |
                    ForEach-Object { "$($_.Directory):$($_.Hash.Substring(0, 8))@$($_.LastWrite.ToString('HH:mm:ss'))" } |
                    Select-Object -Unique) -join ', '
                $ValidationFailures += "$($pkg.Name) 的 $fileName 与 bin\$Configuration 快照产物均不一致（包内 $($entryHash.Substring(0, 8))；候选 $candidateDetail）：疑似错版/陈旧打包或基准被外部构建污染"
                Write-Host "  [FAIL] $($pkg.Name) → $fileName 不一致（包内 $($entryHash.Substring(0, 8)) vs 快照 $candidateDetail）" -ForegroundColor Red
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}

if ($ValidationFailures.Count -eq 0) {
    Write-Host "  [ OK ] 包集合 $($producedPackages.Count) 个，DLL 与 bin\$Configuration 构建快照逐字节一致（校验 $checkedDllCount 个）" -ForegroundColor Green
}
Write-Host ""

Write-Host "========================================" -ForegroundColor Cyan

if ($FailedProjects.Count -gt 0 -or $ValidationFailures.Count -gt 0) {
    if ($FailedProjects.Count -gt 0) {
        Write-Host "  打包完成，以下项目失败:" -ForegroundColor Red
        foreach ($p in $FailedProjects) {
            Write-Host "    - $p" -ForegroundColor Red
        }
    }
    if ($ValidationFailures.Count -gt 0) {
        Write-Host "  打包后校验未通过，共 $($ValidationFailures.Count) 项:" -ForegroundColor Red
        foreach ($v in $ValidationFailures) {
            Write-Host "    - $v" -ForegroundColor Red
        }
    }
    exit 1
} else {
    Write-Host "  打包完成！共生成 $($nupkgs.Count) 个包（已通过配置与集合校验）" -ForegroundColor Green
    Write-Host "  输出目录: $ArtifactsDir" -ForegroundColor Cyan
}

Write-Host "========================================" -ForegroundColor Cyan
