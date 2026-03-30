using ValorantBot.Models;

namespace ValorantBot.Services;

public interface IMatchHistoryStore
{
    List<MatchHistoryEntry> GetHistory(string playerKey);
    void AddMatch(string playerKey, MatchHistoryEntry entry);
    string? GetLastRank(string playerKey);
}
