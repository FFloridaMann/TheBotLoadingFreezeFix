# The Bot Loading Freeze Fix

A BepInEx client mod for SPT 4.1.6 that removes the roughly one-second freeze about 115 seconds into a raid, and the smaller ones that can follow about 50 seconds later.

## What causes the freeze

During a raid the game asks the server for spare bot profiles on a timer: first about 60 seconds after the bot system starts, then every 50 seconds. The server answers with a full batch for each bot group (around 400 profiles on a busy map), and the game builds every one of them on the main thread in a single frame. On the test PC that frame took between 560 ms and 1,294 ms.

## What this mod does

It sends the same request and builds the same profiles, but spreads the building over frames. It builds for about 1 ms, then lets the next frame draw. Bots, their gear and their spawns are unchanged. Only the timed spare fetches are spread out; the fetches at raid start and when a bot is needed immediately are left to the game.

Measured on the test PC: 210 spare profiles built in about 650 ms of work spread over 211 frames. The worst frame near the fetch was 59 ms, where it had been 560–1,294 ms.

It also fixes two bugs in how the game keeps those spare profiles:

- **Every spare profile went into the pool twice**, so two bots could be given the same profile. Now each goes in once.
- **Spare requests piled up.** Every 30 seconds the game added another top-up for each bot type it had run out of, even while the last top-up was still waiting to be sent, so a request for 8 profiles of a type grew to 32. Now it waits until the waiting request has been sent.

## Install

Open the zip and drag its `BepInEx` folder into your SPT folder: the one that holds `EscapeFromTarkov.exe` and its own `BepInEx` folder, wherever it is installed (any drive). Say yes if Windows asks to merge folders. The DLL ends up at:

`BepInEx/plugins/FFloridaMann-BotLoadingFreezeFix/FFloridaMann.BotLoadingFreezeFix.dll`

To remove it, delete that folder.

## Settings

None. It writes one line to the BepInEx log for each spare fetch, for example:
`Built 48 spare bot profiles in 150 ms over 49 frames; 0 waits hit the 50 ms limit. Asked for: assault/easy 8, assault/normal 8, marksman/hard 8. Pool before adding: 30 profiles, 0 duplicates. Top-up checks held while waiting: 2.`

"Asked for" is what was requested, "duplicates" should stay at 0, and "held" counts the 30-second checks that waited for the request to be sent.

## Compatibility

- Built and tested on SPT 4.1.6 (EFT 0.16.9).
- Only acts when the game uses its normal backend session, so other setups fall back to the game's own behaviour.
- Fika: not tested yet.
- A game or SPT update that changes `BotProfileClient.LoadProfiles`, `BotProfileBackuploader` or the bot generate request may need a rebuild.

## Building

Build against your own SPT install by passing its folder:

`dotnet build -c Release -p:SptDir="D:\Games\SPT"`

It references `Assembly-CSharp`, `Comfort`, `UnityEngine` and BepInEx/Harmony from that install. The DLL lands in `bin/Release/netstandard2.1/`.

## Changelog

- **1.0.2**: each spare profile now goes into the pool once instead of twice; spare requests no longer pile up while one is waiting to be sent; the log line shows what was asked for and the pool size.
- **1.0.1**: waits for the next frame for at most 50 ms, so the build keeps moving even if the plugin's update loop is not running.
- **1.0.0**: first version.

## License

MIT. See `LICENSE`.
