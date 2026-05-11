# OpenClaw vs Hermes Agent — 能力对比调研

## 简介

| | OpenClaw | Hermes Agent |
|--|----------|--------------|
| **开发者** | Peter Steinberger（PSPDFKit 创始人） | NousResearch |
| **定位** | 消息网关优先的自主 AI Agent | Agent 学习循环优先的自主 AI Agent |
| **核心理念** | 把 AI 模型包装在消息平台 + 本地系统网关上 | 每次执行任务后提炼经验、积累可复用技能，越用越聪明 |

---

## 10 项共同能力

| # | 共同能力 | OpenClaw | Hermes Agent |
|---|----------|----------|--------------|
| **1** | **持久化本地记忆（Markdown）** | `MEMORY.md` + 每日笔记 | `MEMORY.md` + `USER.md`，支持全文检索 |
| **2** | **自主多步 Agent 循环** | 连续执行，无需每步确认 | 执行→评估→提取→精炼的学习循环 |
| **3** | **多平台消息集成** | 支持 20+ 平台（WhatsApp、Discord、Slack、Telegram 等） | 支持 14+ 平台（Telegram、Discord、Slack、WhatsApp 等） |
| **4** | **网页浏览与浏览器自动化** | 导航、填表、数据抓取 | 点击、截图、内容提取，支持视觉分析 |
| **5** | **定时任务 / Cron 调度** | 支持 Cron 任务，可 24/7 无人值守 | 自然语言描述定时任务（如"每天早8点"） |
| **6** | **技能扩展系统** | 100+ 预置 AgentSkills（Markdown 文件格式） | 自动创建可复用的程序化技能（`skill_manage` 工具） |
| **7** | **Shell 与代码执行** | 执行 Shell 命令、运行脚本 | 每个子 Agent 独立终端 + Python RPC 脚本 |
| **8** | **多模型 / 多后端支持** | 支持 OpenAI、Anthropic 等多家，带故障转移 | 支持 200+ 模型（OpenRouter、NVIDIA NIM、Hugging Face 等） |
| **9** | **隐私优先 / 自托管部署** | 数据本地存储，支持 Docker 隔离 | 完全自托管，支持 Docker / SSH / Modal 等多种后端 |
| **10** | **MCP Server 支持** | 专用 MCP 桥接（含 OAuth2），可供 Claude.ai 调度 | 原生 MCP 客户端，可连接任意 MCP Server |

---

## 两者的核心差异

- **OpenClaw** — **消息网关优先**：把 AI 模型包装在消息平台+本地系统网关上，适合需要跨平台"随时可达"的个人助手场景。
- **Hermes Agent** — **Agent 学习循环优先**：每次执行任务后会提炼经验、积累可复用技能，越用越聪明，适合需要重复执行复杂工作流的场景。

两者互补性很强，社区中已有不少用户将 **OpenClaw 作编排调度层、Hermes 作执行专家层**配合使用。

---

## 参考资料

- [OpenClaw 官网](https://openclaw.ai/)
- [OpenClaw GitHub](https://github.com/openclaw/openclaw)
- [Hermes Agent 官网](https://hermes-agent.nousresearch.com/)
- [Hermes Agent GitHub](https://github.com/nousresearch/hermes-agent)
- [Hermes Agent vs OpenClaw 深度对比](https://screenshotone.com/blog/hermes-agent-versus-openclaw/)
