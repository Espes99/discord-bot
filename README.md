# Valorant Discord Bot

A .NET 9 Discord bot that tracks Valorant match stats for configured players and generates banter/roast messages using Claude AI.

## How It Works

1. The bot polls tracked players for new competitive matches every 20 minutes (configurable)
2. When a new match is found, it analyzes performance (KDA, ACS, HS%) and assigns a rating from Terrible to Excellent
3. Match results are persisted locally (last 20 per player) to build player history and prevent duplicate messages
4. If 2+ tracked players were on the same team, a squad message is generated that compares and roasts the group
5. Claude Sonnet generates a funny/toxic message based on performance, trends, and player history
6. The message and a color-coded stats embed are posted to a configured Discord channel

Players can also trigger a check on-demand using the `/latest-match <name> <tag>` slash command.

## Features

- **Competitive-only filtering** - only tracks competitive matches, skipping unranked/custom games
- **Squad detection** - detects when 2+ tracked players queue together and generates a group roast comparing their performances
- **Player history tracking** - persists the last 20 matches per player to `data/match_history.json`, enabling trend analysis (improving/stable/declining), win streaks, and per-map stats
- **Duplicate prevention** - tracks the last seen match ID per player in `data/last_matches.json` to avoid re-posting
- **Performance ratings** - point-based system across KDA, ACS, and HS% producing five tiers: Terrible, Bad, Average, Good, Excellent
- **AI-powered roasts** - Claude Sonnet generates personalized messages using player stats, history trends, and agent/map context; falls back to static templates if the API is unavailable
- **Rank change detection** - detects promotions and demotions between matches, generates dedicated AI messages for tier changes (e.g. Silver to Gold)
- **Restart cooldown** - persists last poll timestamp to disk, so the bot waits out the remaining interval on restart instead of polling immediately
- **Retry logic** - HenrikDev API calls retry up to 3 times with 2-second backoff on failure
- **Rate limiting** - 10-second delay between player API requests, 2-second delay between individual API calls

## Slash Commands

| Command                      | Description                                                                                                                                       |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| `/latest-match <name> <tag>` | On-demand lookup of the latest competitive match for any player. Returns an AI-generated message and a stats embed.                               |
| `/track <name> <tag> [region]` | Add a player to the tracked list (admin only). Persisted to `data/tracked_players.json`.                                                        |
| `/untrack <name> <tag>`      | Remove a player from the tracked list (admin only).                                                                                               |
| `/status`                    | Bot dashboard showing uptime, polling interval, last/next poll times, and per-player stats (last match, history summary, agents played, streaks). |
| `/ranks`                     | Ranked leaderboard for all tracked players, sorted by tier and RR. Shows promotion/demotion indicators.                                           |

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (for local development)
- [Docker](https://www.docker.com/products/docker-desktop/) (for containerized deployment)
- A [Discord bot token](https://discord.com/developers/applications)
- A [HenrikDev API key](https://api.henrikdev.xyz)
- An [Anthropic API key](https://console.anthropic.com)

## Configuration

Copy the example config and fill in your values:

```bash
cp ValorantBot/appsettings.example.json ValorantBot/appsettings.json
```

| Key                           | Description                                           |
| ----------------------------- | ----------------------------------------------------- |
| `DiscordBot.Token`            | Discord bot token                                     |
| `DiscordBot.ChannelId`        | Discord channel ID for posting messages               |
| `DiscordBot.GuildId`          | Discord server (guild) ID                             |
| `HenrikDevValorantApi.ApiKey` | HenrikDev API key                                     |
| `Anthropic.ApiKey`            | Anthropic API key                                     |
| `Polling.IntervalSeconds`     | Polling interval in seconds (default: 1200)           |
| `BotAdmin.AllowedUserIds`    | Array of Discord user IDs allowed to use `/track` and `/untrack` commands |

## Data Persistence

The bot stores state in the `data/` directory (created automatically):

| File                 | Purpose                                                                             |
| -------------------- | ----------------------------------------------------------------------------------- |
| `tracked_players.json` | Tracked players list, managed via `/track` and `/untrack` commands                |
| `last_matches.json`   | Last seen match ID per player, used to prevent duplicate messages                  |
| `match_history.json`  | Last 20 match records per player, used for trend analysis and AI context           |
| `message_history.json` | Recent AI-generated messages, used to avoid repetitive messages                   |
| `poll_state.json`     | Timestamp of last poll, used to skip polling on restart if interval has not elapsed |

## Running Locally

```bash
dotnet build
dotnet run --project ValorantBot
```

## Running with Docker

```bash
docker compose up -d
```

To rebuild after code changes:

```bash
docker compose up -d --build
```

View logs:

```bash
docker logs -f valorant-bot
```

## Deploying to Fly.io

The project includes a `fly.toml` configured for Fly.io deployment.

```bash
fly deploy
```

Set secrets for API keys and configuration:

```bash
fly secrets set DiscordBot__Token="..."
fly secrets set DiscordBot__ChannelId="..."
fly secrets set DiscordBot__GuildId="..."
fly secrets set HenrikDevValorantApi__ApiKey="..."
fly secrets set Anthropic__ApiKey="..."
fly secrets set Polling__IntervalSeconds=1200
fly secrets set BotAdmin__AllowedUserIds__0=123456789012345678
```

Data is persisted via a Fly volume mounted at `/data`. Create it before the first deploy:

```bash
fly volumes create bot_data --region lhr --size 1
```
