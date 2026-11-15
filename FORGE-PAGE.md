# The Bot Loading Freeze Fix — Forge page draft

**Short description (one line):**
Removes the one-second freeze about two minutes into every raid, when the game builds spare bot profiles in a single frame.

**Page text:**

Ever had the game freeze for about a second roughly two minutes into a raid, and sometimes again a minute later? That's the game building spare bot profiles. It asks the server for a batch of several hundred, then builds all of them on the main thread in one frame.

This mod makes the same request and builds the same profiles, but a little each frame instead of all at once. Bots, their gear and their spawns don't change. Only those timed spare fetches are spread out.

On my PC the freeze was 560–1,294 ms. With the mod, the worst frame around the fetch was 59 ms.

Since 1.1.0 it also takes about 5 seconds off the loading-screen freeze of your first raid after each game start: the game's bot difficulty settings, which it otherwise loads all at once on that loading screen, are loaded at the main menu instead, a little each frame.

Since 1.0.2 it also fixes two bugs in the game's spare profiles: each one was added to the pool twice, so two bots could share a profile, and requests for a bot type the game had run out of kept growing (8, then 16, up to 32) while waiting to be sent.

**Install:** drag the zip's `BepInEx` folder into your SPT folder (the one with `EscapeFromTarkov.exe`), on any drive. It lands in `BepInEx/plugins/FFloridaMann-BotLoadingFreezeFix/`. To remove, delete that folder.
**Settings:** none.
**Tested on:** SPT 4.1.6. Fika not tested yet.
**Source:** https://github.com/FFloridaMann/TheBotLoadingFreezeFix
**License:** MIT.
