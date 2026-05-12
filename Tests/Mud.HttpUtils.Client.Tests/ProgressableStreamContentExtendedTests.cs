using System.Net;

namespace Mud.HttpUtils.Client.Tests;

public class ProgressableStreamContentExtendedTests
{
    [Fact]
    public async Task SerializeToStreamAsync_LargeFile_ReportsIncrementalProgress()
    {
        var data = new byte[10000];
        Random.Shared.NextBytes(data);
        var originalContent = new ByteArrayContent(data);

        var progressReports = new List<long>();
        var progress = new Progress<long>(bytes => progressReports.Add(bytes));

        var progressable = new ProgressableStreamContent(originalContent, progress, bufferSize: 1024);

        using var stream = new MemoryStream();
        await progressable.CopyToAsync(stream);

        progressReports.Should().HaveCountGreaterOrEqualTo(2, "大文件应报告多次进度");
        progressReports.Should().BeInAscendingOrder("进度值应单调递增");
        progressReports.Last().Should().Be(10000);
    }

    [Fact]
    public async Task SerializeToStreamAsync_SmallBufferSize_ReportsMoreFrequently()
    {
        var data = new byte[100];
        var originalContent = new ByteArrayContent(data);

        var progressReports = new List<long>();
        var progress = new Progress<long>(bytes => progressReports.Add(bytes));

        var progressable = new ProgressableStreamContent(originalContent, progress, bufferSize: 10);

        using var stream = new MemoryStream();
        await progressable.CopyToAsync(stream);

        progressReports.Should().HaveCount(10, "每10字节报告一次进度，100字节应报告10次");
    }

    [Fact]
    public async Task SerializeToStreamAsync_DataIntegrity_PreservesContent()
    {
        var originalData = new byte[500];
        for (int i = 0; i < originalData.Length; i++) originalData[i] = (byte)(i % 256);
        var originalContent = new ByteArrayContent(originalData);

        var progressable = new ProgressableStreamContent(originalContent, null);

        using var stream = new MemoryStream();
        await progressable.CopyToAsync(stream);

        var result = stream.ToArray();
        result.Should().Equal(originalData, "传输后数据应完整一致");
    }

    [Fact]
    public async Task SerializeToStreamAsync_StreamContent_ReportsProgress()
    {
        var data = new byte[200];
        Random.Shared.NextBytes(data);
        var stream = new MemoryStream(data);
        var originalContent = new StreamContent(stream);

        var progressReports = new List<long>();
        var progress = new Progress<long>(bytes => progressReports.Add(bytes));

        var progressable = new ProgressableStreamContent(originalContent, progress, bufferSize: 50);

        using var outputStream = new MemoryStream();
        await progressable.CopyToAsync(outputStream);

        progressReports.Should().NotBeEmpty();
        progressReports.Last().Should().Be(200);
    }

    [Fact]
    public void ContentLength_WithKnownContentLength_ReturnsCorrectValue()
    {
        var data = new byte[100];
        var originalContent = new ByteArrayContent(data);
        var progressable = new ProgressableStreamContent(originalContent, null);

        progressable.Headers.ContentLength.Should().Be(100);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var originalContent = new StringContent("test");
        var progressable = new ProgressableStreamContent(originalContent, null);

        progressable.Dispose();
        progressable.Dispose();
    }

    [Fact]
    public async Task SerializeToStreamAsync_WithCustomBufferSize_ReportsProgress()
    {
        var data = new byte[256];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)i;
        var originalContent = new ByteArrayContent(data);

        var progressReports = new List<long>();
        var progress = new Progress<long>(bytes => progressReports.Add(bytes));

        var progressable = new ProgressableStreamContent(originalContent, progress, bufferSize: 64);

        using var stream = new MemoryStream();
        await progressable.CopyToAsync(stream);

        progressReports.Should().NotBeEmpty("应报告进度");
        progressReports.Last().Should().Be(256, "最终进度应等于总字节数");
        progressReports.Should().BeInAscendingOrder("进度值应单调递增");
    }
}
