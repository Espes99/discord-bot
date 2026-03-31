using ValorantBot.Models;

namespace ValorantBot.Services;

public interface IPlayerProfileStore
{
    PlayerProfile? GetProfile(string playerKey);
    void SetBio(string playerKey, string bio);
    void AddManualTrait(string playerKey, string trait);
    void RemoveManualTrait(string playerKey, string trait);
    void UpdateAutoTraits(string playerKey, List<string> traits);
    bool IsProfileCommandPublic { get; }
    void SetProfileCommandPublic(bool isPublic);
    bool MigrateKey(string oldKey, string newKey);
}
