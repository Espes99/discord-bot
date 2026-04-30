using Discord.WebSocket;
using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Manages the Discord bot connection, slash commands, and message posting.
/// </summary>
public interface IDiscordNotifier : IAsyncDisposable
{
    /// <summary>
    /// Raised when a user invokes the /latest-match slash command.
    /// </summary>
    event Func<SocketSlashCommand, Task>? OnLatestMatchCommand;

    /// <summary>
    /// Raised when a user invokes the /status slash command.
    /// </summary>
    event Func<SocketSlashCommand, Task>? OnStatusCommand;

    /// <summary>
    /// Raised when a user invokes the /ranks slash command.
    /// </summary>
    event Func<SocketSlashCommand, Task>? OnRanksCommand;

    /// <summary>
    /// Raised when a user invokes the /track slash command.
    /// </summary>
    event Func<SocketSlashCommand, Task>? OnTrackCommand;

    /// <summary>
    /// Raised when a user invokes the /untrack slash command.
    /// </summary>
    event Func<SocketSlashCommand, Task>? OnUntrackCommand;

    /// <summary>
    /// Raised when a user invokes the /set-bio slash command.
    /// </summary>
    event Func<SocketSlashCommand, Task>? OnSetBioCommand;

    /// <summary>
    /// Raised when a user invokes the /add-trait slash command.
    /// </summary>
    event Func<SocketSlashCommand, Task>? OnAddTraitCommand;

    /// <summary>
    /// Raised when a user invokes the /profile slash command.
    /// </summary>
    event Func<SocketSlashCommand, Task>? OnProfileCommand;

    /// <summary>
    /// Raised when a user invokes the /toggle-profile slash command.
    /// </summary>
    event Func<SocketSlashCommand, Task>? OnToggleProfileCommand;

    /// <summary>
    /// Raised when a user invokes the /summary slash command.
    /// </summary>
    event Func<SocketSlashCommand, Task>? OnSummaryCommand;

    /// <summary>
    /// Raised when a user invokes the /repair-player slash command.
    /// </summary>
    event Func<SocketSlashCommand, Task>? OnRepairPlayerCommand;

    /// <summary>
    /// Raised when a user invokes the /tracked-players slash command.
    /// </summary>
    event Func<SocketSlashCommand, Task>? OnTrackedPlayersCommand;

    /// <summary>
    /// Connects the bot to Discord and registers slash commands.
    /// </summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Waits until the Discord client is fully connected and ready.
    /// </summary>
    Task WaitUntilReadyAsync(CancellationToken ct);

    /// <summary>
    /// Sends a performance message with an embed to the configured channel.
    /// Returns true if the message was sent, false if skipped.
    /// </summary>
    Task<bool> SendPerformanceMessageAsync(PerformanceResult result, RankChangeInfo? rankChange = null);

    /// <summary>
    /// Sends a squad roast message with individual embeds when players queued together.
    /// </summary>
    Task<bool> SendSquadMessageAsync(List<PerformanceResult> results, Dictionary<string, RankChangeInfo>? rankChanges = null);

    /// <summary>
    /// Sends a standalone rank change announcement. Used as fallback only.
    /// </summary>
    Task<bool> SendRankChangeMessageAsync(string playerName, string oldRank, string newRank, bool isPromotion, bool isMajorChange);

    /// <summary>
    /// Disconnects the bot from Discord.
    /// </summary>
    Task StopAsync();
}
