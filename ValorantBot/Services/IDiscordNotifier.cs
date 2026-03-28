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
    /// Connects the bot to Discord and registers slash commands.
    /// </summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Sends a performance message with an embed to the configured channel.
    /// </summary>
    Task SendPerformanceMessageAsync(PerformanceResult result);

    /// <summary>
    /// Disconnects the bot from Discord.
    /// </summary>
    Task StopAsync();
}
