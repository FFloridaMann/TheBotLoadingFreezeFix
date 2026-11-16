# The Bot Loading Freeze Fix — Forge page draft

**Short description (one line):**
Shorter loading screens at raid start (about 15 s less on a first Customs raid) and no more one-second freeze two minutes into every raid.

**Page text:**

Bots cause three freezes this mod fixes: one about two minutes into every raid, and two on the loading screen right before a raid starts.

**1. The freeze about two minutes into a raid.** The game asks the server for a batch of spare bot profiles, then builds all of them on the main thread in one frame, and does it again every 50 seconds or so. This mod makes the same request and builds the same profiles, a little each frame instead of all at once.

**2. Bot difficulty settings on the first loading screen.** The first time you load into a raid after starting the game, the game loads the difficulty settings for every bot type at every difficulty, about 280 sets, all in one frame on the loading screen. This mod loads them at the main menu instead, using the game's own code, a little each frame. Changing bot difficulty between raids, or turning bots off for a raid, works exactly as before.

**3. Spawn point setup.** At raid start the game adds the spawn points the server lists but the map itself doesn't have: 158 on Customs. For each one it searched the whole map for every bot zone, just to find the one it belongs to. This mod lets each new spawn point use the list of bot zones the game found a moment earlier. Same zones, same result, one search instead of 159.

It also fixes two bugs in how the game keeps its spare bot profiles: every spare profile was added to the pool twice, so two bots could be given the same profile, and requests for a bot type the game had run out of kept growing (8, then 16, up to 32) while waiting to be sent.

Bots, their gear, their spawns and their difficulty don't change.

**Performance gains** (one PC, mostly Customs):

| What | Before | With the mod |
|---|---|---|
| Freeze about 2 minutes into a raid | 560–1,294 ms | worst frame 59 ms |
| Bot difficulty settings, first raid after starting the game | 5.8 s on the loading screen | 10 ms (the work moves to the main menu) |
| Spawn point setup, Customs | 9.2 s on the loading screen | 0.14 s |
| Duplicate spare bot profiles | every spare profile in the pool twice | 0 duplicates in every fetch logged |

Together with [Raid GC Throttle](https://github.com/FFloridaMann/RaidGcThrottle) 1.1.0, which trims the game's memory cleanup before a raid, the longest loading-screen freezes of a first Customs raid went from 26.5 s to 8.6 s on the test PC. Maps that add few spawn points (Icebreaker, for example) gain less from fix 3.

**Known issues:**
- **The main menu is a little slower for 5–10 seconds** the first time it appears after starting the game, while the bot difficulty settings load (about 12 ms more per frame), with one short pause of about 0.3–0.4 s while they are fetched from the server. If you start a raid before it finishes, the rest loads at raid start, never slower than without the mod.
- **Spare infected profiles keep being requested.** The game asks for spare infected bots (infectedCivil, infectedLaborant) about every 50 seconds and never counts the ones it gets, so its pool of spare profiles grows by about 60 profiles per request. The mod builds them over frames, so there's no freeze, but it isn't fixed yet.
- **The loading screen still has a long frame.** What's left is mostly the game's own memory cleanup and other mods' work when bots start (for example bot AI mods); this mod doesn't touch those.
- **Tested on one PC**, mostly on Customs. Streets and Icebreaker haven't been checked with 1.1.0 yet. Fika is not tested.
- A game or SPT update that changes the code this mod relies on may need a new build. If the spawn point or difficulty code changes shape, those parts switch themselves off and say so in the BepInEx log.

**Changelog:**
- **1.1.0**: loads the bot difficulty settings at the main menu instead of on the first raid's loading screen (about 5–6 s shorter); new spawn points reuse the game's bot zone list instead of searching the map for each one (about 9 s shorter on Customs).
- **1.0.2**: each spare bot profile goes into the pool once instead of twice; spare requests no longer pile up while one is waiting to be sent; the log line shows what was asked for and the pool size.
- **1.0.1**: waits for the next frame for at most 50 ms, so the build keeps moving even if the plugin's update loop is not running.
- **1.0.0**: first version.

**Install:** drag the zip's `BepInEx` folder into your SPT folder (the one with `EscapeFromTarkov.exe`), on any drive. It lands in `BepInEx/plugins/FFloridaMann-BotLoadingFreezeFix/`. To remove, delete that folder.
**Settings:** none.
**Tested on:** SPT 4.1.6. Fika not tested yet.
**Source:** https://github.com/FFloridaMann/TheBotLoadingFreezeFix
**License:** MIT.
