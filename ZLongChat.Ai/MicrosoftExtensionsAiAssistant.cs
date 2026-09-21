using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using ZLongChat.Ai.Abstractions;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace ZLongChat.Ai;

/// <summary>
/// 把 <c>Microsoft.Extensions.AI</c> 的 <see cref="IChatClient"/> 适配成 <see cref="IAiAssistant"/>。
///
/// 这是「零成本换模型」的关键：本类不认识 OpenAI / Azure / Ollama / Anthropic，
/// 只认识 <see cref="IChatClient"/>。宿主项目换 provider 时只改一行 DI 注册，
/// 本类与调用方都不需要改动。
///
/// 用法（宿主侧）：
/// <code>
///   IChatClient chatClient = new OpenAIClient(apiKey).GetChatClient("gpt-4o").AsIChatClient();
///   IAiAssistant assistant = new MicrosoftExtensionsAiAssistant(chatClient, "你是一个简洁的中文助手。");
/// </code>
/// </summary>
public sealed class MicrosoftExtensionsAiAssistant : IAiAssistant
{
    private readonly IChatClient _chatClient;
    private readonly ChatOptions? _chatOptions;
    private readonly string? _systemPrompt;

    /// <param name="chatClient">任意 provider 提供的聊天客户端</param>
    /// <param name="systemPrompt">系统提示词（可选）</param>
    /// <param name="name">助手名称（可选，默认取 provider 类型名）</param>
    /// <param name="chatOptions">采样参数等（可选）</param>
    public MicrosoftExtensionsAiAssistant(
        IChatClient chatClient,
        string? systemPrompt = null,
        string? name = null,
        ChatOptions? chatOptions = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _systemPrompt = systemPrompt;
        _chatOptions = chatOptions;
        Name = name ?? $"Microsoft.Extensions.AI（{chatClient.GetType().Name}）";
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> StreamReplyAsync(
        AiConversationContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messages = new List<AiChatMessage>();

        if (!string.IsNullOrWhiteSpace(_systemPrompt))
        {
            messages.Add(new AiChatMessage(ChatRole.System, _systemPrompt));
        }

        // 用「是不是助手自己发的」来区分角色，从而把多轮历史还原成标准对话格式
        foreach (var item in context.History)
        {
            var role = string.Equals(item.FromUserId, context.AssistantUserId, StringComparison.Ordinal)
                ? ChatRole.Assistant
                : ChatRole.User;

            messages.Add(new AiChatMessage(role, item.GetText()));
        }

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, _chatOptions, cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }
}
