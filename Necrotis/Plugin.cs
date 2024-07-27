using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using LocalizationManager;
using Managers;
using Necrotis.Behaviors;
using Necrotis.Managers;
using PieceManager;
using ServerSync;
using UnityEngine;
using CraftingTable = Necrotis.Managers.CraftingTable;

namespace Necrotis
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class NecrotisPlugin : BaseUnityPlugin
    {
        internal const string ModName = "Necrotis";
        internal const string ModVersion = "1.0.2";
        internal const string Author = "RustyMods";
        private const string ModGUID = Author + "." + ModName;
        private static readonly string ConfigFileName = ModGUID + ".cfg";
        private static readonly string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        internal static string ConnectionError = "";
        private readonly Harmony _harmony = new(ModGUID);
        public static readonly ManualLogSource NecrotisLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
        private static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
        public enum Toggle { On = 1, Off = 0 }

        public static NecrotisPlugin _Plugin = null!;
        public static readonly AssetBundle _AssetBundle = GetAssetBundle("necromancerbundle");
        public static GameObject _Root = null!;
        public static readonly Sprite DvergerGateIcon = _AssetBundle.LoadAsset<Sprite>("dvergergateicon");
        public static readonly Sprite BonePileIcon = _AssetBundle.LoadAsset<Sprite>("bonepile_icon");
        private static ConfigEntry<Toggle> _serverConfigLocked = null!;
        public static ConfigEntry<string> _offeringItem = null!;
        public static ConfigEntry<float> _itemStandMaxRange = null!;
        public static ConfigEntry<Toggle> _forceRaid = null!;
        public static ConfigEntry<Toggle> _alwaysDark = null!;

        private static AssetBundle GetAssetBundle(string fileName)
        {
            Assembly execAssembly = Assembly.GetExecutingAssembly();
            string resourceName = execAssembly.GetManifestResourceNames().Single(str => str.EndsWith(fileName));
            using Stream? stream = execAssembly.GetManifestResourceStream(resourceName);
            return AssetBundle.LoadFromStream(stream);
        }
        
        private void InitConfigs()
        {
            _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On,
                "If on, the configuration is locked and can be changed by server admins only.");
            _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);

            _offeringItem = config("2 - Altar Settings", "Offering Item", "NecromancerTotem", "Set offering item using prefab name");
            _itemStandMaxRange = config("2 - Altar Settings", "Offering Stone Range", 20f, "Set max range for altar to search for offering stones");
            _forceRaid = config("3 - Raid Settings", "Only Necro Raids", Toggle.On, "If on, only necromancer raids until necromancer is defeated, else they are random");
            _alwaysDark = config("3 - Raid Settings", "Always Dark", Toggle.On, "If on, world is always dark until necromancer is defeated");
        }

        private void LoadMusic()
        {
            MusicManager.Music necromancerMusic = new MusicManager.Music("Necromancer_Music", "Necromancer", _AssetBundle);
            necromancerMusic.SetEnvironmentCopy("Fader");
        }

        private void LoadItems()
        {
            SE_Necromancer se = ScriptableObject.CreateInstance<SE_Necromancer>();
            se.name = "SE_NecromancerSet";
            se.m_name = "$enemy_necromancer";
            se.m_tooltip = "$se_necromancer_tooltip";
            se.m_speedModifier = 0.1f;
            se.m_addMaxCarryWeight = 50;
            se.m_jumpModifier = new Vector3(0f, 0.1f, 0f);
            Item Mummy = new Item(_AssetBundle, "NecromancerTotem");
            Mummy.Name.English("Necromancer Totem");
            Mummy.Description.English("Rumored to conjure the necromancer from his slumber");
            Mummy.Crafting.Add(global::Managers.CraftingTable.ArtisanTable, 2);
            Mummy.RequiredItems.Add("SwordCheat", 1, 1);
            Mummy.Configurable = Configurability.Drop;
            
            Item SwordNecro = new Item(_AssetBundle, "SwordNecro");
            SwordNecro.Name.English("Sword Necro Ghoul");
            SwordNecro.Description.English("Bestows upon its bearer the eerie gift to summon the undead from their slumber.");
            SwordNecro.Crafting.Add(global::Managers.CraftingTable.Forge, 4);
            SwordNecro.RequiredItems.Add("FlametalNew", 10);
            SwordNecro.RequiredItems.Add("Eitr", 5);
            SwordNecro.RequiredItems.Add("CharredBone", 10);
            SwordNecro.RequiredItems.Add("NecromancerTotem", 1);
            SwordNecro.RequiredUpgradeItems.Add("FlametalNew", 5);
            SwordNecro.RequiredUpgradeItems.Add("Eitr", 2);
            SwordNecro.RequiredUpgradeItems.Add("CharredBone", 5);
            SwordNecro.AddHitEffect("vfx_HitSparks");
            SwordNecro.AddHitEffect("sfx_sword_hit");
            SwordNecro.AddHitEffect("fx_hit_camshake");
            SwordNecro.AddBlockEffect("sfx_metal_blocked");
            SwordNecro.AddBlockEffect("vfx_blocked");
            SwordNecro.AddBlockEffect("fx_block_camshake");
            SwordNecro.AddTriggerEffect("fx_swing_camshake");
            SwordNecro.AddStartEffect("sfx_sword_swing");
            
            Item SwordNecro1 = new Item(_AssetBundle, "SwordNecro1");
            SwordNecro1.Name.English("Sword Necro Blood");
            SwordNecro1.Description.English("Bestows upon its bearer the eerie gift to summon the undead from their slumber.");
            SwordNecro1.Crafting.Add(global::Managers.CraftingTable.Forge, 4);
            SwordNecro1.RequiredItems.Add("FlametalNew", 10);
            SwordNecro1.RequiredItems.Add("SwordNecro", 1);
            SwordNecro1.RequiredItems.Add("NecromancerHeart", 3);
            SwordNecro1.RequiredItems.Add("YagluthDrop", 1);
            SwordNecro1.RequiredUpgradeItems.Add("FlametalNew", 5);
            SwordNecro1.AddHitEffect("vfx_HitSparks");
            SwordNecro1.AddHitEffect("sfx_sword_hit");
            SwordNecro1.AddHitEffect("fx_hit_camshake");
            SwordNecro1.AddBlockEffect("sfx_metal_blocked");
            SwordNecro1.AddBlockEffect("vfx_blocked");
            SwordNecro1.AddBlockEffect("fx_block_camshake");
            SwordNecro1.AddTriggerEffect("fx_swing_camshake");
            SwordNecro1.AddStartEffect("sfx_sword_swing");

            Item TrophyNecromancer = new Item(_AssetBundle, "TrophyNecromancer");
            TrophyNecromancer.Name.English("Necromancer Trophy");
            TrophyNecromancer.Description.English(
                "The necromancer's severed arm, taken as a trophy, pulses with residual dark magic and a lingering, malevolent will.");

            FaunaManager.ProjectileSpawnAbility SwordSpawner = new FaunaManager.ProjectileSpawnAbility("sword_necro_spawn", _AssetBundle);
            SwordSpawner.AddSpawn("Draugr_Friendly");
            SwordSpawner.AddPreSpawnEffect("fx_summon_skeleton_spawn");
            SwordSpawner.AddPreSpawnEffect("fx_Fader_Fissure_Prespawn");
            
            FaunaManager.ProjectileSpawnAbility SwordSpawner1 = new FaunaManager.ProjectileSpawnAbility("sword_necro_spawn1", _AssetBundle);
            SwordSpawner1.AddSpawn("Charred_Melee_Friendly");
            SwordSpawner1.AddPreSpawnEffect("fx_summon_skeleton_spawn");
            SwordSpawner1.AddPreSpawnEffect("fx_Fader_Fissure_Prespawn");

            Item Helmet = new(_AssetBundle, "HelmetNecromancer");
            Helmet.Name.English("Necromancer's Hood");
            Helmet.Description.English("Imbued with the powers of the necromancer, this hood will grant its wearer access into the underworld");
            Helmet.AddArmorMaterial("HelmetMage_Ashlands");
            Helmet.Crafting.Add(global::Managers.CraftingTable.BlackForge, 2);
            Helmet.AddSetStatusEffect(se);
            Helmet.RequiredItems.Add("FlametalNew", 10);
            Helmet.RequiredItems.Add("AskHide", 10);
            Helmet.RequiredItems.Add("LinenThread", 20);
            Helmet.RequiredItems.Add("NecromancerHeart", 1);
            Helmet.RequiredUpgradeItems.Add("FlametalNew", 5);
            Helmet.RequiredUpgradeItems.Add("AskHide", 5);
            Helmet.RequiredUpgradeItems.Add("LinenThread", 10);

            Item Chest = new(_AssetBundle, "ArmorNecromancerChest");
            Chest.Name.English("Necromancer's Chestplate");
            Chest.Description.English("The runic spell carved on the back of this cuirass protects its wearer from the dark arts");
            Chest.AddArmorMaterial("ArmorMageChest_Ashlands");
            Chest.Crafting.Add(global::Managers.CraftingTable.BlackForge, 2);
            Chest.AddSetStatusEffect(se);
            Chest.RequiredItems.Add("FlametalNew", 10);
            Chest.RequiredItems.Add("AskHide", 10);
            Chest.RequiredItems.Add("Eitr", 20);
            Chest.RequiredItems.Add("NecromancerHeart", 1);
            Chest.RequiredUpgradeItems.Add("FlametalNew", 5);
            Chest.RequiredUpgradeItems.Add("AskHide", 5);
            Chest.RequiredUpgradeItems.Add("Eitr", 10);
            Chest.AddHeatResistance(0.2f);

            Item Legs = new(_AssetBundle, "ArmorNecromancerLegs");
            Legs.Name.English("Necromancer's Trousers");
            Legs.Description.English("Enchanted by the necromancer, the skull protects its wearer from the undead");
            Legs.AddArmorMaterial("ArmorMageLegs_Ashlands");
            Legs.Crafting.Add(global::Managers.CraftingTable.BlackForge, 2);
            Legs.AddSetStatusEffect(se);
            Legs.RequiredItems.Add("FlametalNew", 10);
            Legs.RequiredItems.Add("AskHide", 10);
            Legs.RequiredItems.Add("Eitr", 20);
            Legs.RequiredItems.Add("NecromancerHeart", 1);
            Legs.RequiredUpgradeItems.Add("FlametalNew", 5);
            Legs.RequiredUpgradeItems.Add("AskHide", 5);
            Legs.RequiredUpgradeItems.Add("Eitr", 10);
            Legs.AddHeatResistance(0.2f);
        }

        private void LoadMonsters()
        {
            FaunaManager.Critter Necromancer = new FaunaManager.Critter("Necromancer", _AssetBundle)
            {
                m_maxSpawned = 0,
            };
            Necromancer.SetBoss(true);
            Necromancer.AddDrop("TrophyNecromancer", 1, 1, 1f);
            Necromancer.AddDrop("Bloodbag", 10, 20, 1f);
            Necromancer.AddDrop("SwordNecro", 1, 1, 1f);
            Necromancer.AddDrop("NecromancerHeart", 3, 10, 1f);
            Necromancer.AddHitEffect("vfx_draugr_hit");
            Necromancer.AddHitEffect("sfx_draugr_hit");
            Necromancer.AddDeathEffect("vfx_draugr_death");
            Necromancer.AddDeathEffect("sfx_draugr_death");
            Necromancer.AddDeathEffect("fx_Fader_CorpseExplosion");
            Necromancer.AddWaterEffect("vfx_water_surface");
            Necromancer.AddAlertedEffect("sfx_charred_alert");
            Necromancer.AddAlertedEffect("sfx_wraith_alerted");
            Necromancer.AddIdleSound("sfx_dverger_vo_alerted");
            Necromancer.AddIdleSound("sfx_wraith_idle");
            Necromancer.AddIdleSound("sfx_vulture_alert");
            Necromancer.AddWaterEffect("vfx_water_surface");
            Necromancer.CloneFootStepsFrom("Troll");
            Necromancer.SetBossEvent("Necromancer");
            Necromancer.EditAttack("Necromancer_Attack", hitEffects: new(){"vfx_HitSparks", "sfx_sword_hit"},  trailEffects: new(){"sfx_kromsword_swing"});
            Necromancer.EditAttack("Necromancer_Rage", hitEffects: new(){"vfx_HitSparks", "sfx_sword_hit"},  trailEffects: new(){"sfx_kromsword_swing"});
            Necromancer.EditAttack("Necromancer_Fire", hitEffects:new(){"vfx_clubhit", "sfx_clubhit"}, startEffects:new(){"sfx_kromsword_swing"});
            Necromancer.EditAttack("Necromancer_Protect", hitEffects:new(){"vfx_clubhit", "sfx_clubhit"}, startEffects:new(){"sfx_kromsword_swing"}, triggerEffects:new(){"fx_swing_camshake"});
            Necromancer.EditAttack("Necromancer_Taunt", hitEffects:new(){"vfx_clubhit", "sfx_clubhit"}, startEffects:new(){"sfx_kromsword_swing"}, triggerEffects: new(){"DvergerStaffHeal_aoe"});
            Necromancer.EditAttack("Necromancer_Dodge", hitEffects:new(){"vfx_clubhit", "sfx_clubhit"}, startEffects:new(){"sfx_kromsword_swing"});
            
            FaunaManager.ProjectileData NecromancerProjectile = new FaunaManager.ProjectileData("Necromancer_Projectile_Beam", _AssetBundle);
            NecromancerProjectile.AddHitEffect("fx_goblinking_beam_hit");

            FaunaManager.ProjectileData Fireball = new FaunaManager.ProjectileData("Necromancer_Projectile_Ball", _AssetBundle);
            Fireball.AddHitEffect("fx_DvergerMage_Fire_hit");
            Fireball.AddSpawnOnHit("DvergerStaffFire_clusterbomb_aoe");
            Fireball.AddSpawnOnHit("Charred_Twitcher");
            Fireball.SetSpawnOnHitChance(0.4f);
            Fireball.SetConfigKeys("Fireball", "Necromancer");
            
            FaunaManager.ProjectileData Meteor = new FaunaManager.ProjectileData("Necromancer_Projectile_Meteor", _AssetBundle);
            Meteor.AddHitEffect("fx_fader_meteor_hit");
            Meteor.AddHitEffect("fx_Fader_Fissure_Prespawn");
            Meteor.SetSpawnOnHitChance(0.5f);
            Meteor.AddSpawnOnHit("Charred_Mage");
            Meteor.SetConfigKeys("Meteor", "Necromancer");

            FaunaManager.ProjectileSpawnAbility MistileSpawner = new FaunaManager.ProjectileSpawnAbility("Necromancer_Mistile_Spawn", _AssetBundle);
            MistileSpawner.AddSpawn("Mistile_Clone");
            MistileSpawner.AddSpawnEffect("fx_DvergerMage_MistileSpawn");
            
            FaunaManager.AOEData NecromancerAOE = new FaunaManager.AOEData("Necromancer_AOE", _AssetBundle);
            NecromancerAOE.AddHitEffect("Fader_WallOfFire_AOE");
        }

        public void LoadPieces()
        {
            BuildPiece Altar = new BuildPiece(_AssetBundle, "Necromancer_Altar");
            Altar.Name.English("Necromancer Altar");
            Altar.Description.English("");
            Altar.Crafting.Set(CraftingTable.StoneCutter);
            Altar.RequiredItems.Add("Grausten", 10, true);
            Altar.Category.Set("Necromancer");
            Altar.PlaceEffects = new() { "vfx_Place_workbench", "sfx_build_hammer_stone" };
            Altar.DestroyedEffects = new() { "vfx_RockDestroyed", "sfx_rock_destroyed" };
            Altar.HitEffects = new() { "vfx_RockHit" };
            Altar.SwitchEffects = new() { "vfx_Place_throne02" };
            MaterialReplacer.RegisterGameObjectForShaderSwap(Altar.Prefab, MaterialReplacer.ShaderType.PieceShader);
            BuildPiece Brazier = new BuildPiece(_AssetBundle, "Necromancer_Brazier");
            Brazier.Name.English("Necromancer Brazier");
            Brazier.Description.English("");
            Brazier.Crafting.Set(CraftingTable.StoneCutter);
            Brazier.RequiredItems.Add("Grausten", 10, true);
            Brazier.Category.Set("Necromancer");
            Brazier.PlaceEffects = new() { "vfx_Place_workbench", "sfx_build_hammer_stone" };
            Brazier.DestroyedEffects = new() { "vfx_RockDestroyed", "sfx_rock_destroyed" };
            Brazier.HitEffects = new() { "vfx_RockHit" };
            Brazier.SwitchEffects = new() { "vfx_Place_throne02" };
            MaterialReplacer.RegisterGameObjectForShaderSwap(Brazier.Prefab, MaterialReplacer.ShaderType.PieceShader);
            BuildPiece Runestone = new BuildPiece(_AssetBundle, "Necromancer_Runestone");
            Runestone.Name.English("Necromancer Runestone");
            Runestone.Description.English("");
            Runestone.Crafting.Set(CraftingTable.StoneCutter);
            Runestone.RequiredItems.Add("Grausten", 10, true);
            Runestone.PlaceEffects = new() { "vfx_Place_workbench", "sfx_build_hammer_stone" };
            Runestone.Category.Set("Necromancer");
            Runestone.DestroyedEffects = new() { "vfx_RockDestroyed", "sfx_rock_destroyed" };
            Runestone.HitEffects = new() { "vfx_RockHit" };
            Runestone.SwitchEffects = new() { "vfx_Place_throne02" };
            MaterialReplacer.RegisterGameObjectForShaderSwap(Runestone.Prefab, MaterialReplacer.ShaderType.PieceShader);
            BuildPiece Pillar = new BuildPiece(_AssetBundle, "Necromancer_Pillar");
            Pillar.Name.English("Necromancer Pillar");
            Pillar.Description.English("");
            Pillar.Crafting.Set(CraftingTable.StoneCutter);
            Pillar.RequiredItems.Add("Grausten", 10, true);
            Pillar.PlaceEffects = new() { "vfx_Place_workbench", "sfx_build_hammer_stone" };
            Pillar.Category.Set("Necromancer");
            Pillar.DestroyedEffects = new() { "vfx_RockDestroyed", "sfx_rock_destroyed" };
            Pillar.HitEffects = new() { "vfx_RockHit" };
            Pillar.SwitchEffects = new() { "vfx_Place_throne02" };
            MaterialReplacer.RegisterGameObjectForShaderSwap(Pillar.Prefab, MaterialReplacer.ShaderType.PieceShader);
            BuildPiece Lamp = new BuildPiece(_AssetBundle, "Necromancer_Lamps");
            Lamp.Name.English("Necromancer Lamps");
            Lamp.Description.English("");
            Lamp.Crafting.Set(CraftingTable.StoneCutter);
            Lamp.RequiredItems.Add("Grausten", 10, true);
            Lamp.RequiredItems.Add("FlametalNew", 1, true);
            Lamp.PlaceEffects = new() { "vfx_Place_workbench", "sfx_build_hammer_stone" };
            Lamp.Category.Set("Necromancer");
            Lamp.DestroyedEffects = new() { "vfx_RockDestroyed", "sfx_rock_destroyed" };
            Lamp.HitEffects = new() { "vfx_RockHit" };
            Lamp.SwitchEffects = new() { "vfx_Place_throne02" };
            MaterialReplacer.RegisterGameObjectForShaderSwap(Lamp.Prefab, MaterialReplacer.ShaderType.PieceShader);
            BuildPiece WallLamp = new BuildPiece(_AssetBundle, "Necromancer_WallLamps");
            WallLamp.Name.English("Necromancer Wall Lamp");
            WallLamp.Description.English("");
            WallLamp.Crafting.Set(CraftingTable.StoneCutter);
            WallLamp.RequiredItems.Add("Grausten", 10, true);
            WallLamp.RequiredItems.Add("FlametalNew", 1, true);
            WallLamp.PlaceEffects = new() { "vfx_Place_workbench", "sfx_build_hammer_stone" };
            WallLamp.Category.Set("Necromancer");
            WallLamp.DestroyedEffects = new() { "vfx_RockDestroyed", "sfx_rock_destroyed" };
            WallLamp.HitEffects = new() { "vfx_RockHit" };
            WallLamp.SwitchEffects = new() { "vfx_Place_throne02" };
            MaterialReplacer.RegisterGameObjectForShaderSwap(WallLamp.Prefab, MaterialReplacer.ShaderType.PieceShader);
            BuildPiece GlowRock1 = new BuildPiece(_AssetBundle, "Necromancer_Glow_Rock1");
            GlowRock1.Name.English("Necromancer Glow Rock 1");
            GlowRock1.Description.English("");
            GlowRock1.Crafting.Set(CraftingTable.StoneCutter);
            GlowRock1.RequiredItems.Add("Grausten", 10, true);
            GlowRock1.PlaceEffects = new() { "vfx_Place_workbench", "sfx_build_hammer_stone" };
            GlowRock1.Category.Set("Necromancer");
            GlowRock1.DestroyedEffects = new() { "vfx_RockDestroyed", "sfx_rock_destroyed" };
            GlowRock1.HitEffects = new() { "vfx_RockHit" };
            GlowRock1.SwitchEffects = new() { "vfx_Place_throne02" };
            MaterialReplacer.RegisterGameObjectForShaderSwap(GlowRock1.Prefab, MaterialReplacer.ShaderType.PieceShader);
            BuildPiece GlowRock2 = new BuildPiece(_AssetBundle, "Necromancer_Glow_Rock2");
            GlowRock2.Name.English("Necromancer Glow Rock 2");
            GlowRock2.Description.English("");
            GlowRock2.Crafting.Set(CraftingTable.StoneCutter);
            GlowRock2.RequiredItems.Add("Grausten", 10, true);
            GlowRock2.PlaceEffects = new() { "vfx_Place_workbench", "sfx_build_hammer_stone" };
            GlowRock2.Category.Set("Necromancer");
            GlowRock2.DestroyedEffects = new() { "vfx_RockDestroyed", "sfx_rock_destroyed" };
            GlowRock2.HitEffects = new() { "vfx_RockHit" };
            GlowRock2.SwitchEffects = new() { "vfx_Place_throne02" };
            MaterialReplacer.RegisterGameObjectForShaderSwap(GlowRock2.Prefab, MaterialReplacer.ShaderType.PieceShader);
            BuildPiece GlowRock3 = new BuildPiece(_AssetBundle, "Necromancer_Glow_Rock3");
            GlowRock3.Name.English("Necromancer Glow Rock 3");
            GlowRock3.Description.English("");
            GlowRock3.Crafting.Set(CraftingTable.StoneCutter);
            GlowRock3.RequiredItems.Add("Grausten", 10, true);
            GlowRock3.PlaceEffects = new() { "vfx_Place_workbench", "sfx_build_hammer_stone" };
            GlowRock3.Category.Set("Necromancer");
            GlowRock3.DestroyedEffects = new() { "vfx_RockDestroyed", "sfx_rock_destroyed" };
            GlowRock3.HitEffects = new() { "vfx_RockHit" };
            GlowRock3.SwitchEffects = new() { "vfx_Place_throne02" };
            MaterialReplacer.RegisterGameObjectForShaderSwap(GlowRock3.Prefab, MaterialReplacer.ShaderType.PieceShader);
            BuildPiece Mummy1 = new BuildPiece(_AssetBundle, "Necromancer_Mummy1");
            Mummy1.Name.English("Necromancer Mummy 1");
            Mummy1.Description.English("");
            Mummy1.Crafting.Set(CraftingTable.StoneCutter);
            Mummy1.RequiredItems.Add("Grausten", 10, true);
            Mummy1.PlaceEffects = new() { "vfx_Place_workbench", "sfx_build_hammer_stone" };
            Mummy1.Category.Set("Necromancer");
            Mummy1.RandomSpeakEffects = new List<string>() { "fx_deadspeak_vo" };
            Mummy1.DestroyedEffects = new() { "vfx_SawDust", "sfx_wood_destroyed" };
            Mummy1.HitEffects = new() { "vfx_SawDust" };
            Mummy1.SwitchEffects = new() { "vfx_Place_throne02" };
            MaterialReplacer.RegisterGameObjectForShaderSwap(Mummy1.Prefab, MaterialReplacer.ShaderType.PieceShader);
            BuildPiece Mummy2 = new BuildPiece(_AssetBundle, "Necromancer_Mummy2");
            Mummy2.Name.English("Necromancer Mummy 2");
            Mummy2.Description.English("");
            Mummy2.Crafting.Set(CraftingTable.StoneCutter);
            Mummy2.RequiredItems.Add("Grausten", 10, true);
            Mummy2.PlaceEffects = new() { "vfx_Place_workbench", "sfx_build_hammer_stone" };
            Mummy2.Category.Set("Necromancer");
            Mummy2.RandomSpeakEffects = new List<string>() { "fx_deadspeak_vo" };
            Mummy2.DestroyedEffects = new() { "vfx_SawDust", "sfx_wood_destroyed" };
            Mummy2.HitEffects = new() { "vfx_SawDust" };
            Mummy2.SwitchEffects = new() { "vfx_Place_throne02" };
            MaterialReplacer.RegisterGameObjectForShaderSwap(Mummy2.Prefab, MaterialReplacer.ShaderType.PieceShader);
            BuildPiece Pedestal = new BuildPiece(_AssetBundle, "Necromancer_Pedestal");
            Pedestal.Name.English("Necromancer Pedestal");
            Pedestal.Description.English("Spawns the necromancer");
            Pedestal.Crafting.Set(CraftingTable.StoneCutter);
            Pedestal.RequiredItems.Add("SwordCheat", 10, true);
            Pedestal.PlaceEffects = new() { "vfx_Place_workbench", "sfx_build_hammer_stone" };
            Pedestal.Category.Set("Necromancer");
            Pedestal.DestroyedEffects = new() { "vfx_RockDestroyed", "sfx_rock_destroyed" };
            Pedestal.HitEffects = new() { "vfx_RockHit" };
            Pedestal.SwitchEffects = new() { "vfx_Place_throne02" };
            MaterialReplacer.RegisterGameObjectForShaderSwap(Pedestal.Prefab, MaterialReplacer.ShaderType.PieceShader);
        }
        public void Awake()
        {
            Localizer.Load(); 
            _Plugin = this;
            _Root = new GameObject("root");
            DontDestroyOnLoad(_Root);
            _Root.SetActive(false);
            
            InitConfigs();
            LoadMonsters();
            LoadPieces();
            LoadItems();
            LoadMusic();
            
            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            SetupWatcher();
        }

        private void OnDestroy()
        {
            Config.Save();
        }

        private void SetupWatcher()
        {
            FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                NecrotisLogger.LogDebug("ReadConfigValues called");
                Config.Reload();
            }
            catch
            {
                NecrotisLogger.LogError($"There was an issue loading your {ConfigFileName}");
                NecrotisLogger.LogError("Please check your config entries for spelling and format!");
            }
        }
        

        public ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description,
            bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription =
                new(
                    description.Description +
                    (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
                    description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
            //var configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        public ConfigEntry<T> config<T>(string group, string name, T value, string description,
            bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        private class ConfigurationManagerAttributes
        {
            [UsedImplicitly] public int? Order;
            [UsedImplicitly] public bool? Browsable;
            [UsedImplicitly] public string? Category;
            [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer;
        }
    }
}