using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenEgg.Acp;

public static class JsonRpc
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed record JsonRpcRequest(
    [property: JsonPropertyName("id")] ulong Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params = null)
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpcVersion => "2.0";
}

public sealed record JsonRpcResponse(
    [property: JsonPropertyName("id")] ulong Id,
    [property: JsonPropertyName("result")] JsonElement Result)
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpcVersion => "2.0";
}

public sealed class JsonRpcMessage
{
    [JsonPropertyName("id")]
    public ulong? Id { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }
}

public sealed class JsonRpcError
{
    [JsonPropertyName("code")]
    public long Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    public override string ToString() => $"JSON-RPC error {Code}: {Message}";
}
