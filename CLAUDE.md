# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build                    # Build the solution
dotnet run --project ValorantBot  # Run the bot
```

No tests exist yet.

## Project Overview

A .NET 9 Worker Service Discord bot that checks Valorant match stats for tracked players via the HenrikDev API, then uses Claude Haiku to generate banter/roast messages in Norwegian and posts them to a Discord channel.

Triggered on-demand via the `/latest` Discord slash command — no polling or scheduling.

## Architecture

**Entry flow:** `Program.cs` (DI wiring) → `Worker.cs` (BackgroundService) → connects Discord, registers `/latest` slash command, waits for invocations.

**`/latest` command flow:**
1. `Worker.HandleLatestCommandAsync` iterates all tracked players from config
2. `HenrikDevClient` fetches recent matches (v4 matchlist endpoint), then full match details (v4 match endpoint) from HenrikDev API
3. `PerformanceAnalyzer.Analyze` scores the player on KDA, ACS, and HS% → produces a `PerformanceRating` (Terrible through Excellent)
4. `MessageGenerator` sends stats to Claude Haiku with a system prompt requesting toxic/funny Norwegian messages; falls back to static `MessageTemplates` on API failure
5. `DiscordNotifier` posts the AI message + a stats embed to the configured channel

**External APIs:**
- HenrikDev Valorant API v4 (`https://api.henrikdev.xyz/valorant/`) — match list and match detail endpoints, authenticated via `Authorization` header
- Anthropic Claude API — message generation via `Anthropic.SDK`, model `claude-haiku-4-5-20251001`
- Discord Gateway — via `Discord.Net` socket client

## Configuration

`appsettings.json` is gitignored. Copy `appsettings.example.json` and fill in:
- `DiscordBot.Token` / `DiscordBot.ChannelId` — from Discord Developer Portal
- `HenrikDevValorantApi.ApiKey` — from api.henrikdev.xyz dashboard
- `Anthropic.ApiKey` — from console.anthropic.com
- `TrackedPlayers` — array of `{ Name, Tag, Region }` for players to track

## Key Dependencies

- `Discord.Net` 3.19 — bot client, slash commands, embeds
- `Anthropic.SDK` 5.10 — Claude API for message generation
- `Microsoft.Extensions.Http` — typed `HttpClient` for HenrikDev API

## Notes

- `MatchTracker` and `MessageTemplates` exist but are currently unused in the main flow
- Bot messages are generated in Norwegian (bokmål) via the AI system prompt
- Debug logging for API responses is enabled when `ValorantBot` log level is set to `Debug` in `appsettings.Development.json`
