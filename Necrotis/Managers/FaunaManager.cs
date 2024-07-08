using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Necrotis.Managers;

public class FaunaManager
{
    private static readonly List<Critter> m_critters = new();
    private static readonly List<Bird> m_birds = new();
    private static readonly Dictionary<string, AssetBundle> m_effects = new();
    private static readonly HashSet<Shader> m_cachedShaders = new();
    private static readonly int MainTex = Shader.PropertyToID("_MainTex");

    static FaunaManager()
    {
        Harmony harmony = new("org.bepinex.helpers.FaunaManager");
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ZNetScene), nameof(ZNetScene.Awake)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(FaunaManager), nameof(Load_Fauna_Patch))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(SpawnSystem), nameof(SpawnSystem.Awake)),
            postfix: new HarmonyMethod(
                AccessTools.DeclaredMethod(typeof(FaunaManager), nameof(Patch_SpawnSystem_Awake))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(ObjectDB), nameof(ObjectDB.Awake)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(FaunaManager),
                nameof(ObjectDB_RegisterCustomItems_Patch))));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Character), nameof(Character.Awake)),
            postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(FaunaManager), nameof(AddCreatureStatus))));
    }

    internal static void AddCreatureStatus(Character __instance)
    {
        if (!__instance) return;
        Critter critter = m_critters.Find(x => x.m_name == __instance.name.Replace("(Clone)", string.Empty));
        if (critter == null) return;
        if (critter.m_effect != null) __instance.GetSEMan().AddStatusEffect(critter.m_effect);
    }
    
    [HarmonyPriority(Priority.Last)]
    internal static void ObjectDB_RegisterCustomItems_Patch(ObjectDB __instance)
    {
        if (!ZNetScene.instance) return;
        foreach (Critter? critter in m_critters)
        {
            if (!critter.m_prefab) continue;

            if (critter.m_statusEffect != null)
            {
                CreatureStatus effect = ScriptableObject.CreateInstance<CreatureStatus>();
                effect.m_data = critter.m_statusEffect;
                effect.name = $"SE_{critter.m_name}";
                if (!__instance.m_StatusEffects.Contains(effect)) __instance.m_StatusEffects.Add(effect);
                critter.m_effect = effect;
            }
            
            if (!critter.m_prefab.TryGetComponent(out Humanoid humanoid)) continue;
            if (critter.m_characterData.m_damageMultiplier > 0f)
            {
                ManipulateItemArray(ZNetScene.instance, __instance, critter, ref humanoid.m_defaultItems);
                ManipulateItemArray(ZNetScene.instance, __instance, critter, ref humanoid.m_randomWeapon);
            }
            if (critter.m_damages.HaveDamage())
            {
                AddDamages(critter, ref humanoid.m_defaultItems);
                AddDamages(critter, ref humanoid.m_randomWeapon);
            }
            AddCharacterDrops(ZNetScene.instance, critter, critter.m_prefab);
            AddSaddle(__instance, critter);
            AddConsumeItems(__instance, critter);
            
            foreach (var item in humanoid.m_defaultItems)
            {
                if (item.GetComponent<ZNetView>())
                {
                    RegisterToObjectDB(__instance, item);
                    RegisterToZNetScene(ZNetScene.instance, item);
                }
            }
        }
        
        var saddleBeast = ZNetScene.instance.GetPrefab("SaddleBeast");
        var trophy = __instance.GetItemPrefab("TrophySaddleBeast");
        var saddle = saddleBeast.GetComponentInChildren<Sadle>(true);
        if (saddle) saddle.m_mountIcon = trophy.GetComponent<ItemDrop>().m_itemData.GetIcon();
    }

    private static void EditTame(ZNetScene __instance, Critter critter)
    {
        if (!critter.m_prefab) return;
        if (!critter.m_prefab.TryGetComponent(out Tameable component)) return;
        List<EffectList.EffectData> tameEffects = (from name in critter.m_tame.TamedEffects select __instance.GetPrefab(name) into effect where effect select new EffectList.EffectData()
        {
            m_prefab = effect, m_enabled = true,
        }).ToList();
        List<EffectList.EffectData> sootheEffects = (from name in critter.m_tame.SootheEffects select __instance.GetPrefab(name) into effect where effect select new EffectList.EffectData()
        {
            m_prefab = effect, m_enabled = true,
        }).ToList();
        List<EffectList.EffectData> petEffects = (from name in critter.m_tame.PetEffects select __instance.GetPrefab(name) into effect where effect select new EffectList.EffectData()
        {
            m_prefab = effect, m_enabled = true,
        }).ToList();
        component.m_tamedEffect = new() { m_effectPrefabs = tameEffects.ToArray() };
        component.m_sootheEffect = new() { m_effectPrefabs = sootheEffects.ToArray() };
        component.m_petEffect = new() { m_effectPrefabs = petEffects.ToArray() };
    }

    private static void AddSaddle(ObjectDB __instance, Critter critter)
    {
        if (critter.m_saddleItem.IsNullOrWhiteSpace()) return;
        GameObject item = __instance.GetItemPrefab(critter.m_saddleItem);
        if (!item) return;
        if (!item.TryGetComponent(out ItemDrop itemDrop)) return;
        if (!critter.m_prefab.TryGetComponent(out Tameable component)) return;
        component.m_saddleItem = itemDrop;
    }

    private static void RegisterToObjectDB(ObjectDB __instance, GameObject prefab)
    {
        if (!__instance.m_items.Contains(prefab)) __instance.m_items.Add(prefab);
        __instance.m_itemByHash[prefab.name.GetStableHashCode()] = prefab;
    }
    
    public static void RegisterAssetToZNetScene(string EffectName, AssetBundle bundle) => m_effects[EffectName] = bundle;

    internal static void Load_Fauna_Patch(ZNetScene __instance)
    {
        RegisterCustomAssets(__instance);
        
        SpawnSystemList root = NecrotisPlugin._Root.AddComponent<SpawnSystemList>();
        List<SpawnSystem.SpawnData> list = new List<SpawnSystem.SpawnData>();
        foreach (Critter critter in m_critters)
        {
            if (!critter.isCustom)
            {
                GameObject? prefab = GetPrefab(__instance, critter);
                if (prefab == null) continue;
                critter.m_prefab = prefab;
            }
            else
            {
                EditHumanoid(__instance, critter.m_prefab, critter);
                EditMonsterAI(__instance, critter.m_prefab, critter);
                EditTame(__instance, critter);
                CloneFootSteps(__instance, critter);
                RegisterToZNetScene(__instance, critter.m_prefab);
            }

            var data = critter.GetSpawnData();

            var biome = NecrotisPlugin._Plugin.config(critter.m_name, "Biome", data.m_biome, "Set biome");
            var interval = NecrotisPlugin._Plugin.config(critter.m_name, "Spawn Interval", data.m_spawnInterval, "Set spawn interval");
            var max = NecrotisPlugin._Plugin.config(critter.m_name, "Max Spawned", data.m_maxSpawned, "Set max amount spawned");

            biome.SettingChanged += (sender, args) =>
            {
                var info = list.Find(x => x.m_name == data.m_name);
                if (info == null) return;
                info.m_biome = biome.Value;
            };

            interval.SettingChanged += (sender, args) =>
            {
                var info = list.Find(x => x.m_name == data.m_name);
                if (info == null) return;
                info.m_spawnInterval = interval.Value;
            };

            max.SettingChanged += (sender, args) =>
            {
                var info = list.Find(x => x.m_name == data.m_name);
                if (info == null) return;
                info.m_maxSpawned = max.Value;
            };

            data.m_biome = biome.Value;
            data.m_spawnInterval = interval.Value;
            data.m_maxSpawned = max.Value;
            
            list.Add(data);
        }

        root.m_spawners = list;
        
        AddBirds(__instance, ref root);
    }

    private static void AddBirds(ZNetScene __instance, ref SpawnSystemList root)
    {
        var list = root.m_spawners;
        foreach (var bird in m_birds)
        {
            if (bird.m_prefab.TryGetComponent(out RandomFlyingBird randomFlyingBird))
            {
                randomFlyingBird.m_flyRange = bird.m_range.Value;
                randomFlyingBird.m_minAlt = 5f;
                randomFlyingBird.m_maxAlt = 20f;
                randomFlyingBird.m_speed = bird.m_speed.Value;
                randomFlyingBird.m_turnRate = 10f;
                randomFlyingBird.m_wpDuration = 1f;
                randomFlyingBird.m_flapDuration = 2f;
                randomFlyingBird.m_sailDuration = 0.2f;
                randomFlyingBird.m_landChance = 0.2f;
                randomFlyingBird.m_landDuration = 10f;
                randomFlyingBird.m_avoidDangerDistance = 10f;
                randomFlyingBird.m_noRandomFlightAtNight = false;
                randomFlyingBird.m_randomNoiseIntervalMin = 5f;
                randomFlyingBird.m_randomNoiseIntervalMax = 10f;
                randomFlyingBird.m_noNoiseAtNight = true;
                randomFlyingBird.m_singleModel = true;

                bird.m_range.SettingChanged += (sender, args) => randomFlyingBird.m_flyRange = bird.m_range.Value;
                bird.m_speed.SettingChanged += (sender, args) => randomFlyingBird.m_speed = bird.m_speed.Value;
            }

            if (bird.m_prefab.TryGetComponent(out Destructible destructible))
            {
                destructible.m_health = bird.m_health.Value;
                bird.m_health.SettingChanged += (sender, args) => destructible.m_health = bird.m_health.Value;
                List<EffectList.EffectData> effects = new();
                foreach (var name in bird.m_destroyedEffects)
                {
                    GameObject effect = __instance.GetPrefab(name);
                    if (!effect) continue;
                    effects.Add(new EffectList.EffectData()
                    {
                        m_prefab = effect,
                        m_enabled = true,
                    });
                }

                destructible.m_destroyedEffect = new EffectList() { m_effectPrefabs = effects.ToArray() };
            }

            if (bird.m_prefab.TryGetComponent(out DropOnDestroyed dropOnDestroyed))
            {
                List<DropTable.DropData> dropData = new();
                List<string> configs = new();
                foreach (var drop in bird.m_dropData)
                {
                    GameObject item = __instance.GetPrefab(drop.m_prefabName);
                    if (!item) continue;
                    configs.Add($"{item.name}:{drop.m_min}:{drop.m_max}:{drop.m_chance}");
                }

                var config = NecrotisPlugin._Plugin.config(bird.m_name, "Drops", String.Join(",", configs),
                    "Set drops, [item]:[min]:[max]:[weight]");
                foreach (var array in config.Value.Split(','))
                {
                    string[] info = array.Split(':');
                    if (info.Length < 4) continue;
                    GameObject item = __instance.GetPrefab(info[0]);
                    if (!item) continue;
                    dropData.Add(new DropTable.DropData()
                    {
                        m_item = item,
                        m_stackMin = int.TryParse(info[1], out int min) ? min : 1,
                        m_stackMax = int.TryParse(info[2], out int max) ? max : 1,
                        m_weight = float.TryParse(info[3], out float weight) ? weight : 1f
                    });
                }

                dropOnDestroyed.m_dropWhenDestroyed.m_drops = dropData;

                config.SettingChanged += (sender, args) =>
                {
                    List<DropTable.DropData> dataConfig = new();
                    foreach (var array in config.Value.Split(','))
                    {
                        string[] info = array.Split(':');
                        if (info.Length < 4) continue;
                        GameObject item = __instance.GetPrefab(info[0]);
                        if (!item) continue;
                        dataConfig.Add(new DropTable.DropData()
                        {
                            m_item = item,
                            m_stackMin = int.TryParse(info[1], out int min) ? min : 1,
                            m_stackMax = int.TryParse(info[2], out int max) ? max : 1,
                            m_weight = float.TryParse(info[3], out float weight) ? weight : 1f
                        });
                    }

                    dropOnDestroyed.m_dropWhenDestroyed.m_drops = dataConfig;
                };
            }

            if (!bird.m_shader.IsNullOrWhiteSpace())
            {
                GetShadersFromBundles();
                foreach (Renderer? renderer in bird.m_prefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material? material in renderer.sharedMaterials)
                    {
                        if (material == null) continue;
                        material.shader = GetShader(bird.m_shader, material.shader);
                        if (material.HasProperty(Hue))
                        {
                            ConfigEntry<float> hue = NecrotisPlugin._Plugin.config(bird.m_name, "Hue", material.GetFloat(Hue), new ConfigDescription("Set hue", new AcceptableValueRange<float>(-0.5f, 0.5f)));
                            hue.SettingChanged += (sender, args) => material.SetFloat(Hue, hue.Value);
                        }
                    }
                }
            }
            
            RegisterToZNetScene(__instance, bird.m_prefab);

            var data = bird.GetSpawnData();
            bird.m_biome.SettingChanged += (sender, args) =>
            {
                var info = list.Find(x => x.m_name == data.m_name);
                info.m_biome = bird.m_biome.Value;
            };
            bird.m_maxSpawned.SettingChanged += (sender, args) =>
            {
                var info = list.Find(x => x.m_name == data.m_name);
                info.m_maxSpawned = bird.m_maxSpawned.Value;
            };
            bird.m_spawnInterval.SettingChanged += (sender, args) =>
            {
                var info = list.Find(x => x.m_name == data.m_name);
                info.m_spawnInterval = bird.m_spawnInterval.Value;
            };
            
            list.Add(data);
        }

        root.m_spawners = list;
    }

    private static Shader GetShader(string name, Shader original)
    {
        foreach (var shader in m_cachedShaders)
        {
            if (shader.name == name) return shader;
        }

        return original;
    }

    private static void GetShadersFromBundles()
    {
        if (m_cachedShaders.Count == 0)
        {
            foreach (var bundle in Resources.FindObjectsOfTypeAll<AssetBundle>())
            {
                IEnumerable<Shader>? bundleShaders;
                try
                {
                    bundleShaders = bundle.isStreamedSceneAssetBundle && bundle
                        ? bundle.GetAllAssetNames().Select(bundle.LoadAsset<Shader>).Where(shader => shader != null)
                        : bundle.LoadAllAssets<Shader>();
                }
                catch (Exception)
                {
                    continue;
                }

                if (bundleShaders == null) continue;
                foreach (var shader in bundleShaders)
                {
                    m_cachedShaders.Add(shader);
                }
            }
        }
    }

    private static void CloneFootSteps(ZNetScene __instance, Critter critter)
    {
        if (!critter.m_prefab) return;
        if (critter.m_footStepClone.IsNullOrWhiteSpace()) return;
        if (!critter.m_prefab.TryGetComponent(out FootStep footStep)) return;
        GameObject original = __instance.GetPrefab(critter.m_footStepClone);
        if (!original) return;
        if (!original.TryGetComponent(out FootStep component)) return;
        footStep.m_effects = component.m_effects;
    }

    internal static void Patch_SpawnSystem_Awake(SpawnSystem __instance)
    {
        if (!NecrotisPlugin._Root.TryGetComponent(out SpawnSystemList spawnSystemList)) return;
        __instance.m_spawnLists.Add(spawnSystemList);
    }

    private static GameObject? GetPrefab(ZNetScene __instance, Critter critter)
    {
        GameObject prefab;
        if (critter.isClone)
        {
            GameObject? clone = CreateClone(__instance, critter);
            if (clone == null) return null;
            prefab = clone;
        }
        else
        {
            GameObject? original = GetOriginal(__instance, critter);
            if (original == null) return null;
            prefab = original;
        }

        return prefab;
    }

    private static GameObject? GetOriginal(ZNetScene __instance, Critter critter) => __instance.GetPrefab(critter.m_creatureName);

    private static GameObject? CreateClone(ZNetScene __instance, Critter critter)
    {
        GameObject original = __instance.GetPrefab(critter.m_creatureName);
        if (!original) return null;
        GameObject clone = Object.Instantiate(original, NecrotisPlugin._Root.transform, false);
        clone.name = critter.m_cloneName;
        
        ManipulateMaterial(__instance, clone, critter);

        if (critter.m_cloneName == "BlobIce")
        {
            var particles = Utils.FindChild(clone.transform, "particles");
            Object.Destroy(particles.gameObject);
        }

        EditHumanoid(__instance, clone, critter);
        EditMonsterAI(__instance, clone, critter);
        CreateNewRagdoll(__instance, critter, clone);
        
        RegisterToZNetScene(__instance, clone);
        return clone;
    }

    private static void EditMonsterAI(ZNetScene __instance, GameObject prefab, Critter critter)
    {
        if (!prefab.TryGetComponent(out MonsterAI monsterAI)) return;
        monsterAI.m_spawnMessage = critter.m_monsterData.m_spawnMessage;
        monsterAI.m_deathMessage = critter.m_monsterData.m_deathMessage;

        AddAlertedEffects(__instance, monsterAI, critter);
        AddIdleSounds(__instance, monsterAI, critter);
    }

    private static void AddConsumeItems(ObjectDB __instance, Critter critter)
    {
        if (!critter.m_prefab) return;
        if (!critter.m_prefab.TryGetComponent(out MonsterAI monsterAI)) return;
        List<ItemDrop> consumeItems = new();
        foreach (GameObject? prefab in critter.m_monsterData.m_consumeItems.Select(__instance.GetItemPrefab).Where(prefab => prefab))
        {
            if (!prefab.TryGetComponent(out ItemDrop component)) continue;
            consumeItems.Add(component);
        }

        monsterAI.m_consumeItems = consumeItems;

        List<string> items = monsterAI.m_consumeItems.Select(x => x.name).ToList();
        var config = NecrotisPlugin._Plugin.config(critter.m_name, "Consume Items", String.Join(",", items),
            "Set items creature can consume");
        List<ItemDrop> consumeConfigs = new();
        string[] itemConfig = config.Value.Split(',');
        foreach (var item in itemConfig.Select(__instance.GetItemPrefab).Where(prefab => prefab))
        {
            if (!item.TryGetComponent(out ItemDrop component)) continue;
            consumeConfigs.Add(component);
        }

        monsterAI.m_consumeItems = consumeConfigs;

        config.SettingChanged += (sender, args) =>
        {
            List<ItemDrop> itemConfigs = new();
            string[] configs = config.Value.Split(',');
            foreach (var item in configs.Select(__instance.GetItemPrefab).Where(prefab => prefab))
            {
                if (!item.TryGetComponent(out ItemDrop component)) continue;
                itemConfigs.Add(component);
            }

            monsterAI.m_consumeItems = itemConfigs;
        };
    }

    private static void AddIdleSounds(ZNetScene __instance, MonsterAI monsterAI, Critter critter)
    {
        if (critter.m_monsterData.m_idleSounds.Count <= 0) return;
        monsterAI.m_idleSound = new EffectList()
        {
            m_effectPrefabs = (from name in critter.m_monsterData.m_idleSounds select __instance.GetPrefab(name) into effect where effect select new EffectList.EffectData()
            {
                m_prefab = effect, 
                m_enabled = true
            }).ToArray()
        };
    }

    private static void AddAlertedEffects(ZNetScene __instance, MonsterAI monsterAI, Critter critter)
    {
        if (critter.m_monsterData.m_alertedEffects.Count <= 0) return;
        monsterAI.m_alertedEffects = new EffectList()
        {
            m_effectPrefabs = (from name in critter.m_monsterData.m_alertedEffects select __instance.GetPrefab(name) into effect where effect select new EffectList.EffectData()
            {
                m_prefab = effect, 
                m_enabled = true
            }).ToArray()
        };
    }

    private static void EditHumanoid(ZNetScene __instance, GameObject prefab, Critter critter)
    {
        if (!prefab.TryGetComponent(out Humanoid humanoid)) return;
        humanoid.m_name = $"$enemy_{critter.m_cloneName.ToLower()}";
        humanoid.m_defeatSetGlobalKey = $"defeated_{critter.m_cloneName.ToLower()}";
        if (critter.m_characterData.m_health > 0f) humanoid.m_health = critter.m_characterData.m_health;
        if (critter.m_characterData.m_faction is not Character.Faction.Players) humanoid.m_faction = critter.m_characterData.m_faction;
        humanoid.m_boss = critter.m_characterData.m_isBoss;
        humanoid.m_bossEvent = critter.m_characterData.m_bossEvent;
        AddCustomEffectList(critter.m_characterData.m_hitEffects, ref humanoid.m_hitEffects, __instance);
        AddCustomEffectList(critter.m_characterData.m_deathEffects, ref humanoid.m_deathEffects, __instance);
        AddCustomEffectList(critter.m_characterData.m_jumpEffects, ref humanoid.m_jumpEffects, __instance);
        AddCustomEffectList(critter.m_characterData.m_consumeItemEffects, ref humanoid.m_consumeItemEffects, __instance);
        AddCustomEffectList(critter.m_characterData.m_waterEffects, ref humanoid.m_waterEffects, __instance);
        AddCustomEffectList(critter.m_characterData.m_equipEffects, ref humanoid.m_equipEffects, __instance);
        EditAttackItems(__instance, critter, humanoid);

        var faction = NecrotisPlugin._Plugin.config(critter.m_name, "Faction", humanoid.m_faction, "Set faction");
        faction.SettingChanged += (sender, args) =>
        {
            humanoid.m_faction = faction.Value;
        };

        humanoid.m_faction = faction.Value;

        var health = NecrotisPlugin._Plugin.config(critter.m_name, "Health", humanoid.m_health, "Set Health");
        health.SettingChanged += (sender, args) =>
        {
            humanoid.m_health = health.Value;
        };
        humanoid.m_health = health.Value;
    }

    private static void EditAttackItems(ZNetScene __instance, Critter critter, Humanoid humanoid)
    {
        Dictionary<string, GameObject> defaultItems = new();
        if (!critter.m_replaceAttacks)
        {
            foreach (var defaultItem in humanoid.m_defaultItems)
            {
                defaultItems[defaultItem.name] = defaultItem;
            }
        }
        
        foreach (var attack in critter.m_attacks)
        {
            if (!attack.m_custom)
            {
                attack.Prefab = humanoid.m_defaultItems.FirstOrDefault(x => x.name == attack.m_prefabName);
            }
            if (attack.Prefab != null)
            {
                if (!attack.Prefab.TryGetComponent(out ItemDrop component)) continue;
                
                AddCustomEffectList(attack.m_triggerEffects, ref component.m_itemData.m_shared.m_attack.m_triggerEffect, __instance);
                AddCustomEffectList(attack.m_trailStartEffects, ref component.m_itemData.m_shared.m_attack.m_trailStartEffect, __instance); 
                AddCustomEffectList(attack.m_startEffects, ref component.m_itemData.m_shared.m_attack.m_startEffect, __instance);
                AddCustomEffectList(attack.m_hitEffects, ref component.m_itemData.m_shared.m_attack.m_hitEffect, __instance);
                defaultItems[attack.Prefab.name] = attack.Prefab;
            };
        }

        humanoid.m_defaultItems = defaultItems.Values.ToArray();
    }

    private static void AddDamages(Critter critter, ref GameObject[] array)
    {
        if (array.Length == 0) return;
        foreach (var prefab in array)
        {
            if (!prefab.TryGetComponent(out ItemDrop component)) continue;
            component.m_itemData.m_shared.m_damages.Add(critter.m_damages);
        }
    }

    private static void ManipulateItemArray(ZNetScene __instance, ObjectDB db, Critter critter, ref GameObject[] array)
    {
        if (array.Length == 0) return;
        Dictionary<string, GameObject> m_items = new();
        foreach (var prefab in array)
        {
            m_items[prefab.name] = prefab;
        }

        Dictionary<string, GameObject> m_clones = new();
        foreach (var kvp in m_items)
        {
            GameObject clone = Object.Instantiate(kvp.Value, NecrotisPlugin._Root.transform, false);
            clone.name = kvp.Value.name + "_clone";
            if (!clone.TryGetComponent(out ItemDrop component)) continue;
            component.m_itemData.m_shared.m_damages.Modify(critter.m_characterData.m_damageMultiplier);
            component.m_itemData.m_dropPrefab = kvp.Value;
            RegisterToZNetScene(__instance, clone);
            RegisterToObjectDB(db, clone);
            m_clones[kvp.Key] = clone;
        }

        GameObject[] list = array.Select(prefab => m_clones[prefab.name]).ToArray();
        array = list;
    }

    private static void AddCharacterDrops(ZNetScene __instance, Critter critter, GameObject clone)
    {
        if (!clone.TryGetComponent(out CharacterDrop component)) return;
        if (critter.m_dropData.Count == 0) return;

        List<CharacterDrop.Drop> m_drops = new();
        foreach (Critter.DropData? data in critter.m_dropData)
        {
            GameObject prefab = __instance.GetPrefab(data.m_prefabName);
            if (!prefab) continue;
            m_drops.Add(new()
            {
                m_prefab = prefab,
                m_amountMin = data.m_min,
                m_amountMax = data.m_max,
                m_chance = data.m_chance,
                m_levelMultiplier = data.m_levelMultiplier,
                m_onePerPlayer = data.m_onePerPlayer,
                m_dontScale = data.m_doNotScale
            });
        }

        component.m_drops = m_drops;

        List<string> configs = new();
        foreach (var drop in component.m_drops)
        {
            configs.Add($"{drop.m_prefab.name}:{drop.m_amountMin}:{drop.m_amountMax}:{drop.m_chance}");
        }

        var itemConfig = NecrotisPlugin._Plugin.config(critter.m_name, "Drops", String.Join(",", configs), "Set the character drops, [prefabName]:[min]:[max]:[chance], ... ,");
        itemConfig.SettingChanged += (sender, args) =>
        {
            string[] array = itemConfig.Value.Split(',');
            List<CharacterDrop.Drop> n_drops = new();
            foreach (var data in array)
            {
                string[] info = data.Split(':');
                if (info.Length < 4) continue;
                var name = info[0];
                var min = info[1];
                var max = info[2];
                var chance = info[3];

                var prefab = __instance.GetPrefab(name);
                if (!prefab) continue;

                n_drops.Add(new CharacterDrop.Drop()
                {
                    m_prefab = prefab,
                    m_amountMin = int.TryParse(min, out int minimum) ? minimum : 0,
                    m_amountMax = int.TryParse(max, out int maximum) ? maximum : 1,
                    m_chance = float.TryParse(chance, out float cha) ? cha : 1f
                });
            }

            component.m_drops = n_drops;
        };
        
        AddConfigDrops(__instance, component, itemConfig);
    }

    private static void AddConfigDrops(ZNetScene __instance, CharacterDrop component, ConfigEntry<string> itemConfig)
    {
        string[] array = itemConfig.Value.Split(',');
        List<CharacterDrop.Drop> n_drops = new();
        foreach (var data in array)
        {
            string[] info = data.Split(':');
            if (info.Length < 4) continue;
            var name = info[0];
            var min = info[1];
            var max = info[2];
            var chance = info[3];

            var prefab = __instance.GetPrefab(name);
            if (!prefab) continue;

            n_drops.Add(new CharacterDrop.Drop()
            {
                m_prefab = prefab,
                m_amountMin = int.TryParse(min, out int minimum) ? minimum : 0,
                m_amountMax = int.TryParse(max, out int maximum) ? maximum : 1,
                m_chance = float.TryParse(chance, out float cha) ? cha : 1f
            });
        }

        component.m_drops = n_drops;
    }

    private static void ManipulateMaterial(ZNetScene __instance, GameObject clone, Critter critter)
    {
        SkinnedMeshRenderer renderer = clone.GetComponentInChildren<SkinnedMeshRenderer>();

        List<Material> materials = new();
        foreach (var material in renderer.materials)
        {
            Material mat = new Material(material)
            {
                color = critter.m_color
            };
            if (critter.m_texture != null)
            {
                mat.SetTexture(MainTex, critter.m_texture);
            }
            materials.Add(mat);
        }
        
        renderer.materials = materials.ToArray();
        renderer.sharedMaterials = materials.ToArray();
    }

    private static void CreateNewRagdoll(ZNetScene __instance, Critter critter, GameObject clone)
    {
        if (critter.m_texture == null) return;
        
        if (!clone.TryGetComponent(out Humanoid humanoid)) return;
        List<EffectList.EffectData> data = new();
        if (humanoid.m_deathEffects == null) return;
        foreach (EffectList.EffectData effect in humanoid.m_deathEffects.m_effectPrefabs)
        {
            if (effect.m_prefab == null) continue;
            if (!effect.m_prefab.TryGetComponent(out Ragdoll component))
            {
                data.Add(effect);
            }
            else
            {
                var clonedEffect = Object.Instantiate(effect.m_prefab, NecrotisPlugin._Root.transform, false);
                clonedEffect.name = clone.name + "_ragdoll";

                List<Material> newRagdollMats = new();
                if (!component.m_mainModel) continue;
                foreach (var ragMat in component.m_mainModel.materials)
                {
                    Material newRagMat = new Material(ragMat);
                    if (newRagMat.HasProperty(MainTex)) newRagMat.SetTexture(MainTex, critter.m_texture);
                    newRagdollMats.Add(newRagMat);
                }

                var newMainModel = clonedEffect.GetComponent<Ragdoll>().m_mainModel;
                if (!newMainModel) continue;
                newMainModel.materials = newRagdollMats.ToArray();
                newMainModel.sharedMaterials = newRagdollMats.ToArray();
                        
                RegisterToZNetScene(__instance, clonedEffect);
                EffectList.EffectData cloneEffect = new EffectList.EffectData()
                {
                    m_prefab = clonedEffect,
                    m_enabled = true
                };
                data.Add(cloneEffect);
            }
        }

        humanoid.m_deathEffects = new EffectList() { m_effectPrefabs = data.ToArray() };
    }

    private static void RegisterCustomAssets(ZNetScene __instance)
    {
        foreach (GameObject prefab in m_effects.Select(kvp => kvp.Value.LoadAsset<GameObject>(kvp.Key)))
        {
            RegisterToZNetScene(__instance, prefab);
        }

        RegisterProjectiles(__instance);
    }

    private static void RegisterProjectiles(ZNetScene __instance)
    {
        foreach (var projectile in m_projectiles)
        {
            GameObject prefab = projectile.m_bundle.LoadAsset<GameObject>(projectile.m_name);
            if (!prefab) continue;

            if (prefab.TryGetComponent(out Projectile component))
            {
                if (!projectile.m_spawnOnHit.IsNullOrWhiteSpace())
                {
                    GameObject spawn = __instance.GetPrefab(projectile.m_spawnOnHit);
                    if (spawn)
                    {
                        component.m_spawnOnHit = spawn;
                    }

                    component.m_spawnOnHitChance = projectile.m_spawnOnHitChance;
                }

                if (projectile.m_hitEffects.Count > 0)
                {
                    component.m_hitEffects = new EffectList()
                    {
                        m_effectPrefabs = (from name in projectile.m_hitEffects select __instance.GetPrefab(name) into fx where fx select new EffectList.EffectData()
                        {
                            m_prefab = fx, m_enabled = true
                        }).ToArray()
                    };
                }

                if (projectile.m_hitWaterEffects.Count > 0)
                {
                    component.m_hitWaterEffects = new EffectList()
                    {
                        m_effectPrefabs = (from name in projectile.m_hitWaterEffects select __instance.GetPrefab(name) into fx where fx select new EffectList.EffectData()
                        {
                            m_prefab = fx, m_enabled = true
                        }).ToArray()
                    };
                }

                if (projectile.m_randomSpawnOnHit.Count > 0)
                {
                    component.m_randomSpawnOnHit = projectile.m_randomSpawnOnHit.Select(__instance.GetPrefab).Where(spawn => spawn).ToList();
                }
            }

            RegisterToZNetScene(__instance, prefab);
        }
        
        foreach (var projectile in m_projectileSpawns)
        {
            GameObject prefab = projectile.m_bundle.LoadAsset<GameObject>(projectile.m_name);
            if (!prefab) continue;

            foreach (var critter in projectile.m_spawnPrefabs)
            {
                CreateFriendly(critter);
                CreateClone(critter);
            }
            if (prefab.TryGetComponent(out SpawnAbility component))
            {
                component.m_spawnPrefab = projectile.m_spawnPrefabs.Select(__instance.GetPrefab).Where(monster => monster).ToArray();
                component.m_spawnEffects.m_effectPrefabs = (from effect in projectile.m_spawnEffects select __instance.GetPrefab(effect) into go where go select new EffectList.EffectData() { m_prefab = go, m_enabled = true, }).ToArray();
                component.m_preSpawnEffects.m_effectPrefabs = (from effect in projectile.m_preSpawnEffects select __instance.GetPrefab(effect) into go where go select new EffectList.EffectData() { m_prefab = go, m_enabled = true, }).ToArray();
            }
            
            if (prefab.GetComponent<ZNetView>()) RegisterToZNetScene(__instance, prefab);
        }
    }

    private static void CreateClone(string critter)
    {
        if (!critter.EndsWith("_Clone")) return;
        GameObject original = ZNetScene.instance.GetPrefab(critter.Replace("_Clone", string.Empty));
        if (!original) return;
        GameObject clone = Object.Instantiate(original, NecrotisPlugin._Root.transform, false);
        clone.name = critter;
        if (clone.TryGetComponent(out Humanoid humanoid))
        {
            humanoid.m_faction = Character.Faction.Boss;
        }
        RegisterToZNetScene(ZNetScene.instance, clone);
    }
    
    private static void CreateFriendly(string critter)
    {
        if (!critter.EndsWith("_Friendly")) return;
        GameObject Skelet = ZNetScene.instance.GetPrefab("Skeleton_Friendly");
        if (!Skelet) return;
        if (!Skelet.TryGetComponent(out Tameable component)) return;
        
        GameObject original = ZNetScene.instance.GetPrefab(critter.Replace("_Friendly", string.Empty));
        if (!original) return;
        GameObject clone = Object.Instantiate(original, NecrotisPlugin._Root.transform, false);
        clone.name = critter;
        if (!clone.TryGetComponent(out Humanoid humanoid)) return;
        humanoid.m_faction = Character.Faction.Players;
        humanoid.m_health *= 2;
        if (!clone.TryGetComponent(out Tameable tameable)) tameable = clone.AddComponent<Tameable>();
        AddCustomEffectList(new (){"vfx_creature_soothed"}, ref tameable.m_sootheEffect, ZNetScene.instance);
        AddCustomEffectList(new() {"fx_skeleton_pet"}, ref tameable.m_petEffect, ZNetScene.instance);
        tameable.m_unSummonEffect = humanoid.m_deathEffects;
        tameable.m_levelUpOwnerSkill = Skills.SkillType.BloodMagic;
        tameable.m_levelUpFactor = 0.5f;
        tameable.m_randomStartingName = component.m_randomStartingName;
        tameable.m_commandable = true;
        tameable.m_startsTamed = true;
        if (clone.TryGetComponent(out CharacterDrop characterDrop)) Object.Destroy(characterDrop);
        if (clone.TryGetComponent(out MonsterAI monsterAI)) monsterAI.m_attackPlayerObjects = false;
        RegisterToZNetScene(ZNetScene.instance, clone);
    }

    private static void AddCustomEffectList(List<string> effectNames, ref EffectList list, ZNetScene __instance)
    {
        if (effectNames.Count > 0)
        {
            list = new EffectList
            {
                m_effectPrefabs = (from effect in effectNames select __instance.GetPrefab(effect) into prefab where prefab select new EffectList.EffectData()
                {
                    m_prefab = prefab, 
                    m_enabled = true,
                }).ToArray()
            };
        }
    }
    
    private static void RegisterToZNetScene(ZNetScene __instance, GameObject prefab)
    {
        if (!__instance.m_prefabs.Contains(prefab)) __instance.m_prefabs.Add(prefab);
        if (!__instance.m_namedPrefabs.ContainsKey(prefab.name.GetStableHashCode()))
        {
            __instance.m_namedPrefabs[prefab.name.GetStableHashCode()] = prefab;
        }
    }

    private static readonly List<ProjectileData> m_projectiles = new();
    private static readonly List<ProjectileSpawnAbility> m_projectileSpawns = new();
    private static readonly int Hue = Shader.PropertyToID("_Hue");

    public class ProjectileSpawnAbility
    {
        public readonly string m_name;
        public readonly AssetBundle m_bundle;
        public readonly List<string> m_spawnPrefabs = new();
        public readonly List<string> m_spawnEffects = new();
        public readonly List<string> m_preSpawnEffects = new();

        public ProjectileSpawnAbility(string name, AssetBundle bundle)
        {
            m_name = name;
            m_bundle = bundle;
            m_projectileSpawns.Add(this);
        }

        public void AddSpawn(string name) => m_spawnPrefabs.Add(name);
        public void AddSpawnEffect(string name) => m_spawnEffects.Add(name);
        public void AddPreSpawnEffect(string name) => m_preSpawnEffects.Add(name);
    }
    public class ProjectileData
    {
        public readonly string m_name;
        public readonly List<string> m_hitEffects = new();
        public readonly List<string> m_hitWaterEffects = new();
        public string m_spawnOnHit = "";
        public float m_spawnOnHitChance = 1f;
        public readonly List<string> m_randomSpawnOnHit = new();
        public readonly AssetBundle m_bundle;

        public ProjectileData(string name, AssetBundle bundle)
        {
            m_name = name;
            m_bundle = bundle;
            m_projectiles.Add(this);
        }

        public void AddHitEffect(string effectName) => m_hitEffects.Add(effectName);
        public void AddHitWaterEffect(string effectName) => m_hitWaterEffects.Add(effectName);
        public void AddSpawnOnHit(string name) => m_spawnOnHit = name;
        public void SetSpawnOnHitChance(float value) => m_spawnOnHitChance = value;
        public void AddRandomSpawnOnHit(string name) => m_randomSpawnOnHit.Add(name);
    }

    public class Bird
    {
        public readonly string m_name = "";
        public readonly GameObject m_prefab;
        public ConfigEntry<int> m_health = null!;
        public ConfigEntry<float> m_range = null!;
        public ConfigEntry<float> m_speed = null!;
        public ConfigEntry<Heightmap.Biome> m_biome = null!;
        public readonly Heightmap.BiomeArea m_biomeArea = Heightmap.BiomeArea.Everything;
        public ConfigEntry<int> m_maxSpawned = null!;
        public ConfigEntry<float> m_spawnInterval = null!;
        public float m_spawnDistance = 50f;
        public float m_spawnRadiusMin = 10f;
        public float m_spawnRadiusMax = 100f;
        public string m_requiredGlobalKey = "";
        public List<string> m_requiredEnvironments = new();
        public int m_groupSizeMin = 0;
        public int m_groupSizeMax = 1;
        public float m_groupRadius = 50f;
        public bool m_spawnAtNight = true;
        public bool m_spawnAtDay = true;
        public float m_minAltitude = -1000f;
        public float m_maxAltitude = 1000f;
        public float m_minTilt = 0f;
        public float m_maxTilt = 50f;
        public bool m_huntPlayer = false;
        public float m_groundOffset = 0.5f;
        public int m_maxLevel = 3;
        public int m_minLevel = 1;
        public float m_levelUpMinCenterDistance = 1f;
        public float m_overrideLevelUpChance = 0f;
        public bool m_foldout = false;
        public readonly List<string> m_destroyedEffects = new();
        public readonly List<Critter.DropData> m_dropData = new();
        public string m_shader = "";

        public Bird(string name, AssetBundle assetBundle)
        {
            m_prefab = assetBundle.LoadAsset<GameObject>(name);
            m_name = name;
            m_birds.Add(this);
        }

        public void SetShader(string shader) => m_shader = shader;

        public void AddDestroyedEffect(string name) => m_destroyedEffects.Add(name);

        public void SetHealth(int health) =>
            m_health = NecrotisPlugin._Plugin.config(m_name, "Health", health, "Set health");

        public void SetRange(float range) =>
            m_range = NecrotisPlugin._Plugin.config(m_name, "Range", range, "Set fly range");

        public void SetSpeed(float speed) =>
            m_speed = NecrotisPlugin._Plugin.config(m_name, "Speed", speed, "Set fly speed");
        
        public void SetBiome(Heightmap.Biome biome) => m_biome = NecrotisPlugin._Plugin.config(m_name, "Biome", biome, "Set biome");

        public void SetMaxSpawned(int max) =>
            m_maxSpawned = NecrotisPlugin._Plugin.config(m_name, "Max Spawned", max, "Set max spawned");

        public void SetSpawnInterval(float interval) => m_spawnInterval =
            NecrotisPlugin._Plugin.config(m_name, "Spawn Interval", interval, "Set interval");
        
        public void AddDrop(string prefabName, int min, int max, float chance, bool onePerPlayer = false, bool levelMultiplier = false, bool doNotScale = false)
        {
            Critter.DropData data = new Critter.DropData()
            {
                m_prefabName = prefabName,
                m_min = min,
                m_max = max,
                m_chance = chance,
                m_onePerPlayer = onePerPlayer,
                m_levelMultiplier = levelMultiplier,
                m_doNotScale = doNotScale,
            };
            m_dropData.Add(data);
        }
        
        public SpawnSystem.SpawnData GetSpawnData()
        {
            return new()
            {
                m_name = m_name,
                m_enabled = true,
                m_prefab = m_prefab,
                m_biome = m_biome.Value,
                m_biomeArea = m_biomeArea,
                m_maxSpawned = m_maxSpawned.Value,
                m_spawnInterval = m_spawnInterval.Value,
                m_spawnDistance = m_spawnDistance,
                m_spawnRadiusMin = m_spawnRadiusMin,
                m_spawnRadiusMax = m_spawnRadiusMax,
                m_requiredGlobalKey = m_requiredGlobalKey,
                m_requiredEnvironments = m_requiredEnvironments,
                m_groupSizeMin = m_groupSizeMin,
                m_groupSizeMax = m_groupSizeMax,
                m_groupRadius = m_groupRadius,
                m_spawnAtNight = m_spawnAtNight,
                m_spawnAtDay = m_spawnAtDay,
                m_minAltitude = m_minAltitude,
                m_maxAltitude = m_maxAltitude,
                m_minTilt = m_minTilt,
                m_maxTilt = m_maxTilt,
                m_huntPlayer = m_huntPlayer,
                m_groundOffset = m_groundOffset,
                m_maxLevel = m_maxLevel,
                m_minLevel = m_minLevel,
                m_levelUpMinCenterDistance = m_levelUpMinCenterDistance,
                m_overrideLevelupChance = m_overrideLevelUpChance,
                m_foldout = m_foldout
            };
        }

    }

    public class Critter
    {
        public string m_saddleItem = "";
        public readonly bool isCustom;
        public readonly bool isClone;
        public Color32 m_color;
        public Texture? m_texture;
        public readonly string m_cloneName;
        public readonly string m_creatureName;
        public readonly string m_name;
        public readonly bool m_enabled = true;
        public GameObject m_prefab = null!;
        public Heightmap.Biome m_biome = Heightmap.Biome.None;
        public readonly Heightmap.BiomeArea m_biomeArea = Heightmap.BiomeArea.Everything;
        public int m_maxSpawned = 1;
        public float m_spawnInterval = 100f;
        public float m_spawnDistance = 50f;
        public float m_spawnRadiusMin = 10f;
        public float m_spawnRadiusMax = 100f;
        public string m_requiredGlobalKey = "";
        public List<string> m_requiredEnvironments = new();
        public int m_groupSizeMin = 0;
        public int m_groupSizeMax = 1;
        public float m_groupRadius = 50f;
        public bool m_spawnAtNight = true;
        public bool m_spawnAtDay = true;
        public float m_minAltitude = -1000f;
        public float m_maxAltitude = 1000f;
        public float m_minTilt = 0f;
        public float m_maxTilt = 50f;
        public bool m_huntPlayer = false;
        public float m_groundOffset = 0.5f;
        public int m_maxLevel = 3;
        public int m_minLevel = 1;
        public float m_levelUpMinCenterDistance = 1f;
        public float m_overrideLevelUpChance = 0f;
        public bool m_foldout = false;

        public void SetMainTexture(Texture texture) => m_texture = texture;
        public void SetSaddleItem(string item) => m_saddleItem = item;

        public readonly CharacterData m_characterData = new();
        public class CharacterData
        {
            public Character.Faction m_faction = Character.Faction.Players;
            public bool m_isBoss;
            public string m_bossEvent = "";
            public float m_speed;
            public bool m_tolerateWater;
            public bool m_tolerateFire;
            public bool m_tolerateSmoke;
            public bool m_tolerateTar;
            public float m_health;
            public HitData.DamageModifiers m_damageModifiers = new();
            public readonly List<string> m_hitEffects = new();
            public readonly List<string> m_deathEffects = new();
            public readonly List<string> m_jumpEffects = new();
            public readonly List<string> m_consumeItemEffects = new();
            public readonly List<string> m_waterEffects = new();
            public readonly List<string> m_equipEffects = new();
            public float m_damageMultiplier;
        }

        public readonly Tame m_tame = new();

        public void AddTameEffect(string effectName) => m_tame.TamedEffects.Add(effectName);
        public void AddSootheEffect(string effectName) => m_tame.SootheEffects.Add(effectName);
        public void AddPetEffect(string effectName) => m_tame.PetEffects.Add(effectName);
        public void AddConsumeEffect(string effectName) => m_characterData.m_consumeItemEffects.Add(effectName);
        public void AddWaterEffect(string effectName) => m_characterData.m_waterEffects.Add(effectName);
        public void SetSaddleIcon(Sprite sprite) => m_tame.SaddleIcon = sprite;
        public class Tame
        {
            public float FedDuration;
            public float TameTime;
            public bool StartTamed;
            public readonly List<string> TamedEffects = new();
            public readonly List<string> SootheEffects = new();
            public readonly List<string> PetEffects = new();
            public bool Command;
            public List<string> UnSummonEffects = new();
            public Skills.SkillType LevelUpSkill = Skills.SkillType.Ride;
            public Sprite? SaddleIcon;
        }

        public void MultiplyDefaultItems(float amount) => m_characterData.m_damageMultiplier = amount;

        public HitData.DamageTypes m_damages = new();

        public void AddDamage(HitData.DamageType type, float amount)
        {
            switch (type)
            {
                case HitData.DamageType.Blunt:
                    m_damages.m_blunt += amount;
                    break;
                case HitData.DamageType.Slash:
                    m_damages.m_slash += amount;
                    break;
                case HitData.DamageType.Pierce:
                    m_damages.m_pierce += amount;
                    break;
                case HitData.DamageType.Chop:
                    m_damages.m_chop += amount;
                    break;
                case HitData.DamageType.Pickaxe:
                    m_damages.m_pickaxe += amount;
                    break;
                case HitData.DamageType.Fire:
                    m_damages.m_fire += amount;
                    break;
                case HitData.DamageType.Frost:
                    m_damages.m_frost += amount;
                    break;
                case HitData.DamageType.Lightning:
                    m_damages.m_lightning += amount;
                    break;
                case HitData.DamageType.Poison:
                    m_damages.m_poison += amount;
                    break;
                case HitData.DamageType.Spirit:
                    m_damages.m_spirit += amount;
                    break;
            }
        }

        public void AddHitEffect(string effectName) => m_characterData.m_hitEffects.Add(effectName);
        public void AddDeathEffect(string effectName) => m_characterData.m_deathEffects.Add(effectName);
        public void AddJumpEffect(string effectName) => m_characterData.m_jumpEffects.Add(effectName);
        public void AddEquipEffect(string effectName) => m_characterData.m_equipEffects.Add(effectName);
        public void SetBoss(bool enable) => m_characterData.m_isBoss = enable;
        public void SetBossEvent(string name) => m_characterData.m_bossEvent = name;
        public readonly MonsterAIData m_monsterData = new();
        public class MonsterAIData
        {
            public readonly string m_spawnMessage = "";
            public readonly string m_deathMessage = "";

            public readonly List<string> m_alertedEffects = new();
            public readonly List<string> m_idleSounds = new ();
            public readonly List<string> m_consumeItems = new();
        }

        public void AddConsumeItem(string name) => m_monsterData.m_consumeItems.Add(name);
        public void AddAlertedEffect(string effectName) => m_monsterData.m_alertedEffects.Add(effectName);
        public void AddIdleSound(string effectName) => m_monsterData.m_idleSounds.Add(effectName);

        public readonly List<Attack> m_attacks = new();
        public bool m_replaceAttacks;
        
        public void ReplaceAttack(string attackName, AssetBundle bundleName, List<string>? triggerEffects = null, List<string>? trailEffects = null)
        {
            Attack attack = new(attackName, bundleName);
            if (triggerEffects != null) attack.m_triggerEffects = triggerEffects;
            if (trailEffects != null) attack.m_trailStartEffects = trailEffects;
            m_attacks.Add(attack);
            m_replaceAttacks = true;
        }

        public void EditAttack(string attackName, 
            List<string>? startEffects = null, 
            List<string>? triggerEffects = null,
            List<string>? trailEffects = null,
            List<string>? hitEffects = null)
        {
            Attack attack = new(attackName);
            if (triggerEffects != null) attack.m_triggerEffects = triggerEffects;
            if (trailEffects != null) attack.m_trailStartEffects = trailEffects;
            if (startEffects != null) attack.m_startEffects = startEffects;
            if (hitEffects != null) attack.m_hitEffects = hitEffects;
            m_attacks.Add(attack);
        }
        
        public class Attack
        {
            public GameObject? Prefab;
            public readonly bool m_custom;
            public string m_prefabName;
            public List<string> m_triggerEffects = new();
            public List<string> m_trailStartEffects = new();
            public List<string> m_startEffects = new();
            public List<string> m_hitEffects = new();
            
            public Attack(string prefabName, AssetBundle bundle)
            {
                Prefab = bundle.LoadAsset<GameObject>(prefabName);
                m_prefabName = prefabName;
                m_custom = true;
            }

            public Attack(string prefabName)
            {
                m_prefabName = prefabName;
            }
        }

        public string m_footStepClone = "";
        public void CloneFootStepsFrom(string prefabName) => m_footStepClone = prefabName;

        public readonly List<DropData> m_dropData = new();
            
        public class DropData
        {
            public string m_prefabName = null!;
            public int m_min;
            public int m_max;
            public float m_chance;
            public bool m_onePerPlayer;
            public bool m_levelMultiplier;
            public bool m_doNotScale;
        }

        public void AddDrop(string prefabName, int min, int max, float chance, bool onePerPlayer = false, bool levelMultiplier = false, bool doNotScale = false)
        {
            DropData data = new DropData()
            {
                m_prefabName = prefabName,
                m_min = min,
                m_max = max,
                m_chance = chance,
                m_onePerPlayer = onePerPlayer,
                m_levelMultiplier = levelMultiplier,
                m_doNotScale = doNotScale,
            };
            m_dropData.Add(data);
        }
        
        public Critter(string creatureName, string name, Color32 color = new(), bool clone = false, string cloneName = "")
        {
            m_creatureName = creatureName;
            m_name = name;

            m_color = color;
            isClone = clone;
            m_cloneName = cloneName;

            m_statusEffect = new StatusEffectData()
            {
                m_damageMultiplier = NecrotisPlugin._Plugin.config(name, "Damage Multiplier", 1f,
                    new ConfigDescription("Set damage multiplier", new AcceptableValueRange<float>(0f, 10f))),
                m_armorMultiplier = NecrotisPlugin._Plugin.config(name, "Damage Taken", 1f,
                    new ConfigDescription("Set the damage reduction multiplier",
                        new AcceptableValueRange<float>(0f, 1f)))
            };
            
            m_critters.Add(this);
        }

        public Critter(string creatureName, AssetBundle bundle)
        {
            m_creatureName = creatureName;
            m_cloneName = creatureName;
            m_name = creatureName;
            m_prefab = bundle.LoadAsset<GameObject>(creatureName);
            isCustom = true;
            
            m_statusEffect = new StatusEffectData()
            {
                m_damageMultiplier = NecrotisPlugin._Plugin.config(creatureName, "Damage Multiplier", 1f,
                    new ConfigDescription("Set damage multiplier", new AcceptableValueRange<float>(0f, 10f))),
                m_armorMultiplier = NecrotisPlugin._Plugin.config(creatureName, "Armor Multiplier", 1f,
                    new ConfigDescription("Set the damage reduction multiplier",
                        new AcceptableValueRange<float>(0f, 1f)))
            };
            
            m_critters.Add(this);
        }

        public readonly StatusEffectData? m_statusEffect;
        public CreatureStatus? m_effect;

        public class StatusEffectData
        {
            public ConfigEntry<float> m_damageMultiplier = null!;
            public ConfigEntry<float> m_armorMultiplier = null!;
        }

        public SpawnSystem.SpawnData GetSpawnData()
        {
            return new()
            {
                m_name = m_name,
                m_enabled = m_enabled,
                m_prefab = m_prefab,
                m_biome = m_biome,
                m_biomeArea = m_biomeArea,
                m_maxSpawned = m_maxSpawned,
                m_spawnInterval = m_spawnInterval,
                m_spawnDistance = m_spawnDistance,
                m_spawnRadiusMin = m_spawnRadiusMin,
                m_spawnRadiusMax = m_spawnRadiusMax,
                m_requiredGlobalKey = m_requiredGlobalKey,
                m_requiredEnvironments = m_requiredEnvironments,
                m_groupSizeMin = m_groupSizeMin,
                m_groupSizeMax = m_groupSizeMax,
                m_groupRadius = m_groupRadius,
                m_spawnAtNight = m_spawnAtNight,
                m_spawnAtDay = m_spawnAtDay,
                m_minAltitude = m_minAltitude,
                m_maxAltitude = m_maxAltitude,
                m_minTilt = m_minTilt,
                m_maxTilt = m_maxTilt,
                m_huntPlayer = m_huntPlayer,
                m_groundOffset = m_groundOffset,
                m_maxLevel = m_maxLevel,
                m_minLevel = m_minLevel,
                m_levelUpMinCenterDistance = m_levelUpMinCenterDistance,
                m_overrideLevelupChance = m_overrideLevelUpChance,
                m_foldout = m_foldout
            };
        }
    }
}