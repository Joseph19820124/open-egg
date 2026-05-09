using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenEgg.Acp;
using OpenEgg.Configuration;
using OpenEgg.Discord;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddEnvironmentVariables("OPENEGG_");
builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection("Discord"));
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
builder.Services.Configure<PoolOptions>(builder.Configuration.GetSection("Pool"));

builder.Services.AddSingleton(_ => new DiscordSocketClient(new DiscordSocketConfig
{
    GatewayIntents = GatewayIntents.Guilds
        | GatewayIntents.GuildMessages
        | GatewayIntents.MessageContent
        | GatewayIntents.GuildMessageReactions
        | GatewayIntents.DirectMessages,
    AlwaysDownloadUsers = false,
    MessageCacheSize = 50
}));

builder.Services.AddSingleton<AcpSessionPool>();
builder.Services.AddHostedService<DiscordBridgeService>();

await builder.Build().RunAsync();
