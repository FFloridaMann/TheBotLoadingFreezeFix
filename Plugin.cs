using System.Collections.Generic;
using System.Diagnostics;
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
    [BepInPlugin("ffloridamann.botloadingfreezefix", "The Bot Loading Freeze Fix", "1.0.1")]
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
            new Harmony("ffloridamann.botloadingfreezefix").PatchAll(typeof(SmoothSpareFetch));
        }

        private void Update()
        {
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
            client._simpleProfiles.AddRange(result);

            Log.LogInfo($"Built {result.Length} spare bot profiles in {buildTicks * 1000.0 / Stopwatch.Frequency:0} ms over {frames} frames; {waitsOverLimit} waits hit the {FrameWaitLimitMs} ms limit.");
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
}
