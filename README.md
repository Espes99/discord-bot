# Valorant Discord Bot

A .NET 9 Discord bot that tracks Valorant match stats for configured players and generates banter/roast messages using Claude AI.

## How It Works

1. The bot polls tracked players for new matches every 20 minutes
2. When a new match is found, it analyzes performance (KDA, ACS, HS%) and assigns a rating
3. Claude Haiku generates a funny/toxic message based on the performance
4. The message and a stats embed are posted to a configured Discord channel

Players can also trigger a check on-demand using the `/latest` slash command.

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
| `TrackedPlayers`              | Array of `{ Name, Tag, Region }` for players to track |
| `Polling.IntervalSeconds`     | Polling interval in seconds (default: 60)             |

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
