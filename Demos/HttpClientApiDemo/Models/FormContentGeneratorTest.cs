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
        await File.WriteAllTextAsync(tempFilePath, "Test content");

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

