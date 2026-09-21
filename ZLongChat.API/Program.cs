using ZLongChat.API.Endpoints;
using ZLongChat.API.Hubs;
using ZLongChat.API.Options;
using ZLongChat.API.Services;
using ZLongChat.Contracts;
using ZLongChat.Mcp;

// ============================================================
// 后端启动入口（组合根）
//
// 这个文件只做三件事：注册服务、装配中间件、映射路由。
// 具体实现分别位于：
//   Hubs/       —— SignalR 信令与消息转发
//   Endpoints/  —— REST 接口（含客户端配置下发）
//   Services/   —— 存储抽象与内存实现（可替换为 Redis / 数据库）
//   Options/    —— 配置模型
//
// 全部配置集中在 appsettings.json 的 ZLongChat 根节下，
// 节名常量由 ZLongChat.Contracts 的 ZLongChatSections 提供，前后端一致。
// ============================================================

// ContentRootPath 显式固定为「程序所在目录」：
// 否则用别的工作目录启动（例如 dotnet xxx\ZLongChat.API.dll）时会读不到 appsettings.json，
// 表现为「服务端下发的 ICE 服务器数量为 0」这类难以排查的问题。
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// ---------------- 服务注册 ----------------

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

// 开发阶段放开跨域，方便本地联调
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 存储抽象：默认使用内存实现。
// 将来要换 Redis / 数据库，只需要替换下面三行注册，Hub 与接口代码零改动。
builder.Services.AddSingleton<IUserConnectionRegistry, InMemoryUserConnectionRegistry>();
builder.Services.AddSingleton<INodeRegistry, InMemoryNodeRegistry>();
builder.Services.AddSingleton<IOfflineMessageStore, InMemoryOfflineMessageStore>();

// 配置绑定：客户端配置（含 STUN/TURN，由本服务下发给客户端）
builder.Services.Configure<ClientConfiguration>(
    builder.Configuration.GetSection(ZLongChatSections.Client));

// 配置绑定：鉴权
builder.Services.Configure<AuthSettings>(
    builder.Configuration.GetSection(ZLongChatSections.Auth));

// 树洞泡泡：MCP 工具契约 + 领域实现（业务实现在 ZLongChat.Core，这里只是挂上来）
builder.Services.AddShudongMcp();

var app = builder.Build();

// ---------------- 启动信息 ----------------

var clientConfiguration = app.Configuration
    .GetSection(ZLongChatSections.Client)
    .Get<ClientConfiguration>() ?? new ClientConfiguration();

app.Logger.LogInformation("========================================");
app.Logger.LogInformation("✅ P2SP调度服务启动成功！");

if (clientConfiguration.IceServers.Count > 0)
{
    // 只打印地址，不打印 TURN 凭据
    app.Logger.LogInformation(
        "🧊 将下发给客户端的 ICE 服务器（{Count} 个）：{Servers}",
        clientConfiguration.IceServers.Count,
        string.Join("、", clientConfiguration.IceServers.Select(server => server.Url)));
}
else
{
    app.Logger.LogWarning("⚠️ 未配置 ICE 服务器（{Section}:IceServers），客户端 P2P 打洞很可能失败",
        ZLongChatSections.Client);
}

// 实际监听地址在启动完成后才确定（配置 > launchSettings > ASPNETCORE_URLS > 默认端口）
app.Lifetime.ApplicationStarted.Register(() =>
{
    foreach (var url in app.Urls)
    {
        app.Logger.LogInformation("📡 服务地址：{Url}", url);
        app.Logger.LogInformation("🔌 SignalR Hub：{Url}{HubPath}", url, clientConfiguration.ChatHubPath);
        app.Logger.LogInformation("⚙️  客户端配置：{Url}{Route}", url, ChatApiRoutes.ClientConfig);
        app.Logger.LogInformation("🌳 树洞泡泡 MCP：{Url}{Mcp}", url, ZLongChatDefaults.ShudongMcpPath);
    }
    app.Logger.LogInformation("========================================");
});

// ---------------- 中间件 ----------------

app.UseCors("DevCors");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// ---------------- 路由 ----------------

app.MapUserEndpoints();
app.MapNodeEndpoints();
app.MapClientConfigEndpoints();
app.MapHub<ChatHub>(clientConfiguration.ChatHubPath);
app.MapShudongMcp();

// ---------------- 启动 ----------------

// 监听地址优先取 ZLongChat:Server:Urls；
// 未配置时交给 ASP.NET Core 决定（launchSettings.json / ASPNETCORE_URLS / 默认端口），
// 不再像改造前那样硬编码 app.Run("http://localhost:5000") 把 launchSettings 覆盖掉。
var serverUrls = app.Configuration
    .GetSection($"{ZLongChatSections.Server}:Urls")
    .Get<string[]>();

if (serverUrls is { Length: > 0 })
{
    app.Run(string.Join(";", serverUrls));
}
else
{
    app.Run();
}
