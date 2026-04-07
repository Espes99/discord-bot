namespace ValorantBot.Services;

public interface IMessageHistoryStore
{
    void AddMessage(string message, string? playerKey = null);
    List<string> GetRecentMessages(int count = 8);
    List<string> GetRecentPlayerMessages(string playerKey, int count = 5);
}
