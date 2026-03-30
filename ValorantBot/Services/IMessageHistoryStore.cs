namespace ValorantBot.Services;

public interface IMessageHistoryStore
{
    void AddMessage(string message);
    List<string> GetRecentMessages(int count = 8);
}
