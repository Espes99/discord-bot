using Discord.WebSocket;
using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// Manages the Discord bot connection, slash commands, and message posting.
/// </summary>
public interface IDiscordNotifier : IAsyncDisposable
{
    /// <summary>
    /// Raised when a user invokes the /latest slash command.
    /// </summary>
    event Func<SocketSlashCommand, Task>? OnLatestCommand;

    /// <summary>
    /// Raised when a user invokes the /status slash command.
    /// </summary>
    event Func<SocketSlashCommand, Task>? OnStatusCommand;

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
    Task<bool> SendPerformanceMessageAsync(PerformanceResult result);

    /// <summary>
    /// Sends a squad roast message with individual embeds when players queued together.
    /// </summary>
    Task<bool> SendSquadMessageAsync(List<PerformanceResult> results);

    /// <summary>
    /// Disconnects the bot from Discord.
    /// </summary>
    Task StopAsync();
}
