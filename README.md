# OpenEgg

OpenEgg is a small .NET bridge for:

```text
Discord -> OpenEgg -> Codex ACP harness -> Discord progress
```

It is intentionally narrower than `openabdev/openab`: only Discord ingress, a thread/channel keyed ACP session pool, and a Codex-compatible ACP subprocess over stdio JSON-RPC are included.

## Features

- Discord bot message listener using `Discord.Net`.
- Optional channel allowlist.
- Mention-gated top-level channel prompts, with follow-up messages allowed inside Discord threads.
- One ACP session per Discord channel/thread id.
- `initialize`, `session/new`, and streamed `session/prompt` JSON-RPC calls.
- ACP progress classification for text, thinking, and tool updates.
- Throttled Discord progress message edits.
- Long Discord replies split into safe chunks.
- Idle ACP session cleanup.

## Configuration

Copy `appsettings.example.json` to `appsettings.json` or use environment variables.

```json
{
  "Discord": {
    "BotToken": "${DISCORD_BOT_TOKEN}",
    "AllowedChannels": [],
    "RequireMentionOutsideThreads": true
  },
  "Agent": {
    "Command": "codex-acp",
    "Arguments": [],
    "WorkingDirectory": "."
  }
}
```

For Discord, enable Message Content Intent in the Discord Developer Portal and invite the bot with permission to read/send messages.

## Run

```bash
dotnet run --project src/OpenEgg/OpenEgg.csproj
```

Then mention the bot in an allowed channel:

```text
@OpenEgg inspect this repository and suggest the next coding step
```

Messages posted inside a Discord thread continue on that thread's ACP session without requiring another mention.

## Tests

```bash
dotnet test
```

## Notes

OpenEgg auto-selects permissive ACP permission responses so Codex can perform coding actions. Run it only in a workspace and Discord server where that behavior is acceptable.
