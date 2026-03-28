using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using ValorantBot.Models;

namespace ValorantBot.Services;

public class DiscordNotifier : IAsyncDisposable
{
    private readonly DiscordSocketClient _client;
    private readonly DiscordSettings _settings;
    private readonly MessageGenerator _messageGenerator;
    private readonly ILogger<DiscordNotifier> _logger;
    private bool _isReady;
    private readonly TaskCompletionSource _readyTcs = new();

    public event Func<SocketSlashCommand, Task>? OnLatestCommand;

    public DiscordNotifier(IOptions<DiscordSettings> settings, MessageGenerator messageGenerator, ILogger<DiscordNotifier> logger)
    {
        _settings = settings.Value;
        _messageGenerator = messageGenerator;
        _logger = logger;

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages
        });

        _client.Log += msg =>
        {
            _logger.LogDebug("[Discord.Net] {Message}", msg.ToString());
            return Task.CompletedTask;
        };

        _client.Ready += OnReadyAsync;
        _client.SlashCommandExecuted += OnSlashCommandExecutedAsync;
    }

    private async Task OnReadyAsync()
    {
        _logger.LogInformation("Discord bot connected — registering slash commands...");

        var latestCommand = new SlashCommandBuilder()
            .WithName("latest")
            .WithDescription("Check the latest Valorant match for a player")
            .AddOption("name", ApplicationCommandOptionType.String, "Player name", isRequired: true)
            .AddOption("tag", ApplicationCommandOptionType.String, "Player tag (e.g. 1234)", isRequired: true);

        var guild = _client.GetGuild(_settings.GuildId);
        await guild.CreateApplicationCommandAsync(latestCommand.Build());

        _isReady = true;
        _readyTcs.TrySetResult();
        _logger.LogInformation("Discord bot ready with /latest command");
    }

    private async Task OnSlashCommandExecutedAsync(SocketSlashCommand command)
    {
        if (command.Data.Name == "latest")
        {
            if (OnLatestCommand is not null)
                await OnLatestCommand.Invoke(command);
            else
                await command.RespondAsync("Bot is not fully initialized yet.");
        }
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("Connecting to Discord...");
        await _client.LoginAsync(TokenType.Bot, _settings.Token);
        await _client.StartAsync();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await _readyTcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Discord client did not become ready within 30 seconds");
        }
    }

    public async Task SendPerformanceMessageAsync(PerformanceResult result)
    {
        if (!_isReady)
        {
            _logger.LogWarning("Discord client not ready, skipping message");
            return;
        }

        var channel = _client.GetChannel(_settings.ChannelId) as IMessageChannel;
        if (channel is null)
        {
            _logger.LogError("Could not find channel {ChannelId}", _settings.ChannelId);
            return;
        }

        var message = await _messageGenerator.GenerateMessageAsync(result);
        var embed = BuildEmbed(result);

        await channel.SendMessageAsync(text: message, embed: embed);
        _logger.LogInformation("Sent {Rating} message for {Player} to Discord",
            result.Rating, result.Player.Name);
    }

    private static Embed BuildEmbed(PerformanceResult result)
    {
        var stats = result.MatchPlayer.Stats;
        var color = result.Rating switch
        {
            PerformanceRating.Terrible => Color.DarkRed,
            PerformanceRating.Bad => Color.Orange,
            PerformanceRating.Average => Color.LightGrey,
            PerformanceRating.Good => Color.Green,
            PerformanceRating.Excellent => Color.Gold,
            _ => Color.Default
        };

        var outcomeEmoji = result.Won ? "✅" : "❌";

        return new EmbedBuilder()
            .WithTitle($"{result.MatchPlayer.Name}#{result.MatchPlayer.Tag} — {result.MapName}")
            .WithColor(color)
            .AddField("Result", $"{outcomeEmoji} {result.Score}", inline: true)
            .AddField("Agent", result.MatchPlayer.Agent.Name, inline: true)
            .AddField("K/D/A", $"{stats.Kills}/{stats.Deaths}/{stats.Assists}", inline: true)
            .AddField("ACS", $"{result.Acs:F0}", inline: true)
            .AddField("KDA", $"{stats.Kda:F2}", inline: true)
            .AddField("HS%", $"{stats.HeadshotPercentage:F1}%", inline: true)
            .WithFooter("Valorant Bot")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();
    }

    public async Task StopAsync()
    {
        if (_client.LoginState == LoginState.LoggedIn)
        {
            await _client.StopAsync();
            await _client.LogoutAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
