// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

namespace Mud.HttpUtils.Tests;

/// <summary>
/// HttpClientUtils 工具类单元测试
/// </summary>
public class HttpClientUtilsTests : IDisposable
{
    private readonly string _testDirectory;

    public HttpClientUtilsTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MudHttpUtilsTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    #region GetByteArrayContentAsync Tests

    [Fact]
    public async Task GetByteArrayContentAsync_WithValidFile_ShouldReturnByteArrayContent()
    {
        var testFile = Path.Combine(_testDirectory, "test.txt");
        var testContent = "Hello, World!";
        await File.WriteAllTextAsync(testFile, testContent);

        var result = await HttpClientUtils.GetByteArrayContentAsync(testFile);

        result.Should().NotBeNull();
        result.Headers.ContentType.Should().NotBeNull();
        result.Headers.ContentType!.MediaType.Should().Be("text/plain");
    }

    [Fact]
    public async Task GetByteArrayContentAsync_WithNullFilePath_ShouldThrowArgumentNullException()
    {
        var act = async () => await HttpClientUtils.GetByteArrayContentAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("filePath");
    }

    [Fact]
    public async Task GetByteArrayContentAsync_WithEmptyFilePath_ShouldThrowArgumentNullException()
    {
        var act = async () => await HttpClientUtils.GetByteArrayContentAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("filePath");
    }

    [Fact]
    public async Task GetByteArrayContentAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
    {
        var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.txt");

        var act = async () => await HttpClientUtils.GetByteArrayContentAsync(nonExistentFile);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage($"文件未找到: {nonExistentFile}*");
    }

    [Theory]
    [InlineData("test.jpg", "image/jpeg")]
    [InlineData("test.png", "image/png")]
    [InlineData("test.pdf", "application/pdf")]
    [InlineData("test.json", "application/json")]
    [InlineData("test.xml", "application/xml")]
    [InlineData("test.unknown", "application/octet-stream")]
    public async Task GetByteArrayContentAsync_WithDifferentFileTypes_ShouldSetCorrectContentType(string fileName, string expectedContentType)
    {
        var testFile = Path.Combine(_testDirectory, fileName);
        await File.WriteAllBytesAsync(testFile, new byte[] { 1, 2, 3, 4, 5 });

        var result = await HttpClientUtils.GetByteArrayContentAsync(testFile);

        result.Headers.ContentType!.MediaType.Should().Be(expectedContentType);
    }

    #endregion

    #region CreateFileContent Tests

    [Fact]
    public void CreateFileContent_WithValidParameters_ShouldReturnByteArrayContent()
    {
        var fileName = "test.txt";
        var fileBytes = Encoding.UTF8.GetBytes("Hello, World!");

        var result = HttpClientUtils.CreateFileContent(fileName, fileBytes);

        result.Should().NotBeNull();
        result.Headers.ContentType.Should().NotBeNull();
    }

    [Fact]
    public void CreateFileContent_WithNullFileName_ShouldThrowArgumentNullException()
    {
        var fileBytes = Encoding.UTF8.GetBytes("Hello, World!");

        var act = () => HttpClientUtils.CreateFileContent(null!, fileBytes);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("fileName");
    }

    [Fact]
    public void CreateFileContent_WithEmptyFileName_ShouldThrowArgumentNullException()
    {
        var fileBytes = Encoding.UTF8.GetBytes("Hello, World!");

        var act = () => HttpClientUtils.CreateFileContent(string.Empty, fileBytes);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("fileName");
    }

    [Fact]
    public void CreateFileContent_WithWhitespaceFileName_ShouldThrowArgumentNullException()
    {
        var fileBytes = Encoding.UTF8.GetBytes("Hello, World!");

        var act = () => HttpClientUtils.CreateFileContent("   ", fileBytes);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("fileName");
    }

    [Fact]
    public void CreateFileContent_WithNullFileBytes_ShouldThrowArgumentNullException()
    {
        var fileName = "test.txt";

        var act = () => HttpClientUtils.CreateFileContent(fileName, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("fileBytes");
    }

    [Fact]
    public void CreateFileContent_WithEmptyFileBytes_ShouldThrowArgumentNullException()
    {
        var fileName = "test.txt";
        var emptyBytes = Array.Empty<byte>();

        var act = () => HttpClientUtils.CreateFileContent(fileName, emptyBytes);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("fileBytes");
    }

    [Theory]
    [InlineData("test.jpg", "image/jpeg")]
    [InlineData("test.jpeg", "image/jpeg")]
    [InlineData("test.png", "image/png")]
    [InlineData("test.gif", "image/gif")]
    [InlineData("test.bmp", "image/bmp")]
    [InlineData("test.webp", "image/webp")]
    [InlineData("test.pdf", "application/pdf")]
    [InlineData("test.doc", "application/msword")]
    [InlineData("test.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("test.xls", "application/vnd.ms-excel")]
    [InlineData("test.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("test.json", "application/json")]
    [InlineData("test.xml", "application/xml")]
    [InlineData("test.zip", "application/zip")]
    [InlineData("test.mp3", "audio/mpeg")]
    [InlineData("test.mp4", "video/mp4")]
    [InlineData("test.unknown", "application/octet-stream")]
    public void CreateFileContent_WithDifferentExtensions_ShouldSetCorrectContentType(string fileName, string expectedContentType)
    {
        var fileBytes = new byte[] { 1, 2, 3, 4, 5 };

        var result = HttpClientUtils.CreateFileContent(fileName, fileBytes);

        result.Headers.ContentType!.MediaType.Should().Be(expectedContentType);
    }

    #endregion
}
