# -----------------------------------------------------------------------
#  Mud.HttpUtils 打包脚本
#  用法: .\pack.ps1 [配置] [版本号] [输出目录]
#  示例: .\pack.ps1 Release 2.0.8
#        .\pack.ps1 Debug   2.0.8 artifacts-debug   # Debug 包必须落到独立目录
# -----------------------------------------------------------------------

param(
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$OutputDir = "artifacts"
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
    "Mud.HttpUtils.Resilience",
    "Mud.HttpUtils.Xml"
)

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

    dotnet pack $csproj -c $Configuration -o $ArtifactsDir --no-restore

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
Write-Host "  打包后校验（配置=$Configuration）" -ForegroundColor Cyan

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

            $candidates = @(Get-ChildItem -Path $configBinRoot -Recurse -Filter $fileName -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '\\(obj|ref)\\' })
            if ($tfmSegment) {
                $tfmMatched = @($candidates | Where-Object { $_.Directory.Name -eq $tfmSegment })
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
            $matched = @($candidates | Where-Object {
                    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash -eq $entryHash
                })
            $candidateNames = ($candidates | ForEach-Object { $_.Directory.Name } | Select-Object -Unique) -join '/'
            $checkedDllCount++
            if ($matched.Count -eq 0) {
                $ValidationFailures += "$($pkg.Name) 的 $fileName 与 bin\$Configuration 下同名产物均不一致（[$candidateNames]）：疑似错版/陈旧打包"
                Write-Host "  [FAIL] $($pkg.Name) → $fileName 与 bin\$Configuration\[$candidateNames] 均不一致" -ForegroundColor Red
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}

if ($ValidationFailures.Count -eq 0) {
    Write-Host "  [ OK ] 包集合 $($producedPackages.Count) 个，DLL 与 bin\$Configuration 逐字节一致（校验 $checkedDllCount 个）" -ForegroundColor Green
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
