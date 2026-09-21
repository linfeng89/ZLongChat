using System.ComponentModel;
using ModelContextProtocol.Server;
using ZLongChat.Contracts.Shudong;

namespace ZLongChat.Mcp.Resources;

/// <summary>
/// 生成式轻界面的 UI 资源（契约 §5）。
///
/// <b>骨架阶段说明</b>：这里返回的是**占位 HTML**，只用于打通"宿主请求资源 → 我们返回 HTML"的链路。
/// 真正的界面在阶段 1 用前端工程产出后替换，URI 与版本号不变。
///
/// 三个不可妥协的约束（契约 §5.3）：
/// <list type="number">
///   <item><b>内容寻址</b>：URI 带版本号，同一版本永远同一份 HTML</item>
///   <item><b>自包含</b>：内联脚本样式，不依赖外部资源</item>
///   <item><b>读宿主上下文</b>：颜色/字体/语言来自 host context，不硬编码</item>
/// </list>
/// </summary>
[McpServerResourceType]
public sealed class ShudongUiResources
{
    /// <summary>MCP Apps 规定的 MIME 类型，写错宿主不会渲染</summary>
    public const string MimeType = "text/html;profile=mcp-app";

    [McpServerResource(UriTemplate = ShudongUiUris.Publish, Name = "倾诉发布面板", MimeType = MimeType)]
    [Description("让用户自己写下想说的话。")]
    public string Publish() => Placeholder("倾诉", "想说什么都可以…", "发布");

    [McpServerResource(UriTemplate = ShudongUiUris.Signals, Name = "树洞池", MimeType = MimeType)]
    [Description("浏览正在等待被听见的人（只显示摘要）。")]
    public string Signals() => Placeholder("树洞池", "正在加载…", "刷新");

    [McpServerResource(UriTemplate = ShudongUiUris.SignalDetail, Name = "信号详情", MimeType = MimeType)]
    [Description("查看一条信号的全文，并决定是否接住。")]
    public string SignalDetail() => Placeholder("信号详情", "正在加载…", "接住 TA");

    [McpServerResource(UriTemplate = ShudongUiUris.Chat, Name = "临时陪伴", MimeType = MimeType)]
    [Description("与对方的临时陪伴对话。")]
    public string Chat() => Placeholder("临时陪伴", "对方：你好，我在听。", "发送");

    [McpServerResource(UriTemplate = ShudongUiUris.State, Name = "我的状态", MimeType = MimeType)]
    [Description("查看我发出的信号与正在进行的会话。")]
    public string State() => Placeholder("我的状态", "正在加载…", "刷新");

    [McpServerResource(UriTemplate = ShudongUiUris.Crisis, Name = "求助资源", MimeType = MimeType)]
    [Description("心理援助热线与紧急求助。")]
    public string Crisis() => Placeholder("你不需要一个人扛", "12356 · 12355 · 110 · 120", "知道了");

    /// <summary>
    /// 占位页面。刻意保持最小 —— 它只证明链路可用，不承担任何真实交互。
    /// 注意：这里 <b>不硬编码颜色</b>，而是读宿主下发的 CSS 变量（契约 §5.3）。
    /// </summary>
    private static string Placeholder(string title, string body, string action) => $$"""
        <!doctype html>
        <html lang="zh">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width,initial-scale=1" />
          <style>
            :root { color-scheme: light dark; }
            body {
              font: 14px/1.6 var(--mcp-font-family, system-ui, sans-serif);
              color: var(--mcp-color-text, inherit);
              background: var(--mcp-color-background, transparent);
              margin: 0; padding: 12px;
            }
            h3 { margin: 0 0 8px; font-size: 15px; }
            .body { opacity: .75; margin-bottom: 12px; }
            button {
              font: inherit; padding: 8px 14px; border-radius: 8px; cursor: pointer;
              border: 1px solid var(--mcp-color-border, currentColor);
              background: var(--mcp-color-surface, transparent);
              color: inherit;
            }
            .skeleton { opacity: .5; font-size: 12px; }
          </style>
        </head>
        <body>
          <h3>{{title}}</h3>
          <div class="body">{{body}}</div>
          <button type="button">{{action}}</button>
          <p class="skeleton">骨架占位页 · 真实界面在阶段 1 替换</p>
        </body>
        </html>
        """;
}
