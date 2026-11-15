using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;

namespace BotLoadingFreezeFix
{
    // Removes the freeze about 115 s into a raid, and again about 50 s later.
    //
    // BotProfileBackuploader asks the server for spare bot profiles on a timer. The server
    // sends a full batch per group, about 435 profiles, and ClientBackendSession.LoadBots
    // builds them all on the main thread in one frame, which measured 1055 ms.
    //
    // This builds the same profiles from the same request, about one per frame.
    // Only those timed spare fetches change. Raid-start and on-demand fetches are left alone.
    //
    // It also fixes two bugs in how the game keeps those spares:
    // - Each spare profile went into the pool twice (once in LoadProfiles, once in the LoadedByBackup callback),
    //   so two bots could be handed the same profile. Now only the game's callback adds them.
    // - The 30 s check added another full top-up for every empty bot type while a request was already
    //   waiting, so requests grew from 8 to 32 per type before they were sent. Now it waits for the send.
    //
    // Since 1.1.0 it also shortens two loading-screen freezes at raid start:
    // - it loads the bot difficulty settings at the main menu (DifficultyPreload.cs), 5-6 s of the first raid;
    // - it stops the game searching the whole scene for bot zones once per added spawn point (SpawnZoneCache.cs),
    //   9 s on Customs.
    [BepInPlugin("ffloridamann.botloadingfreezefix", "The Bot Loading Freeze Fix", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal const double BudgetMs = 1.0;

        // Longest wait for the next frame. Keeps the build moving if Update isn't running,
        // which can happen when BepInEx's manager object doesn't survive on a given setup.
        internal const int FrameWaitLimitMs = 50;

        internal static ManualLogSource Log;

        private static TaskCompletionSource<bool> _nextFrame;

        private void Awake()
        {
            Log = Logger;
            var harmony = new Harmony("ffloridamann.botloadingfreezefix");
            harmony.PatchAll(typeof(SmoothSpareFetch));
            harmony.PatchAll(typeof(OneTopUpAtATime));
            DifficultyPreload.Patch(harmony);
            SpawnZoneCache.Patch(harmony);
        }

        private void Update()
        {
            DifficultyPreload.Step();

            if (_nextFrame == null)
                return;

            // Cleared first: the waiting build resumes inside SetResult and may ask again.
            TaskCompletionSource<bool> waiting = _nextFrame;
            _nextFrame = null;
            waiting.SetResult(true);
        }

        internal static Task NextFrame()
        {
            if (_nextFrame == null)
                _nextFrame = new TaskCompletionSource<bool>();
            return _nextFrame.Task;
        }

        // The same work as BotProfileClient.LoadProfiles and ClientBackendSession.LoadBots,
        // except that the build loop gives each frame back once its budget is used.
        internal static async Task<Profile[]> LoadSpareProfiles(BotProfileClient client, ClientBackendSession session, List<CountTypeBotWave> waves)
        {
            ProfileDescriptor[] descriptors = await session.Send<ProfileDescriptor[]>(new SendRequest
            {
                Url = session._backendUrls.Main + "/client/game/bot/generate",
                Params = new BotGenerateRequestParams<List<CountTypeBotWave>>(waves),
                Retries = SendRequest.DefaultRetries
            });

            var profiles = new List<Profile>(descriptors.Length);
            long budgetTicks = (long)(BudgetMs * Stopwatch.Frequency / 1000.0);
            long sliceStart = Stopwatch.GetTimestamp();
            long buildTicks = 0;
            int frames = 1;
            int waitsOverLimit = 0;

            foreach (ProfileDescriptor descriptor in descriptors)
            {
                if (!Singleton<GameWorld>.Instantiated)
                {
                    Log.LogInfo($"Raid ended; stopped after {profiles.Count} of {descriptors.Length} spare profiles.");
                    break;
                }

                profiles.Add(new Profile(descriptor));

                long elapsed = Stopwatch.GetTimestamp() - sliceStart;
                if (elapsed < budgetTicks)
                    continue;

                buildTicks += elapsed;
                Task frame = NextFrame();
                if (await Task.WhenAny(frame, Task.Delay(FrameWaitLimitMs)) != frame)
                    waitsOverLimit++;

                frames++;
                sliceStart = Stopwatch.GetTimestamp();
            }

            buildTicks += Stopwatch.GetTimestamp() - sliceStart;

            Profile[] result = profiles.ToArray();
            client.CheckLoadingResult(result, waves);
            // Not added to the pool here: BotProfileBackuploader.DoLoad passes the result to
            // LoadedByBackup, which adds it. Adding it here too put every spare in the pool twice.

            string asked = string.Join(", ", waves.Select(w => $"{w.Role}/{w.Difficulty} {w.Limit}"));
            List<Profile> pool = client._simpleProfiles;
            int duplicates = pool.Count - pool.Select(p => p.Id).Distinct().Count();
            int skips = OneTopUpAtATime.TakeSkips();

            Log.LogInfo($"Built {result.Length} spare bot profiles in {buildTicks * 1000.0 / Stopwatch.Frequency:0} ms over {frames} frames; {waitsOverLimit} waits hit the {FrameWaitLimitMs} ms limit. " +
                        $"Asked for: {asked}. Pool before adding: {pool.Count} profiles, {duplicates} duplicates. Top-up checks held while waiting: {skips}.");
            return result;
        }
    }

    [HarmonyPatch(typeof(BotProfileClient), nameof(BotProfileClient.LoadProfiles))]
    internal static class SmoothSpareFetch
    {
        [HarmonyPrefix]
        private static bool Prefix(BotProfileClient __instance, List<CountTypeBotWave> waves, EProfilesAskingStat stat, ref Task<Profile[]> __result)
        {
            if (stat != EProfilesAskingStat.byBackup || !(__instance._backEndSession is ClientBackendSession session))
                return true;

            __result = Plugin.LoadSpareProfiles(__instance, session, waves);
            return false;
        }
    }

    // The game's 30 s spare check adds a full top-up for each bot type with no profiles left, without marking
    // a request as pending, so it adds the same top-up again 30 s later until the request is sent (every 50 s,
    // the first one about 110 s into the raid). Skipping the check while a request is waiting stops that.
    [HarmonyPatch(typeof(BotProfileBackuploader), nameof(BotProfileBackuploader.CacheSimpleProfilesChecker))]
    internal static class OneTopUpAtATime
    {
        private static int _skips;
        private static float _nextCount;

        internal static int TakeSkips()
        {
            int skips = _skips;
            _skips = 0;
            return skips;
        }

        [HarmonyPrefix]
        private static bool Prefix(BotProfileBackuploader __instance)
        {
            if (__instance._cacheToLoad == null || __instance._cacheToLoad.Count == 0)
                return true;

            // Counted at most once per 30 s, the interval the game's check acts on (it is called every 5 s).
            if (__instance._uploadCacheComplete && __instance._nextCacheUpload <= UnityEngine.Time.time && UnityEngine.Time.time >= _nextCount)
            {
                _skips++;
                _nextCount = UnityEngine.Time.time + 30f;
            }
            return false;
        }
    }
}
