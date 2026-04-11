// -----------------------------------------------------------------------
//  作者：Mud Studio  版权所有 (c) Mud Studio 2025   
//  Mud.CodeGenerator 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
//  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
//  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
// -----------------------------------------------------------------------

using System.Reflection;

namespace Mud.HttpUtils.Tests;

/// <summary>
/// ExceptionUtils 异常工具类单元测试
/// </summary>
public class ExceptionUtilsTests
{
    private readonly Type _exceptionUtilsType;
    private readonly MethodInfo _throwIfNullGenericMethod;
    private readonly MethodInfo _throwIfNullMethod;
    private readonly MethodInfo _throwIfNullOrEmptyMethod;

    public ExceptionUtilsTests()
    {
        _exceptionUtilsType = typeof(HttpClientUtils).Assembly.GetType("Mud.HttpUtils.ExceptionUtils")!;
        _throwIfNullGenericMethod = _exceptionUtilsType.GetMethod("ThrowIfNull", new[] { typeof(object), typeof(string) })!;
        _throwIfNullMethod = _exceptionUtilsType.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .First(m => m.Name == "ThrowIfNull" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(object));
        _throwIfNullOrEmptyMethod = _exceptionUtilsType.GetMethod("ThrowIfNullOrEmpty", BindingFlags.Static | BindingFlags.Public)!;
    }

    #region ThrowIfNull Tests

    [Fact]
    public void ThrowIfNull_WithNullObject_ShouldThrowArgumentNullException()
    {
        object? obj = null;

        var act = () => _throwIfNullMethod.Invoke(null, new object?[] { obj, "testParam" });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>()
            .WithParameterName("testParam");
    }

    [Fact]
    public void ThrowIfNull_WithValidObject_ShouldNotThrow()
    {
        var obj = new object();

        var act = () => _throwIfNullMethod.Invoke(null, new object?[] { obj, "testParam" });

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfNull_WithNullParamName_ShouldThrowArgumentNullException()
    {
        object? obj = null;

        var act = () => _throwIfNullMethod.Invoke(null, new object?[] { obj, null });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void ThrowIfNull_WithDifferentTypes_ShouldWork()
    {
        var stringObj = "test";
        var intObj = 42;
        var listObj = new List<int> { 1, 2, 3 };

        var actString = () => _throwIfNullMethod.Invoke(null, new object?[] { stringObj, "stringParam" });
        var actInt = () => _throwIfNullMethod.Invoke(null, new object?[] { intObj, "intParam" });
        var actList = () => _throwIfNullMethod.Invoke(null, new object?[] { listObj, "listParam" });

        actString.Should().NotThrow();
        actInt.Should().NotThrow();
        actList.Should().NotThrow();
    }

    #endregion

    #region ThrowIfNullOrEmpty Tests

    [Fact]
    public void ThrowIfNullOrEmpty_WithNullString_ShouldThrowArgumentNullException()
    {
        string? str = null;

        var act = () => _throwIfNullOrEmptyMethod.Invoke(null, new object?[] { str, "testParam" });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>()
            .WithParameterName("testParam");
    }

    [Fact]
    public void ThrowIfNullOrEmpty_WithEmptyString_ShouldThrowArgumentNullException()
    {
        var str = string.Empty;

        var act = () => _throwIfNullOrEmptyMethod.Invoke(null, new object?[] { str, "testParam" });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>()
            .WithParameterName("testParam");
    }

    [Fact]
    public void ThrowIfNullOrEmpty_WithValidString_ShouldNotThrow()
    {
        var str = "test";

        var act = () => _throwIfNullOrEmptyMethod.Invoke(null, new object?[] { str, "testParam" });

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_WithWhitespaceString_ShouldNotThrow()
    {
        var str = "   ";

        var act = () => _throwIfNullOrEmptyMethod.Invoke(null, new object?[] { str, "testParam" });

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfNullOrEmpty_WithNullParamName_ShouldThrowArgumentNullException()
    {
        string? str = null;

        var act = () => _throwIfNullOrEmptyMethod.Invoke(null, new object?[] { str, null });

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    #endregion
}
