// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2026   
//  Mud.HttpUtils 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace HttpClientApiTest.Models;

/// <summary>
/// FormContent 生成器测试演示
/// </summary>
public static class FormContentGeneratorTest
{
    /// <summary>
    /// 运行所有测试
    /// </summary>
    public static async Task RunAllTestsAsync()
    {
        Console.WriteLine("=== FormContent 生成器测试 ===\n");

        await TestUploadFileRequestAsync();
        await TestNullableFieldsAsync();

        Console.WriteLine("\n=== 所有测试完成 ===");
    }

    /// <summary>
    /// 测试上传文件请求
    /// </summary>
    private static async Task TestUploadFileRequestAsync()
    {
        Console.WriteLine("测试 1: UploadAllFileRequest with file");

        var tempFilePath = Path.Combine(Path.GetTempPath(), "test_upload.txt");

#if NET6_0_OR_GREATER
        await File.WriteAllTextAsync(tempFilePath, "Test content");
#else
        File.WriteAllText(tempFilePath, "Test content");
#endif

        try
        {
            var request = new UploadAllFileRequest
            {
                FileName = "test.txt",
                ParentType = "folder",
                ParentNode = "root",
                Size = 100,
                Checksum = "abc123",
                FilePath = tempFilePath
            };

            var formData = await request.GetFormDataContentAsync();

            Console.WriteLine($"  ✓ 成功生成 FormData");
            Console.WriteLine($"  ✓ FormData 内容数量: {formData.Count()}");
            Console.WriteLine($"  ✓ 成功处理文件: {Path.GetFileName(tempFilePath)}");
            Console.WriteLine();
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    /// <summary>
    /// 测试可空字段
    /// </summary>
    private static async Task TestNullableFieldsAsync()
    {
        Console.WriteLine("测试 2: UploadAllFileRequest with nullable fields");

        var request = new UploadAllFileRequest
        {
            FileName = "test.txt",
            ParentType = "folder",
            ParentNode = "root",
            Size = 100,
            Checksum = null,
            FilePath = null
        };

        var formData = await request.GetFormDataContentAsync();

        Console.WriteLine($"  ✓ 成功生成 FormData");
        Console.WriteLine($"  ✓ FormData 内容数量: {formData.Count()}");
        Console.WriteLine($"  ✓ 正确跳过 null 字段");
        Console.WriteLine();
    }
}

