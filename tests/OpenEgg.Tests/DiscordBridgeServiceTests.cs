using OpenEgg.Discord;
using Xunit;

namespace OpenEgg.Tests;

public sealed class DiscordBridgeServiceTests
{
    [Fact]
    public void ShortenThreadNameUsesPromptPrefix()
    {
        Assert.Equal("please inspect this bug", DiscordBridgeService.ShortenThreadName("please inspect this bug"));
    }

    [Fact]
    public void ShortenThreadNameCollapsesGitHubIssueUrls()
    {
        Assert.Equal(
            "openabdev/openab#123",
            DiscordBridgeService.ShortenThreadName("https://github.com/openabdev/openab/issues/123"));
    }

    [Fact]
    public void ShortenThreadNameFallsBackWhenPromptIsOnlyMention()
    {
        Assert.Equal("OpenEgg", DiscordBridgeService.ShortenThreadName("<@123>"));
    }

    [Fact]
    public void DirectMessageProcessingFollowsExplicitToggle()
    {
        Assert.True(DiscordBridgeService.ShouldProcessDirectMessage(allowDirectMessages: true));
        Assert.False(DiscordBridgeService.ShouldProcessDirectMessage(allowDirectMessages: false));
    }
}
