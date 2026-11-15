using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT.Game.Spawning;
using HarmonyLib;
using UnityEngine;

namespace BotLoadingFreezeFix
{
    // Shortens the loading-screen frame in which the game sets up the map's spawn points.
    //
    // At raid start SpawnPointsCollection.FixSpawns adds every spawn point the server lists but the map
    // doesn't have (158 on Customs). For each one, SpawnPointMarker.Create searches the whole scene for
    // all bot zones, only to pick the one with the right name. On Customs one search took about 58 ms,
    // so the step took 9.2 s.
    //
    // FixSpawns already has that list of bot zones: the game found it a moment earlier and passes it in.
    // While FixSpawns runs, Create gets that list instead of searching again. Same zones, same order,
    // so the same zone is picked. At any other time Create searches as before.
    internal static class SpawnZoneCache
    {
        private static BotZone[] _zones;
        private static int _reused;

        internal static void Patch(Harmony harmony)
        {
            MethodInfo create = typeof(SpawnPointMarker).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == nameof(SpawnPointMarker.Create)
                                     && m.GetParameters().Length == 2
                                     && m.GetParameters()[1].ParameterType == typeof(GameObject));
            MethodInfo fixSpawns = AccessTools.Method(typeof(SpawnPointsCollection), nameof(SpawnPointsCollection.FixSpawns));

            if (create == null || fixSpawns == null)
            {
                Plugin.Log.LogWarning("Spawn point zone reuse: SpawnPointMarker.Create or FixSpawns not found. Left to the game.");
                return;
            }

            harmony.Patch(create, transpiler: new HarmonyMethod(typeof(SpawnZoneCache), nameof(Transpiler)));
            harmony.Patch(fixSpawns,
                prefix: new HarmonyMethod(typeof(SpawnZoneCache), nameof(FixSpawnsPrefix)),
                finalizer: new HarmonyMethod(typeof(SpawnZoneCache), nameof(FixSpawnsFinalizer)));
        }

        // Swaps the one scene-wide search for bot zones in Create for Zones(). Anything else: unchanged.
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = instructions.ToList();
            MethodInfo zones = AccessTools.Method(typeof(SpawnZoneCache), nameof(Zones));

            List<int> found = new List<int>();
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode == OpCodes.Call && code[i].operand is MethodInfo m
                    && m.Name == "FindUnityObjectsOfType" && m.IsGenericMethod
                    && m.GetGenericArguments()[0] == typeof(BotZone))
                    found.Add(i);
            }

            if (found.Count != 1)
            {
                Plugin.Log.LogWarning($"Spawn point zone reuse: expected 1 bot zone search in SpawnPointMarker.Create, found {found.Count}. Left to the game.");
                return code;
            }

            code[found[0]] = new CodeInstruction(OpCodes.Call, zones).MoveLabelsFrom(code[found[0]]).MoveBlocksFrom(code[found[0]]);
            return code;
        }

        private static BotZone[] Zones()
        {
            if (_zones != null)
            {
                _reused++;
                return _zones;
            }
            return FindObjectsProxy.FindUnityObjectsOfType<BotZone>();
        }

        private static void FixSpawnsPrefix(BotZone[] zones)
        {
            _zones = zones;
            _reused = 0;
        }

        private static void FixSpawnsFinalizer()
        {
            if (_zones != null && _reused > 0)
                Plugin.Log.LogInfo($"Spawn points: {_reused} added using the bot zone list the game already had ({_zones.Length} zones), instead of searching the scene {_reused} more times.");
            _zones = null;
            _reused = 0;
        }
    }
}
