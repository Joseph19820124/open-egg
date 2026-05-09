using System.Text.Json;
using OpenEgg.Acp;
using Xunit;

namespace OpenEgg.Tests;

public sealed class AcpEventClassifierTests
{
    [Fact]
    public void ClassifiesAgentMessageChunkAsText()
    {
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(
            """
            {
              "method": "session/update",
              "params": {
                "update": {
                  "sessionUpdate": "agent_message_chunk",
                  "content": { "text": "hello" }
                }
              }
            }
            """,
            JsonRpc.SerializerOptions)!;

        var result = Assert.IsType<AcpEvent.Text>(AcpEventClassifier.Classify(message));
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void ClassifiesToolUpdate()
    {
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(
            """
            {
              "params": {
                "update": {
                  "sessionUpdate": "tool_call_update",
                  "toolCallId": "t1",
                  "title": "rg",
                  "status": "completed"
                }
              }
            }
            """,
            JsonRpc.SerializerOptions)!;

        var result = Assert.IsType<AcpEvent.Tool>(AcpEventClassifier.Classify(message));
        Assert.Equal("t1", result.Id);
        Assert.Equal("rg", result.Title);
        Assert.Equal("completed", result.Status);
    }
}
