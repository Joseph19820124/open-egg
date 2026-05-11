# openab Cron → Claude → Discord 时序图

本文档描述 openab 定时任务（cron）如何通过 ACP 协议驱动 Claude Code 并将结果推送到 Discord。

## 架构概览

```
Discord Thread ← openab (Rust) ↔ claude-agent-acp (ACP/stdio) ↔ Claude Code
```

- **openab**：唯一连接 Discord 的进程，负责消息的收发和路由
- **claude-agent-acp**：ACP 适配器，将 openab 的 JSON-RPC 消息转换为 Claude Code 的 stdin/stdout 协议
- **Claude Code**：实际的 AI agent，通过标准输入输出与外界通信，不直接接触 Discord

## 时序图

```
┌──────────┐    ┌─────────┐    ┌───────────────────┐    ┌─────────┐    ┌─────────┐
│  Cron    │    │  openab │    │ claude-agent-acp  │    │  Claude │    │ Discord │
│Scheduler │    │(Rust)   │    │  (Node.js ACP)    │    │  Code   │    │  API    │
└────┬─────┘    └────┬────┘    └────────┬──────────┘    └────┬────┘    └────┬────┘
     │               │                  │                     │              │
  整点触发            │                  │                     │              │
     │──fire()──────►│                  │                     │              │
     │               │                  │                     │              │
     │               │  1. 发触发消息到 Discord thread         │              │
     │               │─────────────────────────────────────────────────────►│
     │               │     "🕐 [openab-cron]: <prompt>"       │              │
     │               │◄────────────────────────────────────────────────────-│
     │               │     返回 message_id                    │              │
     │               │                  │                     │              │
     │               │  2. 启动 ACP 子进程                    │              │
     │               │──spawn stdio────►│                     │              │
     │               │                  │                     │              │
     │               │  3. ACP 握手 (JSON-RPC)                │              │
     │               │◄────────────────►│                     │              │
     │               │  session/create  │                     │              │
     │               │                  │                     │              │
     │               │  4. 发送 prompt (JSON-RPC)             │              │
     │               │──message/send───►│                     │              │
     │               │                  │                     │              │
     │               │                  │  5. 调用 Claude     │              │
     │               │                  │────claude --acp────►│              │
     │               │                  │   (stream-json)     │              │
     │               │                  │                     │              │
     │               │                  │  6. Claude 生成回复 │              │
     │               │                  │◄────── "收到" ──────│              │
     │               │                  │                     │              │
     │               │  7. ACP 返回结果                       │              │
     │               │◄──message/done──-│                     │              │
     │               │                  │                     │              │
     │               │  8. 写入 Discord thread                │              │
     │               │─────────────────────────────────────────────────────►│
     │               │     edit/reply: "收到"                 │              │
     │               │                  │                     │              │
┌────┴─────┐    ┌────┴────┐    ┌────────┴──────────┐    ┌────┴────┐    ┌────┴────┐
│  Cron    │    │  openab │    │ claude-agent-acp  │    │  Claude │    │ Discord │
└──────────┘    └─────────┘    └───────────────────┘    └─────────┘    └─────────┘
```

## 关键设计要点

| 要点 | 说明 |
|------|------|
| Claude 不直接连 Discord | Claude 只通过 ACP (stdio JSON-RPC) 收发文字 |
| openab 是唯一的 Discord 进程 | 所有 Discord API 调用都经过 openab |
| 🕐 触发消息与 ACP 层独立 | Discord thread 里的触发消息是 openab 自己发的通知，Claude 不感知 |
| Claude 视角与终端对话相同 | Claude 收到的 prompt 和本地 `claude` 命令行对话一模一样 |
| ACP 协议 | [Agent Client Protocol](https://github.com/agentclientprotocol) — JSON-RPC over stdio |

## 相关配置

`cronjob.toml` 示例：

```toml
[[jobs]]
schedule = "0 * * * *"          # 每小时整点
channel = "1502842156048715836"  # Discord thread ID
thread_id = "1502842156048715836"
message = "这是每小时测试消息，请回复「收到」。"
platform = "discord"
sender_name = "openab-cron"
timezone = "UTC"
```
