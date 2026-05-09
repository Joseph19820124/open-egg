using System.Text.Json;

namespace OpenEgg.Discord;

public static class PromptBuilder
{
    public static string WithSenderContext(
        string content,
        string authorId,
        string authorName,
        string channelId,
        string sessionKey,
        string messageId)
    {
        var context = JsonSerializer.Serialize(new
        {
            schema = "open-egg.sender.v1",
            channel = "discord",
            sender_id = authorId,
            sender_name = authorName,
            discord_channel_id = channelId,
            session_key = sessionKey,
            discord_message_id = messageId
        });

        return $"<sender_context>\n{context}\n</sender_context>\n\n{content}";
    }
}
