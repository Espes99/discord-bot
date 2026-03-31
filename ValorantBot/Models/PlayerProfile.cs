namespace ValorantBot.Models;

public class PlayerProfile
{
    public string? Bio { get; set; }
    public List<string> ManualTraits { get; set; } = [];
    public List<string> AutoTraits { get; set; } = [];
    public DateTime LastAutoTraitUpdate { get; set; }
}
