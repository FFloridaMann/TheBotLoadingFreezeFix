using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace BotLoadingFreezeFix
{
    // Loads the bot difficulty settings at the main menu instead of in the first raid's loading freeze.
    //
    // The first raid of each launch calls BotInternalSettingsController.Load(), which parses the settings for
    // every bot type at every difficulty (280 pairs, two parses each) on the main thread in one frame: about
    // 5-6 s on the test PC. The game keeps them for the rest of the launch, so later raids skip it.
    //
    // This runs the game's own LoadByDifficulty for each pair at the menu, about one per frame, in the game's
    // own order, into temporary lists. When all are done it fills the game's tables the way LoadInternal does
    // and marks them loaded, so the game's Load() returns at once. If a raid starts first, Load() finishes the
    // rest right there. If anything is missing or fails, it drops its work and the game loads as before.
    internal static class DifficultyPreload
    {
        private enum State { Waiting, Running, Done }

        private struct Pair
        {
            public BotDifficulty Difficulty;
            public WildSpawnType Role;
            public BotSettingsComponents Pvp;
            public BotSettingsComponents Pve;
        }

        private static State _state = State.Waiting;
        private static bool _menuSeen;
        private static BotGlobalsCoreSettings _core;
        private static List<Pair> _pairs;
        private static int _next;
        private static long _workTicks;
        private static int _frames;
        private static double _fetchMs;

        internal static void Patch(Harmony harmony)
        {
            MethodInfo show = typeof(MenuScreen).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Show" && m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == typeof(Profile));
            MethodInfo load = AccessTools.Method(typeof(BotInternalSettingsController), nameof(BotInternalSettingsController.Load));

            if (show == null || load == null)
            {
                Plugin.Log.LogWarning("Bot difficulty preload: the main menu or the game's settings loader was not found. Left to the game.");
                _state = State.Done;
                return;
            }

            harmony.Patch(show, postfix: new HarmonyMethod(typeof(DifficultyPreload), nameof(MenuShown)));
            harmony.Patch(load, prefix: new HarmonyMethod(typeof(DifficultyPreload), nameof(LoadPrefix)));
        }

        private static void MenuShown()
        {
            _menuSeen = true;
        }

        // Called from Plugin.Update every frame.
        internal static void Step()
        {
            if (_state == State.Done)
                return;

            try
            {
                if (_state == State.Waiting)
                {
                    if (_menuSeen && !InRaid())
                        Begin();
                    return;
                }

                long budget = (long)(Plugin.BudgetMs * Stopwatch.Frequency / 1000.0);
                long start = Stopwatch.GetTimestamp();
                do
                {
                    if (!LoadOne())
                        return;
                }
                while (_state == State.Running && _next < _pairs.Count && Stopwatch.GetTimestamp() - start < budget);

                _workTicks += Stopwatch.GetTimestamp() - start;
                _frames++;

                if (_state == State.Running && _next >= _pairs.Count)
                {
                    int count = _pairs.Count; // Commit clears the list
                    Commit();
                    Plugin.Log.LogInfo($"Preloaded bot difficulty settings at the menu: {count} bot types and difficulties " +
                                       $"in {Ms(_workTicks):0} ms of work over {_frames} frames, plus {_fetchMs:0} ms fetching them from the server.");
                }
            }
            catch (Exception e)
            {
                GiveUp("failed: " + e);
            }
        }

        private static void Begin()
        {
            if (BotInternalSettingsController._isLoaded)
            {
                _state = State.Done;
                return;
            }

            // The game prefers settings files on disk when they exist; leave that case to it.
            if (File.Exists(string.Format(BotInternalSettingsController.EXTERNAL_PATH, "", "")))
            {
                GiveUp("settings files on disk are in use");
                return;
            }

            long t0 = Stopwatch.GetTimestamp();
            string coreText = BotInternalSettingsController.LoadCoreByString();
            _fetchMs = Ms(Stopwatch.GetTimestamp() - t0);
            if (coreText == null || UnityResourcesProxy.Load<TextAsset>(string.Format(BotInternalSettingsController.PATH_INTERNAL, "", "")) == null)
            {
                GiveUp("the game's built-in settings are not there");
                return;
            }

            _core = BotGlobalsCoreSettings.Create(coreText);
            _pairs = new List<Pair>();
            foreach (WildSpawnType role in Enum.GetValues(typeof(WildSpawnType)))
                foreach (BotDifficulty difficulty in Enum.GetValues(typeof(BotDifficulty)))
                    _pairs.Add(new Pair { Difficulty = difficulty, Role = role });

            _next = 0;
            _workTicks = 0;
            _frames = 0;
            _state = State.Running;
        }

        // One LoadByDifficulty call: the PvP settings of the next pair, or its PvE settings.
        private static bool LoadOne()
        {
            if (_next >= _pairs.Count)
                return true;

            Pair pair = _pairs[_next];
            BotDifficulty d = BotInternalSettingsController.CheckOnExclude(pair.Difficulty, pair.Role);

            if (pair.Pvp == null)
            {
                pair.Pvp = BotInternalSettingsController.LoadByDifficulty(d, pair.Role, external: false, isPve: false);
                if (pair.Pvp == null)
                {
                    GiveUp($"no settings for {pair.Role} {pair.Difficulty}");
                    return false;
                }
                _pairs[_next] = pair;
                return true;
            }

            pair.Pve = BotInternalSettingsController.LoadByDifficulty(d, pair.Role, external: false, isPve: true);
            _pairs[_next] = pair;
            _next++;
            return true;
        }

        // Fills the game's tables exactly as LoadInternal does.
        private static void Commit()
        {
            var pvp = BotInternalSettingsController.AllSettingsPvp;
            var pve = BotInternalSettingsController.AllSettingsPve;

            BotInternalSettingsController.Core = _core;
            foreach (Pair p in _pairs)
            {
                if (!pvp.ContainsKey(p.Difficulty, p.Role))
                    pvp.Add(p.Difficulty, p.Role, p.Pvp);
                if (!pve.ContainsKey(p.Difficulty, p.Role))
                    pve.Add(p.Difficulty, p.Role, p.Pve ?? p.Pvp);
            }

            BotInternalSettingsController._isLoaded = true;
            Clear(State.Done);
        }

        // The game's Load(). If the preload is still running, finish it here instead of starting over.
        private static bool LoadPrefix()
        {
            if (_state != State.Running || BotInternalSettingsController._isLoaded)
                return true;

            try
            {
                int left = _pairs.Count - _next;
                long start = Stopwatch.GetTimestamp();
                while (_state == State.Running && _next < _pairs.Count)
                {
                    if (!LoadOne())
                        return true; // gave up: the game loads everything itself
                }

                if (_state != State.Running)
                    return true;

                int total = _pairs.Count;
                Commit();
                Plugin.Log.LogInfo($"A raid started before the bot difficulty preload finished: loaded the last {left} of {total} at raid start in {Ms(Stopwatch.GetTimestamp() - start):0} ms.");
                return false;
            }
            catch (Exception e)
            {
                GiveUp("failed at raid start: " + e);
                return true;
            }
        }

        private static void GiveUp(string why)
        {
            Clear(State.Done);
            Plugin.Log.LogWarning($"Bot difficulty preload skipped ({why}). The game loads them in the first raid as usual.");
        }

        private static void Clear(State state)
        {
            _state = state;
            _pairs = null;
            _core = null;
        }

        // The main menu runs inside the hideout world, which is a GameWorld too. Only a real raid counts.
        private static bool InRaid()
        {
            return Singleton<GameWorld>.Instantiated && !(Singleton<GameWorld>.Instance is HideoutGameWorld);
        }

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
    }
}
