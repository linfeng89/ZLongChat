# ZLongChat 架构说明

## 解决方案级统一

两个 MSBuild 文件位于解决方案根目录，**所有项目自动继承**，不需要在 csproj 里重复声明：

| 文件 | 统一了什么 |
| --- | --- |
| `Directory.Build.props` | `TargetFramework`（PC 覆盖为 `net10.0-windows`）、`Nullable`、`ImplicitUsings`、XML 文档生成、`Product`/`Company`/`VersionPrefix` |
| `Directory.Packages.props` | **全部 NuGet 包版本**（Central Package Management） |

效果：csproj 里 `<PackageReference>` 只写包名、不写版本；升级包只改一行、全解决方案生效；
新增项目漏写版本会**直接编译报错**，而不是悄悄解析成别的版本。

```xml
<!-- 现在这样写就够了，版本去 Directory.Packages.props 查 -->
<PackageReference Include="Microsoft.Extensions.Configuration.Json" />
```

需要单项目特殊版本时，在该项目里显式写 `Version="x"` 并配套调整 `PackageVersion` 即可。

## 分层结构

```
解决方案根目录
  Directory.Build.props     公共构建属性（TFM / Nullable / 版本号 …）
  Directory.Packages.props  包版本集中管理（CPM）
  ARCHITECTURE.md           本文档

ZLongChat.Contracts        契约层（无任何依赖）
  Models/                  前后端共用的数据结构
  Protocol/                Hub 方法名、REST 路由、P2P 二进制帧格式
  Signaling/               SDP / ICE 信令载荷
  Configuration/           配置节名常量、缺省值、ClientConfiguration（全解决方案唯一配置模型）
  Shudong/                 树洞泡泡契约：数据模型 / 错误码 / 工具名 / UI 资源 URI 常量

ZLongChat.Core             领域层（只依赖 Contracts）
  CallContext.cs           调用者身份（实名 UserId + 匿名 ID + 角色）
  Ports/ISignalService.cs  信号端口 + 内存实现
  Ports/ISessionService.cs 会话端口 + 内存实现（服务端权威的倒计时与状态机）
  Ports/ISafetyService.cs  安全端口：举报 / 求助资源 / 规则兜底
  ※ 这一层**不知道 MCP 存在** —— 所以换成 HTTP / gRPC 暴露给别的前端时，领域代码零改动

ZLongChat.Mcp              MCP 层（薄适配，依赖 Core + Contracts）
  ShudongMcpExtensions.cs  组合根扩展：AddShudongMcp() / MapShudongMcp()
  CallContextAccessor.cs   令牌 → 身份（Bearer 头优先，SHUDONG_TOKEN 兜底）
  ShudongToolErrors.cs     ShudongException → 结构化错误（错误码 + 文案 + 告知）
  Tools/                   16 个工具（SignalTools / CompanionTools / SafetyTools）
  Resources/               6 个 ui:// 资源（骨架阶段为占位 HTML）

ZLongChat.Communication    通信层（只依赖 Contracts）
  Abstractions/            通信接口：IMessageTransport / ISignalingService / IChatApiClient
                           + IIceServerConfigurable（窄接口，用于热更新 ICE 配置）
  SignalR/                 SignalR 实现（信令 + 服务端转发）
  WebRtc/                  WebRTC DataChannel 实现（P2P 直连）
  Http/                    REST 客户端实现
  Composite/               复合通道：P2P 优先 + 自动降级

ZLongChat.Ai                AI 能力层（只依赖 Contracts）
  Abstractions/            IAiAssistant / AiConversationContext
  EchoAiAssistant.cs       占位实现：不依赖云服务，用于跑通链路
  MicrosoftExtensionsAiAssistant.cs  把 IChatClient 适配成 IAiAssistant

ZLongChat.AiBot            AI 机器人宿主（控制台进程）
  appsettings.json         配置：ZLongChat:Client + ZLongChat:AiBot
  AiBotSettings.cs         机器人自身设置模型
  BotArguments.cs          命令行参数（优先级高于配置文件）
  Program.cs               组合根：选择「用哪个助手实现」
  AiBotWorker.cs           以普通用户身份接入 Hub，收消息→生成→回发

ZLongChat.API              后端（依赖 Contracts + Mcp；不引用通信层）
  appsettings.json         配置：ZLongChat:Server / Auth / Client（Client 即下发给客户端的部分）
  Program.cs               组合根：注册服务、装配中间件、映射路由 + MapShudongMcp()
  Hubs/                    ChatHub + 强类型客户端接口
  Endpoints/               REST 接口 + 客户端配置下发
  Services/                存储抽象 + 内存实现
  Options/                 配置模型（AuthSettings）

ZLongChat.PC               前端 WPF（依赖 Contracts + Communication）
  appsettings.json         只留 2 项：ServerBaseUrl + UseServerProvidedConfig（其余靠服务端下发）
  AppConfiguration.cs      配置加载：json → Local → 环境变量
  App.xaml.cs              组合根：决定「用哪套通信实现」
  MainWindow.xaml.cs       纯 UI
  Services/ChatSession.cs  业务编排：登录 / 建连 / 发消息 / 重连策略

tools/                     自检脚本（不是运行时组件）
  mcp-probe.ps1            结构：16 个工具 / 6 个资源 / _meta.ui 分级 / 鉴权
  mcp-scenario.ps1         行为：13 个业务不变量（含 broadcast 不可接住）
  mcp-snapshot.ps1         契约：与 docs/contracts/mcp-manifest.json 比对
```

依赖方向严格单向：`PC/API/AiBot → Communication → Contracts`，`AiBot → Ai`，
`API → Mcp → Core → Contracts`。
UI 里不再出现任何 `HubConnection`、`RTCPeerConnection` 等具体通信类型。

**两条互不干扰的线**：`Contracts/Communication/PC/AiBot` 是**端到端通信**那条线；
`Contracts/Core/Mcp/API` 是**树洞泡泡**那条线。二者只共享 `Contracts` 与配置模型，
所以换通信方式不会碰到树洞业务，改树洞工具契约也不会碰到 P2P。

**关键设计：AI 不是「塞进」聊天链路，而是一个对端。**
`AiBot` 就是一个用 `userId` 连上 Hub 的普通用户 —— 和 `user1`、`user2` 地位完全相同，
因此它复用了全部现成通信代码，没有新增一行。这也天然带来了进程级隔离：
AI 崩了不影响聊天主链路。

**配置也是单向的**：`Contracts` 定义唯一的配置模型与节名 →
`API` 持有权威值并下发 → `PC` / `AiBot` 消费（本地仅作兜底）。

**关键设计：AI 不是「塞进」聊天链路，而是一个对端。**
`AiBot` 就是一个用 `userId` 连上 Hub 的普通用户 —— 和 `user1`、`user2` 地位完全相同，
因此它复用了全部现成通信代码，没有新增一行。这也天然带来了进程级隔离：
AI 崩了不影响聊天主链路。

## 四个核心接口

| 接口 | 层 | 职责 | 当前实现 | 可替换为 |
| --- | --- | --- | --- | --- |
| `IMessageTransport` | 通信 | 统一消息通道：连接、收发、状态 | WebRTC DataChannel、SignalR 转发 | WebSocket / gRPC / MQTT / 原始 TCP |
| `ISignalingService` | 通信 | 交换 SDP / ICE 候选 | SignalR | 原生 WebSocket / gRPC 双向流 |
| `IChatApiClient` | 通信 | 服务端 REST 调用 | HttpClient | gRPC / GraphQL |
| `IAiAssistant` | AI | 由对话上下文流式生成回复 | Echo 占位、Microsoft.Extensions.AI | 任意 LLM provider（OpenAI / Azure / Ollama / 本地模型） |
| `ISignalService` | 领域 | 信号的发布 / 浏览 / 详情 / 正文 / 撤回 | `InMemorySignalService` | Redis / PostgreSQL |
| `ISessionService` | 领域 | 接住 → 陪伴 → 延时 → 结束（服务端权威） | `InMemorySessionService` | Redis + 定时器 |
| `ISafetyService` | 领域 | 举报 / 求助资源 / 规则兜底 | `InMemorySafetyService` | 云审核 + 数据库 |

前四个解决的是「**怎么把消息送到对方那里**」；后三个解决的是「**树洞业务本身是什么**」。
两组接口没有交集，这是刻意的 —— 换通信方式不该动业务，改业务流程也不该动通信。

`IMessageTransport.SendAsync` 在失败时抛 `TransportException`，
复合通道 `FallbackMessageTransport` 捕获后自动改用兜底通道 ——
这就是原来写在 `MainWindow.SendMessage` 里的「P2P 优先 + 3 秒 ACK 超时转离线」策略。

## 如何更换实现

### 场景一：换掉信令方式（例如改用原生 WebSocket）

1. 在 `ZLongChat.Communication` 下新增 `WebSocket/WebSocketSignalingService.cs`，实现 `ISignalingService`。
2. 修改 `App.xaml.cs` 里的一行组装代码：

```csharp
// var signaling = new SignalRSignalingService(communicationOptions, logger);
var signaling = new WebSocketSignalingService(communicationOptions, logger);
```

`WebRtcMessageTransport`、`ChatSession`、`MainWindow` 全部不需要改动。

### 场景二：新增一种消息通道（例如 MQTT）

1. 新增 `Mqtt/MqttMessageTransport.cs`，实现 `IMessageTransport`。
2. 在 `App.xaml.cs` 里替换通道实现，或塞进 `FallbackMessageTransport` 作为新的兜底：

```csharp
var transport = new FallbackMessageTransport(webRtc, mqtt, logger);
```

### 场景三：换掉后端存储（例如 Redis）

只改 `ZLongChat.API/Program.cs` 里的三行注册：

```csharp
builder.Services.AddSingleton<IUserConnectionRegistry, RedisUserConnectionRegistry>();
builder.Services.AddSingleton<INodeRegistry, RedisNodeRegistry>();
builder.Services.AddSingleton<IOfflineMessageStore, RedisOfflineMessageStore>();
```

`ChatHub` 与各 REST 接口依赖的是接口，无需改动。

### 场景三之二：换掉树洞泡泡的内存实现（例如 Redis）

只改 `ZLongChat.Mcp/ShudongMcpExtensions.cs` 里的四行注册：

```csharp
builder.Services.AddShudongMcp();   // 里面是：
// services.TryAddSingleton<InMemorySignalService>();
// services.TryAddSingleton<ISignalService>(sp => sp.GetRequiredService<InMemorySignalService>());
// services.TryAddSingleton<ISignalLookup>(sp => sp.GetRequiredService<InMemorySignalService>());
// services.TryAddSingleton<ISessionService, InMemorySessionService>();
// services.TryAddSingleton<ISafetyService, InMemorySafetyService>();
```

或者更干净：在调用 `AddShudongMcp()` **之前**先注册自己的实现
（内部用的是 `TryAdd`，先注册的胜出）。

`Tools/` 里那 16 个工具与 `Core/Ports/` 里的业务代码**一行都不用改**。

> 为什么要一起注册 `ISignalService` 和 `ISignalLookup`：
> 它们是**同一份数据**的两个视角 —— 工具层只读摘要与正文，会话层要读原始记录来裁决
> 「这条能不能被接住」。指向不同实例就会出现"列表里看得到但接不住"的鬼故事。

### 场景四：后端换掉 SignalR 作为信令传输

`ChatHub` 承载的是「SDP/ICE 转发 + 消息兜底」这一职责。
换成 WebSocket 时，新建一个端点实现同样的转发逻辑即可，
前端只需替换 `ISignalingService` 实现；`ChatHubMethods` 常量保证两端方法名一致。

### 场景五：接入真实大模型（AI 能力）

`ZLongChat.AiBot/Program.cs` 里**只有一行**需要改：

```csharp
// 现在：不依赖任何云服务、不需要 API Key 的占位实现，用于跑通链路
IAiAssistant assistant = new EchoAiAssistant();

// 换成真实大模型：
IChatClient chatClient = new OpenAIClient(apiKey)
    .GetChatClient("gpt-4o")
    .AsIChatClient();
IAiAssistant assistant = new MicrosoftExtensionsAiAssistant(
    chatClient,
    systemPrompt: "你是一个简洁的中文聊天助手。");
```

`MicrosoftExtensionsAiAssistant` 只认 `IChatClient`（`Microsoft.Extensions.AI` 官方抽象），
所以换 OpenAI / Azure OpenAI / Ollama / 本地模型都只是换 provider 包，
`AiBotWorker`、通信层、UI 全部零改动。

```bash
# OpenAI / Azure OpenAI
dotnet add ZLongChat.AiBot package Microsoft.Extensions.AI.OpenAI
# 本地模型用对应的 M.E.AI provider 包即可，IChatClient 接口完全相同
```

### AI 机器人的运行方式

```bash
# 1. 启动调度服务
dotnet run --project ZLongChat.API

# 2. 启动 AI 机器人（默认用户ID aibot）
dotnet run --project ZLongChat.AiBot -- --user-id aibot

# 3. PC 客户端里把「对方用户ID」填 aibot，即可对话
```

在客户端看来，`aibot` 和普通用户没有任何区别 —— 这正是「零成本扩展」的含义：
**没有为 AI 改动任何既有代码，只新增了两个平级项目。**

## 配置

### 统一命名

全部配置收敛到**一个根节 `ZLongChat`**，节名常量由
`ZLongChat.Contracts/Configuration/ZLongChatSections.cs` 提供，三个项目共用同一份，
保证「同一个设置在所有项目里叫同一个名字」。

| 节 | 类型 | 用途 |
| --- | --- | --- |
| `ZLongChat:Server` | — | 服务端监听地址（仅 API） |
| `ZLongChat:Auth` | `AuthSettings` | 令牌前缀等（仅 API） |
| `ZLongChat:Client` | `ClientConfiguration` | **客户端连接与 P2P 的全部参数**（API 持有并下发；PC / AiBot 本地兜底） |
| `ZLongChat:AiBot` | `AiBotSettings` | 机器人自身设置（仅 AiBot） |

> **重构前**：API 用 `STUNSettings:StunServers`（字符串列表），PC 用 `WebRtc:IceServers`（对象列表）
> —— 同一件事、两套名字、两套结构。
> **现在**：两边都是 `ZLongChat:Client:IceServers`，同一个 `IceServerOptions` 类型；
> 原先的 `CommunicationOptions` 与 `WebRtcOptions` 两个类也合并为一个 `ClientConfiguration`。

### 优先级

| 项目 | 优先级（后者覆盖前者） |
| --- | --- |
| API | 命令行 > 环境变量 > `appsettings.{Env}.json` > `appsettings.json` |
| PC | 环境变量 > `appsettings.Local.json` > `appsettings.json` > 代码缺省值 |
| AiBot | 命令行 > 环境变量 > `appsettings.Local.json` > `appsettings.json` > 代码缺省值 |

环境变量用双下划线表示层级：

```bash
# 换服务地址
ZLongChat__Client__ServerBaseUrl=http://10.0.0.9:5000

# 覆盖第 1 个 ICE 服务器（下标从 0 开始）
ZLongChat__Client__IceServers__0__Url=stun:my-stun.example.com:3478

# 注入 TURN 密码，避免提交进仓库
ZLongChat__Client__IceServers__2__Credential=真实密码
```

代码里的缺省值**只在一处定义**：`ZLongChat.Contracts/Configuration/ZLongChatDefaults.cs`。
（重构前 `http://localhost:5000` 散落在 4 个文件里，改一处漏一处。）

### 由服务端下发客户端配置（推荐做法）

**STUN / TURN 只需要在服务端维护一份**，客户端登录时自动拉取：

```
客户端启动 → 拉取 GET /api/client-config ← 服务端下发 ICE / 超时 / 重连间隔
         → 覆盖本地值 → 连信令 → 建 P2P
```

于是「改一处，所有客户端一起生效」：

```jsonc
// 服务端 appsettings.json
"ZLongChat": { "Client": { "IceServers": [ /* … */ ] } }
```

客户端侧行为由 `ZLongChat:Client:UseServerProvidedConfig` 控制：

- `true`（默认）：服务端值优先，本地值作为**拉取失败时的兜底**
- `false`：只用本地配置

**客户端本地因此只剩两项配置**（`ZLongChat.PC/appsettings.json`）：

```jsonc
"ZLongChat": { "Client": {
  "ServerBaseUrl": "http://localhost:5000",   // 连哪台服务端（只有本机知道）
  "UseServerProvidedConfig": true             // 是否接受服务端下发
}}
```

ICE 服务器、超时、心跳、重连间隔、DataChannel 参数**全都不在客户端配**，改服务端一处即可。

> 为什么客户端不需要留 ICE 兜底？P2P 建连要先靠服务端交换 SDP/ICE，
> 服务端不可达时 P2P 本来也建不起来。拉取失败会退回代码缺省值并在日志明确告警。

有两个字段**只属于客户端本地，不会下发**：`ServerBaseUrl`（客户端自己决定连哪台）
与 `UseServerProvidedConfig`。实现方式是在 `ClientConfiguration` 上标 `[JsonIgnore]`，已实测确认不出现在响应里。

拉取失败不会阻塞登录：客户端改用本地配置继续跑，并在界面提示降级原因。

> **安全提醒**：下发给客户端的内容等同于公开信息。生产环境不建议下发长期有效的
> TURN 静态凭据，应改用**临时凭据**（coturn REST API 风格的 time-limited credential）。
> 当前实现适合内网 / 开发环境。

### 常见改动

| 想改什么 | 改哪里 |
| --- | --- |
| STUN / TURN | 服务端 `ZLongChat:Client:IceServers`（改一处，全客户端生效） |
| 客户端连哪台服务端 | 客户端 `ZLongChat:Client:ServerBaseUrl` |
| 服务端监听地址 | `ZLongChat:Server:Urls`（留空则交给 launchSettings / `ASPNETCORE_URLS`） |
| P2P 超时 / 心跳 / 重连 | `ZLongChat:Client:*`（服务端改，全客户端生效） |
| 断线重连次数 / 退避 | `ZLongChat:Client:MaxReconnectAttempts` / `ReconnectDelaySeconds` / `MaxReconnectDelaySeconds` |
| P2P 建连超时 | `ZLongChat:Client:P2PConnectTimeoutSeconds` |
| 机器人用户ID / 历史长度 | `ZLongChat:AiBot:*`，或命令行 `--user-id` / `--history` |

配置缺失时程序仍会正常启动并在启动日志明确告警（如「未配置 ICE 服务器…P2P 打洞很可能失败」），不会静默降级。

### 硬编码自查结果

重构后全仓库只剩 `ZLongChatDefaults.cs` 一处地址/缺省值定义（其余命中均为注释）。原先散落的硬编码：

| 原硬编码 | 处理 |
| --- | --- |
| `http://localhost:5000`（4 处） | 收敛为 `ZLongChatDefaults` 单个定义 + 各项目配置节 |
| `app.Run("http://localhost:5000")` | 改为读 `ZLongChat:Server:Urls`，未配置则交给 ASP.NET Core（顺带修好了 launchSettings 被覆盖的问题） |
| `dev_token_` 令牌前缀 | `ZLongChat:Auth:DevTokenPrefix` |
| 重连固定 3 秒 | `ZLongChat:Client:ReconnectDelaySeconds` |
| 机器人用户ID / 历史长度 20 | `ZLongChat:AiBot:UserId` / `HistoryLimit` |
| STUN/TURN 地址 | `ZLongChat:Client:IceServers`（原先硬编码在 `WebRtcOptions`，现已彻底移出代码） |
| 占位助手分片参数 | `ZLongChat:AiBot:ChunkSize` / `ChunkDelayMs` |
| **包版本散落在 6 个 csproj** | `Directory.Packages.props` 集中管理（CPM），csproj 只写包名 |
| **TFM / Nullable / ImplicitUsings / 版本号重复 6 份** | `Directory.Build.props` 统一，项目文件只写差异 |
| **客户端重复配置 ICE / 超时（与后端重复一遍）** | 客户端只留 2 项，其余由服务端下发 |

配置项都带 `Normalize()`：手写配置把超时写成 0 之类的错误会被修正回缺省值，不会把程序改崩。

## 断线重连与离线提示

### 为什么需要「建连看门狗」

只有 ICE 失败事件是不够的：**当对端已下线时，我方发出的 Offer 拿不到 Answer，
ICE 永远不会进入 failed 状态** —— 重试循环会静默卡死，用户既没等到连接，也看不到任何提示。

因此每次发起连接（手动或自动）都会启动一个 `P2PConnectTimeoutSeconds` 看门狗：
超时仍未连通即判定本次失败，继续走退避；到达上限就停止。

### 重连策略

```
P2P 断开 → 第 1 次重连（等 3s）→ 看门狗超时判失败
        → 第 2 次重连（等 6s）→ 看门狗超时判失败
        → 第 3 次重连（等 12s）→ 看门狗超时判失败
        → 停止自动重连，状态置为「直连失败（走服务端转发）」
```

- 延迟按 **2 倍退避**递增，封顶 `MaxReconnectDelaySeconds`
- `MaxReconnectAttempts = 0` 表示不限次数（需自行承担刷屏）
- 连接成功即清零计数；手动点「发起连接」也清零重试
- **放弃时会明确告知原因和补救办法**，不再无声循环：

```
⛔ P2P 直连已连续失败 3 次，已停止自动重连。
   · 消息仍会通过服务端转发，聊天不受影响
   · 请检查网络（对方是否在线 / 防火墙是否放行 / 网卡是否正常）
   · 修好后点「发起连接」可手动重试
```

关键点：**P2P 直连失败不影响聊天** —— `FallbackMessageTransport` 会自动改走服务端转发。

### 离线 / 无网卡时的提示

若本次协商**一个 ICE 候选都没收集到**，说明本机很可能没有可用网络接口
（未联网 / 网线未插 / 网卡被禁用），此时会给出针对性提示，而不是让人干等：

```
❌ 未能收集到任何 ICE 候选：本机可能没有可用的网络接口（未联网 / 网线未插 / 网卡被禁用）。
   P2P 直连不可用，消息仍可通过服务端转发。
```

> 实测：本机/局域网内即使 STUN/TURN 全部不可达，**host 候选仍能正常收集并建连成功**
> （0.9s 出候选、2.1s 连通）。所以「STUN 不通」本身不会导致 P2P 失败，
> 真正会导致失败的是**没有任何可用网络接口**。

## 协议兼容性

本次重构保持了线上协议不变：

- REST 返回的 JSON 字段与改造前一致（`{"success":true}`、`{"token":...,"userId":...}`）。
- Hub 方法名与参数顺序不变，通过 `ChatHubMethods` 常量在前后端共享。
- ICE 候选的 JSON 属性名保持小写（`candidate` / `sdpMid` / `sdpMLineIndex`），
  用 `[JsonPropertyName]` 固定，老客户端仍可互通。
- P2P 二进制帧格式（类型字节 + 8 字节消息ID + 负载）与改造前完全一致。

## 顺带修掉的问题

1. **存储键冲突**：原先 REST 接口与 Hub 共用同一个 `ConcurrentDictionary`，
   一个写 `ip:port`、一个写 `connectionId`，互相覆盖。
   现已拆成 `INodeRegistry` 与 `IUserConnectionRegistry` 两张独立的表。
2. **离线消息收不到**：服务端发的是 `ChatMessage` 对象，客户端却按 `byte[]` 注册
   （`On<byte[]>("ReceiveMessage")`），反序列化必然失败。
   现在两端共用 `ChatMessage`，问题消失。
3. **未检查的 SDP 结果**：`setRemoteDescription` 的返回值原先被直接忽略，
   现在会把异常结果记入日志并提示到界面。
4. **启动日志永不执行**：原先 `app.Run(...)` 之后的日志是死代码，已移到启动之前。
5. **硬编码的 STUN/TURN**：从 `MainWindow` 移到配置（现为服务端 `ZLongChat:Client:IceServers`，
   由服务端下发给所有客户端），改地址不用改代码、不用重新编译；TURN 凭据可用环境变量注入，不必提交进仓库。
6. **无限重连**：原先 P2P 一断就每 3 秒重连一次、永不停止（没网时刷屏且无提示）。
   现在改为指数退避 + 次数上限 + 建连看门狗，放弃时给出可操作提示。
7. **API 配置依赖工作目录**：`WebApplication.CreateBuilder` 默认用当前工作目录当 content root，
   从别处启动 DLL 时会读不到 `appsettings.json`，表现为「服务端下发的 ICE 服务器为 0 个」。
   现已显式固定为 `AppContext.BaseDirectory`。
8. **过期的配置节名**：`WebRtcMessageTransport` 的告警文案仍写 `WebRtc:IceServers`，
   已改为统一后的 `ZLongChat:Client:IceServers`。
9. **缺少 .gitignore**：`appsettings.Local.json`（可能含 TURN 凭据）此前没有任何忽略规则，
   现已补上 `.gitignore`。
