# -----------------------------------------------------------------------
#  Mud.HttpUtils Debug 打包脚本（仅供本地调试/排障使用）
#
#  ⚠️ 本脚本只是 pack.ps1 的**薄封装**：
#     ① 输出到独立的 artifacts-debug 目录 —— 不得覆盖发布目录 artifacts
#        （曾因本脚本直接把 Debug 产物写进 artifacts 而把未优化程序集当作发布包）；
#     ② 项目清单不再各自维护 —— 本脚本旧清单曾漏了 Mud.HttpUtils.Xml，
#        导致 artifacts 静默少发一个包（后由 pack.ps1 的包集合校验兜住）。
#
#  用法: .\pack_debug.ps1 [版本号]
# -----------------------------------------------------------------------

param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "!!! Debug 打包：产物仅供本地调试，请勿发布到 NuGet !!!" -ForegroundColor Red
Write-Host ""

$forward = @{ Configuration = "Debug"; OutputDir = "artifacts-debug" }
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $forward.Version = $Version
}

& (Join-Path $RootDir "pack.ps1") @forward
exit $LASTEXITCODE
