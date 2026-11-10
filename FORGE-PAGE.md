# The Bot Loading Freeze Fix — Forge page draft

**Short description (one line):**
Removes the one-second freeze about two minutes into every raid, when the game builds spare bot profiles in a single frame.

**Page text:**

Ever had the game freeze for about a second roughly two minutes into a raid, and sometimes again a minute later? That's the game building spare bot profiles. It asks the server for a batch of several hundred, then builds all of them on the main thread in one frame.

This mod makes the same request and builds the same profiles, but a little each frame instead of all at once. Bots, their gear and their spawns don't change. Only those timed spare fetches are spread out.

On my PC the freeze was 560–1,294 ms. With the mod, the worst frame around the fetch was 59 ms.

**Install:** drag the zip's `BepInEx` folder into your SPT folder (the one with `EscapeFromTarkov.exe`), on any drive. It lands in `BepInEx/plugins/FFloridaMann-BotLoadingFreezeFix/`. To remove, delete that folder.
**Settings:** none.
**Tested on:** SPT 4.1.6. Fika not tested yet.
**Source:** <link to the public repository>
**License:** MIT.
