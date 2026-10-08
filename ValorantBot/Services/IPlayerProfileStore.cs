using ValorantBot.Models;

namespace ValorantBot.Services;

public interface IPlayerProfileStore
{
    PlayerProfile? GetProfile(string playerKey);
    void SetBio(string playerKey, string bio);
    void AddManualTrait(string playerKey, string trait);
    void RemoveManualTrait(string playerKey, string trait);
    IReadOnlyList<string> RemoveManualTraitsAt(string playerKey, IReadOnlyCollection<int> indices);
    bool ClearBioAndManualTraits(string playerKey);
    void UpdateAutoTraits(string playerKey, List<string> traits);
    bool IsProfileCommandPublic { get; }
    void SetProfileCommandPublic(bool isPublic);
    BotLanguage Language { get; }
    void SetLanguage(BotLanguage language);
    bool MigrateKey(string oldKey, string newKey);
}
