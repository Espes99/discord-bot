namespace ValorantBot.Models;

public class RankChangeInfo
{
    public required string OldRank { get; init; }
    public required string NewRank { get; init; }
    public required bool IsPromotion { get; init; }
    public required bool IsMajor { get; init; }
}
