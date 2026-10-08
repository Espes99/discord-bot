namespace ValorantBot.Models;

/// <summary>
/// How one player acted towards one teammate in a match, from the player's side.
/// Stored for every teammate so tracked mates can be compared with the randoms in the same matches.
/// </summary>
public class PairSignals
{
    public double MateAcs { get; set; }
    public int MateTierId { get; set; }
    public int Rounds { get; set; }
    public int OwnKills { get; set; }
    public int MateKills { get; set; }
    public int MateNearbyDeaths { get; set; }
    public int Trades { get; set; }
    public int KillsOnMateDamaged { get; set; }
    public int AssistsOnMateKills { get; set; }
    public int Teamkills { get; set; }
    public int DiedTogether { get; set; }
    public int DiedFirst { get; set; }
    public int MateDiedFirst { get; set; }

    public static PairSignals Sum(IEnumerable<PairSignals> items)
    {
        var sum = new PairSignals();
        foreach (var s in items)
        {
            sum.Rounds += s.Rounds;
            sum.OwnKills += s.OwnKills;
            sum.MateKills += s.MateKills;
            sum.MateNearbyDeaths += s.MateNearbyDeaths;
            sum.Trades += s.Trades;
            sum.KillsOnMateDamaged += s.KillsOnMateDamaged;
            sum.AssistsOnMateKills += s.AssistsOnMateKills;
            sum.Teamkills += s.Teamkills;
            sum.DiedTogether += s.DiedTogether;
            sum.DiedFirst += s.DiedFirst;
            sum.MateDiedFirst += s.MateDiedFirst;
        }
        return sum;
    }
}
