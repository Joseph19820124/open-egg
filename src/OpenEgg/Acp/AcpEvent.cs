using System.Text.Json;

namespace OpenEgg.Acp;

public abstract record AcpEvent
{
    public sealed record Text(string Value) : AcpEvent;

    public sealed record Thinking : AcpEvent;

    public sealed record Tool(string Id, string Title, string Status) : AcpEvent;

    public sealed record State : AcpEvent;
}

public static class AcpEventClassifier
{
    public static AcpEvent? Classify(JsonRpcMessage message)
    {
        if (message.Params is not { ValueKind: JsonValueKind.Object } parameters ||
            !parameters.TryGetProperty("update", out var update) ||
            !update.TryGetProperty("sessionUpdate", out var sessionUpdateProperty))
        {
            return null;
        }

        var sessionUpdate = sessionUpdateProperty.GetString();
        var toolId = update.TryGetProperty("toolCallId", out var toolIdProperty)
            ? toolIdProperty.GetString() ?? string.Empty
            : string.Empty;

        return sessionUpdate switch
        {
            "agent_message_chunk" => ReadText(update),
            "agent_thought_chunk" => new AcpEvent.Thinking(),
            "tool_call" => new AcpEvent.Tool(toolId, ReadString(update, "title"), "running"),
            "tool_call_update" => new AcpEvent.Tool(toolId, ReadString(update, "title"), ReadString(update, "status")),
            "plan" => new AcpEvent.State(),
            _ => null
        };
    }

    private static AcpEvent? ReadText(JsonElement update)
    {
        if (!update.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("text", out var textProperty))
        {
            return null;
        }

        var text = textProperty.GetString();
        return string.IsNullOrEmpty(text) ? null : new AcpEvent.Text(text);
    }

    private static string ReadString(JsonElement value, string propertyName)
    {
        return value.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }
}
