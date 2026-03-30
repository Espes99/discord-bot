namespace ValorantBot.Models;

public enum WeaponCategory
{
    Precision,
    NonPrecision,
    Unknown
}

public class WeaponContext
{
    public double? PrecisionKillPercent { get; init; }
    public int TotalWeaponKills { get; init; }
    public int PrecisionKills { get; init; }
    public int NonPrecisionKills { get; init; }
    public string? MostUsedWeapon { get; init; }

    public bool HasData => TotalWeaponKills > 0;

    /// <summary>True if player used mostly non-precision weapons, making low HS% expected.</summary>
    public bool LowHsExpected => HasData && PrecisionKillPercent < 40;
}

public static class WeaponClassifier
{
    private static readonly HashSet<string> NonPrecisionWeapons = new(StringComparer.OrdinalIgnoreCase)
    {
        // Shotguns
        "Judge", "Bucky",
        // Snipers (body shots are high damage / one-shot)
        "Operator", "Marshal", "Outlaw",
        // Machine guns
        "Odin", "Ares",
        // Melee
        "Melee", "Knife", "Tactical Knife"
    };

    public static WeaponCategory Classify(string? weaponName, string? damageType)
    {
        if (damageType is not null &&
            !damageType.Equals("Weapon", StringComparison.OrdinalIgnoreCase))
            return WeaponCategory.Unknown;

        if (string.IsNullOrEmpty(weaponName))
            return WeaponCategory.Unknown;

        return NonPrecisionWeapons.Contains(weaponName)
            ? WeaponCategory.NonPrecision
            : WeaponCategory.Precision;
    }

    public static WeaponContext ExtractForPlayer(MatchDetailData matchData, string puuid)
    {
        if (matchData.Kills is null || matchData.Kills.Count == 0)
            return new WeaponContext();

        var weaponCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int precision = 0, nonPrecision = 0, total = 0;

        foreach (var kill in matchData.Kills)
        {
            if (!string.Equals(kill.Killer?.Puuid, puuid, StringComparison.OrdinalIgnoreCase))
                continue;

            var weapon = kill.Weapon;
            var category = Classify(weapon?.Name, weapon?.Type);

            if (category == WeaponCategory.Unknown) continue;

            total++;
            if (category == WeaponCategory.Precision) precision++;
            else nonPrecision++;

            if (weapon?.Name is not null)
            {
                weaponCounts.TryGetValue(weapon.Name, out var count);
                weaponCounts[weapon.Name] = count + 1;
            }
        }

        var mostUsed = weaponCounts.Count > 0
            ? weaponCounts.MaxBy(kvp => kvp.Value).Key
            : null;

        return new WeaponContext
        {
            TotalWeaponKills = total,
            PrecisionKills = precision,
            NonPrecisionKills = nonPrecision,
            PrecisionKillPercent = total > 0 ? (double)precision / total * 100 : null,
            MostUsedWeapon = mostUsed
        };
    }
}
