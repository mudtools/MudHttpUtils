using System.Net;
using FluentAssertions;

namespace Mud.HttpUtils.Client.Tests;

public class ProgressableStreamContentTests
{
    [Fact]
    public void Constructor_NullContent_Throws()
    {
        var act = () => new ProgressableStreamContent(null!, null);

        act.Should().Throw<ArgumentNullException>().WithParameterName("content");
    }

    [Fact]
    public void Constructor_ValidContent_CopiesHeaders()
    {
        var originalContent = new StringContent("test");
        originalContent.Headers.Add("X-Custom", "value");

        var progressable = new ProgressableStreamContent(originalContent, null);

        progressable.Headers.Contains("X-Custom").Should().BeTrue();
    }

    [Fact]
    public async Task SerializeToStreamAsync_ReportsProgress()
    {
        var data = new byte[100];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 256);
        var originalContent = new ByteArrayContent(data);

        var progressReports = new List<long>();
        var progress = new Progress<long>(bytes => progressReports.Add(bytes));

        var progressable = new ProgressableStreamContent(originalContent, progress, bufferSize: 30);

        using var stream = new MemoryStream();
        await progressable.CopyToAsync(stream);

        progressReports.Should().NotBeEmpty();
        progressReports.Last().Should().Be(100);
    }

    [Fact]
    public async Task SerializeToStreamAsync_NoProgress_CompletesSuccessfully()
    {
        var originalContent = new StringContent("hello world");
        var progressable = new ProgressableStreamContent(originalContent, null);

        using var stream = new MemoryStream();
        await progressable.CopyToAsync(stream);

        stream.ToArray().Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SerializeToStreamAsync_EmptyContent_NoProgressReports()
    {
        var originalContent = new ByteArrayContent(Array.Empty<byte>());
        var progressReports = new List<long>();
        var progress = new Progress<long>(bytes => progressReports.Add(bytes));

        var progressable = new ProgressableStreamContent(originalContent, progress);

        using var stream = new MemoryStream();
        await progressable.CopyToAsync(stream);

        progressReports.Should().BeEmpty();
    }
}
