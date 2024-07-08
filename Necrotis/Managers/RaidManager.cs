using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Necrotis.Managers;

public static class RaidManager
{
    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.SetupLocations))]
    private static class RegisterRandomEvent
    {
        private static void Postfix()
        {
            if (!RandEventSystem.instance) return;

            RandEventSystem.instance.m_events.Add(new RandomEvent()
            {
                m_name = "Necromancer",
                m_enabled = true,
                m_duration = 60f,
                m_nearBaseOnly = false,
                m_pauseIfNoPlayerInArea = false,
                m_biome = Heightmap.Biome.All,
                m_forceMusic = "Necromancer",
                m_forceEnvironment = "Necromancer"
            });
            
            ConfigEntry<float> duration = NecrotisPlugin._Plugin.config("3 - Raid Settings", "Duration", 120f, "Set duration");
            ConfigEntry<Heightmap.Biome> biome = NecrotisPlugin._Plugin.config("3 - Raid Settings", "Biome", Heightmap.Biome.All, "Set biomes");
            ConfigEntry<string> requiredKey = NecrotisPlugin._Plugin.config("3 - Raid Settings", "Required Key", "", "Set required key");
            ConfigEntry<string> blockKey = NecrotisPlugin._Plugin.config("3 - Raid Settings", "Blocking Key", "defeated_necromancer", "Set the keys that block the raid");
            ConfigEntry<string> startMsg = NecrotisPlugin._Plugin.config("3 - Raid Settings", "Start Message", "", "Set start message");
            ConfigEntry<string> stopMsg = NecrotisPlugin._Plugin.config("3 - Raid Settings", "Stop Message", "", "Set end message");
            ConfigEntry<string> music = NecrotisPlugin._Plugin.config("3 - Raid Settings", "Music", "Necromancer", "Set music");
            ConfigEntry<string> weather = NecrotisPlugin._Plugin.config("3 - Raid Settings", "Weather", "", "Set environment");
            ConfigEntry<string> spawnData = NecrotisPlugin._Plugin.config("3 - Raid Settings", "Spawns", "Draugr:2:4:100,Skeleton:3:4:100,Abomination:1:30:50",
                "Set spawns, [prefabName]:[maxSpawned]:[spawnInterval]:[chance]");
            RandEventSystem.instance.m_events.Add(new RandomEvent()
            {
                m_name = "NecromancerRaid",
                m_enabled = true,
                m_duration = duration.Value,
                m_nearBaseOnly = false,
                m_pauseIfNoPlayerInArea = false,
                m_biome = biome.Value,
                m_requiredGlobalKeys = new List<string>() { requiredKey.Value},
                m_notRequiredGlobalKeys = new List<string>() { blockKey.Value },
                m_startMessage = startMsg.Value,
                m_endMessage = stopMsg.Value,
                m_forceMusic = music.Value,
                m_forceEnvironment = weather.Value,
                m_spawn = GetSpawnData(spawnData.Value)
            });
            duration.SettingChanged += (sender, args) =>
            {
                var raid = GetEvent();
                if (raid == null) return;

                raid.m_duration = duration.Value;
            };
            biome.SettingChanged += (sender, args) =>
            {
                var raid = GetEvent();
                if (raid == null) return;

                raid.m_biome = biome.Value;
            };
            requiredKey.SettingChanged += (sender, args) =>
            {
                var raid = GetEvent();
                if (raid == null) return;

                raid.m_requiredGlobalKeys = new List<string>() { requiredKey.Value };
            };
            blockKey.SettingChanged += (sender, args) =>
            {
                var raid = GetEvent();
                if (raid == null) return;

                raid.m_notRequiredGlobalKeys = new List<string>() { blockKey.Value };
            };
            startMsg.SettingChanged += (sender, args) =>
            {
                var raid = GetEvent();
                if (raid == null) return;
                raid.m_startMessage = startMsg.Value;
            };
            stopMsg.SettingChanged += (sender, args) =>
            {
                var raid = GetEvent();
                if (raid == null) return;
                raid.m_endMessage = stopMsg.Value;
            };
            music.SettingChanged += (sender, args) =>
            {
                var raid = GetEvent();
                if (raid == null) return;
                raid.m_forceMusic = music.Value;
            };
            weather.SettingChanged += (sender, args) =>
            {
                var raid = GetEvent();
                if (raid == null) return;
                raid.m_forceEnvironment = weather.Value;
            };
            spawnData.SettingChanged += (sender, args) =>
            {
                var raid = GetEvent();
                if (raid == null) return;
                raid.m_spawn = GetSpawnData(spawnData.Value);
            };
        }
    }

    private static RandomEvent? GetEvent()
    {
        if (!RandEventSystem.instance) return null;
        var raid = RandEventSystem.instance.m_events.Find(x => x.m_name == "NecromancerRaid");
        if (raid == null) return null;
        return raid;
    }

    private static List<SpawnSystem.SpawnData> GetSpawnData(string data)
    {
        List<SpawnSystem.SpawnData> output = new();
        foreach (var input in data.Split(','))
        {
            string[] info = input.Split(':');
            if (info.Length != 4) continue;
            GameObject prefab = ZNetScene.instance.GetPrefab(info[0]);
            if (!prefab) continue;
            output.Add(new SpawnSystem.SpawnData()
            {
                m_name = prefab.name,
                m_prefab = prefab,
                m_biome = Heightmap.Biome.All,
                m_maxSpawned = int.TryParse(info[1], out int max) ? max : 1,
                m_spawnInterval = float.TryParse(info[2], out float interval) ? interval : 4f,
                m_spawnChance = float.TryParse(info[3], out float chance) ? chance : 100f,
                m_groupSizeMax = data.Split(',').Length,
                m_spawnDistance = 20f,
                m_spawnRadiusMin = 5f,
                m_spawnRadiusMax = 20f,
                m_huntPlayer = true,
                m_maxLevel = 3,
                m_overrideLevelupChance = 0.5f,
                m_canSpawnCloseToPlayer = true,
                m_insidePlayerBase = false,
            });
        }
        return output;
    }
    
    [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.GetEvent))]
    private static class RandEventSystem_GetEvent_Patch
    {
        private static void Postfix(RandEventSystem __instance, ref RandomEvent? __result)
        {
            if (NecrotisPlugin._forceRaid.Value is NecrotisPlugin.Toggle.Off) return;
            if (__result == null) return;
            if (ZoneSystem.instance.CheckKey("defeated_necromancer")) return;
            if (ZoneSystem.instance.CheckKey("defeated_necromancer", GameKeyType.Player)) return;
            var necroRaid = __instance.m_events.Find(x => x.m_name == "NecromancerRaid");
            if (necroRaid == null) return;
            __result = necroRaid;
        }
    }

    [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.SetEnv))]
    private static class EnvMan_AlwaysDark_Patch
    {
        private static void Prefix(EnvMan __instance, ref EnvSetup env)
        {
            if (NecrotisPlugin._alwaysDark.Value is NecrotisPlugin.Toggle.Off) return;
            if (EnemyHud.instance.GetActiveBoss() != null) return;
            if (!__instance.m_forceEnv.IsNullOrWhiteSpace()) return;
            if (ZoneSystem.instance.CheckKey("defeated_necromancer")) return;
            if (ZoneSystem.instance.CheckKey("defeated_necromancer", GameKeyType.Player)) return;
            env = __instance.GetEnv("Darklands_dark");
        }
    }
}