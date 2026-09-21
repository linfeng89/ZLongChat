# tools/ —— 树洞泡泡 MCP 自检脚本

这三块脚本是**验收证据**，不是演示。
它们覆盖契约里能自动验证的部分，「文档说做到了」和「真的做到了」之间的差别就在这里。

## 跑之前

先起后端（脚本连的是 `http://localhost:5000/mcp`）：

```powershell
dotnet build ZLongChat.slnx
dotnet ZLongChat.API\bin\Debug\net10.0\ZLongChat.API.dll
```

> ⚠️ 用 `dotnet <dll>` 而不是 `dotnet run`：
> `dotnet run` 会去改写 `bin\Debug\net10.0\ZLongChat.API.exe`，
> 而该文件经常被杀毒软件或 IDE 占着，表现为莫名其妙的「另一个程序正在使用此文件」。

## 三块脚本

| 脚本 | 验证什么 | 失败意味着 |
| --- | --- | --- |
| `mcp-probe.ps1` | **结构**：16 个工具、6 个 UI 资源、`_meta.ui` 分级、Bearer 鉴权 | 协议层形状变了，或鉴权漏了 |
| `mcp-scenario.ps1` | **行为**：13 个业务不变量（发布→接住→陪伴→举报、`broadcast` 不可接住…） | 业务规则被改坏了 |
| `mcp-snapshot.ps1` | **契约**：与 `docs/contracts/mcp-manifest.json` 逐字段比对 | 契约漂移，已装好的客户端会对不上 |

三者退出码非 0 即失败，可直接接进 CI。

## 快照怎么用

```powershell
.\tools\mcp-snapshot.ps1                    # 比对
.\tools\mcp-snapshot.ps1 -UpdateSnapshot    # 更新（**只在有意改契约时**）
```

快照里**不含**工具的 `description`：文案调整不该让契约测试失败。
但工具名、参数、必填项、注记、`_meta.ui`、资源 URI / MIME 的改动**必须**失败。

更新快照时，请同时在 `docs/MCP工具契约.md` §12 变更记录里写清楚改了什么、为什么。

## 鉴权

脚本默认用开发令牌 `dev_token_<userId>`（见 `ZLongChat.Core/DevIdentityService`）。
`mcp-probe.ps1` 会额外验证**无令牌**与**伪造令牌**都被拒绝。

## 为什么用 PowerShell 而不是 xunit

骨架阶段这三个脚本零依赖、零构建、随时可跑，改一行立刻能看到结果。
等阶段 1 引入测试工程时，这里的断言应当**原样搬过去**（而不是删掉）——
它们是契约的守卫，不是临时脚手架。
