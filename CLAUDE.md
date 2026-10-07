# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build                    # Build the solution
dotnet run --project ValorantBot  # Run the bot
```

No tests exist yet.

## Project Overview

A .NET 9 Worker Service Discord bot that checks Valorant match stats for tracked players via the HenrikDev API, then uses Claude Sonnet to generate banter/roast messages and posts them to a Discord channel.

Triggered on-demand via the `/latest-match` Discord slash command — no polling or scheduling.

## Architecture

**Entry flow:** `Program.cs` (DI wiring) → `Worker.cs` (BackgroundService) → connects Discord, registers `/latest-match` slash command, waits for invocations.

**`/latest-match` command flow:**

1. `Worker.HandleLatestMatchCommandAsync` iterates all tracked players from config
2. `HenrikDevClient` fetches recent matches (v4 matchlist endpoint), then full match details (v4 match endpoint) from HenrikDev API
3. `PerformanceAnalyzer.Analyze` scores the player on KDA, ACS, and HS% → produces a `PerformanceRating` (Terrible through Excellent)
4. `HighlightExtractor` (called from `PerformanceAnalyzer`) pulls per-player stories out of rounds/kills (first deaths, aces, clutches, eco buys, AFK, ADR, standings)
5. `RoastPlanner` picks, in code, what the message is about (one main focus, optional side focus), its form, voice, length, emoji budget, banned openers, and at most one profile allusion (bio or trait). Rotation is driven by `RoastPlanStore` (`roast_plans.json`), never by feeding old messages back to the model
6. `MessageGenerator` renders the plan into a short prompt and calls Claude Sonnet; the system prompt is stable and cache-marked. Falls back to static text on API failure
7. `DiscordNotifier` posts the AI message + a stats embed to the configured channel

Stacks are grouped by `party_id` from the match details (team id as fallback). Squad messages assign roles (scapegoat, carry, ghost, footnote) that set how much text each player gets; everyone is named.

**External APIs:**

- HenrikDev Valorant API v4 (`https://api.henrikdev.xyz/valorant/`) — match list and match detail endpoints, authenticated via `Authorization` header
- Anthropic Claude API — message generation via `Anthropic.SDK`, model `claude-sonnet-5-5`
- Discord Gateway — via `Discord.Net` socket client

## Configuration

`appsettings.json` is gitignored. Copy `appsettings.example.json` and fill in:

- `DiscordBot.Token` / `DiscordBot.ChannelId` — from Discord Developer Portal
- `HenrikDevValorantApi.ApiKey` — from api.henrikdev.xyz dashboard
- `Anthropic.ApiKey` — from console.anthropic.com

Tracked players are managed dynamically via `/track` and `/untrack` Discord commands and persisted to `/data/tracked_players.json` (not in appsettings).

## Key Dependencies

- `Discord.Net` 3.19 — bot client, slash commands, embeds
- `Anthropic.SDK` 5.10 — Claude API for message generation
- `Microsoft.Extensions.Http` — typed `HttpClient` for HenrikDev API

## Notes

- Claude Sonnet 5.5 has adaptive thinking on by default, so responses start with thinking blocks. `MessageGenerator.CallClaudeAsync` only reads `TextContent` blocks (never `Content.First().ToString()`, which yields the type name) posts `Klarte ikke å parse responsen ;_; Error; <details>` if there is no text, and adds `ThinkingTokenHeadroom` on top of each caller's text budget, since thinking counts toward `max_tokens`
- Sonnet 5.5 rejects `thinking: disabled` and non-default `temperature`/`top_p`/`top_k` with a 400 error
- `MatchTracker` and `MessageTemplates` exist but are currently unused in the main flow
- Bot messages are generated via the AI system prompt
- Debug logging for API responses is enabled when `ValorantBot` log level is set to `Debug` in `appsettings.Development.json`
