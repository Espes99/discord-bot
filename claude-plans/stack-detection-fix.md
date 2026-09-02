<!-- Source: /Users/espensele/.claude/plans/moonlit-frolicking-lagoon.md -->

# Fix: partial stack detection in polling

## Context

Stack detection in `Worker.PollAllPlayersAsync` (`ValorantBot/Worker.cs`) groups only the results gathered in one poll cycle. Any tracked player whose match is not returned in that exact cycle is excluded from the stack for good, and the next cycle posts them as a solo message or a smaller second stack. Triggers seen in the analysis:

- HenrikDev per-player match lists go out of sync for minutes after a game ends (most common).
- A match ends during the one-minute sequential poll window.
- Rate limiting, 5xx, or connection failures make a single player's fetch return null or throw; the poll loop swallows it.
- A player who plays a second match before the next poll never has the first match considered at all.

The match details already fetched for the first detected player contain all ten participants with puuid and team id. Stack membership can therefore be derived from the match details instead of from each player's own match list. That removes the dependency on per-player API visibility and on per-player fetch success.

## Approach

### 1. `MatchTracker`: bounded set of seen match IDs per player

Files: `ValorantBot/Services/MatchTracker.cs`, `ValorantBot/Services/IMatchTracker.cs`

- Change the in-memory store from `Dictionary<string, string>` to `Dictionary<string, List<string>>`, most recent last, capped at 10 per player.
- `IsNewMatch`: true when the id is not in the player's list.
- `SetLastMatch`: append if absent, trim to cap, save. Keep the method name so `Worker.cs` call sites at lines 202 and 246 stay unchanged.
- `GetLastMatchId` (used by the admin list command, `Worker.cs:581`): return the last element.
- `MigrateKey`: merge lists when both keys exist, mirroring `MatchHistoryStore.MigrateKey`.
- `Load`: the file `last_matches.json` currently holds `Dictionary<string, string>`. Try the new shape first; on failure deserialize the legacy shape and wrap each value in a one-element list. Log which format was loaded. Same file name, so no change to `DataMigrator`.

Why: with details-based expansion a player can receive two results in one cycle (an older stack match plus a newer solo match). A single "last id" would be overwritten by whichever sends last and re-post the other one next cycle. A set makes send order irrelevant for correctness.

### 2. `MatchService`: expose a pure "build result from details" method and a per-cycle details cache

Files: `ValorantBot/Services/MatchService.cs`, `ValorantBot/Services/IMatchService.cs`

- Extract the block in `GetLatestPerformanceAsync` that locates the `MatchPlayer` (puuid first, name#tag fallback), backfills empty name/tag, and calls `performanceAnalyzer.Analyze` into:
  `PerformanceResult? BuildPerformance(TrackedPlayer player, MatchDetailData details)`. `GetLatestPerformanceAsync` calls it.
- Add an optional parameter `IDictionary<string, MatchDetailData>? detailsCache = null` to `GetLatestPerformanceAsync`. Before calling `henrikDev.GetMatchDetailsAsync`, check the cache; after a successful fetch, store in it. Skip the 2 second pacing delay on a cache hit. The `/latest-match` command call at `Worker.cs:358` passes nothing and keeps working.

Why the cache: a five stack currently fetches the same match five times, and the stack cycle is the most request-heavy one, so it is the one most likely to hit the 429 path.

### 3. `Worker.PollAllPlayersAsync`: expand stacks from match details

File: `ValorantBot/Worker.cs`

- Create `var detailsCache = new Dictionary<string, MatchDetailData>()` at the top of the cycle and pass it through `GetNewMatchResultAsync` into `matchService.GetLatestPerformanceAsync`.
- After the per-player loop and before grouping, add an expansion step:
  - For each distinct match id in `newResults`, take its `MatchData` and the set of store keys already present for that match.
  - For every tracked player (from `trackedPlayerStore.GetAll()`) not in that set: find them in `MatchData.Players` by puuid, falling back to name#tag. If found and `matchTracker.IsNewMatch(StoreKey(p), matchId)`, build a result with `matchService.BuildPerformance` (resolve `IMatchService` from a scope as `GetNewMatchResultAsync` does) and add it to `newResults`. Log at Information: which player was added to which match from details.
  - A player already excluded by `IsNewMatch` was posted for that match in an earlier cycle. Skipping them is correct and also handles the one-time migration case where a partial stack was already sent under the old code.
- Sort `newResults` by `MatchData.Metadata.StartedAt` ascending before grouping so messages go out chronologically. Keep the existing "individuals first, then squads" order; the seen set in step 1 makes order irrelevant for correctness.
- Grouping by `(MatchId, TeamId)` and everything downstream (rank change detection, `SendSquadMessageAsync`, `SetLastMatch`, `AddMatch`, `UpdateAutoTraits`) stays as is. Players expanded from details flow through `DetectAndApplyNameChange` like any other result, so puuid backfill and rename detection still work for them.

### Not changed

- `MatchHistoryStore` already dedupes on match id; no change.
- The individual and squad message formats in `DiscordNotifier` are untouched.
- Poll interval and pacing delays stay as configured.

## Verification

1. `dotnet build` clean.
2. Migration: start the bot against an existing `data/last_matches.json` in the old shape. Log must show the legacy format being loaded, and the file must be rewritten as lists after the first `SetLastMatch`. Confirm no "New match detected" for matches that were already posted.
3. Expansion: pick a match id that was posted as a stack, remove that id from every stack member's list in `last_matches.json`, restart. Expected: the first polled member detects the match, the log shows the other members being added from details before their own poll runs, their own polls hit the details cache, and exactly one squad message is posted with the full stack.
4. Error path: repeat step 3 but replace one member's puuid in `tracked_players.json` with a bogus value while keeping name and tag correct. Their own match list fetch returns nothing, so their poll yields no result. Expected: expansion still finds them in the match details through the name#tag fallback and the squad message includes them. Restore the real puuid afterwards; `DetectAndApplyNameChange` only backfills an empty puuid, not a wrong one.
5. Admin list command still shows a last match id per player.
