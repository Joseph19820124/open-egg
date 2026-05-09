using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using OpenEgg.Configuration;

namespace OpenEgg.Acp;

public sealed class AcpConnection : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly ILogger<AcpConnection> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<JsonRpcMessage>> _pending = new();
    private readonly object _subscriberGate = new();
    private Channel<JsonRpcMessage>? _subscriber;
    private ulong _nextId;
    private bool _disposed;

    private AcpConnection(Process process, ILogger<AcpConnection> logger)
    {
        _process = process;
        _stdin = process.StandardInput;
        _logger = logger;
        _ = Task.Run(ReadStdoutLoopAsync);
        _ = Task.Run(ReadStderrLoopAsync);
    }

    public string? SessionId { get; private set; }

    public DateTimeOffset LastActiveUtc { get; private set; } = DateTimeOffset.UtcNow;

    public static async Task<AcpConnection> SpawnAsync(
        AgentOptions options,
        ILogger<AcpConnection> logger,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.Command,
            WorkingDirectory = Path.GetFullPath(ExpandEnvironment(options.WorkingDirectory)),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in options.Arguments)
        {
            startInfo.ArgumentList.Add(ExpandEnvironment(argument));
        }

        foreach (var (key, value) in options.Environment)
        {
            startInfo.Environment[key] = ExpandEnvironment(value);
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start ACP command '{options.Command}'.");

        var connection = new AcpConnection(process, logger);
        await connection.InitializeAsync(cancellationToken);
        await connection.StartSessionAsync(startInfo.WorkingDirectory, cancellationToken);
        return connection;
    }

    public async IAsyncEnumerable<JsonRpcMessage> PromptAsync(
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (SessionId is null)
        {
            throw new InvalidOperationException("ACP session has not been created.");
        }

        LastActiveUtc = DateTimeOffset.UtcNow;
        var channel = Channel.CreateUnbounded<JsonRpcMessage>();
        lock (_subscriberGate)
        {
            _subscriber = channel;
        }

        var requestId = NextId();
        var completion = new TaskCompletionSource<JsonRpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = completion;

        var parameters = JsonSerializer.SerializeToElement(new
        {
            sessionId = SessionId,
            prompt = new object[]
            {
                new { type = "text", text = prompt }
            }
        }, JsonRpc.SerializerOptions);

        await SendAsync(new JsonRpcRequest(requestId, "session/prompt", parameters), cancellationToken);

        await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return message;
            if (message.Id == requestId)
            {
                break;
            }
        }

        lock (_subscriberGate)
        {
            if (ReferenceEquals(_subscriber, channel))
            {
                _subscriber = null;
            }
        }

        LastActiveUtc = DateTimeOffset.UtcNow;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ignoring ACP process shutdown error.");
        }

        _writeLock.Dispose();
        _process.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var parameters = JsonSerializer.SerializeToElement(new
        {
            protocolVersion = 1,
            clientCapabilities = new { },
            clientInfo = new { name = "open-egg", version = "0.1.0" }
        }, JsonRpc.SerializerOptions);

        await SendRequestAsync("initialize", parameters, TimeSpan.FromSeconds(30), cancellationToken);
    }

    private async Task StartSessionAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var parameters = JsonSerializer.SerializeToElement(new
        {
            cwd = workingDirectory,
            mcpServers = Array.Empty<object>()
        }, JsonRpc.SerializerOptions);

        var response = await SendRequestAsync("session/new", parameters, TimeSpan.FromSeconds(120), cancellationToken);
        if (response.Result is not { ValueKind: JsonValueKind.Object } result ||
            !result.TryGetProperty("sessionId", out var sessionIdProperty))
        {
            throw new InvalidOperationException("ACP session/new did not return a sessionId.");
        }

        SessionId = sessionIdProperty.GetString()
            ?? throw new InvalidOperationException("ACP session/new returned an empty sessionId.");
    }

    private async Task<JsonRpcMessage> SendRequestAsync(
        string method,
        JsonElement parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var id = NextId();
        var completion = new TaskCompletionSource<JsonRpcMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException($"Duplicate JSON-RPC id {id}.");
        }

        await SendAsync(new JsonRpcRequest(id, method, parameters), cancellationToken);

        var completed = await Task.WhenAny(completion.Task, Task.Delay(timeout, cancellationToken));
        if (completed != completion.Task)
        {
            _pending.TryRemove(id, out _);
            throw new TimeoutException($"Timed out waiting for ACP response to {method}.");
        }

        var message = await completion.Task;
        if (message.Error is not null)
        {
            throw new InvalidOperationException(message.Error.ToString());
        }

        return message;
    }

    private async Task SendAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(request, JsonRpc.SerializerOptions);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _stdin.WriteLineAsync(line.AsMemory(), cancellationToken);
            await _stdin.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task SendPermissionResponseAsync(ulong id, JsonElement? parameters)
    {
        var result = BuildPermissionOutcome(parameters);
        var response = new JsonRpcResponse(id, result);
        var line = JsonSerializer.Serialize(response, JsonRpc.SerializerOptions);

        await _writeLock.WaitAsync();
        try
        {
            await _stdin.WriteLineAsync(line);
            await _stdin.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadStdoutLoopAsync()
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JsonRpcMessage? message;
                try
                {
                    message = JsonSerializer.Deserialize<JsonRpcMessage>(line, JsonRpc.SerializerOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Ignoring non-JSON ACP stdout line: {Line}", line);
                    continue;
                }

                if (message is null)
                {
                    continue;
                }

                if (message.Method == "session/request_permission" && message.Id is { } permissionId)
                {
                    await SendPermissionResponseAsync(permissionId, message.Params);
                    continue;
                }

                if (message.Id is { } id && _pending.TryRemove(id, out var completion))
                {
                    completion.TrySetResult(message);
                    ForwardToSubscriber(message);
                    continue;
                }

                ForwardToSubscriber(message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ACP stdout reader stopped.");
        }
        finally
        {
            foreach (var (_, completion) in _pending)
            {
                completion.TrySetException(new IOException("ACP process stdout closed."));
            }

            lock (_subscriberGate)
            {
                _subscriber?.Writer.TryComplete();
                _subscriber = null;
            }
        }
    }

    private async Task ReadStderrLoopAsync()
    {
        while (await _process.StandardError.ReadLineAsync() is { } line)
        {
            _logger.LogInformation("ACP stderr: {Line}", line);
        }
    }

    private void ForwardToSubscriber(JsonRpcMessage message)
    {
        Channel<JsonRpcMessage>? subscriber;
        lock (_subscriberGate)
        {
            subscriber = _subscriber;
        }

        subscriber?.Writer.TryWrite(message);
    }

    private ulong NextId() => Interlocked.Increment(ref _nextId);

    private static JsonElement BuildPermissionOutcome(JsonElement? parameters)
    {
        string? optionId = null;
        if (parameters is { ValueKind: JsonValueKind.Object } parameterObject &&
            parameterObject.TryGetProperty("options", out var options) &&
            options.ValueKind == JsonValueKind.Array)
        {
            optionId = PickBestPermissionOption(options);
        }

        var result = optionId is null
            ? new { outcome = new { outcome = "selected", optionId = "allow_always" } }
            : new { outcome = new { outcome = "selected", optionId } };

        return JsonSerializer.SerializeToElement(result, JsonRpc.SerializerOptions);
    }

    private static string? PickBestPermissionOption(JsonElement options)
    {
        foreach (var preferred in new[] { "allow_always", "allow_once" })
        {
            foreach (var option in options.EnumerateArray())
            {
                if (option.TryGetProperty("kind", out var kind) &&
                    kind.GetString() == preferred &&
                    option.TryGetProperty("optionId", out var id))
                {
                    return id.GetString();
                }
            }
        }

        foreach (var option in options.EnumerateArray())
        {
            var kind = option.TryGetProperty("kind", out var kindProperty) ? kindProperty.GetString() : null;
            if (kind is "reject_once" or "reject_always")
            {
                continue;
            }

            if (option.TryGetProperty("optionId", out var id))
            {
                return id.GetString();
            }
        }

        return null;
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
