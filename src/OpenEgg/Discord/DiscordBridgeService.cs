using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenEgg.Acp;
using OpenEgg.Configuration;

namespace OpenEgg.Discord;

public sealed partial class DiscordBridgeService : BackgroundService
{
    private readonly DiscordSocketClient _client;
    private readonly AcpSessionPool _pool;
    private readonly DiscordOptions _options;
    private readonly ILogger<DiscordBridgeService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks = new();
    private Timer? _cleanupTimer;

    public DiscordBridgeService(
        DiscordSocketClient client,
        AcpSessionPool pool,
        IOptions<DiscordOptions> options,
        ILogger<DiscordBridgeService> logger)
    {
        _client = client;
        _pool = pool;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var token = ExpandEnvironment(_options.BotToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            token = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Discord:BotToken is required. Set it in appsettings or DISCORD_BOT_TOKEN.");
        }

        _client.Log += LogDiscordAsync;
        _client.Ready += OnReadyAsync;
        _client.MessageReceived += OnMessageReceivedAsync;

        _cleanupTimer = new Timer(
            _ => _ = Task.Run(() => _pool.CleanupIdleAsync(), stoppingToken),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await _client.StopAsync();
            await _client.LogoutAsync();
        }
    }

    public override void Dispose()
    {
        _cleanupTimer?.Dispose();
        _client.MessageReceived -= OnMessageReceivedAsync;
        _client.Ready -= OnReadyAsync;
        _client.Log -= LogDiscordAsync;
        base.Dispose();
    }

    private Task OnReadyAsync()
    {
        _logger.LogInformation("Discord bot connected as {User}.", _client.CurrentUser);
        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(SocketMessage socketMessage)
    {
        if (socketMessage is not SocketUserMessage message || message.Author.IsBot)
        {
            return;
        }

        if (!IsAllowedChannel(message))
        {
            return;
        }

        var inThread = message.Channel is SocketThreadChannel;
        var mentioned = _client.CurrentUser is not null &&
            message.MentionedUsers.Any(user => user.Id == _client.CurrentUser.Id);

        if (_options.RequireMentionOutsideThreads && !inThread && !mentioned)
        {
            return;
        }

        var content = CleanMention(message.Content, _client.CurrentUser?.Id);
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var sessionKey = message.Channel.Id.ToString();
        var sessionLock = _sessionLocks.GetOrAdd(sessionKey, _ => new SemaphoreSlim(1, 1));
        await sessionLock.WaitAsync();
        try
        {
            await HandlePromptAsync(message, sessionKey, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process Discord message {MessageId}.", message.Id);
            await message.Channel.SendMessageAsync($"OpenEgg failed: `{ex.Message}`");
        }
        finally
        {
            sessionLock.Release();
        }
    }

    private async Task HandlePromptAsync(SocketUserMessage message, string sessionKey, string content)
    {
        var progress = await message.Channel.SendMessageAsync("OpenEgg received the request. Starting Codex ACP...");
        var prompt = PromptBuilder.WithSenderContext(
            content,
            message.Author.Id.ToString(),
            message.Author.Username,
            message.Channel.Id.ToString(),
            sessionKey,
            message.Id.ToString());

        using var cts = new CancellationTokenSource();
        var connection = await _pool.GetOrCreateAsync(sessionKey, cts.Token);
        var text = new StringBuilder();
        var tools = new Dictionary<string, string>();
        var lastEdit = DateTimeOffset.MinValue;

        await foreach (var rpcMessage in connection.PromptAsync(prompt, cts.Token))
        {
            var acpEvent = AcpEventClassifier.Classify(rpcMessage);
            switch (acpEvent)
            {
                case AcpEvent.Text textEvent:
                    text.Append(textEvent.Value);
                    break;
                case AcpEvent.Thinking:
                    tools["thinking"] = "[running] thinking";
                    break;
                case AcpEvent.Tool tool:
                    if (!string.IsNullOrWhiteSpace(tool.Title))
                    {
                        var status = tool.Status is "completed" ? "done" :
                            tool.Status is "failed" ? "failed" :
                            "running";
                        tools[string.IsNullOrWhiteSpace(tool.Id) ? tool.Title : tool.Id] = $"[{status}] {tool.Title}";
                    }
                    break;
            }

            if (DateTimeOffset.UtcNow - lastEdit >= TimeSpan.FromSeconds(_options.ProgressEditIntervalSeconds))
            {
                lastEdit = DateTimeOffset.UtcNow;
                await EditProgressAsync(progress, tools.Values, text.ToString(), final: false);
            }
        }

        await SendFinalAsync(message.Channel, progress, tools.Values, text.ToString());
    }

    private async Task EditProgressAsync(IUserMessage progress, IEnumerable<string> tools, string text, bool final)
    {
        var body = BuildDisplay(tools, text, final);
        var chunks = TextChunker.Chunk(body, _options.MessageChunkLimit);
        await progress.ModifyAsync(properties => properties.Content = chunks[0]);
    }

    private async Task SendFinalAsync(
        ISocketMessageChannel channel,
        IUserMessage progress,
        IEnumerable<string> tools,
        string text)
    {
        var body = BuildDisplay(tools, text, final: true);
        var chunks = TextChunker.Chunk(body, _options.MessageChunkLimit)
            .Where(chunk => !string.IsNullOrWhiteSpace(chunk))
            .ToList();

        if (chunks.Count == 0)
        {
            chunks.Add("Done.");
        }

        await progress.ModifyAsync(properties => properties.Content = chunks[0]);
        foreach (var chunk in chunks.Skip(1))
        {
            await channel.SendMessageAsync(chunk);
        }
    }

    private bool IsAllowedChannel(SocketUserMessage message)
    {
        if (_options.AllowedChannels.Count == 0)
        {
            return true;
        }

        if (_options.AllowedChannels.Contains(message.Channel.Id))
        {
            return true;
        }

        return message.Channel is SocketThreadChannel thread &&
            thread.ParentChannel is not null &&
            _options.AllowedChannels.Contains(thread.ParentChannel.Id);
    }

    private static string BuildDisplay(IEnumerable<string> tools, string text, bool final)
    {
        var builder = new StringBuilder();
        var toolLines = tools.Where(tool => !string.IsNullOrWhiteSpace(tool)).ToList();
        if (toolLines.Count > 0)
        {
            builder.AppendLine(string.Join('\n', toolLines));
            builder.AppendLine();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            builder.Append(final ? "Done." : "Working...");
        }
        else
        {
            builder.Append(text);
        }

        return builder.ToString();
    }

    private static string CleanMention(string content, ulong? currentUserId)
    {
        if (currentUserId is null)
        {
            return content.Trim();
        }

        return MentionRegex(currentUserId.Value).Replace(content, string.Empty).Trim();
    }

    private Task LogDiscordAsync(LogMessage message)
    {
        var level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Trace,
            LogSeverity.Debug => LogLevel.Debug,
            _ => LogLevel.Information
        };

        _logger.Log(level, message.Exception, "{Source}: {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }

    private static Regex MentionRegex(ulong userId)
    {
        return new Regex($@"<@!?{userId}>", RegexOptions.Compiled);
    }

    private static string ExpandEnvironment(string value)
    {
        if (value.StartsWith("${", StringComparison.Ordinal) &&
            value.EndsWith("}", StringComparison.Ordinal) &&
            value.Length > 3)
        {
            return Environment.GetEnvironmentVariable(value[2..^1]) ?? string.Empty;
        }

        return Environment.ExpandEnvironmentVariables(value);
    }
}
