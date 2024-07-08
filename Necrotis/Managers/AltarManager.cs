using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Necrotis.Behaviors;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Necrotis.Managers;

public static class AltarManager
{
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    [HarmonyPriority(Priority.LowerThanNormal)]
    [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
    private static class Finalize_Altar
    {
        private static void Postfix(ObjectDB __instance)
        {
            if (!ZNetScene.instance) return;
            CloneAssets(__instance);

            GameObject item = __instance.GetItemPrefab(NecrotisPlugin._offeringItem.Value);
            if (!item) item = __instance.GetItemPrefab("DragonEgg");

            if (!item.TryGetComponent(out ItemDrop offering)) return;
            
            GameObject altar = ZNetScene.instance.GetPrefab("Necromancer_Pedestal");
            if (!altar) return;

            if (!altar.TryGetComponent(out OfferingBowl offeringBowl)) return;

            GameObject prefab = ZNetScene.instance.GetPrefab("Necromancer_Altar");
            if (!prefab) return;

            if (!prefab.TryGetComponent(out ItemStand itemStand)) return;
            itemStand.m_supportedItems.Clear();
            itemStand.m_supportedItems.Add(offering);
            itemStand.m_effects.m_effectPrefabs = GetEffects(new() { "vfx_pickable_pick", "sfx_pickable_pick" });
            itemStand.m_destroyEffects.m_effectPrefabs = GetEffects(new() { "fx_totem_destroyed" });

            offeringBowl.m_bossItem = offering;
            offeringBowl.m_itemstandMaxRange = NecrotisPlugin._itemStandMaxRange.Value;

            offeringBowl.m_fuelAddedEffects.m_effectPrefabs = GetEffects(new() { "sfx_offering", "vfx_offering" });
            offeringBowl.m_spawnBossStartEffects.m_effectPrefabs = GetEffects(new() { "vfx_prespawn_fader", "sfx_prespawn" });
            offeringBowl.m_spawnBossDoneffects.m_effectPrefabs = GetEffects(new() { "vfx_spawn", "sfx_spawn", "fx_Fader_Fissure_Prespawn", "fx_Fader_Roar_Projectile_Hit", "fx_Fader_Roar_Projectile_Hit" });

            NecrotisPlugin._offeringItem.SettingChanged += (sender, args) =>
            {
                GameObject newItem = __instance.GetItemPrefab(NecrotisPlugin._offeringItem.Value);
                if (!newItem) return;
                if (!newItem.TryGetComponent(out ItemDrop component)) return;
                offeringBowl.m_bossItem = component;
                itemStand.m_supportedItems.Clear();
                itemStand.m_supportedItems.Add(component);
            };
            NecrotisPlugin._itemStandMaxRange.SettingChanged += (sender, args) =>
                offeringBowl.m_itemstandMaxRange = NecrotisPlugin._itemStandMaxRange.Value;
            
        }
    }
    
    private static EffectList.EffectData[] GetEffects(List<string> effectNames)
    {
        return (from name in effectNames select ZNetScene.instance.GetPrefab(name) into prefab where prefab select new EffectList.EffectData()
        {
            m_prefab = prefab, m_enabled = true
        }).ToArray();
    }

    private static void CloneAssets(ObjectDB __instance)
    {
        ItemDrop? key = CloneKey(__instance);
        if (key == null) return;

        ConfigEntry<string> keyConfig = NecrotisPlugin._Plugin.config("Necromancer Gate", "Key", key.name, "Set key prefab");
        GameObject configItem = __instance.GetItemPrefab(keyConfig.Value);
        if (configItem)
        {
            if (configItem.TryGetComponent(out ItemDrop keyDrop))
            {
                key = keyDrop;
            }
        }
        
        GameObject gate = ZNetScene.instance.GetPrefab("dungeon_queen_door");
        if (!gate) return;
        GameObject clone = Object.Instantiate(gate, NecrotisPlugin._Root.transform, false);
        clone.name = "Necromancer_Gate";
        
        if (clone.TryGetComponent(out Door component))
        {
            component.m_name = "$piece_necromancer_gate";
            component.m_keyItem = key;
            component.m_canNotBeClosed = false;
            keyConfig.SettingChanged += (sender, args) =>
            {
                var item = __instance.GetItemPrefab(keyConfig.Value);
                if (!item) return;
                if (!item.TryGetComponent(out ItemDrop configComponent)) return;
                component.m_keyItem = configComponent;
            };
        }
        
        GameObject collider = new GameObject("collider");
        collider.transform.SetParent(gate.transform);

        SkinnedMeshRenderer renderer = clone.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (!renderer) return;

        BoxCollider boxCollider = collider.AddComponent<BoxCollider>();
        var bounds = renderer.bounds;
        boxCollider.size = new Vector3(1f, bounds.size.y);
        boxCollider.center = bounds.center;

        List<Material> materials = new();
        foreach (Material? mat in renderer.sharedMaterials)
        {
            Material material = new Material(mat);
            if (material.HasProperty(EmissionColor))
            {
                material.SetColor(EmissionColor, new Color(0f, 1f, 0f, 2f));
            }
            materials.Add(material);
        }

        renderer.sharedMaterials = materials.ToArray();
        renderer.materials = materials.ToArray();

        Piece piece = clone.AddComponent<Piece>();
        piece.m_icon = NecrotisPlugin.DvergerGateIcon;
        piece.m_name = "$piece_necromancer_gate";
        piece.m_description = "$piece_necromancer_gate_desc";

        AddEffect(new List<string>(){"vfx_Place_workbench", "sfx_build_hammer_stone"}, ref piece.m_placeEffect);

        ConfigEntry<string> station = NecrotisPlugin._Plugin.config("Necromancer Gate", "Crafting Station", "piece_workbench", "Set crafting station");
        ConfigEntry<string> recipe = NecrotisPlugin._Plugin.config("Necromancer Gate", "Recipe", "SwordCheat:1", "Set recipe, [prefabName]:[amount]");
        ConfigEntry<Piece.PieceCategory> category =
            NecrotisPlugin._Plugin.config("Necromancer Gate", "Category", piece.m_category, "Set category");

        piece.m_resources = GetRequirements(recipe.Value).ToArray();
        piece.m_craftingStation = GetCraftingStation(station.Value);

        recipe.SettingChanged += (sender, args) => piece.m_resources = GetRequirements(recipe.Value).ToArray();
        station.SettingChanged += (sender, args) => piece.m_craftingStation = GetCraftingStation(station.Value);
        category.SettingChanged += (sender, args) =>
        {
            if (Enum.IsDefined(typeof(Piece.PieceCategory), category.Value))
            {
                piece.m_category = category.Value;
            }
        }; 
        
        RegisterToHammer(clone);
        RegisterToScene(clone);
        
        CloneIronGrate(key, keyConfig);
        CloneBoneSpawner();
        CreateHeart(__instance);
    }

    private static void CloneIronGrate(ItemDrop key, ConfigEntry<string> keyConfig)
    {
        GameObject grate = ZNetScene.instance.GetPrefab("iron_grate");
        if (!grate) return;

        GameObject clone = Object.Instantiate(grate, NecrotisPlugin._Root.transform, false);
        clone.name = "Necromancer_Grate";

        if (!clone.TryGetComponent(out Door door)) return;
        if (!clone.TryGetComponent(out Piece piece)) return;

        door.m_name = "$piece_necromancer_grate";
        door.m_keyItem = key;
        keyConfig.SettingChanged += (sender, args) =>
        {
            var item = ObjectDB.instance.GetItemPrefab(keyConfig.Value);
            if (!item) return;
            if (!item.TryGetComponent(out ItemDrop configComponent)) return;
            door.m_keyItem = configComponent;
        };
        piece.m_name = "$piece_necromancer_grate";
        piece.m_description = "$piece_necromancer_grate_desc";
        ConfigEntry<string> station = NecrotisPlugin._Plugin.config("Necromancer Grate", "Crafting Station", "Forge", "Set crafting station");
        ConfigEntry<string> recipe = NecrotisPlugin._Plugin.config("Necromancer Grate", "Recipe", "Iron:4", "Set recipe, [prefabName]:[amount]");
        ConfigEntry<Piece.PieceCategory> category = NecrotisPlugin._Plugin.config("Necromancer Grate", "Category", Piece.PieceCategory.Misc, "Set category");

        piece.m_craftingStation = GetCraftingStation(station.Value);
        piece.m_resources = GetRequirements(recipe.Value).ToArray();
        piece.m_category = category.Value;

        station.SettingChanged += (sender, args) => piece.m_craftingStation = GetCraftingStation(station.Value);
        recipe.SettingChanged += (sender, args) => piece.m_resources = GetRequirements(recipe.Value).ToArray();
        category.SettingChanged += (sender, args) =>
        {
            if (Enum.IsDefined(typeof(Piece.PieceCategory), category.Value))
            {
                piece.m_category = category.Value;
            }
        };
        
        RegisterToHammer(clone);
        RegisterToScene(clone);
    }

    private static void CloneBoneSpawner()
    {
        GameObject bonePile = ZNetScene.instance.GetPrefab("BonePileSpawner");
        if (!bonePile) return;

        GameObject clone = Object.Instantiate(bonePile, NecrotisPlugin._Root.transform, false);
        clone.name = "NecromancerBonePileSpawner";
        
        GameObject collider = new GameObject("collider");
        collider.transform.SetParent(clone.transform);

        MeshRenderer renderer = clone.GetComponentInChildren<MeshRenderer>(true);
        if (!renderer) return;

        BoxCollider boxCollider = collider.AddComponent<BoxCollider>();
        var bounds = renderer.bounds;
        boxCollider.size = new Vector3(1f, bounds.size.y);
        boxCollider.center = bounds.center;

        Piece piece = clone.AddComponent<Piece>();
        piece.m_icon = NecrotisPlugin.BonePileIcon;
        piece.m_name = "$piece_necromancer_bone_pile";
        piece.m_description = "$piece_necromancer_bone_pile_desc";

        AddEffect(new List<string>(){"vfx_Place_workbench", "sfx_build_hammer_stone"}, ref piece.m_placeEffect);

        ConfigEntry<string> station = NecrotisPlugin._Plugin.config("Necromancer Bone Pile", "Crafting Station", "piece_workbench", "Set crafting station");
        ConfigEntry<string> recipe = NecrotisPlugin._Plugin.config("Necromancer Bone Pile", "Recipe", "SwordCheat:1", "Set recipe, [prefabName]:[amount]");
        ConfigEntry<Piece.PieceCategory> category = NecrotisPlugin._Plugin.config("Necromancer Bone Pile", "Category", piece.m_category, "Set category");

        piece.m_resources = GetRequirements(recipe.Value).ToArray();
        piece.m_craftingStation = GetCraftingStation(station.Value);

        recipe.SettingChanged += (sender, args) => piece.m_resources = GetRequirements(recipe.Value).ToArray();
        station.SettingChanged += (sender, args) => piece.m_craftingStation = GetCraftingStation(station.Value);
        category.SettingChanged += (sender, args) =>
        {
            if (Enum.IsDefined(typeof(Piece.PieceCategory), category.Value))
            {
                piece.m_category = category.Value;
            }
        };

        if (clone.TryGetComponent(out HoverText hoverText))
        {
            hoverText.m_text = "$piece_necromancer_bone_pile";
        }

        if (clone.TryGetComponent(out Destructible destructible))
        {
            var health = NecrotisPlugin._Plugin.config("Necromancer Bone Pile", "Health", destructible.m_health, "Set health");
            destructible.m_health = health.Value;
            health.SettingChanged += (sender, args) => destructible.m_health = health.Value;
        }

        if (clone.TryGetComponent(out SpawnArea spawnArea))
        {
            var spawns = NecrotisPlugin._Plugin.config("Necromancer Bone Pile", "Spawns",
                "Skeleton:1:3:1,Skeleton,Skeleton_Poison:1:3:0.5,Wraith:1:3:0.3",
                "Set spawns, [prefab]:[minLevel]:[maxLevel]:[weight]");
            spawnArea.m_prefabs = GetSpawns(spawns.Value);
            spawns.SettingChanged += (sender, args) => spawnArea.m_prefabs = GetSpawns(spawns.Value);
        }

        RegisterToHammer(clone);
        RegisterToScene(clone);
    }

    private static List<SpawnArea.SpawnData> GetSpawns(string config)
    {
        List<SpawnArea.SpawnData> output = new();
        foreach (string input in config.Split(','))
        {
            var info = input.Split(':');
            if (info.Length != 4) continue;
            GameObject prefab = ZNetScene.instance.GetPrefab(info[0]);
            if (!prefab) continue;
            output.Add(new SpawnArea.SpawnData()
            {
                m_prefab = prefab,
                m_weight = float.TryParse(info[3], out float weight) ? weight : 1f,
                m_minLevel = int.TryParse(info[1], out int min) ? min : 1,
                m_maxLevel = int.TryParse(info[2], out int max) ? max : 3
            });
        }
        return output;
    }

    private static void RegisterToHammer(GameObject prefab)
    {
        GameObject hammer = ObjectDB.instance.GetItemPrefab("Hammer");
        if (!hammer.TryGetComponent(out ItemDrop itemDrop)) return;
        if (itemDrop.m_itemData.m_shared.m_buildPieces.m_pieces.Contains(prefab)) return;
        itemDrop.m_itemData.m_shared.m_buildPieces.m_pieces.Add(prefab);
    }
    
    private static void RegisterToScene(GameObject prefab)
    {
        if (!ZNetScene.instance.m_prefabs.Contains(prefab)) ZNetScene.instance.m_prefabs.Add(prefab);
        ZNetScene.instance.m_namedPrefabs[prefab.name.GetStableHashCode()] = prefab;
    }

    private static CraftingStation? GetCraftingStation(string name)
    {
        GameObject prefab = ZNetScene.instance.GetPrefab(name);
        if (!prefab) return null;
        return prefab.TryGetComponent(out CraftingStation component) ? component : null;
    }

    private static List<Piece.Requirement> GetRequirements(string recipe)
    {
        List<Piece.Requirement> requirements = new();
        foreach (var data in recipe.Split(','))
        {
            string[] info = data.Split(':');
            if (info.Length != 2) continue;
            GameObject item = ObjectDB.instance.GetItemPrefab(info[0]);
            if (!item) continue;
            if (!item.TryGetComponent(out ItemDrop reqDrop)) continue;
            requirements.Add(new Piece.Requirement()
            {
                m_resItem = reqDrop,
                m_recover = true,
                m_amount = int.TryParse(info[1], out int amount) ? amount : 1,
                m_amountPerLevel = 1,
                m_extraAmountOnlyOneIngredient = 1
            });
        }

        return requirements;
    }
    
    private static void AddEffect(List<string> effects, ref EffectList list)
    {
        if (effects.Count == 0 || !ZNetScene.instance) return;

        list.m_effectPrefabs = (from name in effects select ZNetScene.instance.GetPrefab(name) into effect where effect select new EffectList.EffectData()
        {
            m_prefab = effect, m_enabled = true,
        }).ToArray();
    }

    private static ItemDrop? CloneKey(ObjectDB __instance)
    {
        CloneFragments(__instance);
        
        GameObject key = __instance.GetItemPrefab("DvergrKey");
        if (!key) return null;
        GameObject clone = Object.Instantiate(key, NecrotisPlugin._Root.transform, false);
        clone.name = "NecroKey";

        if (!clone.TryGetComponent(out ItemDrop component)) return null;
        component.m_itemData.m_shared.m_name = "$item_necrokey";
        component.m_itemData.m_shared.m_description = "$item_necrokey_desc";

        MeshRenderer? renderer = clone.GetComponentInChildren<MeshRenderer>(true);
        List<Material> materials = new();
        foreach (Material? mat in renderer.sharedMaterials)
        {
            Material material = new Material(mat);
            if (material.HasProperty(EmissionColor))
            {
                material.SetColor(EmissionColor, new Color(0f, 1f, 0f, 2f));
            }
            materials.Add(material);
        }

        renderer.sharedMaterials = materials.ToArray();
        renderer.materials = materials.ToArray();

        Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
        recipe.m_amount = 1;
        recipe.m_enabled = true;

        ConfigEntry<string> config = NecrotisPlugin._Plugin.config("Necromancer Gate", "Key Recipe", "NecroKeyFragment:3",
            "Set recipe, [prefabName]:[amount]");
        ConfigEntry<string> station = NecrotisPlugin._Plugin.config("Necromancer Gate", "Key Craft Station", "piece_workbench",
            "Set crafting station");
        recipe.m_resources = GetRequirements(config.Value).ToArray();
        recipe.m_craftingStation = GetCraftingStation(station.Value);
        recipe.m_item = component;
        recipe.m_repairStation = GetCraftingStation(station.Value);
        recipe.m_minStationLevel = 1;
        recipe.m_qualityResultAmountMultiplier = 1;
        recipe.m_requireOnlyOneIngredient = false;
        
        config.SettingChanged += (sender, args) => recipe.m_resources = GetRequirements(config.Value).ToArray();
        station.SettingChanged += (sender, args) => 
            recipe.m_craftingStation = GetCraftingStation(station.Value);
            recipe.m_repairStation = GetCraftingStation(station.Value);
        
        if (!__instance.m_items.Contains(clone)) __instance.m_items.Add(clone);
        __instance.m_itemByHash[clone.name.GetStableHashCode()] = clone;
        if (!ZNetScene.instance.m_prefabs.Contains(clone)) ZNetScene.instance.m_prefabs.Add(clone);
        ZNetScene.instance.m_namedPrefabs[clone.name.GetStableHashCode()] = clone;
        if (!__instance.m_recipes.Contains(recipe)) __instance.m_recipes.Add(recipe);
        return component;
    }

    private static void CloneFragments(ObjectDB __instance)
    {
        GameObject prefab = __instance.GetItemPrefab("DvergrKeyFragment");
        if (!prefab) return;
        GameObject clone = Object.Instantiate(prefab, NecrotisPlugin._Root.transform, false);
        clone.name = "NecroKeyFragment";
        
        if (!clone.TryGetComponent(out ItemDrop component)) return;
        component.m_itemData.m_shared.m_name = "$item_necrokeyfragment";
        component.m_itemData.m_shared.m_description = "$item_necrokeyfragment_desc";

        MeshRenderer? renderer = clone.GetComponentInChildren<MeshRenderer>(true);
        List<Material> materials = new();
        foreach (var mat in renderer.sharedMaterials)
        {
            var material = new Material(mat);
            if (material.HasProperty(EmissionColor))
            {
                material.SetColor(EmissionColor, new Color(0f, 1f, 0f, 2f));
            }
            materials.Add(material);
        }

        renderer.sharedMaterials = materials.ToArray();
        renderer.materials = materials.ToArray();
        
        RegisterToDB(clone);
        RegisterToScene(clone);
    }

    private static void RegisterToDB(GameObject prefab)
    {
        if (!ObjectDB.instance.m_items.Contains(prefab)) ObjectDB.instance.m_items.Add(prefab);
        ObjectDB.instance.m_itemByHash[prefab.name.GetStableHashCode()] = prefab;
    }

    private static void CreateHeart(ObjectDB __instance)
    {
        GameObject heart = __instance.GetItemPrefab("HealthUpgrade_Bonemass");
        if (!heart) return;
        GameObject clone = Object.Instantiate(heart, NecrotisPlugin._Root.transform, false);
        clone.name = "NecromancerHeart";
        if (!clone.TryGetComponent(out ItemDrop component)) return;
        component.m_itemData.m_shared.m_name = "$item_necromancer_heart";
        component.m_itemData.m_shared.m_description = "$item_necromancer_heart_desc";
        component.m_itemData.m_shared.m_questItem = false;

        var food = NecrotisPlugin._Plugin.config("Necromancer Heart", "Health", 100f, "Set health bonus");
        var stamina = NecrotisPlugin._Plugin.config("Necromancer Heart", "Stamina", 10f, "Set stamina bonus");
        var eitr = NecrotisPlugin._Plugin.config("Necromancer Heart", "Eitr", 50f, "Set eitr bonus");
        var burn = NecrotisPlugin._Plugin.config("Necromancer Heart", "Burn Time", 1800f, "Set burn time");
        var regen = NecrotisPlugin._Plugin.config("Necromancer Heart", "Health Per Tick", 3f, "Set health regeneration per tick");
        
        component.m_itemData.m_shared.m_food = food.Value;
        component.m_itemData.m_shared.m_foodStamina = stamina.Value;
        component.m_itemData.m_shared.m_foodEitr = eitr.Value;
        component.m_itemData.m_shared.m_foodBurnTime = burn.Value;
        component.m_itemData.m_shared.m_foodRegen = regen.Value;

        food.SettingChanged += (sender, args) => component.m_itemData.m_shared.m_food = food.Value;
        stamina.SettingChanged += (sender, args) => component.m_itemData.m_shared.m_foodStamina = stamina.Value;
        eitr.SettingChanged += (sender, args) => component.m_itemData.m_shared.m_foodEitr = eitr.Value;
        burn.SettingChanged += (sender, args) => component.m_itemData.m_shared.m_foodBurnTime = burn.Value;
        regen.SettingChanged += (sender, args) => component.m_itemData.m_shared.m_foodRegen = regen.Value;
        
        HeartSE se = ScriptableObject.CreateInstance<HeartSE>();
        se.name = "SE_NecromancerHeart";
        se.m_name = "$item_necromancer_heart";
        se.m_burnTime = burn;
        se.m_icon = component.m_itemData.GetIcon();
        se.m_carryWeight = NecrotisPlugin._Plugin.config("Necromancer Heart", "Carry Weight", 100f, "Set carry weight bonus");
        se.m_healthRegen = NecrotisPlugin._Plugin.config("Necromancer Heart", "Health Regeneration", 1.25f, "Set health regeneration");
        se.m_stamRegen = NecrotisPlugin._Plugin.config("Necromancer Heart", "Stamina Regeneration", 1.25f, "Set stamina regeneration");
        se.m_eitrRegen = NecrotisPlugin._Plugin.config("Necromancer Heart", "Eitr Regeneration", 1.25f, "Set eitr regeneration");
        if (!__instance.m_StatusEffects.Contains(se)) __instance.m_StatusEffects.Add(se);

        component.m_itemData.m_shared.m_consumeStatusEffect = se;
        
        RegisterToScene(clone);
        RegisterToDB(clone);
    }
}