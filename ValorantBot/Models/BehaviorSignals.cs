namespace ValorantBot.Models;

/// <summary>
/// Raw per-match counters, kept as counts so they can be summed across matches and players.
/// The lobby baseline is the sum over the other nine players, so rates compare directly.
/// </summary>
public class BehaviorSignals
{
    public int Rounds { get; set; }
    public int LostRounds { get; set; }
    public int Deaths { get; set; }
    public int FirstDeaths { get; set; }
    public int FirstBloods { get; set; }
    public int LastAlive { get; set; }
    public int LastAliveLost { get; set; }
    public int Clutches { get; set; }
    public int NearbyTeammateDeaths { get; set; }
    public int TradesMade { get; set; }
    public int DeathsTraded { get; set; }
    public int DeathsWithTeammateAlive { get; set; }
    public int IsolatedDeaths { get; set; }
    public int SurvivedLostRounds { get; set; }
    public int ZeroDamageRounds { get; set; }
    public int AbilityCasts { get; set; }
    public int Plants { get; set; }
    public int EcoRifleBuys { get; set; }

    public static BehaviorSignals Sum(IEnumerable<BehaviorSignals> items)
    {
        var sum = new BehaviorSignals();
        foreach (var s in items)
        {
            sum.Rounds += s.Rounds;
            sum.LostRounds += s.LostRounds;
            sum.Deaths += s.Deaths;
            sum.FirstDeaths += s.FirstDeaths;
            sum.FirstBloods += s.FirstBloods;
            sum.LastAlive += s.LastAlive;
            sum.LastAliveLost += s.LastAliveLost;
            sum.Clutches += s.Clutches;
            sum.NearbyTeammateDeaths += s.NearbyTeammateDeaths;
            sum.TradesMade += s.TradesMade;
            sum.DeathsTraded += s.DeathsTraded;
            sum.DeathsWithTeammateAlive += s.DeathsWithTeammateAlive;
            sum.IsolatedDeaths += s.IsolatedDeaths;
            sum.SurvivedLostRounds += s.SurvivedLostRounds;
            sum.ZeroDamageRounds += s.ZeroDamageRounds;
            sum.AbilityCasts += s.AbilityCasts;
            sum.Plants += s.Plants;
            sum.EcoRifleBuys += s.EcoRifleBuys;
        }
        return sum;
    }
}
