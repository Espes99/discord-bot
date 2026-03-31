using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Manages the Discord bot connection, slash command registration, and message posting.
/// </summary>
public class DiscordNotifier : IDiscordNotifier
{
    private readonly DiscordSocketClient _client;
    private readonly DiscordSettings _settings;
    private readonly IMessageGenerator _messageGenerator;
    private readonly ILogger<DiscordNotifier> _logger;
    private bool _isReady;
    private readonly TaskCompletionSource _readyTcs = new();

    /// <inheritdoc />
    public event Func<SocketSlashCommand, Task>? OnLatestMatchCommand;

    /// <inheritdoc />
    public event Func<SocketSlashCommand, Task>? OnStatusCommand;

    /// <inheritdoc />
    public event Func<SocketSlashCommand, Task>? OnRanksCommand;

    /// <inheritdoc />
    public event Func<SocketSlashCommand, Task>? OnTrackCommand;

    /// <inheritdoc />
    public event Func<SocketSlashCommand, Task>? OnUntrackCommand;

    /// <inheritdoc />
    public event Func<SocketSlashCommand, Task>? OnSetBioCommand;

    /// <inheritdoc />
    public event Func<SocketSlashCommand, Task>? OnAddTraitCommand;

    /// <inheritdoc />
    public event Func<SocketSlashCommand, Task>? OnProfileCommand;

    /// <inheritdoc />
    public event Func<SocketSlashCommand, Task>? OnToggleProfileCommand;

    private readonly IMatchHistoryStore _historyStore;

    private static string StoreKey(TrackedPlayer player) =>
        !string.IsNullOrEmpty(player.Puuid) ? player.Puuid : MatchTracker.PlayerKey(player.Name, player.Tag);

    public DiscordNotifier(
        IOptions<DiscordSettings> settings,
        IMessageGenerator messageGenerator,
        IMatchHistoryStore historyStore,
        ILogger<DiscordNotifier> logger)
    {
        _settings = settings.Value;
        _messageGenerator = messageGenerator;
        _historyStore = historyStore;
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
            .WithName("latest-match")
            .WithDescription("Check the latest Valorant match for a player with provided name and tag")
            .AddOption("name", ApplicationCommandOptionType.String, "Player name", isRequired: true)
            .AddOption("tag", ApplicationCommandOptionType.String, "Player tag (e.g. 1234)", isRequired: true);

        var statusCommand = new SlashCommandBuilder()
            .WithName("status")
            .WithDescription("Show bot status, tracked players, and recent activity");

        var ranksCommand = new SlashCommandBuilder()
            .WithName("ranks")
            .WithDescription("Show tracked players ranked by current rank and RR");

        var trackCommand = new SlashCommandBuilder()
            .WithName("track")
            .WithDescription("Start tracking a Valorant player")
            .AddOption("name", ApplicationCommandOptionType.String, "Player name", isRequired: true)
            .AddOption("tag", ApplicationCommandOptionType.String, "Player tag (e.g. 1234)", isRequired: true)
            .AddOption("region", ApplicationCommandOptionType.String, "Region (eu/na/kr/ap/br/latam)", isRequired: false);

        var untrackCommand = new SlashCommandBuilder()
            .WithName("untrack")
            .WithDescription("Stop tracking a Valorant player")
            .AddOption("name", ApplicationCommandOptionType.String, "Player name", isRequired: true)
            .AddOption("tag", ApplicationCommandOptionType.String, "Player tag (e.g. 1234)", isRequired: true);

        var setBioCommand = new SlashCommandBuilder()
            .WithName("set-bio")
            .WithDescription("Set a player's roast bio (admin only)")
            .AddOption("name", ApplicationCommandOptionType.String, "Player name", isRequired: true)
            .AddOption("tag", ApplicationCommandOptionType.String, "Player tag (e.g. 1234)", isRequired: true)
            .AddOption("bio", ApplicationCommandOptionType.String, "Free-text bio for roast personalization", isRequired: true);

        var addTraitCommand = new SlashCommandBuilder()
            .WithName("add-trait")
            .WithDescription("Add a roast trait to a player (admin only)")
            .AddOption("name", ApplicationCommandOptionType.String, "Player name", isRequired: true)
            .AddOption("tag", ApplicationCommandOptionType.String, "Player tag (e.g. 1234)", isRequired: true)
            .AddOption("trait", ApplicationCommandOptionType.String, "Trait to add (e.g. \"always blames teammates\")", isRequired: true);

        var profileCommand = new SlashCommandBuilder()
            .WithName("profile")
            .WithDescription("View a player's roast profile")
            .AddOption("name", ApplicationCommandOptionType.String, "Player name", isRequired: true)
            .AddOption("tag", ApplicationCommandOptionType.String, "Player tag (e.g. 1234)", isRequired: true);

        var toggleProfileCommand = new SlashCommandBuilder()
            .WithName("toggle-profile")
            .WithDescription("Toggle whether non-admins can use /profile (admin only)");

        var guild = _client.GetGuild(_settings.GuildId);
        if (guild is null)
        {
            _logger.LogError("Guild {GuildId} not found — check DiscordBot:GuildId in config", _settings.GuildId);
            _readyTcs.TrySetResult();
            return;
        }

        await guild.BulkOverwriteApplicationCommandAsync([
            latestCommand.Build(),
            statusCommand.Build(),
            ranksCommand.Build(),
            trackCommand.Build(),
            untrackCommand.Build(),
            setBioCommand.Build(),
            addTraitCommand.Build(),
            profileCommand.Build(),
            toggleProfileCommand.Build()
        ]);

        _isReady = true;
        _readyTcs.TrySetResult();
        _logger.LogInformation("Discord bot ready...");
    }

    private async Task OnSlashCommandExecutedAsync(SocketSlashCommand command)
    {
        switch (command.Data.Name)
        {
            case "latest-match":
                if (OnLatestMatchCommand is not null)
                    await OnLatestMatchCommand.Invoke(command);
                else
                    await command.RespondAsync("Bot is not fully initialized yet.");
                break;

            case "status":
                if (OnStatusCommand is not null)
                    await OnStatusCommand.Invoke(command);
                else
                    await command.RespondAsync("Bot is not fully initialized yet.");
                break;

            case "ranks":
                if (OnRanksCommand is not null)
                    await OnRanksCommand.Invoke(command);
                else
                    await command.RespondAsync("Bot is not fully initialized yet.");
                break;

            case "track":
                if (OnTrackCommand is not null)
                    await OnTrackCommand.Invoke(command);
                else
                    await command.RespondAsync("Bot is not fully initialized yet.");
                break;

            case "untrack":
                if (OnUntrackCommand is not null)
                    await OnUntrackCommand.Invoke(command);
                else
                    await command.RespondAsync("Bot is not fully initialized yet.");
                break;

            case "set-bio":
                if (OnSetBioCommand is not null)
                    await OnSetBioCommand.Invoke(command);
                else
                    await command.RespondAsync("Bot is not fully initialized yet.");
                break;

            case "add-trait":
                if (OnAddTraitCommand is not null)
                    await OnAddTraitCommand.Invoke(command);
                else
                    await command.RespondAsync("Bot is not fully initialized yet.");
                break;

            case "profile":
                if (OnProfileCommand is not null)
                    await OnProfileCommand.Invoke(command);
                else
                    await command.RespondAsync("Bot is not fully initialized yet.");
                break;

            case "toggle-profile":
                if (OnToggleProfileCommand is not null)
                    await OnToggleProfileCommand.Invoke(command);
                else
                    await command.RespondAsync("Bot is not fully initialized yet.");
                break;
        }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task WaitUntilReadyAsync(CancellationToken ct) => _readyTcs.Task.WaitAsync(ct);

    /// <inheritdoc />
    public async Task<bool> SendPerformanceMessageAsync(PerformanceResult result, RankChangeInfo? rankChange = null)
    {
        if (!_isReady)
        {
            _logger.LogWarning("Discord client not ready, skipping message");
            return false;
        }

        var channel = _client.GetChannel(_settings.ChannelId) as IMessageChannel;
        if (channel is null)
        {
            _logger.LogError("Could not find channel {ChannelId}", _settings.ChannelId);
            return false;
        }

        var storeKey = StoreKey(result.Player);
        var history = HistorySummarizer.Summarize(_historyStore.GetHistory(storeKey));
        var message = await _messageGenerator.GenerateMessageAsync(result, history, rankChange);
        var embed = BuildEmbed(result, rankChange);

        await channel.SendMessageAsync(text: message, embed: embed);
        _logger.LogInformation("Sent {Rating} message for {Player} to Discord",
            result.Rating, result.Player.Name);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SendSquadMessageAsync(List<PerformanceResult> results, Dictionary<string, RankChangeInfo>? rankChanges = null)
    {
        if (!_isReady)
        {
            _logger.LogWarning("Discord client not ready, skipping squad message");
            return false;
        }

        var channel = _client.GetChannel(_settings.ChannelId) as IMessageChannel;
        if (channel is null)
        {
            _logger.LogError("Could not find channel {ChannelId}", _settings.ChannelId);
            return false;
        }

        var histories = results
            .Select(r => (Key: StoreKey(r.Player), Result: r))
            .Select(x => (x.Key, Summary: HistorySummarizer.Summarize(_historyStore.GetHistory(x.Key))))
            .Where(x => x.Summary is not null)
            .ToDictionary(x => x.Key, x => x.Summary!);

        var message = await _messageGenerator.GenerateSquadMessageAsync(results, histories.Count > 0 ? histories : null, rankChanges);
        var embeds = results.Select(r =>
        {
            var key = StoreKey(r.Player);
            var rc = rankChanges is not null && rankChanges.TryGetValue(key, out var change) ? change : null;
            return BuildEmbed(r, rc);
        }).ToArray();
        await channel.SendMessageAsync(text: message, embeds: embeds);

        var names = string.Join(", ", results.Select(r => r.Player.Name));
        _logger.LogInformation("Sent squad message for [{Players}] to Discord", names);
        return true;
    }

    private static Embed BuildEmbed(PerformanceResult result, RankChangeInfo? rankChange = null)
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

        var builder = new EmbedBuilder()
            .WithTitle($"{result.MatchPlayer.Name}#{result.MatchPlayer.Tag} — {result.MapName}")
            .WithColor(color)
            .AddField("Result", $"{outcomeEmoji} {result.Score}", inline: true)
            .AddField("Agent", result.MatchPlayer.Agent.Name, inline: true)
            .AddField("K/D/A", $"{stats.Kills}/{stats.Deaths}/{stats.Assists}", inline: true)
            .AddField("ACS", $"{result.Acs:F0}", inline: true)
            .AddField("KDA", $"{stats.Kda:F2}", inline: true)
            .AddField("HS%", result.WeaponContext is { HasData: true, LowHsExpected: true }
                ? $"{stats.HeadshotPercentage:F1}% (mostly {result.WeaponContext.MostUsedWeapon})"
                : $"{stats.HeadshotPercentage:F1}%", inline: true);

        if (rankChange is not null)
        {
            var arrow = rankChange.IsPromotion ? "⬆️" : "⬇️";
            builder.AddField($"{arrow} Rank Change", $"{rankChange.OldRank} → {rankChange.NewRank}", inline: false);
        }

        return builder
            .WithFooter("Valorant Bot")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();
    }

    /// <inheritdoc />
    public async Task<bool> SendRankChangeMessageAsync(string playerName, string oldRank, string newRank, bool isPromotion, bool isMajorChange)
    {
        if (!_isReady)
        {
            _logger.LogWarning("Discord client not ready, skipping rank change message");
            return false;
        }

        var channel = _client.GetChannel(_settings.ChannelId) as IMessageChannel;
        if (channel is null)
        {
            _logger.LogError("Could not find channel {ChannelId}", _settings.ChannelId);
            return false;
        }

        var message = await _messageGenerator.GenerateRankChangeMessageAsync(playerName, oldRank, newRank, isPromotion, isMajorChange);

        var color = isPromotion ? Color.Green : Color.Red;
        var arrow = isPromotion ? "⬆️" : "⬇️";
        var embed = new EmbedBuilder()
            .WithTitle($"{arrow} Rank Change: {playerName}")
            .WithColor(color)
            .AddField("Previous Rank", oldRank, inline: true)
            .AddField("New Rank", newRank, inline: true)
            .WithFooter("Valorant Bot")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();

        await channel.SendMessageAsync(text: message, embed: embed);
        _logger.LogInformation("Sent rank change message for {Player}: {Old} -> {New}",
            playerName, oldRank, newRank);
        return true;
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (_client.LoginState == LoginState.LoggedIn)
        {
            await _client.StopAsync();
            await _client.LogoutAsync();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
