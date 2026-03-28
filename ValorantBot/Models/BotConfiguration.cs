namespace ValorantBot.Models;

public class DiscordSettings
{
    public string Token { get; set; } = string.Empty;
    public ulong ChannelId { get; set; }
    public ulong GuildId { get; set; }
}

public class HenrikDevSettings
{
    public string BaseUrl { get; set; } = "https://api.henrikdev.xyz/valorant";
    public string ApiKey { get; set; } = string.Empty;
}

public class TrackedPlayer
{
    public string Name { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Region { get; set; } = "eu";
}
