using System.Text.Json;
using ModelContextProtocol.Protocol;
using ZLongChat.Contracts.Shudong;

namespace ZLongChat.Mcp;

/// <summary>
/// 把 <see cref="ShudongException"/> 转成调用方可以分支的结构化错误。
///
/// <b>为什么需要一个过滤器</b>：MCP SDK 默认把工具抛出的异常压成
/// <c>"An error occurred invoking 'xxx'."</c> —— 连错误码都没了。
/// 前端的 widget 就没法区分"这条不能接住"和"次数用完了"。
///
/// <b>注意不要泄露正文</b>：这里只回传错误码、面向用户的文案与告知，
/// <b>从不回传异常堆栈</b>（堆栈里可能带上用户说的话）。
/// </summary>
internal static class ShudongToolErrors
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>把业务异常转成 <c>isError: true</c> 的工具结果。</summary>
    public static CallToolResult FromException(ShudongException exception)
    {
        var payload = new ShudongErrorPayload(exception.Code, exception.Message, exception.Disclosure);

        return new CallToolResult
        {
            IsError = true,
            // content 给不认识 structuredContent 的宿主兜底显示
            Content = [new TextContentBlock { Text = exception.Message }],
            StructuredContent = JsonSerializer.SerializeToElement(payload, SerializerOptions)
        };
    }
}
