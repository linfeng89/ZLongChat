# ZLongChat.Mcp · MCP 工具契约

> 版本：**v1.0**（待评审）
> 对应：`docs/开发计划.md` 阶段 1
> 目标：把 MCP 接口定死，让后端服务与前端界面**并行开发**

---

## 1. 范围与设计原则

### 1.1 定位

`ZLongChat.Mcp` 是**薄层**：只做协议适配与鉴权，**不重复实现业务逻辑**。
所有工具最终调用 `ZLongChat.API` 的现有服务（Hub / REST / 领域服务）。

```
MCP 客户端（宿主 agent / widget）
        │  MCP (stdio 或 streamable HTTP)
        ▼
┌────────────────────────┐
│  ZLongChat.Mcp         │  协议适配 + 鉴权 + 可见性分级
│  （薄层，无业务逻辑）    │
└───────────┬────────────┘
            │  进程内调用
            ▼
┌────────────────────────┐
│  ZLongChat.API         │  房间 / 匹配 / 危机 / 举报 / 限次
│  Hub + Services        │  ← 与 Web、AiBot 共用同一套
└────────────────────────┘
```

### 1.2 五条设计原则

| # | 原则 | 说明 |
| --- | --- | --- |
| 1 | **敏感内容不进 LLM** | 含用户正文的工具一律 `visibility:["app"]`（见 §6） |
| 2 | **工具返回必须自给自足** | widget 渲染失败时，agent 仍能据此回答（graceful degradation） |
| 3 | **服务端权威** | 倒计时、限次、状态一律服务端裁决，工具只是入口 |
| 4 | **幂等** | 所有写操作支持幂等键，重试安全 |
| 5 | **少而清晰** | 16 个工具，每个职责单一 |
| 6 | **状态 1 不进 MCP** | "说出来就好"完全在端侧，**不产生任何 MCP 调用**（见 §1.3） |

### 1.3 三个状态与 MCP 的边界

产品有三个状态（见产品文档 2.1），但**只有两个会经过 MCP**：

| 状态 | 是否经过 MCP | 说明 |
| :---: | :---: | --- |
| **1 · 说出来就好** | ❌ **完全不经过** | 纯端侧记录。**平台看不到，因此没有任何服务端交互** |
| **2 · 被听到** | ✅ `mode: "broadcast"` | 进池、可看，**不可接住** |
| **3 · 需要陪伴** | ✅ `mode: "companion"` | 进池、可看、**可接住** |

**这对 Skill 有直接影响：**

```markdown
## 判断用户意图
- 用户只是想说、不想被任何人看到 → **不要调用任何工具**。
  告诉他："那你可以只写下来，不发给任何人。"
- 用户想被看到但不想要回应 → 用 open_publish，mode 选 broadcast
- 用户想找人陪 → 用 open_publish，mode 选 companion
```

> **为什么状态 1 不进 MCP 是重要设计**：
> 平台看不到内容 → 平台对该内容**没有任何义务**（也就无需审核、无需识别、无需留存）。
> 这是「**用户选择决定平台义务**」这一原则最彻底的体现。

**两种 mode 的服务端硬约束：**

| mode | 可接住 | `catch_signal` 行为 |
| --- | :---: | --- |
| `broadcast` | ❌ | **拒绝**，返回 `MODE_NOT_CATCHABLE` |
| `companion` | ✅ | 正常 |

> 这不是界面层的隐藏，而是**服务端校验** ——
> 保证"用户说只要被听到，就不会被打扰"是**机制保证**。

---

## 2. 认证与会话上下文

### 2.1 问题

MCP Server 必须知道「谁在调用」，才能做限次、匿名 ID、会话归属。

### 2.2 MVP 方案（✅ 已确认）

```
1. 用户在 Web 端完成手机号实名登录
2. Web 端签发 access_token（绑定实名校验结果）
3. 用户在 MCP 客户端配置该 token
     · stdio 传输：环境变量 SHUDONG_TOKEN
     · HTTP 传输：Authorization: Bearer <token>
4. MCP Server 校验 token
     · 解析出 user_id（实名，服务端内部可见）
     · 解析出 anonymous_id（前台展示用）
5. 所有工具调用都挂在这个身份下
```

> **已确认**：MVP 采用 Bearer token，不做完整 OAuth。
> OAuth（SEP-985 / 991 / 1046）留到对外开放第三方智能体时再引入。

### 2.3 匿名 ID 策略（✅ 已确认）

| 项 | 规则 |
| --- | --- |
| 生成 | 与 `user_id` 分离，随机生成 |
| **轮换** | **每次会话结束自动轮换**；且**最长 7 天强制轮换** |
| 存储 | 仅存当前映射（`user_id ↔ anonymous_id`），**不存历史匿名 ID** |
| 目的 | 防止跨会话关联同一用户 |

> **「每次会话轮换」比「定期轮换」更强**：即使两次会话内容都被泄露，
> 也无法通过匿名 ID 证明出自同一人。
>
> **实现要点**：轮换发生在 **`end_session` 的结束缓冲完成之后**（而非会话开始时），
> 保证会话期间双方看到的匿名 ID 稳定，会话一结束即失效。

### 2.4 会话上下文注入

MCP Server 在每次工具调用时构造 `CallContext`，业务层不接触 token：

```
CallContext {
  userId: string          // 实名，服务端内部
  anonymousId: string     // 前台展示
  role: "seeker" | "listener" | "both"
  sessionId?: string      // 当前会话（若有）
}
```

---

## 3. 工具清单（总览）

### 3.1 入口工具（model 可见，带 widget）

用于**打开界面**，本身不含敏感内容。

| 工具 | 用途 | widget |
| --- | --- | --- |
| `open_publish` | 打开倾诉发布面板 | `ui://shudong/publish.html` |
| `get_active_signals` | 浏览树洞池（**仅摘要，无正文**） | `ui://shudong/signals.html` |
| `get_signal_detail` | 打开某条信号详情 | `ui://shudong/signal-detail.html` |
| `open_session` | 打开当前陪伴会话 | `ui://shudong/chat.html` |
| `get_my_state` | 查看我的状态卡 | `ui://shudong/state.html` |
| `get_support_resources` | 危机资源 | `ui://shudong/crisis.html` |

### 3.2 动作工具（含用户正文 → app-only）

**只能由 widget 调用，agent 的工具列表里看不到。** 这是「敏感内容不进 LLM」的实现方式。

| 工具 | 用途 |
| --- | --- |
| `publish_signal` | 发布倾诉（含正文） |
| `catch_signal` | 接住某人（含第一句话） |
| `send_message` | 陪伴中发消息（含正文） |
| `get_signal_detail_content` | 读取信号正文 |

### 3.3 动作工具（不含正文 → model 可调）

agent 可以直接调用，便于**无 widget 宿主降级**。

| 工具 | 用途 |
| --- | --- |
| `request_human` | 呼叫真人切换 |
| `request_extension` | 请求延时 15 分钟 |
| `respond_extension` | 同意 / 拒绝延时 |
| `end_session` | 结束陪伴 |
| `report` | 举报 |
| `cancel_signal` | 撤回我的信号 |

### 3.4 系统内部（**不暴露为 MCP 工具**）

由服务端自动触发，不是 agent 能力：

| 内部动作 | 触发 |
| --- | --- |
| `ai_catch_signal` | 发布后 N 秒无真人接住 |
| `switch_to_human` | 真人匹配成功 |
| `release_expired` | 等待窗口 / 会话超时 |

> **不暴露的理由**：这些是服务端策略，暴露会让 agent 有能力"伪造"AI 接住或跳过流程。
>
> **关于安全兜底**：这里**没有** `crisis_intervention` 这一项。
> 产品已把「危机识别」降级为「安全兜底」—— 只做规则匹配后递一张热线卡片，
> 不宣称识别能力，也就不产生持续履行的义务（见 `docs/审核与危机识别设计.md` §0.2、§2.4）。

### 3.5 分级总表

| 工具 | visibility | 含正文 | widget | 幂等键 |
| --- | --- | :---: | :---: | :---: |
| `open_publish` | `["model","app"]` | ❌ | ✅ | — |
| `get_active_signals` | `["model","app"]` | ❌ | ✅ | — |
| `get_signal_detail` | `["model","app"]` | ❌ | ✅ | — |
| `open_session` | `["model","app"]` | ❌ | ✅ | — |
| `get_my_state` | `["model","app"]` | ❌ | ✅ | — |
| `get_support_resources` | `["model","app"]` | ❌ | ✅ | — |
| **`publish_signal`** | **`["app"]`** | ✅ | — | ✅ |
| **`catch_signal`** | **`["app"]`** | ✅ | — | ✅ |
| **`send_message`** | **`["app"]`** | ✅ | — | ✅ |
| **`get_signal_detail_content`** | **`["app"]`** | ✅ | — | — |
| `request_human` | `["model","app"]` | ❌ | — | ✅ |
| `request_extension` | `["model","app"]` | ❌ | — | ✅ |
| `respond_extension` | `["model","app"]` | ❌ | — | ✅ |
| `end_session` | `["model","app"]` | ❌ | — | ✅ |
| `report` | `["model","app"]` | ❌ | — | ✅ |
| `cancel_signal` | `["model","app"]` | ❌ | — | ✅ |

---

## 4. 工具详规

### 4.0 统一约定

**返回信封**

```jsonc
// 成功
{ "ok": true, "data": { /* 工具特定 */ } }

// 失败
{ "ok": false, "error": { "code": "SIGNAL_ALREADY_CAUGHT", "message": "该信号已被接住" } }
```

**同时提供 `structuredContent`** 供 widget 渲染，并保证 `content`（文本）自给自足。

**幂等**：写操作接受 `idempotency_key`（客户端生成 UUID）。同 key 重复调用返回首次结果，不重复产生副作用。

---

### 4.1 `open_publish`

打开倾诉发布面板。

| 项 | 值 |
| --- | --- |
| visibility | `["model","app"]` |
| widget | `ui://shudong/publish.html` |

**参数**

```jsonc
{
  "type": "object",
  "properties": {
    "tags":   { "type": "array", "items": { "type": "string" },
                "description": "预选标签，可留空由用户填写" },
    "mode":   { "type": "string", "enum": ["broadcast", "companion"] }
  },
  "additionalProperties": false
}
```

**返回** `{ "ok": true, "data": { "quotaRemaining": 2 } }`

> **为什么只返回配额、不发布**：发布需要正文，正文必须由用户在 widget 内输入，
> 而不是从对话里带过来（见 §6.1）。

---

### 4.2 `publish_signal` ⚠️ app-only

**发布倾诉。含正文，只允许 widget 调用。**

| 项 | 值 |
| --- | --- |
| visibility | **`["app"]`** |
| 幂等 | ✅ `idempotency_key` |

**参数**

```jsonc
{
  "type": "object",
  "required": ["content", "mode", "idempotency_key"],
  "properties": {
    "content":         { "type": "string", "minLength": 1, "maxLength": 1000 },
    "tags":            { "type": "array", "items": { "type": "string" }, "maxItems": 3 },
    "mode":            { "type": "string", "enum": ["broadcast", "companion"] },
    "idempotency_key": { "type": "string" }
  },
  "additionalProperties": false
}
```

**返回**

```jsonc
{ "ok": true, "data": {
    "signalId": "sig_...",
    "status": "active",
    "expiresAt": "2026-09-21T14:00:00Z",
    "retention": "ephemeral"
} }
```

**错误**

| code | 场景 |
| --- | --- |
| `QUOTA_EXCEEDED` | 当日倾诉次数用尽 |
| `CONTENT_REJECTED` | 内容审核未通过 |

**成功返回里的可选安全兜底**

| 字段 | 出现条件 |
| --- | --- |
| `support` | 规则匹配到明显信号时返回 `{ "message", "resourceUri" }`，widget 展示热线卡片 |

> `support` **不是错误**，也不代表"我们识别出了什么" —— 它只是顺手递一张纸条，
> 不出现也**不代表**没有风险（见 §6.3）。

---

### 4.3 `get_my_state`

查看我作为倾诉者 / 倾听者的当前状态。**不含正文。**

| 项 | 值 |
| --- | --- |
| visibility | `["model","app"]` |
| widget | `ui://shudong/state.html` |

**参数**：无

**返回**

```jsonc
{ "ok": true, "data": {
    "asSeeker": {
      "signalId": "sig_...",
      "status": "ai_catching",              // active|ai_catching|caught|in_session|expired
      "waitingSeconds": 24,
      "companionType": "ai",                // ai|human|null
      "sessionId": null
    },
    "asListener": {
      "sessionId": "sess_...",
      "peerAnonymousId": "anon_7f3a",
      "remainingSeconds": 1334,
      "extensionUsed": 1
    },
    "quota": { "seekRemaining": 2, "listenRemaining": 4 }
  } }
```

> **`companionType` 是身份透明的数据基础** —— widget 必须据此显示「AI」标识。

---

### 4.4 `get_active_signals`

浏览树洞池。**只返回摘要，不含正文**（正文见 4.5）。

| 项 | 值 |
| --- | --- |
| visibility | `["model","app"]` |
| widget | `ui://shudong/signals.html` |

**参数**

```jsonc
{
  "type": "object",
  "properties": {
    "tags":   { "type": "array", "items": { "type": "string" } },
    "cursor": { "type": "string", "description": "分页游标，首页留空" },
    "limit":  { "type": "integer", "minimum": 1, "maximum": 20, "default": 10 }
  },
  "additionalProperties": false
}
```

**返回**

```jsonc
{ "ok": true, "data": {
    "items": [{
      "signalId": "sig_...",
      "tags": ["孤独","深夜"],
      "preview": "深夜了，还是睡不着…",     // ≤40 字摘要
      "charCount": 86,
      "waitingSeconds": 132,
      "listenerCount": 3,
      "mode": "companion"
    }],
    "nextCursor": "..."
  } }
```

> **分页是硬要求**：MCP 内联结果上限 64 KiB、内联高度 ≤640px（见 §5.4），每页最多 20 条。
> `preview` 是摘要而非全文 —— 既满足「决定要不要点开」，又不把完整正文送进 LLM。

---

### 4.5 `get_signal_detail` + `get_signal_detail_content`

拆成两个工具，是**隐私分级的关键**：

| 工具 | 返回 | visibility | 说明 |
| --- | --- | --- | --- |
| `get_signal_detail` | 元信息 + `resourceUri` | `["model","app"]` | **不含正文**，只负责挂载 widget |
| `get_signal_detail_content` | **正文** | **`["app"]`** | 由 widget 调用，正文只在 widget 内渲染 |

**`get_signal_detail` 参数**：`{ "signalId": "sig_..." }`
**返回**：`{ "ok": true, "data": { "signalId": "...", "tags": [...], "charCount": 86, "waitingSeconds": 132, "canCatch": true } }`

**`get_signal_detail_content` 参数**：`{ "signalId": "sig_..." }`
**返回**：`{ "ok": true, "data": { "content": "……" } }`

> **为什么拆开**：`_meta.ui.resourceUri` 是**单个**字符串，一个工具只能挂一个 widget。
> 入口工具负责挂载，app-only 工具负责取正文 —— 于是**正文永远不出现在 agent 上下文**。

---

### 4.6 `catch_signal` ⚠️ app-only

接住某人并发出第一句话。

| 项 | 值 |
| --- | --- |
| visibility | **`["app"]`** |
| 幂等 | ✅ |

**参数**

```jsonc
{
  "type": "object",
  "required": ["signalId", "firstMessage", "idempotency_key"],
  "properties": {
    "signalId":        { "type": "string" },
    "firstMessage":    { "type": "string", "minLength": 1, "maxLength": 500 },
    "idempotency_key": { "type": "string" }
  },
  "additionalProperties": false
}
```

**返回**

```jsonc
{ "ok": true, "data": {
    "sessionId": "sess_...",
    "state": "waiting",
    "acceptExpiresAt": "2026-09-21T13:33:00Z",   // 3 分钟等待窗口
    "roomResourceUri": "ui://shudong/chat.html"
} }
```

**错误**

| code | 场景 |
| --- | --- |
| `SIGNAL_ALREADY_CAUGHT` | 已被别人接住（先到先得） |
| `SIGNAL_EXPIRED` | 信号已过期 |
| `QUOTA_EXCEEDED` | 当日倾听次数用尽 |
| `SELF_CATCH_FORBIDDEN` | 不能接住自己的信号 |
| `MODE_NOT_CATCHABLE` | 该信号是 `broadcast`（只要被听到），**不提供接住** |
| `ALREADY_IN_LISTENER_SESSION` | 已在倾听会话中（同时最多 1 个） |

---

### 4.7 `open_session`

打开当前陪伴会话（widget 承载聊天）。

| 项 | 值 |
| --- | --- |
| visibility | `["model","app"]` |
| widget | `ui://shudong/chat.html` |

**参数**：`{ "sessionId": "sess_..." }`
**返回**：`{ "ok": true, "data": { "sessionId": "...", "state": "active", "companionType": "ai", "remainingSeconds": 1800, "extensionUsed": 0, "extensionLimit": 3 } }`

---

### 4.8 `send_message` ⚠️ app-only

陪伴中发送消息。**含正文。**

| 项 | 值 |
| --- | --- |
| visibility | **`["app"]`** |
| 幂等 | ✅ |

**参数**：`{ "sessionId", "content", "idempotency_key" }`
**返回**：`{ "ok": true, "data": { "messageId": "msg_...", "sentAt": "..." } }`

**错误**：`SESSION_ENDED` / `SESSION_IN_BUFFER`（结束缓冲期禁止发送） / `CONTENT_REJECTED`

---

### 4.9 `request_human`

AI 缓冲中呼叫真人，加入真人等待队列。

| 项 | 值 |
| --- | --- |
| visibility | `["model","app"]` |

**参数**：`{ "sessionId", "idempotency_key" }`
**返回**：`{ "ok": true, "data": { "queued": true, "queuePosition": 3 } }`

> AI 继续陪伴，真人接入后自动切换（`companionType` 变为 `human`）。

---

### 4.10 `request_extension` / `respond_extension`

| 工具 | 调用方 | 参数 | 返回 |
| --- | --- | --- | --- |
| `request_extension` | 倾诉者 | `{ sessionId, idempotency_key }` | `{ ok, data: { state: "pending", expiresInSeconds: 30 } }` |
| `respond_extension` | 倾听者 | `{ sessionId, accept: bool, idempotency_key }` | `{ ok, data: { state: "accepted"\|"declined", remainingSeconds } }` |

**规则**（服务端权威）：

- 每会话最多 3 次，每次 +15 分钟
- 倾听者 **30 秒未响应视为拒绝**，会话继续（不扣次数）
- 只有「同意」才扣减次数

**错误**：`EXTENSION_LIMIT_REACHED` / `NO_PENDING_REQUEST` / `NOT_YOUR_ROLE`

---

### 4.11 `end_session`

| 项 | 值 |
| --- | --- |
| visibility | `["model","app"]` |
| 幂等 | ✅ |

**参数**：`{ "sessionId", "idempotency_key" }`
**返回**：`{ "ok": true, "data": { "state": "buffer", "bufferSeconds": 10, "reportWindowOpen": true } }`

> 进入 **10 秒结束缓冲**：禁止新消息，保留举报入口。10 秒后房间销毁。
> 这是产品文档 12.2 的状态机在接口层的体现。

---

### 4.12 `report`

| 项 | 值 |
| --- | --- |
| visibility | `["model","app"]` |
| 幂等 | ✅ |

**参数**

```jsonc
{
  "type": "object",
  "required": ["sessionId", "reason", "idempotency_key"],
  "properties": {
    "sessionId": { "type": "string" },
    "reason": { "type": "string",
      "enum": ["harassment","sexual","fraud","self_harm","violence","other"] },
    "detail":  { "type": "string", "maxLength": 300 },
    "idempotency_key": { "type": "string" }
  },
  "additionalProperties": false
}
```

**返回**

```jsonc
{ "ok": true, "data": {
    "reportId": "rep_...",
    "sessionFrozen": true,
    "retention": "report_saved",
    "disclosure": "本次对话将保存至服务端，用于安全审核"
} }
```

> **`disclosure` 字段是硬要求**：产品文档 13.5 规定举报必须**当场告知用户内容将被保存**。
> widget 必须展示该字段，不得忽略。

---

### 4.13 `cancel_signal`

**参数**：`{ "signalId", "idempotency_key" }`
**返回**：`{ "ok": true, "data": { "quotaRefunded": true } }`

> 仅在无人接住时可撤回；已进入等待窗口则不可（返回 `SIGNAL_IN_SESSION`）。

---

### 4.14 `get_support_resources`

**危机资源。最通用、优先做** —— 任何 agent 都应能直接把它给到用户。

| 项 | 值 |
| --- | --- |
| visibility | `["model","app"]` |
| widget | `ui://shudong/crisis.html` |

**参数**：`{ "region": "string?" }`
**返回**

```jsonc
{ "ok": true, "data": {
    "resources": [
      { "name": "全国心理援助热线", "phone": "12356", "hours": "24h" },
      { "name": "青少年服务台",     "phone": "12355", "hours": "24h" },
      { "name": "紧急救助",         "phone": "110" },
      { "name": "急救",             "phone": "120" }
    ]
  } }
```

> 该工具**不依赖登录、不依赖会话**，应始终可调用。

---

## 5. UI 资源（`ui://`）

### 5.1 资源清单

| `ui://` URI | 用途 | 由哪个工具挂载 |
| --- | --- | --- |
| `ui://shudong/publish.html?v=1` | 倾诉发布面板 | `open_publish` |
| `ui://shudong/signals.html?v=1` | 树洞池列表 | `get_active_signals` |
| `ui://shudong/signal-detail.html?v=1` | 信号详情 + 接住 | `get_signal_detail` |
| `ui://shudong/chat.html?v=1` | 临时陪伴聊天 | `open_session` |
| `ui://shudong/state.html?v=1` | 我的状态卡 | `get_my_state` |
| `ui://shudong/crisis.html?v=1` | 危机资源 | `get_support_resources` |

> URI 常量定义在 `ZLongChat.Contracts.Shundong.ShundongUiUris`，C# 与文档同名同值。

**实现状态**：骨架阶段这 6 个资源返回的是**占位 HTML**（只为打通"宿主请求资源 → 返回 HTML"链路）。
真实界面在阶段 1 用前端工程产出后替换，**URI 与版本号不变**。

### 5.2 资源声明

```jsonc
// tools/list —— 工具声明 widget
{
  "name": "get_active_signals",
  "description": "浏览树洞池：看看有谁正在等待被听见。",
  "inputSchema": { /* … */ },
  "_meta": {
    "ui": {
      "resourceUri": "ui://shudong/signals.html?v=1",
      "visibility": ["model", "app"]
    }
  }
}
```

```jsonc
// resources/read 响应
{
  "contents": [{
    "uri": "ui://shudong/signals.html?v=1",
    "mimeType": "text/html;profile=mcp-app",
    "text": "<!doctype html>…",
    "_meta": {
      "ui": {
        // ⚠️ 阶段 1 交付：骨架阶段未设置，宿主会用最严格的默认策略（无外部连接权限）。
        //    等 widget 真的要调 tools/call 时再逐条放开，不要提前放宽。
        "csp": { "connectDomains": ["https://shudong.example.com"] },
        "permissions": { "clipboardWrite": {} }
      }
    }
  }]
}
```

### 5.3 资源设计约束

| 约束 | 要求 |
| --- | --- |
| 自包含 | 内联脚本样式；外部资源用 `data:` URL |
| **内容寻址** | URI 带版本号（`?v=1`），同一版本永远同一份 HTML |
| **读宿主上下文** | 颜色/字体/圆角/语言/时区来自 host context，**不硬编码** |
| 尺寸 | 内联 200–720px 宽、≤640px 高，用 `fullscreen` 模式承载长内容 |
| **不回传正文** | widget 内渲染的正文**不得**通过 `tools/call` 回传（除 `send_message`） |
| 不可信输入 | 倾诉正文按不可信输入处理（防 XSS） |

### 5.4 分页与体积

| 限制 | 应对 |
| --- | --- |
| 内联结果 ≤ 64 KiB | 每页 ≤ 20 条，正文按需拉取 |
| 内联高度 ≤ 640px | 列表虚拟滚动；长文用 `fullscreen` |
| 回调 ≤ 60 次/分 | 分页 + 接住等均为**点击触发**，天然满足 |

---

## 6. 隐私分级与降级

### 6.1 威胁模型（为什么这么设计）

| 场景 | 内容是否已在 LLM 上下文 | 本设计如何处理 |
| --- | --- | --- |
| 用户在对话里说「帮我发个树洞：我最近很累」 | ✅ **已经泄露了** | ⚠️ 无法挽回，只能**事前引导**（见 6.2） |
| 用户点开面板、在 widget 内打字 | ❌ 未泄露 | ✅ `publish_signal` 为 app-only，内容直达服务端 |
| 倾听者通过 agent 读到正文 | ❌ 未泄露 | ✅ 正文走 `get_signal_detail_content`（app-only） |
| 陪伴中双方发消息 | ❌ 未泄露 | ✅ `send_message` 为 app-only |

> **关键认知**：`visibility:["app"]` **不能挽回已经说出口的内容**。
> 它的价值在于**给出一条完全不经过 LLM 的路径**，并用 Skill 把用户引导到那条路径上。

### 6.2 Skill 引导（必须与工具契约同步交付）

```markdown
## 绝对禁止
- **不要让用户把倾诉内容直接说给你听**。
  正确做法：调用 open_publish 打开面板，让用户自己在面板里写。
- 不要复述、总结或转述用户的面板内容。
```

### 6.3 留存例外与安全兜底的告知

**内容留存的唯一例外是举报**（透明告知原则）。常规对话即焚，只有举报会保存。

| 触发 | 返回字段 | 要求 |
| --- | --- | --- |
| 举报提交 | `ReportResult.Disclosure` | widget **必须**展示 |
| 规则命中安全兜底 | `PublishResult.Support` | widget **建议**展示（不是强制，也不代表风险结论） |

告知文案（与产品文档 18.1 一致）：

- 举报：「举报已提交。本次对话将保存至服务端，用于安全审核。」
- 兜底卡片：「如果你现在很难受，这里有一些随时可以打的电话。你不需要一个人扛。」

> ⚠️ **不要写成"我们检测到你有风险"**。
> 产品不做危机识别 —— 规则命中只是"顺手递一张纸条"，
> 没命中**也不代表**没有风险。措辞一旦变成声称，就产生了持续履行的义务
> （见 `docs/审核与危机识别设计.md` §0.2、§2.4）。

### 6.4 降级矩阵

| 宿主能力 | 行为 |
| --- | --- |
| 支持 widget | **主路径**：内容在 widget 内输入与渲染，不进 LLM |
| 不支持 widget | 工具返回**结构化摘要 + 深链**，引导到 Web 应用完成（内容同样不进 LLM） |
| 模型未调用工具 | Skill 提高调用率；Web 入口始终可用 |

> **任何情况下，功能都不能失效。** 工具返回必须自给自足（原则 2）。

---

## 7. 与现有服务的映射

| MCP 工具 | 调用 | 现状 |
| --- | --- | --- |
| `publish_signal` | `ISignalBus.Publish` + `ICrisisDetector` + `IQuotaService` + `IContentModerator` | 新增 |
| `get_active_signals` | `ISignalBus.Query` | 新增 |
| `get_signal_detail(_content)` | `ISignalBus.Get` | 新增 |
| `catch_signal` | `IMatchingService.Catch` + `IRoomStore.Create` | 新增 |
| `open_session` | `IRoomStore.Get` | 新增 |
| `send_message` | **`ChatHub` 现有转发链路** | ✅ **复用** |
| `request_human` | `IMatchingService.EnqueueHuman` | 新增 |
| `request/respond_extension` | `IRoomStore.RequestExtension/Respond` | 新增 |
| `end_session` | `IRoomStore.BeginBuffer` | 新增 |
| `report` | `IReportStore.Create` + `IRetentionService.Mark` | 新增 |
| `get_my_state` | `ISignalBus` + `IRoomStore` + `IQuotaService` | 新增 |
| `get_support_resources` | 静态配置 | 新增（最简） |
| AI 秒接（内部） | **`ZLongChat.AiBot`** | ✅ **复用** |
| 鉴权 | token → `CallContext` | 新增 |

**可复用比例**：消息链路与 AI 缓冲两块直接复用现有实现，其余为新增服务。

---

## 8. 错误码与限流

### 8.1 错误码表

| code | HTTP 类比 | 说明 |
| --- | --- | --- |
| `UNAUTHORIZED` | 401 | token 无效 / 过期 |
| `FORBIDDEN` | 403 | 无权限（如接住自己的信号） |
| `NOT_FOUND` | 404 | 信号 / 会话不存在 |
| `QUOTA_EXCEEDED` | 429 | 限次用尽 |
| `RATE_LIMITED` | 429 | 调用过频 |
| `SIGNAL_ALREADY_CAUGHT` | 409 | 已被接住 |
| `SIGNAL_EXPIRED` | 410 | 已过期 |
| `SIGNAL_IN_SESSION` | 409 | 已进入会话，不可撤回 |
| `SESSION_ENDED` | 410 | 会话已结束 |
| `SESSION_IN_BUFFER` | 409 | 结束缓冲期，禁止发送 |
| `SELF_CATCH_FORBIDDEN` | 403 | 不能接住自己 |
| `MODE_NOT_CATCHABLE` | 403 | 该信号是 `broadcast`，不提供接住 |
| `ALREADY_IN_LISTENER_SESSION` | 409 | 同时在听上限 |
| `EXTENSION_LIMIT_REACHED` | 409 | 延时次数用尽 |
| `NO_PENDING_REQUEST` | 404 | 无待响应的延时请求 |
| `NOT_YOUR_ROLE` | 403 | 角色不符（如倾听者发起延时） |
| `CONTENT_REJECTED` | 422 | 内容审核未通过 |
| `VALIDATION_ERROR` | 400 | 参数校验失败 |

> 表中**没有** `CRISIS_DETECTED`。产品不做危机识别，只做安全兜底：
> 命中规则时在**成功返回**里带一个 `support` 提示（见 §4.2、§6.3），不是错误码。

### 8.2 限流

| 维度 | 限制 | 来源 |
| --- | --- | --- |
| 倾诉 | 3 次 / 日 | `IQuotaService` |
| 倾听 | 5 次 / 日 | `IQuotaService` |
| 列表查询 | 30 次 / 分 | MCP 层 |
| 其他写操作 | 20 次 / 分 | MCP 层 |
| widget 回调 | 60 次 / 分 | **宿主限制**（非我们控制） |

---

## 9. 版本与兼容

| 项 | 策略 |
| --- | --- |
| 契约版本 | 工具与资源 URI 均带版本（`ui://…?v=1`） |
| 破坏性变更 | 升版本号 + 保留旧资源至少一个版本周期 |
| **契约测试** | 工具 schema 与返回结构做**快照测试**，纳入 CI |
| 前后兼容 | 新增可选字段视为兼容；删除/改义必须升版本 |

**快照已经落地**，不是待办：

```powershell
# 需要先启动后端：dotnet ZLongChat.API\bin\Debug\net10.0\ZLongChat.API.dll
.\tools\mcp-snapshot.ps1                    # 比对，漂移则退出码 1
.\tools\mcp-snapshot.ps1 -UpdateSnapshot    # 只在**有意**改契约时更新
```

快照内容：`docs/contracts/mcp-manifest.json`（16 个工具的 name / inputSchema / annotations / `_meta.ui` + 6 个 UI 资源的 uri / mimeType）。
工具的 `description` **不进快照** —— 文案调整不该让契约测试失败；但工具名、参数、必填项、`_meta.ui` 改动必须失败。

---

## 10. 决策记录

### 10.1 已决事项

| # | 问题 | **决定** | 影响 |
| --- | --- | --- | --- |
| Q1 | MCP 鉴权方案 | ✅ **Bearer token**（env / header），不做完整 OAuth | §2.2 落地 |
| Q2 | 匿名 ID 轮换周期 | ✅ **每次会话结束自动轮换** + 最长 7 天强制 | `IAnonymousIdentity` |
| Q5 | 倾诉正文长度上限 | ✅ **1000 字** | §4.2 schema、UI、审核 |
| Q6 | AI 缓冲是否暴露为工具参数 | ✅ **不暴露** —— AI 仅在状态 3 无真人时由服务端触发 | §4.2 |
| Q7 | 多设备同时在线的会话归属 | ✅ **后登录踢前者** | `IUserConnectionRegistry` |

### 10.2 转设计文档讨论

| # | 问题 | 去向 |
| --- | --- | --- |
| Q3 | 内容审核：AI 还是人工？每条都要审吗？ | → `docs/审核与危机识别设计.md` |
| Q4 | 危机识别放端侧还是服务端？ | → `docs/审核与危机识别设计.md` |

> Q3 / Q4 已不在接口层，而是**服务端服务的设计问题**，
> 因此抽到独立设计文档讨论，本契约只保留接口侧约定：
> `publish_signal` 在同步审核失败时返回 `CONTENT_REJECTED`；
> 规则命中安全兜底时在成功返回里带 `support`（均为 §4.2 已定义行为）。

---

## 11. 骨架落地状态

> 本节记录「契约写完」与「代码真的这么跑」之间的差。契约是承诺，这张表是证据。

### 11.1 已落地（`ZLongChat.Mcp`）

| 项 | 实现 | 验证方式 |
| --- | --- | --- |
| 16 个工具全部注册 | `Tools/SignalTools.cs`、`CompanionTools.cs`、`SafetyTools.cs` | `tools\mcp-probe.ps1` 断言 16 |
| 6 个 UI 资源 | `Resources/ShudongUiResources.cs`（占位 HTML） | `tools\mcp-probe.ps1` 逐个 `resources/read` |
| `_meta.ui` 可见性分级 | `[McpMeta("ui", JsonValue = …)]` | 快照 + `mcp-probe.ps1` 断言 4 个 app-only |
| Bearer 鉴权 | `CallContextAccessor`（header 优先，`SHUDONG_TOKEN` 兜底） | `mcp-probe.ps1` 无令牌 / 伪造令牌均被拒 |
| 错误码结构化 | `CallToolFilters` → `ShudongErrorPayload` | `mcp-scenario.ps1` 断言 `MODE_NOT_CATCHABLE` 等 |
| `broadcast` 不可接住 | `InMemorySessionService.CatchAsync` 硬校验 | `mcp-scenario.ps1` 第 10 项 |
| 正文不进 LLM | `get_signal_detail` 无正文；正文走 app-only 工具 | `mcp-scenario.ps1` 第 4、5 项 |
| 举报留存告知 | `ReportResult.Disclosure` | `mcp-scenario.ps1` 第 9 项 |
| 安全兜底卡片 | 规则命中 → `PublishResult.Support` | `mcp-scenario.ps1` 第 13 项 |
| 契约快照 | `docs/contracts/mcp-manifest.json` | `tools\mcp-snapshot.ps1` |

### 11.2 明确未落地（不要当成已完成）

| 项 | 现状 | 计划 |
| --- | --- | --- |
| 真实 widget 界面 | 6 个占位 HTML | 阶段 1 前端工程替换，URI 不变 |
| `_meta.ui.csp` / `permissions` | 未设置（宿主用最严格默认） | widget 真正需要调 `tools/call` 时逐条放开 |
| 内容审核 L1 同步 / L2 异步 | 未接入，`CONTENT_REJECTED` 只定义未触发 | 见 `docs/审核与危机识别设计.md` §1 |
| 配额与限流（§8.2） | 未实现，`get_my_state` 返回的是占位配额 | 阶段 2 |
| 匿名 ID 轮换（Q2） | 接口就位（`IIdentityService.RotateAnonymousId`），未接到会话结束事件 | 阶段 2 |
| 持久化 | 全是进程内内存实现，重启即丢 | 阶段 2 换 Redis / PostgreSQL |

### 11.3 怎么跑这三块自检

```powershell
# 1) 起后端
dotnet ZLongChat.API\bin\Debug\net10.0\ZLongChat.API.dll

# 2) 三块自检（各自退出码非 0 即失败）
.\tools\mcp-probe.ps1       # 结构：工具数、_meta.ui、资源、鉴权
.\tools\mcp-scenario.ps1    # 行为：13 个业务不变量
.\tools\mcp-snapshot.ps1    # 契约：与快照比对
```

---

## 12. 变更记录

| 版本 | 变更 |
| --- | --- |
| v1.0 | 首版：16 个工具 + 6 个 UI 资源 + 隐私分级 + 降级矩阵 + 与现有服务映射 |
| v1.1 | 骨架落地：16 工具与 6 资源已可调用；错误码结构化（`ShudongErrorPayload`）；<br>移除 `CRISIS_DETECTED` 错误码，改为 `PublishResult.Support` 兜底提示；<br>UI 资源 URI 补 `?v=1`；新增 §11 骨架落地状态 |
