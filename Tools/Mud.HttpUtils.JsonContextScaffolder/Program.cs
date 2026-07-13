// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Mud.HttpUtils.JsonContextScaffolder;

// 注册 MSBuild 实例（仅需一次）
if (!MSBuildLocator.IsRegistered)
{
    try
    {
        MSBuildLocator.RegisterDefaults();
    }
    catch (Exception ex)
    {
        // 部分环境（仅安装 .NET SDK、或未设置 DOTNET_ROOT）下 RegisterDefaults 无法定位 MSBuild。
        // 此时从 `dotnet --list-sdks` 的输出推导 SDK 目录并直接注册。
        var sdkDir = FindDotNetSdkDir();
        if (sdkDir == null)
            throw new InvalidOperationException("无法自动定位 MSBuild。请安装 .NET SDK 或通过 DOTNET_ROOT 指定其路径。", ex);

        MSBuildLocator.RegisterMSBuildPath(sdkDir);
    }
}

var projectPath = (string?)null;
var outputDir = (string?)null;
var dryRun = false;
var autoDerivedTypes = false;

// 解析命令行参数
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--project":
        case "-p":
            projectPath = args.ElementAtOrDefault(i + 1);
            i++;
            break;
        case "--output":
        case "-o":
            outputDir = args.ElementAtOrDefault(i + 1);
            i++;
            break;
        case "--dry-run":
            dryRun = true;
            break;
        case "--auto-derived-types":
            autoDerivedTypes = true;
            break;
        case "--help":
        case "-h":
            PrintHelp();
            return 0;
        default:
            if (projectPath == null && !args[i].StartsWith('-'))
                projectPath = args[i];
            break;
    }
}

if (string.IsNullOrEmpty(projectPath))
{
    System.Console.Error.WriteLine("错误：未指定项目文件路径。使用 --help 查看用法。");
    return 1;
}

projectPath = Path.GetFullPath(projectPath);
if (!File.Exists(projectPath))
{
    System.Console.Error.WriteLine($"错误：项目文件不存在：{projectPath}");
    return 1;
}

System.Console.WriteLine($"Mud.HttpUtils JSON Context Scaffolder");
System.Console.WriteLine($"  项目：{projectPath}");
System.Console.WriteLine($"  输出：{outputDir ?? "(项目目录/Generated)"}");
System.Console.WriteLine($"  Dry run：{dryRun}");
System.Console.WriteLine($"  Auto derived types：{autoDerivedTypes}");;
System.Console.WriteLine();

// 加载项目
var workspace = MSBuildWorkspace.Create();
workspace.WorkspaceFailed += (sender, e) =>
{
    System.Console.Error.WriteLine($"[Workspace] {e.Diagnostic.Kind}: {e.Diagnostic.Message}");
};

var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: CancellationToken.None);
var compilation = await project.GetCompilationAsync(CancellationToken.None);
if (compilation == null)
{
    System.Console.Error.WriteLine("错误：无法获取编译单元。请检查项目是否能正常构建。");
    return 1;
}

// 生成 Context 文件
var generator = new JsonContextGenerator();
var files = generator.Generate(compilation, autoDerivedTypes: autoDerivedTypes);

if (files.Count == 0)
{
    System.Console.WriteLine("未找到标注 [HttpJsonSerializable] 的类型，无需生成。");
    return 0;
}

// 确定输出目录
var effectiveOutputDir = outputDir ?? Path.Combine(Path.GetDirectoryName(projectPath)!, "Generated");
if (!dryRun && !Directory.Exists(effectiveOutputDir))
    Directory.CreateDirectory(effectiveOutputDir);

System.Console.WriteLine($"生成 {files.Count} 个 Context 文件：");
System.Console.WriteLine();

foreach (var file in files)
{
    var fullPath = Path.Combine(effectiveOutputDir, file.FileName);
    System.Console.WriteLine($"  {file.ContextClassName}");
    System.Console.WriteLine($"    文件：{fullPath}");
    System.Console.WriteLine($"    类型数：{file.TypeCount}");

    if (!dryRun)
    {
        await File.WriteAllTextAsync(fullPath, file.SourceCode, CancellationToken.None);
        System.Console.WriteLine("    状态：已写入 ✓");
    }
    else
    {
        System.Console.WriteLine("    状态：(dry-run 未写入)");
    }

    System.Console.WriteLine();
}

// 输出诊断信息（AOT001-AOT003）
if (generator.Diagnostics.Count > 0)
{
    System.Console.WriteLine();
    System.Console.WriteLine("诊断：");
    foreach (var diag in generator.Diagnostics)
    {
        var severity = diag.Severity switch
        {
            ScaffolderDiagnosticSeverity.Error => "错误",
            ScaffolderDiagnosticSeverity.Warning => "警告",
            _ => "信息"
        };
        var stream = diag.Severity == ScaffolderDiagnosticSeverity.Error
            ? System.Console.Error
            : System.Console.Out;
        stream.WriteLine($"  [{diag.Id}] {severity}: {diag.Message}");
        if (!string.IsNullOrEmpty(diag.Location))
            stream.WriteLine($"    位置: {diag.Location}");
    }
    System.Console.WriteLine();
}

System.Console.WriteLine($"完成！{(dryRun ? "(dry-run)" : $"{files.Count} 个文件已生成到 {effectiveOutputDir}")}");
System.Console.WriteLine();
System.Console.WriteLine("提示：将生成的文件加入版本控制（git add），仅在新增/变更 [HttpJsonSerializable] 标注时重跑。");
return 0;

static void PrintHelp()
{
    System.Console.WriteLine("""
    Mud.HttpUtils JSON Context Scaffolder

    用法：
      mud-jsonctx --project <项目路径> [选项]

    选项：
      -p, --project <路径>   要扫描的 .csproj 文件路径
      -o, --output <目录>    输出目录（默认：<项目目录>/Generated）
      --dry-run              仅预览，不写入文件
      --auto-derived-types   自动检测同程序集内派生类，生成额外的 [JsonSerializable]
      -h, --help             显示帮助

    示例：
      mud-jsonctx --project src/MyApp.DataModels/MyApp.DataModels.csproj
      mud-jsonctx -p src/MyApp.DataModels/MyApp.DataModels.csproj -o src/MyApp.DataModels/Generated
      mud-jsonctx -p src/MyApp.DataModels/MyApp.DataModels.csproj --dry-run

    说明：
      扫描项目中标注 [HttpJsonSerializable] 的类型，按 SerializerClassName 分组，
      为每组生成一个 JsonSerializerContext 源文件（#if NET8_0_OR_GREATER 包裹）。
      生成的文件应提交到版本控制，仅在实体变更时重跑。
    """);
}

// 推导包含 Microsoft.Build.dll 的 .NET SDK 目录，用于 RegisterDefaults 失败时的兜底注册
// （仅安装 .NET SDK、未设置 DOTNET_ROOT 等场景）。
static string? FindDotNetSdkDir()
{
    // 1) DOTNET_ROOT 环境变量指向的 SDK 目录
    var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
    if (!string.IsNullOrEmpty(dotnetRoot))
    {
        var fromRoot = SearchSdkDirs(Path.Combine(dotnetRoot!, "sdk"));
        if (fromRoot != null)
            return fromRoot;
    }

    // 2) 标准安装位置（Windows）：%ProgramFiles%\dotnet\sdk、%ProgramFiles(x86)%\dotnet\sdk
    foreach (var baseDir in new[]
             {
                 Environment.GetEnvironmentVariable("ProgramFiles"),
                 Environment.GetEnvironmentVariable("ProgramFiles(x86)")
             }.Where(d => !string.IsNullOrEmpty(d)))
    {
        var fromProgramFiles = SearchSdkDirs(Path.Combine(baseDir!, "dotnet", "sdk"));
        if (fromProgramFiles != null)
            return fromProgramFiles;
    }

    // 3) 兜底：通过 dotnet --list-sdks 解析 [<path>]
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "--list-sdks",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc != null)
        {
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            // 输出示例：10.0.301 [C:\Program Files\dotnet\sdk\10.0.301]
            foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var start = line.IndexOf('[');
                var end = line.IndexOf(']', start);
                if (start >= 0 && end > start)
                {
                    var path = line.Substring(start + 1, end - start - 1).Trim();
                    if (Directory.Exists(path) && File.Exists(Path.Combine(path, "Microsoft.Build.dll")))
                        return path;
                }
            }
        }
    }
    catch
    {
        // 忽略异常，返回 null 交由上层处理
    }

    return null;
}

// 在 SDK 根目录下查找含 Microsoft.Build.dll 的版本目录（取版本最高者）。
static string? SearchSdkDirs(string sdkRoot)
{
    if (!Directory.Exists(sdkRoot))
        return null;

    return Directory.EnumerateDirectories(sdkRoot)
        .Where(d => File.Exists(Path.Combine(d, "Microsoft.Build.dll")))
        .OrderByDescending(d => d)
        .FirstOrDefault();
}
