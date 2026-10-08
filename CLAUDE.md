# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build                    # Build the solution
dotnet run --project ValorantBot  # Run the bot
dotnet test                     # Run ValorantBot.Tests (signals, trait derivation, profile migration)
```

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
5. `RoastPlanner` gathers the material in code: the stories that are true for the match (top `FocusCandidates`, strongest first), squad roles, length limits, banned openers, a handful of tone ideas, what was used recently (stories, tones, forms), and at most one profile allusion (bio or trait; solo 60%, squad 70% on one member, weighted towards scapegoat/carry and never the previous message's owner). Rotation is driven by `RoastPlanStore` (`roast_plans.json`), never by feeding old messages back to the model
6. `MessageGenerator` renders the material into a prompt and calls Claude Sonnet with a JSON schema (`output_config.format`). The model picks the story, tone and form itself (tone ideas and form menu are suggestions) and returns `{story, tone, form, angle, message}`; the choices are recorded so they show up as RECENTLY USED next time. The system prompt is stable and cache-marked. Falls back to static text on API failure
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

- Auto traits: `BehaviorSignalExtractor` counts per-match signals for all 10 players (last alive, trades and isolated deaths from kill `player_locations`, first deaths, exit saves, etc.). Each `MatchHistoryEntry` stores the player's counts plus the summed lobby counts, so `ProfileTraitDeriver` judges rates against the lobby, needs 5+ matches with signals, a consistency threshold, and eases thresholds for traits already held (hysteresis). One trait per category, max 6. Auto traits are `{Label, Evidence}`; old string entries in `player_profiles.json` load via `AutoTraitConverter`
- Profile allusions: bio, manual traits and auto traits share one pool; an auto trait's evidence goes into the prompt. Each trait has a 3-day cooldown tracked in `RoastPlanStore.TraitUses`. Profile content is fair game for roasts (the group adds it themselves); the prompt requires the trait to show up, tied to the match focus. The model reports the joke it made in the JSON `angle` field, and recent angles are sent back as "do not reuse" so the same trait gets new jokes
- Claude Sonnet 5.5 has adaptive thinking on by default, so responses start with thinking blocks. `MessageGenerator.SendAsync` only reads `TextContent` blocks (never `Content.First().ToString()`, which yields the type name), posts `Klarte ikke å parse responsen ;_; Error; <details>` if there is no text or the JSON is invalid, and adds `ThinkingTokenHeadroom` on top of each caller's text budget, since thinking counts toward `max_tokens`
- Sonnet 5.5 rejects `thinking: disabled` and non-default `temperature`/`top_p`/`top_k` with a 400 error
- `/toggle-language language:[English|Norsk]` (admin) sets a global flag persisted in `player_profiles.json`. All prompt material stays English; Norwegian only adds a rule line to the user message (never to the cached system prompt) plus Norwegian banned openers. The JSON fields `story`/`tone`/`form`/`angle` must stay English or rotation breaks. Embeds, fallbacks and command replies are always English
- `MatchTracker` and `MessageTemplates` exist but are currently unused in the main flow
- Bot messages are generated via the AI system prompt
- Debug logging for API responses is enabled when `ValorantBot` log level is set to `Debug` in `appsettings.Development.json`
