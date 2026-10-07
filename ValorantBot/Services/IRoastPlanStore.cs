using ValorantBot.Models;

namespace ValorantBot.Services;

public interface IRoastPlanStore
{
    List<RoastPlanRecord> GetPlayerRecords(string playerKey);
    void AddPlayerRecord(string playerKey, RoastPlanRecord record);
    List<SquadPlanRecord> GetSquadRecords(string squadKey);
    void AddSquadRecord(string squadKey, SquadPlanRecord record);
    List<TraitUseRecord> GetTraitUses(string playerKey);
    void AddTraitUse(string playerKey, TraitUseRecord use);
}
