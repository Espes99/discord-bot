namespace ValorantBot.Services;

public interface IPollStateStore
{
    DateTimeOffset? GetLastPollAt();
    void SetLastPollAt(DateTimeOffset timestamp);
}
