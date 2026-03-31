using ValorantBot.Models;

namespace ValorantBot.Services;

/// <summary>
/// One-time startup migration: resolves puuids for tracked players that lack them,
/// and re-keys all data stores from the old "name#tag" format to puuid.
/// </summary>
public class DataMigrator(
    ITrackedPlayerStore trackedPlayerStore,
    IHenrikDevClient henrikDevClient,
    IMatchTracker matchTracker,
    IMatchHistoryStore matchHistoryStore,
    IPlayerProfileStore playerProfileStore,
    ILogger<DataMigrator> logger) : IDataMigrator
{
    public async Task MigrateAsync(CancellationToken ct = default)
    {
        var players = trackedPlayerStore.GetAll();
        var playersNeedingPuuid = players.Where(p => string.IsNullOrEmpty(p.Puuid)).ToList();

        if (playersNeedingPuuid.Count == 0)
        {
            logger.LogInformation("All {Count} tracked player(s) already have puuids, no migration needed",
                players.Count);
            return;
        }

        logger.LogInformation("Migrating {Count} player(s) without puuid", playersNeedingPuuid.Count);

        foreach (var player in playersNeedingPuuid)
        {
            if (ct.IsCancellationRequested) break;

            var oldKey = MatchTracker.PlayerKey(player.Name, player.Tag);

            try
            {
                var account = await henrikDevClient.GetAccountAsync(player.Name, player.Tag, ct);
                if (account is null || string.IsNullOrEmpty(account.Puuid))
                {
                    logger.LogWarning("Could not resolve puuid for {Key}, skipping migration", oldKey);
                    continue;
                }

                var puuid = account.Puuid;
                logger.LogInformation("Resolved {Key} -> puuid {Puuid}", oldKey, puuid);

                // Update the tracked player with puuid and canonical name/tag
                player.Puuid = puuid;
                player.Name = account.Name;
                player.Tag = account.Tag;
                trackedPlayerStore.UpdatePlayer(player);

                // Re-key all stores from old name#tag key to puuid
                // Also try the lowercase variant used by PlayerProfileStore
                var migratedTracker = matchTracker.MigrateKey(oldKey, puuid);
                var migratedHistory = matchHistoryStore.MigrateKey(oldKey, puuid);
                var migratedProfile = playerProfileStore.MigrateKey(oldKey, puuid)
                    || playerProfileStore.MigrateKey(oldKey.ToLowerInvariant(), puuid);

                logger.LogInformation("Migration for {Key}: tracker={Tracker}, history={History}, profile={Profile}",
                    oldKey, migratedTracker, migratedHistory, migratedProfile);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to migrate {Key}, will retry on next startup", oldKey);
            }

            // Small delay between API calls to respect rate limits
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        logger.LogInformation("Data migration complete");
    }
}
