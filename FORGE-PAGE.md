# The Bot Loading Freeze Fix — Forge page draft

**Short description (one line):**
Shorter loading screens at raid start (about 15 s less on a first Customs raid) and no more one-second freeze two minutes into every raid.

**Page text:**

What this mod does

It fixes three freezes caused by bots: one about two minutes into every raid, and two on the loading screen right before a raid starts. Bots, their gear, their spawns and their difficulty are unchanged.

- Spare bot profiles (the freeze about two minutes in): the game asks the server for spare bot profiles and builds them all in one frame, again every 50 seconds or so. The mod sends the same request and builds the same profiles, but spreads the building over frames: it builds for about 1 ms, then lets the next frame draw. The fetches at raid start and when a bot is needed immediately are left to the game.
- Bot difficulty settings (first raid after starting the game): the game loads the settings for every bot type at every difficulty, about 280 sets, in one frame on the loading screen. The mod loads them at the main menu instead, a little each frame, using the game's own code. Changing bot difficulty between raids, or turning bots off for a raid, works exactly as before.
- Spawn point setup (every raid): at raid start the game adds the spawn points the server lists but the map doesn't have, 158 on Customs, and searched the whole map for bot zones once for each one. The mod lets them reuse the list of bot zones the game found a moment earlier: same zones, same result, one search instead of 159.

It also fixes two bugs in the game's spare profiles: every spare profile was added to the pool twice, so two bots could be given the same profile, and requests for a bot type the game had run out of kept growing (8, then 16, up to 32) while waiting to be sent.

Performance improvements

Measured on the test PC, mostly on Customs:

- Freeze about two minutes into a raid: 560–1,294 ms before, worst frame 59 ms with the mod (210 spare profiles built in about 650 ms of work spread over 211 frames).
- Bot difficulty settings, first raid after starting the game: 5.8 s on the loading screen before, 10 ms with the mod (the work moves to the main menu).
- Spawn point setup on Customs: 9.2 s on the loading screen before, 0.14 s with the mod.
- Duplicate spare profiles: none in any spare fetch logged with the mod.
- Together with Raid GC Throttle 1.1.0, the longest loading-screen freezes of a first Customs raid went from 26.5 s to 8.6 s. Maps that add few spawn points (Icebreaker, for example) gain less from the spawn point fix.

Known issues

- The main menu is a little slower for 5–10 seconds the first time it appears after starting the game, while the difficulty settings load, with one short pause of about 0.3–0.4 s. If you start a raid before it finishes, the rest loads at raid start, never slower than without the mod.
- The game keeps asking for spare infected bot profiles about every 50 seconds and never counts the ones it gets, so its spare pool grows. They are built over frames, so there's no freeze, but it isn't fixed yet.
- The loading screen still has one long frame. What's left is mostly the game's own memory cleanup and other mods' work when bots start; this mod doesn't touch those.
- Tested on one PC, mostly on Customs. Streets and Icebreaker haven't been checked with 1.1.0 yet.
- A game or SPT update may need a new build. If the spawn point or difficulty code changes, those parts switch themselves off and say so in the BepInEx log.

Install:

Open the zip and drag its BepInEx folder into your SPT folder: the one that holds EscapeFromTarkov.exe and its own BepInEx folder, wherever it is installed (any drive). Say yes if Windows asks to merge folders. The DLL ends up at:

BepInEx/plugins/FFloridaMann-BotLoadingFreezeFix/FFloridaMann.BotLoadingFreezeFix.dll

With a Fika headless client you can safely install it on both the clients and the headless; whichever one loads the bots uses it. (The 1.1.0 additions have not been tested with Fika yet.)

To remove it, delete that folder.

Changelog

- 1.1.0: loads the bot difficulty settings at the main menu instead of on the first raid's loading screen (about 5–6 s shorter); new spawn points reuse the game's bot zone list instead of searching the map for each one (about 9 s shorter on Customs).
- 1.0.2: each spare bot profile goes into the pool once instead of twice; spare requests no longer pile up while one is waiting to be sent; the log line shows what was asked for and the pool size.
- 1.0.1: waits for the next frame for at most 50 ms, so the build keeps moving even if the plugin's update loop is not running.
- 1.0.0: first version.

**Settings:** none.
**Tested on:** SPT 4.1.6.
**Source:** https://github.com/FFloridaMann/TheBotLoadingFreezeFix
**License:** MIT.
