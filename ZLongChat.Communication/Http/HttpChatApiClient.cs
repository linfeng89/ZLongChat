using System.Net.Http.Json;
using ZLongChat.Communication.Abstractions;
using ZLongChat.Contracts;

namespace ZLongChat.Communication.Http;

/// <summary>
/// 基于 HTTP 的服务端接口实现。
/// 换成 gRPC / GraphQL 时只需要另写一个 <see cref="IChatApiClient"/> 实现，
/// UI 与业务代码不用改。
/// </summary>
public sealed class HttpChatApiClient : IChatApiClient
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private bool _disposed;

    /// <param name="configuration">客户端配置（提供基地址）</param>
    /// <param name="httpClient">外部注入的 HttpClient（如 IHttpClientFactory）；为空则内部创建</param>
    public HttpChatApiClient(ClientConfiguration configuration, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (httpClient is not null)
        {
            _http = httpClient;
            _ownsClient = false;
        }
        else
        {
            _http = new HttpClient();
            _ownsClient = true;
        }

        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri(configuration.ServerBaseUrl.TrimEnd('/') + "/");
        }
    }

    /// <inheritdoc />
    public async Task<UserLoginResponse> LoginAsync(string userId, string password = "", CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("用户ID不能为空", nameof(userId));
        }

        using var response = await _http
            .PostAsJsonAsync(ChatApiRoutes.Login, new UserLoginRequest { UserId = userId, Password = password }, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<UserLoginResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return result ?? new UserLoginResponse { UserId = userId };
    }

    /// <inheritdoc />
    public async Task ReportNodeAsync(NodeReportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await _http
            .PostAsJsonAsync(ChatApiRoutes.NodeReport, request, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task<NodeInfo> GetNodeAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("用户ID不能为空", nameof(userId));
        }

        var result = await _http
            .GetFromJsonAsync<NodeInfo>(ChatApiRoutes.Node(userId), cancellationToken)
            .ConfigureAwait(false);

        return result ?? new NodeInfo();
    }

    /// <inheritdoc />
    public async Task<ClientConfiguration> GetClientConfigurationAsync(CancellationToken cancellationToken = default)
    {
        var result = await _http
            .GetFromJsonAsync<ClientConfiguration>(ChatApiRoutes.ClientConfig, cancellationToken)
            .ConfigureAwait(false);

        return result ?? new ClientConfiguration();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
