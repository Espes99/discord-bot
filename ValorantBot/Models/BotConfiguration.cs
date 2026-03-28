using System.ComponentModel.DataAnnotations;

namespace ValorantBot.Models;

/// <summary>
/// Discord bot connection settings.
/// </summary>
public class DiscordSettings
{
    [Required(ErrorMessage = "Discord bot token is required.")]
    public string Token { get; set; } = string.Empty;

    [Range(1, ulong.MaxValue, ErrorMessage = "A valid Discord channel ID is required.")]
    public ulong ChannelId { get; set; }

    [Range(1, ulong.MaxValue, ErrorMessage = "A valid Discord guild ID is required.")]
    public ulong GuildId { get; set; }
}

/// <summary>
/// HenrikDev Valorant API connection settings.
/// </summary>
public class HenrikDevSettings
{
    [Required(ErrorMessage = "HenrikDev API base URL is required.")]
    [Url]
    public string BaseUrl { get; set; } = "https://api.henrikdev.xyz/valorant";

    [Required(ErrorMessage = "HenrikDev API key is required.")]
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Represents a Valorant player to track.
/// </summary>
public class TrackedPlayer
{
    [Required(ErrorMessage = "Player name is required.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Player tag is required.")]
    public string Tag { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(eu|na|kr|ap|br|latam)$", ErrorMessage = "Region must be one of: eu, na, kr, ap, br, latam.")]
    public string Region { get; set; } = "eu";
}
