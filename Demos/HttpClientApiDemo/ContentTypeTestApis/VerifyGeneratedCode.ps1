# XML内容类型生成代码验证脚本
# 此脚本用于检查生成的代码是否符合预期

Write-Host "=== XML内容类型生成代码验证 ===" -ForegroundColor Cyan
Write-Host ""

# 定义生成的代码文件路径
$generatedCodePath = ".\obj\Debug\net9.0\generated\Mud.HttpUtils.Generator\Mud.HttpUtils.HttpInvokeClassSourceGenerator\ContentTypeEdgeCaseApi.g.cs"

if (-not (Test-Path $generatedCodePath))
{
    $generatedCodePath = ".\obj\Debug\net10.0\generated\Mud.HttpUtils.Generator\Mud.HttpUtils.HttpInvokeClassSourceGenerator\ContentTypeEdgeCaseApi.g.cs"
}

if (-not (Test-Path $generatedCodePath))
{
    Write-Host "错误：找不到生成的代码文件！" -ForegroundColor Red
    Write-Host "请先构建项目：dotnet build /p:EmitCompilerGeneratedFiles=true" -ForegroundColor Yellow
    exit 1
}

Write-Host "找到生成的代码文件：$generatedCodePath" -ForegroundColor Green
Write-Host ""

# 读取生成的代码
$generatedCode = Get-Content $generatedCodePath -Raw

Write-Host "=== 验证 TestThreeLevelsAsync 方法 ===" -ForegroundColor Cyan

# 检查是否使用XML序列化
if ($generatedCode -match 'XmlSerialize\.Serialize\(data\)')
{
    Write-Host "✓ 请求序列化：正确使用 XmlSerialize.Serialize(data)" -ForegroundColor Green
}
else
{
    Write-Host "✗ 请求序列化：未使用 XmlSerialize.Serialize(data)" -ForegroundColor Red
}

# 检查是否使用SendXmlAsync（这个方法应该使用XML，因为Body参数指定了application/xml）
$testThreeLevelsMethod = $generatedCode -split 'TestThreeLevelsAsync' | Select-Object -Index 1
if ($testThreeLevelsMethod -match 'SendXmlAsync')
{
    Write-Host "✓ 响应反序列化：正确使用 SendXmlAsync<TestResponse>" -ForegroundColor Green
}
elseif ($testThreeLevelsMethod -match 'SendAsync')
{
    Write-Host "✗ 响应反序列化：错误使用 SendAsync，应该使用 SendXmlAsync" -ForegroundColor Red
    Write-Host "  原因：Body参数指定了ContentType = 'application/xml'" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=== 验证 TestFullContentTypeAsync 方法 ===" -ForegroundColor Cyan

# 检查是否使用XML序列化
$testFullMethod = $generatedCode -split 'TestFullContentTypeAsync' | Select-Object -Index 1
if ($testFullMethod -match 'XmlSerialize\.Serialize\(data\)')
{
    Write-Host "✓ 请求序列化：正确使用 XmlSerialize.Serialize(data)" -ForegroundColor Green
}
else
{
    Write-Host "✗ 请求序列化：未使用 XmlSerialize.Serialize(data)" -ForegroundColor Red
}

# 检查是否使用SendXmlAsync
if ($testFullMethod -match 'SendXmlAsync')
{
    Write-Host "✓ 响应反序列化：正确使用 SendXmlAsync<TestResponse>" -ForegroundColor Green
}
else
{
    Write-Host "✗ 响应反序列化：未使用 SendXmlAsync<TestResponse>" -ForegroundColor Red
}

# 检查内容类型是否正确
if ($testFullMethod -match 'application/xml; version=1.0; charset=utf-8')
{
    Write-Host "✓ 内容类型：正确使用 'application/xml; version=1.0; charset=utf-8'" -ForegroundColor Green
}
else
{
    Write-Host "✗ 内容类型：未使用正确的内容类型" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== 验证 TestContentTypeWithCharsetAsync 方法（JSON测试）===" -ForegroundColor Cyan

$testCharsetMethod = $generatedCode -split 'TestContentTypeWithCharsetAsync' | Select-Object -Index 1
if ($testCharsetMethod -match 'JsonSerializer\.Serialize')
{
    Write-Host "✓ 请求序列化：正确使用 JsonSerializer.Serialize" -ForegroundColor Green
}
else
{
    Write-Host "✗ 请求序列化：未使用 JsonSerializer.Serialize" -ForegroundColor Red
}

if ($testCharsetMethod -match 'SendAsync<TestResponse>')
{
    Write-Host "✓ 响应反序列化：正确使用 SendAsync<TestResponse>" -ForegroundColor Green
}
else
{
    Write-Host "✗ 响应反序列化：未使用 SendAsync<TestResponse>" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== 总结 ===" -ForegroundColor Cyan
Write-Host "优先级规则：Body参数级 > 方法级 > 接口级" -ForegroundColor Yellow
Write-Host "XML检测：内容类型包含'xml'（不区分大小写）" -ForegroundColor Yellow
Write-Host ""
