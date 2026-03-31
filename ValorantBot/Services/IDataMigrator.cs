namespace ValorantBot.Services;

/// <summary>
/// One-time startup migration: resolves puuids for tracked players that lack them,
/// and re-keys all data stores from the old "name#tag" format to puuid.
/// </summary>
public interface IDataMigrator
{
    Task MigrateAsync(CancellationToken ct = default);
}
