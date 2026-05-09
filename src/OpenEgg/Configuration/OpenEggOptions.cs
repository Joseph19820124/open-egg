namespace OpenEgg.Configuration;

public sealed class DiscordOptions
{
    public string BotToken { get; set; } = string.Empty;

    public List<ulong> AllowedChannels { get; set; } = [];

    public bool AllowDirectMessages { get; set; } = true;

    public bool RequireMentionOutsideThreads { get; set; } = true;

    public double ProgressEditIntervalSeconds { get; set; } = 1.5;

    public int MessageChunkLimit { get; set; } = 1900;
}

public sealed class AgentOptions
{
    public string Command { get; set; } = "codex-acp";

    public List<string> Arguments { get; set; } = ["-c", "model=\"gpt-5.4\""];

    public string WorkingDirectory { get; set; } = ".";

    public Dictionary<string, string> Environment { get; set; } = [];
}

public sealed class PoolOptions
{
    public int MaxSessions { get; set; } = 8;

    public int SessionTtlMinutes { get; set; } = 240;
}
