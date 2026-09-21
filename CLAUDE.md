# CLAUDE.md

本文件是给 AI 编码代理（Claude Code 等）的项目约定。**请先读完再动手。**

---

## 0. 操作授权（长期有效，不必逐次确认）

**本项目已明确授权代理自主执行以下操作，无需每次征求同意：**

| 授权项 | 说明 |
| --- | --- |
| 构建 / 编译 | `dotnet build`、`dotnet restore`、`dotnet publish` |
| 运行 / 调试 | 启动 `ZLongChat.API` / `ZLongChat.PC` / `ZLongChat.AiBot`，读写其输出 |
| 测试 | 运行 `tools/*.ps1` 自检脚本、将来的单元测试 |
| 进程管理 | 结束本项目自己的进程（`ZLongChat.*`、`dotnet`、`MSBuild`、`VBCSCompiler`）以解除文件占用 |
| 文件读写 | 在 `D:\ZLongChat` 内创建、修改、删除文件（含 `bin` / `obj` 清理） |
| 包管理 | `dotnet add package`、`dotnet package search`、修改 `Directory.Packages.props` |
| 联网查资料 | 查 NuGet / Microsoft Learn / 官方文档，用于确认 API 与版本事实 |
| 沙箱升权 | 沙箱拦截 `dotnet.exe` / 网络时，**直接**用 `sandbox_permissions: danger-full-access` 重试，不要回头问 |

**唯一的例外**：涉及**不可逆**或**对外**的动作仍需先确认 ——
删除 `docs/` 下的文档、改写 `树洞泡泡.md`（见 §4）、`git push`、发布部署、花钱的操作。

判断标准：**能自己回退的，直接做；不能回退的，先问。**

---

## 1. 语言与工具链

| 项 | 约定 |
| --- | --- |
| **编程语言** | **C# 优先**。前端也用 C#（WPF）。除非有硬性理由，不引入其他语言 |
| 文档语言 | **中文**。代码注释、XML 文档、Markdown 全部中文 |
| 标识符 | 英文（类型名 / 方法名 / 变量名），与 .NET 惯例一致 |
| 目标框架 | **net10.0**（PC 为 `net10.0-windows`）；见 §6 升级记录 |
| 语言版本 | 跟随 TFM 默认（net10.0 → C# 14），不在项目里另行钉死除非有理由 |
| 脚本 | 辅助脚本用 **PowerShell**（`tools/*.ps1`），不要引入 Python / Node |

---

## 2. 工程标准（硬性）

1. **0 错误 0 警告**是基线。交付前必须跑：
   ```powershell
   dotnet build ZLongChat.slnx -v q --nologo -m:1 -p:UseSharedCompilation=false
   ```
   看到 `已成功生成` + `0 个警告` + `0 个错误` 才算完成。

2. **不要用 `dotnet run` 启动本项目**。见 §5「构建与运行」—— 会撞上文件占用。

3. **新增项目必须加进 `ZLongChat.slnx`**，否则 CI / 统一构建会漏掉它。

4. **包版本只在 `Directory.Packages.props` 维护**（CPM）。csproj 里的 `<PackageReference>` 只写包名。

5. **公共构建属性只在 `Directory.Build.props` 维护**，个别项目差异才在自己的 csproj 里覆盖。

---

## 3. 架构与分层（不要破坏）

```
ZLongChat.Contracts     ← 契约层（无依赖）
   ↑            ↑
ZLongChat.Core  ZLongChat.Communication     ← 领域层 / 通信层
   ↑            ↑
ZLongChat.Mcp   ZLongChat.PC / AiBot
   ↑
ZLongChat.API（组合根）
```

**两条互不干扰的线**：

- `Contracts → Communication → {PC, AiBot}`：端到端通信（SignalR 信令 + WebRTC P2P）
- `Contracts → Core → Mcp → API`：树洞泡泡业务（MCP 工具 + 领域服务）
- `Contracts → Ai → AiBot`：AI 能力抽象

**规则**：

- `ZLongChat.Core` **不知道 MCP 存在**。它不是"MCP 的服务层"，而是与传输无关的领域层。
- `ZLongChat.Mcp` 是**薄适配层**：只做协议适配、鉴权、错误映射。业务规则不放这里。
- `ZLongChat.API` 只依赖 `Contracts` + `Mcp`，**不引用通信层**（避免把 SignalR.Client / SIPSorcery 拖进后端）。
- UI 层不出现 `HubConnection` / `RTCPeerConnection` 等具体通信类型。
- 换实现时只改组合根（`Program.cs` / `ShudongMcpExtensions.cs`）的注册行。

详见 `ARCHITECTURE.md`。

---

## 4. 产品约束（改代码前必须知道）

产品文档：`树洞泡泡-AI版本.md`。相关设计：`docs/`。

**`树洞泡泡.md` 是最初版产品文档，是历史基线 —— 不要修改它。**
（当前 SHA256 `5cb0bcd5cdbde6adb3b45074349ff2b3c2d8131b32b6389fb847d9f2ff25ab79`，改动前请先确认。）

### 已经明确"不做"的事（不要好心加回来）

| 不做 | 理由 |
| --- | --- |
| 情绪理解 / 情感计算 | 说不清用途，且听起来像 AI 产品该有 |
| 危机识别（宣称能识别） | 措辞一旦声称就产生**持续履行的义务**（见 `docs/审核与危机识别设计.md` §2.4） |
| 人工 7×24 值守承诺 | 承诺了就得真有人在，否则风险更大 |
| 端侧变声 / TTS / ASR（MVP） | 不验证核心假设，却引入端侧 SDK + 音频审核 + 对象存储 |
| DID / VC / ZKP | 与"后台实名可追溯"方向相反，移到愿景层 |
| AI 陪伴时长作为成绩 | AI 是**缓冲层**，不是陪伴终点；AI 时长是成本 |

> 共同特征：**听起来很 AI、但说不出具体用途**。加功能前先问「它替代了什么？解决了什么问题？」
> （见 `docs/开发计划.md` §1.4 准入规则）

### 三条已经定型的硬规则

1. **含用户正文的工具一律 `visibility:["app"]`** —— 正文不进 LLM 上下文。
2. **`broadcast` 信号不可被接住** —— 服务端硬校验，返回 `MODE_NOT_CATCHABLE`。
3. **常规对话即焚；内容留存的唯一例外是举报**，且必须透明告知。

这三条都有自动化断言守护（`tools/mcp-scenario.ps1` / `mcp-probe.ps1`），改了就会红。

---

## 5. 构建与运行（踩过的坑，别重踩）

```powershell
# 构建（-m:1 串行 + 关共享编译，避开杀毒软件锁文件）
dotnet build ZLongChat.slnx -v q --nologo -m:1 -p:UseSharedCompilation=false

# 启动后端：用 dll，不要用 dotnet run
dotnet ZLongChat.API\bin\Debug\net10.0\ZLongChat.API.dll

# 三块自检（退出码非 0 即失败）
.\tools\mcp-probe.ps1       # 结构：16 工具 / 6 资源 / _meta.ui / 鉴权
.\tools\mcp-scenario.ps1    # 行为：13 个业务不变量
.\tools\mcp-snapshot.ps1    # 契约：与 docs/contracts/mcp-manifest.json 比对
```

### 环境已知问题（都是真的发生过）

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| `MSB3021` / `MSB3027` / `CS2012` 文件被占用 | **360 杀毒实时监控** + 运行中的应用进程 | 结束 `ZLongChat.*` 进程；`dotnet build-server shutdown`；等几秒重试 |
| `dotnet run` 报「另一个程序正在使用此文件」 | 它会改写 `bin\...\ZLongChat.API.exe`，该文件常被占用 | 改用 `dotnet <dll>` |
| 中文注释在终端显示为乱码 | Windows PowerShell 5.1 默认按 ANSI 读 UTF-8 | 只影响显示；**脚本文件需要 UTF-8 BOM** 才能被 PS 5.1 正确解析中文 |
| PS 脚本里参数名 `$Args` 报类型转换错 | `$Args` 是 PowerShell 自动变量 | 改名（如 `$ToolArgs`） |
| 构建结果不稳定（时好时坏） | 杀毒软件扫描锁 | 重试；或串行构建 |

> 这个仓库**不是 git 仓库**，改错了没有 `git checkout` 兜底。
> 大改动前先自己留备份，或改用可逆的小步编辑。

---

## 6. 版本与升级记录

| 日期 | 变更 |
| --- | --- |
| 2026-09-21 | TFM `net8.0` → `net10.0`（PC 为 `net10.0-windows`）；`LangVersion` 不再钉死（跟随 TFM → C# 14）。<br>理由：.NET 8 LTS 于 **2026-11** 结束支持；.NET 10 为 LTS（支持至 2028-11）。<br>**与 Microsoft Agent Framework 无关** —— MAF 本身同时支持 net8.0 / net9.0 / net10.0。<br>代价：`Swashbuckle.AspNetCore` 6.6.2 → **10.2.3**（6.x 在 ASP.NET Core 10 上运行时抛 `GetSwagger does not have an implementation`）；`Microsoft.AspNetCore.OpenApi` 8.0.26 → 10.0.12。其余 8 个项目零改动。 |

### 升级 .NET 版本时的注意点

- **`dotnet build` 通过 ≠ 能跑**。Swashbuckle 那次就是编译 0 错误、一启动就 `TypeLoadException`。
  改 TFM 后必须**真的把 API 跑起来**再跑一遍 `tools/*.ps1`。
- 升级清单：`Directory.Build.props` 的 TFM、`ZLongChat.PC` 的 `-windows` TFM、ASP.NET Core 相关包的 Major 版本。
- 依赖 ASP.NET Core 内部 API 的第三方包（Swashbuckle / NSwag 这类）**必须跟着升 Major**。
- 机器上同时装着 net8.0 与 net10.0 运行时。**保留 net8.0 运行时**作为回退：把 TFM 改回 `net8.0` 即可恢复。

---

## 7. 改动前的自检清单

- [ ] 分层依赖方向没被破坏（`Core` 仍不知道 MCP；`API` 仍不引用通信层）
- [ ] 没有把「已经明确不做」的功能加回来（§4）
- [ ] 新增项目已加进 `ZLongChat.slnx`
- [ ] 包版本写在 `Directory.Packages.props`
- [ ] 构建 0 错误 0 警告
- [ ] 动了 MCP 工具契约 → 跑 `mcp-snapshot.ps1`，有意变更时同步更新快照 + `docs/MCP工具契约.md` §12 变更记录
- [ ] 中文注释 / 文档；`树洞泡泡.md` 未被修改
