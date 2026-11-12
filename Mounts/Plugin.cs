using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LUI;
using UnityEngine;
using UnityEngine.UI;

namespace Mounts;

[BepInPlugin("com.mounts.cursebreaker", "Mounts", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    private Harmony _harmony;
    internal static Plugin Instance { get; private set; }

    public const ItemHandler.ItemType MountSlotType = (ItemHandler.ItemType)115;
    public const int SummonMountAbilityId = 9500;
    public const int RidingAnimationId = 562;
    public const string HorseIconPath = "Abilities/285";
    private const int MountDialogueOptionId = 95;

    public static ConfigEntry<float> MountSlotPosX;
    public static ConfigEntry<float> MountSlotPosY;
    public static ConfigEntry<KeyboardShortcut> DebugWindowKeybind;
    public static ConfigEntry<bool> IgnoreCombatLevelRequirement;

    private static readonly AccessTools.FieldRef<ItemHandler.Item, HarvestablesHandler.Tool> ItemToolRef =
        AccessTools.FieldRefAccess<ItemHandler.Item, HarvestablesHandler.Tool>("tool");

    private static readonly AccessTools.FieldRef<UI_BottomRightTab, LUIRect> ContainerTabRef =
        AccessTools.FieldRefAccess<UI_BottomRightTab, LUIRect>("container_tab");

    private static readonly AccessTools.FieldRef<DialogueHandler.Node, string> NodeNpcNameRef =
        AccessTools.FieldRefAccess<DialogueHandler.Node, string>("npcName");

    private static readonly Action<UI_BottomRightTabs_Equipment, object[]> UnequipSlotMethod =
        AccessTools.MethodDelegate<Action<UI_BottomRightTabs_Equipment, object[]>>(
            AccessTools.Method(typeof(UI_BottomRightTabs_Equipment), "UnequipSlot", new[] { typeof(object[]) }));

    private static readonly Action<UI_BottomRightTabs_Equipment> SetupEquipmentNavigationMethod =
        AccessTools.MethodDelegate<Action<UI_BottomRightTabs_Equipment>>(
            AccessTools.Method(typeof(UI_BottomRightTabs_Equipment), "SetupEquipmentNavigation"));

    private static readonly Func<UI_BottomRightTabs_Equipment, ItemHandler.ItemType, UI_BottomRightTabs_Equipment.EQSlot> GetEquipmentSlotMethod =
        AccessTools.MethodDelegate<Func<UI_BottomRightTabs_Equipment, ItemHandler.ItemType, UI_BottomRightTabs_Equipment.EQSlot>>(
            AccessTools.Method(typeof(UI_BottomRightTabs_Equipment), "GetEquipmentSlot", new[] { typeof(ItemHandler.ItemType) }));

    public static bool TryGetMountDefinition(int itemId, out MountDefinition def) => MountDefinitions.TryGetValue(itemId, out def);

    public sealed class MountDefinition
    {
        public int ItemId;
        public string Name;
        public int CombatLevel;
        public int Price;
        public float SpeedBoost;
        public int NpcModelId;
        public bool UseHorseResource;
        public int AnimIdle;
        public int AnimWalk;
        public int AnimRun;
        public int SoundId;
        public int IconItemId;
        public float RiderXOffset;
        public float RiderYOffset;
        public float RiderZOffset;
        public float MountScale = 1.0f;
        public string FlavorText;

        public ConfigEntry<float> CfgPlayerOffsetX;
        public ConfigEntry<float> CfgPlayerOffsetY;
        public ConfigEntry<float> CfgPlayerOffsetZ;
        public ConfigEntry<float> CfgMountScale;
        public ConfigEntry<int> CfgIconItemId;
        public ConfigEntry<int> CfgNpcModelId;
    }

    public static readonly Dictionary<int, MountDefinition> MountDefinitions = new()
    {
        [9501] = new MountDefinition
        {
            ItemId = 9501,
            Name = "Deep Forest Snail",
            CombatLevel = 1,
            Price = 1000,
            SpeedBoost = -0.25f,
            NpcModelId = 838,
            AnimIdle = 753,
            AnimWalk = 754,
            AnimRun = 754,
            SoundId = 0,
            IconItemId = 0,
            RiderXOffset = -0.06f,
            RiderYOffset = 0.54f,
            RiderZOffset = -0.27f,
            MountScale = 0.3f,
            FlavorText = "A damp, unhurried giant snail from the depths of the Mistywood. Why are you buying this?"
        },
        [9502] = new MountDefinition
        {
            ItemId = 9502,
            Name = "Dairy Cow",
            CombatLevel = 10,
            Price = 2500,
            SpeedBoost = 0.10f,
            NpcModelId = 71,
            AnimIdle = 675,
            AnimWalk = 674,
            AnimRun = 674,
            SoundId = 329,
            IconItemId = 0,
            RiderXOffset = 0f,
            RiderYOffset = 0.95f,
            RiderZOffset = 0.19f,
            MountScale = 0.87f,
            FlavorText = "A docile pasture cow accustomed to carrying heavy loads."
        },
        [9503] = new MountDefinition
        {
            ItemId = 9503,
            Name = "Riding Sheep",
            CombatLevel = 20,
            Price = 6000,
            SpeedBoost = 0.20f,
            NpcModelId = 73,
            AnimIdle = 701,
            AnimWalk = 702,
            AnimRun = 702,
            SoundId = 423,
            IconItemId = 0,
            RiderXOffset = 0f,
            RiderYOffset = 0.6f,
            RiderZOffset = 0.21f,
            MountScale = 0.79f,
            FlavorText = "A sturdy sheep bred in the hills of Ratcliffe."
        },
        [9504] = new MountDefinition
        {
            ItemId = 9504,
            Name = "Giant Chicken",
            CombatLevel = 30,
            Price = 15000,
            SpeedBoost = 0.30f,
            NpcModelId = 54,
            AnimIdle = 701,
            AnimWalk = 702,
            AnimRun = 702,
            SoundId = 423,
            IconItemId = 0,
            RiderXOffset = 0f,
            RiderYOffset = 0.91f,
            RiderZOffset = -0.11f,
            MountScale = 0.7f,
            FlavorText = "What in tarnation?"
        },
        [9505] = new MountDefinition
        {
            ItemId = 9505,
            Name = "Riding Horse",
            CombatLevel = 40,
            Price = 35000,
            SpeedBoost = 0.50f,
            NpcModelId = 1640,
            UseHorseResource = true,
            AnimIdle = 766,
            AnimWalk = 765,
            AnimRun = 765,
            SoundId = 694,
            IconItemId = 0,
            RiderXOffset = 0f,
            RiderYOffset = 1.21f,
            RiderZOffset = 0.06f,
            MountScale = 0.75f,
            FlavorText = "A swift, well-trained steed from the Haywind stables."
        },
        [9506] = new MountDefinition
        {
            ItemId = 9506,
            Name = "Mistywood Bear",
            CombatLevel = 50,
            Price = 60000,
            SpeedBoost = 0.65f,
            NpcModelId = 50,
            AnimIdle = 664,
            AnimWalk = 665,
            AnimRun = 665,
            SoundId = 0,
            IconItemId = 0,
            RiderXOffset = 0f,
            RiderYOffset = 1.35f,
            RiderZOffset = 0.22f,
            MountScale = 0.9f,
            FlavorText = "A powerful great bear tamed in the heart of the Mistywood."
        },
        [9507] = new MountDefinition
        {
            ItemId = 9507,
            Name = "Dire Wolf",
            CombatLevel = 50,
            Price = 60000,
            SpeedBoost = 0.65f,
            NpcModelId = 1,
            AnimIdle = 689,
            AnimWalk = 823,
            AnimRun = 690,
            SoundId = 340,
            IconItemId = 0,
            RiderXOffset = 0f,
            RiderYOffset = 0.94f,
            RiderZOffset = 0.17f,
            MountScale = 1.65f,
            FlavorText = "A fierce, tireless forest predator broken to the saddle."
        },
        [9508] = new MountDefinition
        {
            ItemId = 9508,
            Name = "Winterlands Elk",
            CombatLevel = 60,
            Price = 95000,
            SpeedBoost = 0.80f,
            NpcModelId = 9076,
            AnimIdle = 1153,
            AnimWalk = 1154,
            AnimRun = 1155,
            SoundId = 0,
            IconItemId = 0,
            RiderXOffset = 0f,
            RiderYOffset = 1.31f,
            RiderZOffset = 0f,
            MountScale = 1f,
            FlavorText = "A majestic antlered stag native to the northern highlands."
        },
        [9509] = new MountDefinition
        {
            ItemId = 9509,
            Name = "Unicorn",
            CombatLevel = 70,
            Price = 150000,
            SpeedBoost = 0.95f,
            NpcModelId = 461,
            AnimIdle = 757,
            AnimWalk = 758,
            AnimRun = 758,
            SoundId = 694,
            IconItemId = 0,
            RiderXOffset = 0f,
            RiderYOffset = 1.15f,
            RiderZOffset = 0.14f,
            MountScale = 0.9f,
            FlavorText = "A rare, mythical steed of unmatched grace and swiftness."
        },
        [9510] = new MountDefinition
        {
            ItemId = 9510,
            Name = "Draconic Hound",
            CombatLevel = 75,
            Price = 200000,
            SpeedBoost = 1.10f,
            NpcModelId = 450,
            AnimIdle = 721,
            AnimWalk = 722,
            AnimRun = 1109,
            SoundId = 0,
            IconItemId = 0,
            RiderXOffset = 0.03f,
            RiderYOffset = 1.32f,
            RiderZOffset = -0.2f,
            MountScale = 0.64f,
            FlavorText = "A ferocious scaled hound radiating draconic fury."
        },
        [9511] = new MountDefinition
        {
            ItemId = 9511,
            Name = "Icewyrm Hound",
            CombatLevel = 75,
            Price = 200000,
            SpeedBoost = 1.10f,
            NpcModelId = 1537,
            AnimIdle = 1106,
            AnimWalk = 1107,
            AnimRun = 1108,
            SoundId = 0,
            IconItemId = 0,
            RiderXOffset = 0f,
            RiderYOffset = 1.67f,
            RiderZOffset = -0.34f,
            MountScale = 0.73f,
            FlavorText = "A frost-hardened wyrmhound from the frozen peaks of Amarien's Foothills."
        }
    };

    // Cumulative shop inventories per FastTravel location tier
    public static readonly Dictionary<int, int[]> RegionalMountShops = new()
    {
        // 1. Timberwell Village & 2. Grainwater Village
        [9501] = new[] { 9502 },
        // 3. Ratcliffe Village
        [9502] = new[] { 9502, 9503 },
        // 4. North Haywind Stables
        [9503] = new[] { 9502, 9503, 9504, 9505 },
        // 5. West Haywind Militia Camp
        [9504] = new[] { 9502, 9503, 9504 },
        // 6. Fog's Dwindling (Mistywood)
        [9505] = new[] { 9501, 9502, 9503, 9504, 9505, 9506, 9507 },
        // 7. Thornhill Gates South
        [9506] = new[] { 9502, 9503, 9504, 9505, 9506, 9507 },
        // 8. Thornhill Gates East
        [9507] = new[] { 9502, 9503, 9504, 9505, 9506, 9507, 9509 },
        // 9-12. Bulwark Cliff & Amarien's Foothills (all mounts)
        [9508] = new[] { 9501, 9502, 9503, 9504, 9505, 9506, 9507, 9508, 9509, 9510, 9511 }
    };

    public static bool IsMounted { get; private set; }
    public static MountDefinition ActiveMount { get; private set; }
    private static GameObject _spawnedMountObject;
    private static EasyAnimator _mountAnimator;
    private static int _currentMountAnimId;

    // In-game debug window state
    private static LUIRect _debugWindow;
    private static int _debugSelectedMountIndex = 0;
    private static List<MountDefinition> _mountList;

    // Icon picker modal state
    private static LUIRect _iconPickerModal;
    private static MountDefinition _iconPickerTargetMount;
    private static int _iconPickerPage = 0;
    private static string _iconPickerSearch = string.Empty;

    public class IconItemEntry
    {
        public int Id;
        public string Name;
        public string IconPath;
    }

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        InitConfig();
        _harmony = new Harmony("com.mounts.cursebreaker");
        _harmony.PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("Mounts v1.0.0 loaded!");
    }

    private void InitConfig()
    {
        MountSlotPosX = Config.Bind("UI", "EquipmentSlotPosX", -155f, "Horizontal offset of mount equipment slot");
        MountSlotPosY = Config.Bind("UI", "EquipmentSlotPosY", -50f, "Vertical offset of mount equipment slot");
        DebugWindowKeybind = Config.Bind("Debug", "DebugWindowKeybind", new KeyboardShortcut(KeyCode.F8), "Hotkey to toggle in-game mount adjustments overlay");
        IgnoreCombatLevelRequirement = Config.Bind("General", "IgnoreCombatLevelRequirement", false,
            "If enabled, allows equipping and riding all mounts regardless of your player's combat level.");

        _mountList = MountDefinitions.Values.ToList();

        foreach (var def in _mountList)
        {
            string sec = $"Mount: {def.Name}";
            def.CfgPlayerOffsetX = Config.Bind(sec, "Player X Offset", def.RiderXOffset,
                new ConfigDescription("Horizontal left/right offset of rider armature", new AcceptableValueRange<float>(-3.0f, 3.0f)));
            def.CfgPlayerOffsetY = Config.Bind(sec, "Player Y Offset", def.RiderYOffset,
                new ConfigDescription("Vertical up/down offset of rider armature", new AcceptableValueRange<float>(-3.0f, 3.0f)));
            def.CfgPlayerOffsetZ = Config.Bind(sec, "Player Z Offset", def.RiderZOffset,
                new ConfigDescription("Forward/backward offset of rider armature", new AcceptableValueRange<float>(-3.0f, 3.0f)));
            def.CfgMountScale = Config.Bind(sec, "Mount Scale", def.MountScale,
                new ConfigDescription("Visual scale of mount model", new AcceptableValueRange<float>(0.1f, 3.0f)));
            def.CfgIconItemId = Config.Bind(sec, "Icon Item ID", def.IconItemId,
                new ConfigDescription("Item ID used for the mount item icon (or 0 for default)"));
            def.CfgNpcModelId = Config.Bind(sec, "NPC Model ID", def.NpcModelId,
                new ConfigDescription("NPC model ID used for this mount visual"));

            // Initialize definition with saved config values
            def.RiderXOffset = def.CfgPlayerOffsetX.Value;
            def.RiderYOffset = def.CfgPlayerOffsetY.Value;
            def.RiderZOffset = def.CfgPlayerOffsetZ.Value;
            def.MountScale = def.CfgMountScale.Value;
            def.IconItemId = def.CfgIconItemId.Value;
            def.NpcModelId = def.CfgNpcModelId.Value;

            int itemId = def.ItemId;
            def.CfgPlayerOffsetX.SettingChanged += (s, e) => ApplyLiveMountAdjustments(itemId);
            def.CfgPlayerOffsetY.SettingChanged += (s, e) => ApplyLiveMountAdjustments(itemId);
            def.CfgPlayerOffsetZ.SettingChanged += (s, e) => ApplyLiveMountAdjustments(itemId);
            def.CfgMountScale.SettingChanged += (s, e) => ApplyLiveMountAdjustments(itemId);
            def.CfgIconItemId.SettingChanged += (s, e) =>
            {
                if (TryGetMountDefinition(itemId, out var d))
                {
                    d.IconItemId = d.CfgIconItemId.Value;
                    UpdateMountItemIcon(d);
                }
            };
            def.CfgNpcModelId.SettingChanged += (s, e) =>
            {
                if (TryGetMountDefinition(itemId, out var d))
                {
                    d.NpcModelId = d.CfgNpcModelId.Value;
                    if (IsMounted && ActiveMount != null && ActiveMount.ItemId == d.ItemId)
                    {
                        Dismount();
                        MountUp();
                    }
                }
            };

            // Buttons for ModConfigMenu
            Config.Bind(sec, "Spawn Mount Item", false,
                new ConfigDescription($"Click to spawn 1x {def.Name} into your inventory", null, (Action)(() => SpawnMountItem(itemId))));
            Config.Bind(sec, "Choose Mount Item Icon", false,
                new ConfigDescription($"Click to open icon picker grid for {def.Name}", null, (Action)(() => OpenIconPicker(itemId))));
            Config.Bind(sec, "Choose Mount Model", false,
                new ConfigDescription($"Click to open model picker for {def.Name}", null, (Action)(() => OpenModelPicker(itemId))));
        }

        Config.Bind("Mount Adjustments Export", "Export Adjustments", false,
            new ConfigDescription("Export all mount adjustments to BepInEx/config/MountAdjustments_Export.txt & clipboard", null, (Action)(() => ExportMountAdjustments())));
    }

    private void OnDestroy()
    {
        if (IsMounted)
        {
            Dismount();
        }
        CloseDebugWindow();
        CloseIconPicker();
        CloseModelPicker();
        _harmony?.UnpatchSelf();
    }

    private static Vector3 _lastPlayerPos;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_iconPickerModal != null) CloseIconPicker();
            if (_modelPickerModal != null) CloseModelPicker();
        }

        if (DebugWindowKeybind != null && DebugWindowKeybind.Value.IsDown())
        {
            ToggleDebugWindow();
        }

        if (Player.instance != null && Player.instance.stats != null)
        {
            var stat = Player.instance.stats.GetStat(StatsHandler.StatType.movementSpeed);
            if (stat != null && stat.value > 8.0f)
            {
                SanitizeMovementSpeedStat();
            }
        }

        if (!IsMounted || ActiveMount == null)
        {
            if (Player.instance != null)
            {
                _lastPlayerPos = Player.instance.transform.position;
            }
            return;
        }

        if (Player.instance == null || Player.instance.dead || _spawnedMountObject == null)
        {
            Dismount();
            return;
        }

        // Comprehensive movement detection (Pathfinder, WASD direct steering, commands, or transform displacement)
        bool moving = false;
        if (Player.instance.pfinder != null && Player.instance.pfinder.moving)
        {
            moving = true;
        }

        if (Player.instance.commands != null && Player.instance.commands.IsMoving())
        {
            moving = true;
        }

        var wasd = Player.instance.GetComponent<SmoothWASDSteering>();
        if (wasd != null && wasd.IsActiveSteering)
        {
            moving = true;
        }

        Vector3 curPos = Player.instance.transform.position;
        float distMoved = Vector3.Distance(new Vector3(curPos.x, 0f, curPos.z), new Vector3(_lastPlayerPos.x, 0f, _lastPlayerPos.z));
        float displacementSpeed = Time.deltaTime > 0.0001f ? (distMoved / Time.deltaTime) : 0f;
        if (displacementSpeed > 0.2f)
        {
            moving = true;
        }
        _lastPlayerPos = curPos;

        // Calculate player's effective movement speed (including mount boost and all speed buffs)
        float pfinderSpeed = Player.instance.pfinder != null ? Player.instance.pfinder.currentSpeed : 0f;
        float currentSpeed = Mathf.Max(pfinderSpeed, displacementSpeed);
        if (currentSpeed < 1.0f && moving)
        {
            currentSpeed = 4.0f * (1f + ActiveMount.SpeedBoost);
        }

        // Scale mount animation speed relative to player's current speed (4.0f is base unmounted run speed)
        const float baseRunSpeed = 4.0f;
        float animSpeedMultiplier = Mathf.Clamp(currentSpeed / baseRunSpeed, 0.4f, 3.5f);

        // Sync mount animation with player movement state
        if (_mountAnimator != null)
        {
            int desiredAnim = ActiveMount.AnimIdle;
            if (moving)
            {
                bool walking = currentSpeed <= 2.8f && ActiveMount.AnimWalk != 0;
                desiredAnim = walking ? ActiveMount.AnimWalk : ActiveMount.AnimRun;
            }

            if (_currentMountAnimId != desiredAnim || _mountAnimator.currentanimationId != desiredAnim)
            {
                _currentMountAnimId = desiredAnim;
                _mountAnimator.PlayAnimation(desiredAnim);
            }

            if (_mountAnimator.animator != null)
            {
                _mountAnimator.animator.speed = moving ? animSpeedMultiplier : 1.0f;
            }
        }

        // Keep player locked in riding animation 562
        if (Player.instance.animations != null && Player.instance.animations.easyAnimator != null)
        {
            if (Player.instance.animations.easyAnimator.currentanimationId != RidingAnimationId)
            {
                Player.instance.animations.easyAnimator.PlayAnimation(RidingAnimationId, 1f);
            }
        }
    }

    private void LateUpdate()
    {
        if (!IsMounted || ActiveMount == null || Player.instance == null)
        {
            return;
        }

        var eqLoader = Player.instance.equipment != null ? Player.instance.equipment.equipmentLoader : null;
        if (eqLoader != null && eqLoader.armature != null)
        {
            eqLoader.armature.transform.localPosition = new Vector3(ActiveMount.RiderXOffset, ActiveMount.RiderYOffset, ActiveMount.RiderZOffset);
        }
    }

    public static bool IsMountItem(int itemId) => MountDefinitions.ContainsKey(itemId);

    public static int GetEquippedMountItemId()
    {
        if (Player.instance == null || Player.instance.equipment == null)
        {
            return 0;
        }
        var slot = Player.instance.equipment.GetSlot(MountSlotType);
        return slot != null ? slot.itemId : 0;
    }

    public static void EnsureSummonMountAbilityLearned(bool showNotification = true)
    {
        if (Player.instance == null || Player.instance.attacks == null || Player.instance.attacks.learnedAbilities == null)
        {
            return;
        }

        if (!Player.instance.attacks.learnedAbilities.Contains(SummonMountAbilityId))
        {
            if (showNotification)
            {
                Player.instance.attacks.LearnAbility(SummonMountAbilityId);
            }
            else
            {
                Player.instance.attacks.learnedAbilities.Add(SummonMountAbilityId);
                if (UI_Spellbook.instance != null && UI_Spellbook.instance.active)
                {
                    UI_Spellbook.instance.DrawUI();
                }
            }
        }
    }

    public static void ToggleMount()
    {
        if (Player.instance == null || Player.instance.dead)
        {
            return;
        }

        if (IsMounted)
        {
            Dismount();
        }
        else
        {
            MountUp();
        }
    }

    public static void MountUp()
    {
        if (Player.instance == null || Player.instance.dead)
        {
            return;
        }

        if (FastTravelHorse.instance != null && FastTravelHorse.instance.active)
        {
            UI_Notifications.instance?.ShowDefaultNotification("You cannot summon a mount while fast traveling.", 0f);
            return;
        }

        int equippedId = GetEquippedMountItemId();
        if (equippedId == 0 || !MountDefinitions.TryGetValue(equippedId, out var mountDef))
        {
            UI_Notifications.instance?.ShowDefaultNotification("You do not have a mount equipped.", 0f);
            return;
        }

        int playerCombatLv = Player.instance.GetCombatLevel();
        if (playerCombatLv < mountDef.CombatLevel && (IgnoreCombatLevelRequirement == null || !IgnoreCombatLevelRequirement.Value))
        {
            UI_Notifications.instance?.ShowDefaultNotification(
                $"You need a Combat Level of {mountDef.CombatLevel} to ride the {mountDef.Name}.", 0f);
            return;
        }

        if (IsMounted)
        {
            Dismount();
        }

        GameObject prefab = null;
        if (mountDef.UseHorseResource)
        {
            prefab = Resources.Load<GameObject>("Horse");
        }
        if (prefab == null)
        {
            prefab = Resources.Load<GameObject>("NPCs/npc_" + mountDef.NpcModelId);
        }
        if (prefab == null)
        {
            Log.LogError($"Could not load mount model for {mountDef.Name} (NpcModelId={mountDef.NpcModelId})");
            return;
        }

        _spawnedMountObject = Instantiate(prefab, Player.instance.transform);
        _spawnedMountObject.name = "ActiveMount_" + mountDef.Name;
        _spawnedMountObject.SetActive(true);
        _spawnedMountObject.transform.localPosition = Vector3.zero;
        _spawnedMountObject.transform.localRotation = Quaternion.identity;
        _spawnedMountObject.transform.localScale = Vector3.one * mountDef.MountScale;

        // Disable colliders on the mount visual so it doesn't intercept mouse clicks or physics
        foreach (var col in _spawnedMountObject.GetComponentsInChildren<Collider>(true))
        {
            col.enabled = false;
        }

        _mountAnimator = _spawnedMountObject.GetComponentInChildren<EasyAnimator>(true);
        if (_mountAnimator != null)
        {
            _mountAnimator.Setup();
            _currentMountAnimId = mountDef.AnimIdle;
            _mountAnimator.PlayAnimation(mountDef.AnimIdle);
        }

        IsMounted = true;
        ActiveMount = mountDef;

        // Elevate player armature to saddle height and play riding animation
        var eqLoader = Player.instance.equipment != null ? Player.instance.equipment.equipmentLoader : null;
        if (eqLoader != null && eqLoader.armature != null)
        {
            eqLoader.armature.transform.localPosition = new Vector3(mountDef.RiderXOffset, mountDef.RiderYOffset, mountDef.RiderZOffset);
            foreach (var smr in Player.instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
            }
        }

        if (Player.instance.animations != null && Player.instance.animations.easyAnimator != null)
        {
            Player.instance.animations.easyAnimator.PlayAnimation(RidingAnimationId, 1f);
        }

        if (mountDef.SoundId != 0 && AudioHandler.instance != null)
        {
            AudioHandler.instance.PlaySound(mountDef.SoundId, Player.instance.transform.position);
        }

        RefreshMovementSpeed();
    }

    public static void Dismount(string notification = null)
    {
        if (!IsMounted && _spawnedMountObject == null)
        {
            return;
        }

        var prevMount = ActiveMount;
        IsMounted = false;
        ActiveMount = null;
        _currentMountAnimId = 0;
        _mountAnimator = null;

        if (_spawnedMountObject != null)
        {
            Destroy(_spawnedMountObject);
            _spawnedMountObject = null;
        }

        if (Player.instance != null)
        {
            var eqLoader = Player.instance.equipment != null ? Player.instance.equipment.equipmentLoader : null;
            if (eqLoader != null && eqLoader.armature != null)
            {
                eqLoader.armature.transform.localPosition = Vector3.zero;
            }

            if (!Player.instance.dead && Player.instance.animations != null)
            {
                Player.instance.animations.UpdateAnimations();
                int nextAnim = Player.instance.pfinder != null && Player.instance.pfinder.moving
                    ? (Player.instance.pfinder.walking ? Player.instance.animations.GetWalkAnimation() : Player.instance.animations.anim_run)
                    : Player.instance.animations.GetIdleAnimation();
                Player.instance.animations.PlayAnimation(nextAnim, 1f, forceChange: true);
            }

            if (prevMount != null && prevMount.SoundId != 0 && AudioHandler.instance != null)
            {
                AudioHandler.instance.PlaySound(prevMount.SoundId, Player.instance.transform.position);
            }
        }

        RefreshMovementSpeed();

        if (!string.IsNullOrEmpty(notification))
        {
            UI_Notifications.instance?.ShowDefaultNotification(notification, 0f);
        }
    }

    public static void SanitizeMovementSpeedStat()
    {
        if (Player.instance != null && Player.instance.stats != null)
        {
            var stat = Player.instance.stats.GetStat(StatsHandler.StatType.movementSpeed);
            if (stat != null && stat.value > 8.0f)
            {
                Log?.LogWarning($"Sanitizing corrupted movementSpeed stat from {stat.value} back to 4.0");
                stat.value = 4.0f;
                if (Player.instance.pfinder != null)
                {
                    Player.instance.pfinder.runSpeed = 4.0f;
                }
            }
        }
    }

    private static void RefreshMovementSpeed()
    {
        SanitizeMovementSpeedStat();

        if (Player.instance != null && Player.instance.pfinder != null)
        {
            float baseSpeed = Player.instance.stats != null ? Player.instance.stats.GetStatValue(StatsHandler.StatType.movementSpeed) : 4f;
            Player.instance.pfinder.currentSpeed = IsMounted && ActiveMount != null
                ? Mathf.Max(0.5f, baseSpeed * (1f + ActiveMount.SpeedBoost))
                : baseSpeed;
            if (Player.instance.pfinder.moving)
            {
                Player.instance.pfinder.SetUnitMoveAnimationAndSpeed(Player.instance.pfinder.targetPos);
            }
        }

        if (UI_BottomRightTabs_Equipment.instance != null && UI_BottomRightTabs_Equipment.instance.active)
        {
            UI_BottomRightTabs_Equipment.instance.UpdateStat(StatsHandler.StatType.movementSpeed, 0f);
        }
        if (UI_Stats.instance != null && UI_Stats.instance.active && Player.instance != null && Player.instance.stats != null)
        {
            UI_Stats.instance.UpdateStat(StatsHandler.StatType.movementSpeed, Player.instance.stats.GetStatValue(StatsHandler.StatType.movementSpeed));
        }
    }

    // =========================================================================
    // Debug, Live Adjustments & Icon Picker
    // =========================================================================

    public static void ApplyLiveMountAdjustments(int mountItemId)
    {
        if (!TryGetMountDefinition(mountItemId, out var def)) return;
        if (def.CfgPlayerOffsetX != null) def.RiderXOffset = def.CfgPlayerOffsetX.Value;
        if (def.CfgPlayerOffsetY != null) def.RiderYOffset = def.CfgPlayerOffsetY.Value;
        if (def.CfgPlayerOffsetZ != null) def.RiderZOffset = def.CfgPlayerOffsetZ.Value;
        if (def.CfgMountScale != null) def.MountScale = def.CfgMountScale.Value;

        if (IsMounted && ActiveMount != null && ActiveMount.ItemId == mountItemId)
        {
            var eqLoader = Player.instance?.equipment?.equipmentLoader;
            if (eqLoader?.armature != null)
            {
                eqLoader.armature.transform.localPosition = new Vector3(def.RiderXOffset, def.RiderYOffset, def.RiderZOffset);
            }
            if (_spawnedMountObject != null)
            {
                _spawnedMountObject.transform.localScale = Vector3.one * def.MountScale;
            }
        }
    }

    public static void UpdateMountItemIcon(MountDefinition def)
    {
        if (ItemHandler.instance?.items != null)
        {
            var item = ItemHandler.instance.GetItem(def.ItemId);
            if (item != null)
            {
                item.copyModel = def.IconItemId;
            }
        }
        UI_BottomRightTabs_Equipment.instance?.ChangeEquips();
        if (UI_Inventory.instance?.itemSlots != null)
        {
            foreach (var slot in UI_Inventory.instance.itemSlots)
            {
                if (slot != null && slot.itemId == def.ItemId && slot.image_itemIcon != null)
                {
                    slot.image_itemIcon.SetImage(UI_Inventory.instance.GetItemIcon(def.ItemId));
                }
            }
        }
    }

    public static void SpawnMountItem(int mountItemId)
    {
        if (Player.instance == null || UI_Inventory.instance == null)
        {
            Log?.LogWarning("Cannot spawn mount item: Player or Inventory not available. Load a game first.");
            return;
        }

        UI_Inventory.instance.AddItemToMainInventoryOnly(mountItemId, 1);
        AudioHandler.instance?.Play2DSound(12);
        if (TryGetMountDefinition(mountItemId, out var def))
        {
            UI_Notifications.instance?.ShowDefaultNotification($"Spawned {def.Name} into inventory!", 0f);
            Log?.LogInfo($"Spawned {def.Name} (#{mountItemId}) into player inventory.");
        }
    }

    public static void ExportMountAdjustments()
    {
        var sb = new StringBuilder();
        sb.AppendLine("// ===========================================================================");
        sb.AppendLine($"// Mount Adjustments Export - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("// Paste these values into MountDefinitions in Mounts/Plugin.cs");
        sb.AppendLine("// ===========================================================================");
        sb.AppendLine();

        foreach (var kvp in MountDefinitions)
        {
            var def = kvp.Value;
            float x = def.CfgPlayerOffsetX != null ? def.CfgPlayerOffsetX.Value : def.RiderXOffset;
            float y = def.CfgPlayerOffsetY != null ? def.CfgPlayerOffsetY.Value : def.RiderYOffset;
            float z = def.CfgPlayerOffsetZ != null ? def.CfgPlayerOffsetZ.Value : def.RiderZOffset;
            float scale = def.CfgMountScale != null ? def.CfgMountScale.Value : def.MountScale;
            int icon = def.CfgIconItemId != null ? def.CfgIconItemId.Value : def.IconItemId;
            int model = def.CfgNpcModelId != null ? def.CfgNpcModelId.Value : def.NpcModelId;

            sb.AppendLine($"// [{def.ItemId}] {def.Name}");
            sb.AppendLine($"NpcModelId = {model},");
            sb.AppendLine($"RiderXOffset = {x:0.###}f,");
            sb.AppendLine($"RiderYOffset = {y:0.###}f,");
            sb.AppendLine($"RiderZOffset = {z:0.###}f,");
            sb.AppendLine($"MountScale = {scale:0.###}f,");
            sb.AppendLine($"IconItemId = {icon},");
            sb.AppendLine();
        }

        string result = sb.ToString();

        try
        {
            string exportPath = Path.Combine(Paths.ConfigPath, "MountAdjustments_Export.txt");
            File.WriteAllText(exportPath, result);
            Log?.LogInfo($"Exported mount adjustments to {exportPath}");
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"Failed to write export file: {ex.Message}");
        }

        try
        {
            GUIUtility.systemCopyBuffer = result;
        }
        catch (Exception ex)
        {
            Log?.LogWarning($"Failed to copy to clipboard: {ex.Message}");
        }

        Log?.LogInfo("\n" + result);
        UI_Notifications.instance?.ShowDefaultNotification("Mount adjustments exported to file & clipboard!", 0f);
        AudioHandler.instance?.Play2DSound(12);
    }

    private static List<IconItemEntry> GetAllAvailableIcons()
    {
        var list = new List<IconItemEntry>();
        var seenIds = new HashSet<int>();

        // 1. Mod's generated icons
        list.Add(new IconItemEntry { Id = 9505, Name = "Mount Silhouette (Generated)", IconPath = "ItemIcons/9505" });
        seenIds.Add(9505);
        list.Add(new IconItemEntry { Id = SummonMountAbilityId, Name = "Summon Mount Ability (Generated)", IconPath = $"Abilities/{SummonMountAbilityId}" });
        seenIds.Add(SummonMountAbilityId);

        // 2. All items in game
        if (ItemHandler.instance != null && (ItemHandler.instance.items == null || ItemHandler.instance.items.Count == 0))
        {
            try
            {
                ItemHandler.instance.LoadItems();
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"ItemHandler.LoadItems attempt failed: {ex.Message}");
            }
        }

        if (ItemHandler.instance?.items != null)
        {
            foreach (var it in ItemHandler.instance.items)
            {
                if (it == null || it.typeId <= 0 || it.typeId >= 1000000) continue;
                if (IsMountItem(it.typeId)) continue;

                if (seenIds.Add(it.typeId))
                {
                    int copyId = it.copyModel != 0 ? it.copyModel : it.typeId;
                    string path = UI_Inventory.instance != null ? UI_Inventory.instance.GetItemIcon(it.typeId) : $"ItemIcons/{copyId}";
                    list.Add(new IconItemEntry
                    {
                        Id = it.typeId,
                        Name = it.itemName ?? $"Item #{it.typeId}",
                        IconPath = path
                    });
                }
            }
        }

        return list;
    }

    public static void OpenIconPicker(int mountItemId)
    {
        if (!TryGetMountDefinition(mountItemId, out var def)) return;
        _iconPickerTargetMount = def;
        _iconPickerPage = 0;
        _iconPickerSearch = string.Empty;
        RenderIconPickerModal();
    }

    public static void CloseIconPicker()
    {
        if (_iconPickerModal != null)
        {
            _iconPickerModal.Destroy();
            _iconPickerModal = null;
        }
        _iconPickerTargetMount = null;
    }

    public static void RenderIconPickerModal()
    {
        if (_iconPickerTargetMount == null) return;

        if (_iconPickerModal != null)
        {
            _iconPickerModal.Destroy();
            _iconPickerModal = null;
        }

        object parent = null;
        if (UI_Options.instance != null && UI_Options.instance.active && UI_Options.instance.container != null)
        {
            parent = UI_Options.instance.container;
        }
        else if (CBUI.instance?.canvasRect != null)
        {
            parent = CBUI.instance.canvasRect;
        }
        else if (UI_Options.instance?.container != null)
        {
            parent = UI_Options.instance.container;
        }

        if (parent == null)
        {
            Log?.LogWarning("RenderIconPickerModal: no valid UI parent container found.");
            return;
        }

        _iconPickerModal = LUI_Core.instance.AddImage(
            "DefaultUI/DefaultWindowBackground",
            Position.center,
            Position.center,
            Vector2.zero,
            new Vector2(560f, 490f),
            parent,
            raycast: true);

        if (_iconPickerModal?.rectTransform != null)
        {
            _iconPickerModal.rectTransform.SetAsLastSibling();
        }

        Transform win = _iconPickerModal.GetParentTransform();

        // Title
        LUI_Core.AddText(
            17,
            TextAnchor.MiddleCenter,
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(0f, -15f),
            new Vector2(400f, 28f),
            win,
            $"Choose Icon: {_iconPickerTargetMount.Name}");

        // Close button
        LUI_Core.instance.AddButton(
            "X",
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(255f, -15f),
            new Vector2(28f, 28f),
            _ => CloseIconPicker(),
            null,
            win);

        // Search input
        LUI_InputField searchInput = LUI_Core.instance.AddInputField(
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(-35f, -50f),
            new Vector2(370f, 28f),
            win,
            TextAnchor.MiddleLeft);

        searchInput.SetText(_iconPickerSearch);
        searchInput.inputField.onEndEdit.AddListener(text =>
        {
            _iconPickerSearch = text ?? "";
            _iconPickerPage = 0;
            RenderIconPickerModal();
        });

        // Search button
        LUI_Core.instance.AddButton(
            "Search",
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(175f, -50f),
            new Vector2(60f, 28f),
            _ =>
            {
                _iconPickerSearch = searchInput.inputField.text ?? "";
                _iconPickerPage = 0;
                RenderIconPickerModal();
            },
            null,
            win);

        // Clear button
        LUI_Core.instance.AddButton(
            "Clear",
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(240f, -50f),
            new Vector2(50f, 28f),
            _ =>
            {
                _iconPickerSearch = "";
                _iconPickerPage = 0;
                RenderIconPickerModal();
            },
            null,
            win);

        // Filter icons
        var allIcons = GetAllAvailableIcons();
        if (!string.IsNullOrWhiteSpace(_iconPickerSearch))
        {
            string q = _iconPickerSearch.Trim();
            allIcons = allIcons.Where(x =>
                x.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                x.Id.ToString().Contains(q) ||
                x.IconPath.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            ).ToList();
        }

        const int cols = 8;
        const int rows = 6;
        int pageSize = cols * rows; // 48
        int totalPages = Mathf.Max(1, (allIcons.Count + pageSize - 1) / pageSize);
        if (_iconPickerPage >= totalPages) _iconPickerPage = totalPages - 1;
        if (_iconPickerPage < 0) _iconPickerPage = 0;

        var pagedIcons = allIcons.Skip(_iconPickerPage * pageSize).Take(pageSize).ToList();

        float startX = -220f;
        float stepX = 63f;
        float startY = -92f;
        float stepY = 54f;

        for (int i = 0; i < pagedIcons.Count; i++)
        {
            var entry = pagedIcons[i];
            int c = i % cols;
            int r = i / cols;
            Vector2 pos = new Vector2(startX + c * stepX, startY - r * stepY);

            var btn = LUI_Core.instance.AddButton(
                "EquipmentTab/SlotBg",
                "",
                12,
                Position.topMiddle,
                Position.topMiddle,
                pos,
                new Vector2(50f, 50f),
                _ =>
                {
                    if (_iconPickerTargetMount != null)
                    {
                        _iconPickerTargetMount.IconItemId = entry.Id;
                        if (_iconPickerTargetMount.CfgIconItemId != null)
                        {
                            _iconPickerTargetMount.CfgIconItemId.Value = entry.Id;
                        }
                        UpdateMountItemIcon(_iconPickerTargetMount);
                        Instance?.Config.Save();
                        AudioHandler.instance?.Play2DSound(12);
                        UI_Notifications.instance?.ShowDefaultNotification($"Icon for {_iconPickerTargetMount.Name} set to #{entry.Id} ({entry.Name})", 0f);
                        CloseIconPicker();
                    }
                },
                null,
                win);

            try
            {
                LUI_Core.instance.AddImage(
                    entry.IconPath,
                    Position.center,
                    Position.center,
                    Vector2.zero,
                    new Vector2(42f, 42f),
                    btn.GetParentTransform(),
                    raycast: false);
            }
            catch (Exception ex)
            {
                Log?.LogWarning($"Failed to render icon {entry.IconPath}: {ex.Message}");
            }

            string hover = $"{entry.Name}\nID: #{entry.Id}\nPath: {entry.IconPath}";
            btn.AddMouseHover(
                _ => UI_Cursor.instance?.SetUIMouseHoverText(hover),
                null,
                _ => UI_Cursor.instance?.SetUIMouseHoverText(""),
                null);
        }

        // Footer Pagination
        LUI_Core.instance.AddButton(
            "< Prev",
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(-150f, -440f),
            new Vector2(85f, 28f),
            _ =>
            {
                if (_iconPickerPage > 0)
                {
                    _iconPickerPage--;
                    RenderIconPickerModal();
                }
            },
            null,
            win);

        LUI_Core.AddText(
            14,
            TextAnchor.MiddleCenter,
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(0f, -440f),
            new Vector2(200f, 28f),
            win,
            $"Page {_iconPickerPage + 1} / {totalPages} ({allIcons.Count} icons)");

        LUI_Core.instance.AddButton(
            "Next >",
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(150f, -440f),
            new Vector2(85f, 28f),
            _ =>
            {
                if (_iconPickerPage < totalPages - 1)
                {
                    _iconPickerPage++;
                    RenderIconPickerModal();
                }
            },
            null,
            win);
    }

    // =========================================================================
    // Model Picker Modal
    // =========================================================================

    private static LUIRect _modelPickerModal;
    private static MountDefinition _modelPickerTargetMount;
    private static int _modelPickerPage = 0;
    private static string _modelPickerSearch = string.Empty;
    private static string _modelPickerDirectInput = string.Empty;

    public sealed class NpcModelEntry
    {
        public int ModelId;
        public string Name;
        public int AnimIdle;
        public int AnimWalk;
        public int AnimRun;
        public bool UseHorseResource;
    }

    private static List<NpcModelEntry> GetAllAvailableNpcModels()
    {
        var list = new List<NpcModelEntry>();
        var seenModelIds = new HashSet<int>();

        var curated = new[]
        {
            new NpcModelEntry { ModelId = 910, Name = "Wooly Sheep (Shearston)", AnimIdle = 701, AnimWalk = 702, AnimRun = 702 },
            new NpcModelEntry { ModelId = 73, Name = "Riding Sheep (Ratcliffe)", AnimIdle = 701, AnimWalk = 702, AnimRun = 702 },
            new NpcModelEntry { ModelId = 498, Name = "Mountain Goat (Trihorn)", AnimIdle = 804, AnimWalk = 805, AnimRun = 805 },
            new NpcModelEntry { ModelId = 1640, Name = "Riding Horse", AnimIdle = 766, AnimWalk = 765, AnimRun = 765, UseHorseResource = true },
            new NpcModelEntry { ModelId = 71, Name = "Dairy Cow", AnimIdle = 717, AnimWalk = 718, AnimRun = 718 },
            new NpcModelEntry { ModelId = 1, Name = "Wolf / Dire Wolf", AnimIdle = 689, AnimWalk = 823, AnimRun = 690 },
            new NpcModelEntry { ModelId = 151, Name = "Scrawny Wolf", AnimIdle = 689, AnimWalk = 823, AnimRun = 690 },
            new NpcModelEntry { ModelId = 50, Name = "Mistywood Bear", AnimIdle = 729, AnimWalk = 730, AnimRun = 731 },
            new NpcModelEntry { ModelId = 224, Name = "Winterlands Elk / Deer", AnimIdle = 747, AnimWalk = 750, AnimRun = 750 },
            new NpcModelEntry { ModelId = 450, Name = "Draconic / Icewyrm Hound", AnimIdle = 721, AnimWalk = 722, AnimRun = 1109 },
            new NpcModelEntry { ModelId = 154, Name = "Deep Forest Snail", AnimIdle = 753, AnimWalk = 754, AnimRun = 754 },
            new NpcModelEntry { ModelId = 838, Name = "Giant Snail", AnimIdle = 753, AnimWalk = 754, AnimRun = 754 },
            new NpcModelEntry { ModelId = 447, Name = "Dog", AnimIdle = 709, AnimWalk = 710, AnimRun = 710 },
            new NpcModelEntry { ModelId = 111, Name = "Cat", AnimIdle = 704, AnimWalk = 824, AnimRun = 705 },
            new NpcModelEntry { ModelId = 45, Name = "Crab", AnimIdle = 738, AnimWalk = 739, AnimRun = 739 },
            new NpcModelEntry { ModelId = 448, Name = "Spider", AnimIdle = 713, AnimWalk = 714, AnimRun = 714 },
            new NpcModelEntry { ModelId = 63, Name = "Giant Rat", AnimIdle = 725, AnimWalk = 726, AnimRun = 726 },
            new NpcModelEntry { ModelId = 440, Name = "Skeleton Warrior", AnimIdle = 1, AnimWalk = 2, AnimRun = 3 }
        };

        foreach (var c in curated)
        {
            if (seenModelIds.Add(c.ModelId))
            {
                list.Add(c);
            }
        }

        if (NPCHandler.instance != null && (NPCHandler.instance.npcTemplates == null || NPCHandler.instance.npcTemplates.Count == 0))
        {
            try { NPCHandler.instance.LoadNpcTemplates(); } catch { }
        }

        if (NPCHandler.instance?.npcTemplates != null)
        {
            foreach (var t in NPCHandler.instance.npcTemplates)
            {
                if (t == null) continue;
                int modelId = t.modelId != 0 ? t.modelId : t.typeId;
                if (modelId <= 0 || !seenModelIds.Add(modelId)) continue;

                list.Add(new NpcModelEntry
                {
                    ModelId = modelId,
                    Name = !string.IsNullOrEmpty(t.npcName) ? t.npcName : $"NPC #{modelId}",
                    AnimIdle = t.anim_idle != 0 ? t.anim_idle : 1,
                    AnimWalk = t.anim_walk != 0 ? t.anim_walk : 2,
                    AnimRun = t.anim_run != 0 ? t.anim_run : (t.anim_walk != 0 ? t.anim_walk : 2),
                    UseHorseResource = false
                });
            }
        }

        return list;
    }

    public static void OpenModelPicker(int mountItemId)
    {
        if (!TryGetMountDefinition(mountItemId, out var def)) return;
        _modelPickerTargetMount = def;
        _modelPickerPage = 0;
        _modelPickerSearch = string.Empty;
        _modelPickerDirectInput = def.NpcModelId.ToString();
        RenderModelPickerModal();
    }

    public static void CloseModelPicker()
    {
        if (_modelPickerModal != null)
        {
            _modelPickerModal.Destroy();
            _modelPickerModal = null;
        }
        _modelPickerTargetMount = null;
    }

    public static void ApplyMountModelChange(MountDefinition def, int modelId, int animIdle, int animWalk, int animRun, bool useHorseResource)
    {
        if (def == null) return;
        def.NpcModelId = modelId;
        if (animIdle != 0) def.AnimIdle = animIdle;
        if (animWalk != 0) def.AnimWalk = animWalk;
        if (animRun != 0) def.AnimRun = animRun;
        def.UseHorseResource = useHorseResource;
        if (def.CfgNpcModelId != null)
        {
            def.CfgNpcModelId.Value = modelId;
        }
        Instance?.Config.Save();

        if (IsMounted && ActiveMount != null && ActiveMount.ItemId == def.ItemId)
        {
            Dismount();
            MountUp();
        }

        UI_Notifications.instance?.ShowDefaultNotification($"Mount model set to #{modelId} ({def.Name})", 0f);
        AudioHandler.instance?.Play2DSound(12);
    }

    public static void RenderModelPickerModal()
    {
        if (_modelPickerTargetMount == null) return;

        if (_modelPickerModal != null)
        {
            _modelPickerModal.Destroy();
            _modelPickerModal = null;
        }

        object parent = null;
        if (UI_Options.instance != null && UI_Options.instance.active && UI_Options.instance.container != null)
        {
            parent = UI_Options.instance.container;
        }
        else if (CBUI.instance?.canvasRect != null)
        {
            parent = CBUI.instance.canvasRect;
        }
        else if (UI_Options.instance?.container != null)
        {
            parent = UI_Options.instance.container;
        }

        if (parent == null)
        {
            Log?.LogWarning("RenderModelPickerModal: no valid UI parent container found.");
            return;
        }

        _modelPickerModal = LUI_Core.instance.AddImage(
            "DefaultUI/DefaultWindowBackground",
            Position.center,
            Position.center,
            Vector2.zero,
            new Vector2(560f, 490f),
            parent,
            raycast: true);

        if (_modelPickerModal?.rectTransform != null)
        {
            _modelPickerModal.rectTransform.SetAsLastSibling();
        }

        Transform win = _modelPickerModal.GetParentTransform();

        // Title
        LUI_Core.AddText(
            17,
            TextAnchor.MiddleCenter,
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(0f, -15f),
            new Vector2(480f, 28f),
            win,
            $"Choose Model: {_modelPickerTargetMount.Name} <color=yellow>(#{_modelPickerTargetMount.NpcModelId})</color>");

        // Close button
        LUI_Core.instance.AddButton(
            "X",
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(255f, -15f),
            new Vector2(28f, 28f),
            _ => CloseModelPicker(),
            null,
            win);

        // Search input
        LUI_InputField searchInput = LUI_Core.instance.AddInputField(
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(-35f, -48f),
            new Vector2(370f, 26f),
            win,
            TextAnchor.MiddleLeft);

        searchInput.SetText(_modelPickerSearch);
        searchInput.inputField.onEndEdit.AddListener(text =>
        {
            _modelPickerSearch = text ?? "";
            _modelPickerPage = 0;
            RenderModelPickerModal();
        });

        // Search button
        LUI_Core.instance.AddButton(
            "Search",
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(175f, -48f),
            new Vector2(60f, 26f),
            _ =>
            {
                _modelPickerSearch = searchInput.inputField.text ?? "";
                _modelPickerPage = 0;
                RenderModelPickerModal();
            },
            null,
            win);

        // Clear button
        LUI_Core.instance.AddButton(
            "Clear",
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(240f, -48f),
            new Vector2(50f, 26f),
            _ =>
            {
                _modelPickerSearch = "";
                _modelPickerPage = 0;
                RenderModelPickerModal();
            },
            null,
            win);

        // Direct Model ID row
        LUI_Core.AddText(
            13,
            TextAnchor.MiddleLeft,
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(-185f, -80f),
            new Vector2(110f, 24f),
            win,
            "Direct Model ID:");

        LUI_InputField directIdInput = LUI_Core.instance.AddInputField(
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(-35f, -80f),
            new Vector2(170f, 24f),
            win,
            TextAnchor.MiddleLeft);

        directIdInput.SetText(_modelPickerDirectInput);
        directIdInput.inputField.onEndEdit.AddListener(t => _modelPickerDirectInput = t ?? "");

        LUI_Core.instance.AddButton(
            "Apply ID",
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(110f, -80f),
            new Vector2(90f, 24f),
            _ =>
            {
                if (int.TryParse(directIdInput.inputField.text, out int customId) && customId > 0)
                {
                    var all = GetAllAvailableNpcModels();
                    var match = all.FirstOrDefault(m => m.ModelId == customId);
                    int idle = match != null ? match.AnimIdle : _modelPickerTargetMount.AnimIdle;
                    int walk = match != null ? match.AnimWalk : _modelPickerTargetMount.AnimWalk;
                    int run = match != null ? match.AnimRun : _modelPickerTargetMount.AnimRun;
                    bool horse = match != null && match.UseHorseResource;
                    ApplyMountModelChange(_modelPickerTargetMount, customId, idle, walk, run, horse);
                    CloseModelPicker();
                }
                else
                {
                    UI_Notifications.instance?.ShowDefaultNotification("Invalid Model ID number.", 0f);
                }
            },
            null,
            win);

        // Filter models
        var allModels = GetAllAvailableNpcModels();
        if (!string.IsNullOrWhiteSpace(_modelPickerSearch))
        {
            string q = _modelPickerSearch.Trim();
            allModels = allModels.Where(x =>
                x.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                x.ModelId.ToString().Contains(q)
            ).ToList();
        }

        const int pageSize = 8;
        int totalPages = Mathf.Max(1, (allModels.Count + pageSize - 1) / pageSize);
        if (_modelPickerPage >= totalPages) _modelPickerPage = totalPages - 1;
        if (_modelPickerPage < 0) _modelPickerPage = 0;

        var pagedModels = allModels.Skip(_modelPickerPage * pageSize).Take(pageSize).ToList();
        float y = -115f;

        for (int i = 0; i < pagedModels.Count; i++)
        {
            var entry = pagedModels[i];
            bool isCurrent = _modelPickerTargetMount != null && _modelPickerTargetMount.NpcModelId == entry.ModelId;
            string prefix = isCurrent ? "<color=#55FF55>► </color>" : "";
            string label = $"{prefix}<color=#F5C542>[#{entry.ModelId}]</color> {entry.Name} <size=11><color=#888888>(Idle: {entry.AnimIdle}, Walk: {entry.AnimWalk}, Run: {entry.AnimRun})</color></size>";

            LUI_Core.AddText(
                13,
                TextAnchor.MiddleLeft,
                Position.topMiddle,
                Position.topMiddle,
                new Vector2(-45f, y),
                new Vector2(360f, 30f),
                win,
                label);

            LUI_Core.instance.AddButton(
                isCurrent ? "Active" : "Select",
                Position.topMiddle,
                Position.topMiddle,
                new Vector2(200f, y),
                new Vector2(80f, 28f),
                _ =>
                {
                    if (_modelPickerTargetMount != null)
                    {
                        ApplyMountModelChange(_modelPickerTargetMount, entry.ModelId, entry.AnimIdle, entry.AnimWalk, entry.AnimRun, entry.UseHorseResource);
                        CloseModelPicker();
                    }
                },
                null,
                win);

            y -= 36f;
        }

        // Footer Pagination
        LUI_Core.instance.AddButton(
            "< Prev",
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(-150f, -445f),
            new Vector2(85f, 28f),
            _ =>
            {
                if (_modelPickerPage > 0)
                {
                    _modelPickerPage--;
                    RenderModelPickerModal();
                }
            },
            null,
            win);

        LUI_Core.AddText(
            14,
            TextAnchor.MiddleCenter,
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(0f, -445f),
            new Vector2(200f, 28f),
            win,
            $"Page {_modelPickerPage + 1} / {totalPages} ({allModels.Count} models)");

        LUI_Core.instance.AddButton(
            "Next >",
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(150f, -445f),
            new Vector2(85f, 28f),
            _ =>
            {
                if (_modelPickerPage < totalPages - 1)
                {
                    _modelPickerPage++;
                    RenderModelPickerModal();
                }
            },
            null,
            win);
    }

    public static void ToggleDebugWindow()
    {
        if (_debugWindow != null)
        {
            CloseDebugWindow();
        }
        else
        {
            OpenDebugWindow();
        }
    }

    public static void CloseDebugWindow()
    {
        if (_debugWindow != null)
        {
            _debugWindow.Destroy();
            _debugWindow = null;
        }
    }

    public static void OpenDebugWindow()
    {
        CloseDebugWindow();
        if (_mountList == null || _mountList.Count == 0)
        {
            _mountList = MountDefinitions.Values.ToList();
        }

        if (IsMounted && ActiveMount != null)
        {
            int idx = _mountList.FindIndex(m => m.ItemId == ActiveMount.ItemId);
            if (idx >= 0) _debugSelectedMountIndex = idx;
        }

        object parent = null;
        if (UI_Options.instance != null && UI_Options.instance.active && UI_Options.instance.container != null)
        {
            parent = UI_Options.instance.container;
        }
        else if (CBUI.instance?.canvasRect != null)
        {
            parent = CBUI.instance.canvasRect;
        }
        else if (UI_Options.instance?.container != null)
        {
            parent = UI_Options.instance.container;
        }

        if (parent == null) return;

        _debugWindow = LUI_Core.instance.AddImage(
            "DefaultUI/DefaultWindowBackground",
            Position.topLeft,
            Position.topLeft,
            new Vector2(25f, -75f),
            new Vector2(340f, 425f),
            parent,
            raycast: true);

        if (_debugWindow?.rectTransform != null)
        {
            _debugWindow.rectTransform.SetAsLastSibling();
        }

        RenderDebugWindowContent();
    }

    private static void RenderDebugWindowContent()
    {
        if (_debugWindow == null || _mountList == null || _mountList.Count == 0) return;

        Transform win = _debugWindow.GetParentTransform();
        var cur = _mountList[_debugSelectedMountIndex];

        // Title
        LUI_Core.AddText(
            16,
            TextAnchor.MiddleCenter,
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(0f, -14f),
            new Vector2(240f, 24f),
            win,
            "<color=#E5B73B><b>Mount Adjustments</b></color>");

        // Close button
        LUI_Core.instance.AddButton(
            "X",
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(145f, -14f),
            new Vector2(24f, 24f),
            _ => CloseDebugWindow(),
            null,
            win);

        // Mount selector row
        LUI_Core.instance.AddButton(
            "<",
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(-130f, -44f),
            new Vector2(28f, 26f),
            _ =>
            {
                _debugSelectedMountIndex = (_debugSelectedMountIndex - 1 + _mountList.Count) % _mountList.Count;
                OpenDebugWindow();
            },
            null,
            win);

        LUI_Core.instance.AddButton(
            $"{cur.Name} ({_debugSelectedMountIndex + 1}/{_mountList.Count})",
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(0f, -44f),
            new Vector2(220f, 26f),
            _ =>
            {
                _debugSelectedMountIndex = (_debugSelectedMountIndex + 1) % _mountList.Count;
                OpenDebugWindow();
            },
            null,
            win);

        LUI_Core.instance.AddButton(
            ">",
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(130f, -44f),
            new Vector2(28f, 26f),
            _ =>
            {
                _debugSelectedMountIndex = (_debugSelectedMountIndex + 1) % _mountList.Count;
                OpenDebugWindow();
            },
            null,
            win);

        // Sliders
        // 1. Player X
        var xLabel = LUI_Core.AddText(13, TextAnchor.MiddleLeft, Position.topLeft, Position.topLeft,
            new Vector2(15f, -76f), new Vector2(300f, 18f), win, $"Player X: <color=yellow>{cur.RiderXOffset:0.##}</color>");

        var xSlider = LUI_Core.instance.AddSlider("", -3f, 3f, Position.topLeft, Position.topLeft,
            new Vector2(15f, -96f), new Vector2(310f, 16f), win);
        xSlider.SetValue(cur.RiderXOffset);
        xSlider.slider.onValueChanged.AddListener(v =>
        {
            cur.RiderXOffset = (float)Math.Round(v, 2);
            if (cur.CfgPlayerOffsetX != null) cur.CfgPlayerOffsetX.Value = cur.RiderXOffset;
            ApplyLiveMountAdjustments(cur.ItemId);
            xLabel.SetText($"Player X: <color=yellow>{cur.RiderXOffset:0.##}</color>");
        });

        // 2. Player Y
        var yLabel = LUI_Core.AddText(13, TextAnchor.MiddleLeft, Position.topLeft, Position.topLeft,
            new Vector2(15f, -122f), new Vector2(300f, 18f), win, $"Player Y: <color=yellow>{cur.RiderYOffset:0.##}</color>");

        var ySlider = LUI_Core.instance.AddSlider("", -3f, 3f, Position.topLeft, Position.topLeft,
            new Vector2(15f, -142f), new Vector2(310f, 16f), win);
        ySlider.SetValue(cur.RiderYOffset);
        ySlider.slider.onValueChanged.AddListener(v =>
        {
            cur.RiderYOffset = (float)Math.Round(v, 2);
            if (cur.CfgPlayerOffsetY != null) cur.CfgPlayerOffsetY.Value = cur.RiderYOffset;
            ApplyLiveMountAdjustments(cur.ItemId);
            yLabel.SetText($"Player Y: <color=yellow>{cur.RiderYOffset:0.##}</color>");
        });

        // 3. Player Z
        var zLabel = LUI_Core.AddText(13, TextAnchor.MiddleLeft, Position.topLeft, Position.topLeft,
            new Vector2(15f, -168f), new Vector2(300f, 18f), win, $"Player Z: <color=yellow>{cur.RiderZOffset:0.##}</color>");

        var zSlider = LUI_Core.instance.AddSlider("", -3f, 3f, Position.topLeft, Position.topLeft,
            new Vector2(15f, -188f), new Vector2(310f, 16f), win);
        zSlider.SetValue(cur.RiderZOffset);
        zSlider.slider.onValueChanged.AddListener(v =>
        {
            cur.RiderZOffset = (float)Math.Round(v, 2);
            if (cur.CfgPlayerOffsetZ != null) cur.CfgPlayerOffsetZ.Value = cur.RiderZOffset;
            ApplyLiveMountAdjustments(cur.ItemId);
            zLabel.SetText($"Player Z: <color=yellow>{cur.RiderZOffset:0.##}</color>");
        });

        // 4. Mount Scale
        var scaleLabel = LUI_Core.AddText(13, TextAnchor.MiddleLeft, Position.topLeft, Position.topLeft,
            new Vector2(15f, -214f), new Vector2(300f, 18f), win, $"Mount Scale: <color=yellow>{cur.MountScale:0.##}</color>");

        var scaleSlider = LUI_Core.instance.AddSlider("", 0.1f, 3f, Position.topLeft, Position.topLeft,
            new Vector2(15f, -234f), new Vector2(310f, 16f), win);
        scaleSlider.SetValue(cur.MountScale);
        scaleSlider.slider.onValueChanged.AddListener(v =>
        {
            cur.MountScale = (float)Math.Round(v, 2);
            if (cur.CfgMountScale != null) cur.CfgMountScale.Value = cur.MountScale;
            ApplyLiveMountAdjustments(cur.ItemId);
            scaleLabel.SetText($"Mount Scale: <color=yellow>{cur.MountScale:0.##}</color>");
        });

        // Action Buttons
        LUI_Core.instance.AddButton(
            "Spawn Mount Item",
            Position.topLeft,
            Position.topLeft,
            new Vector2(15f, -266f),
            new Vector2(150f, 28f),
            _ => SpawnMountItem(cur.ItemId),
            null,
            win);

        LUI_Core.instance.AddButton(
            "Choose Icon",
            Position.topLeft,
            Position.topLeft,
            new Vector2(175f, -266f),
            new Vector2(150f, 28f),
            _ => OpenIconPicker(cur.ItemId),
            null,
            win);

        LUI_Core.instance.AddButton(
            "Choose Model",
            Position.topLeft,
            Position.topLeft,
            new Vector2(15f, -300f),
            new Vector2(150f, 28f),
            _ => OpenModelPicker(cur.ItemId),
            null,
            win);

        LUI_Core.instance.AddButton(
            "Export Adjustments",
            Position.topLeft,
            Position.topLeft,
            new Vector2(175f, -300f),
            new Vector2(150f, 28f),
            _ => ExportMountAdjustments(),
            null,
            win);

        bool ignoreLvl = IgnoreCombatLevelRequirement != null && IgnoreCombatLevelRequirement.Value;
        string lvlBtnText = ignoreLvl ? "Ignore Combat Level: <color=#55FF55>ON</color>" : "Ignore Combat Level: <color=#AAAAAA>OFF</color>";
        LUI_Core.instance.AddButton(
            lvlBtnText,
            Position.topLeft,
            Position.topLeft,
            new Vector2(15f, -334f),
            new Vector2(310f, 28f),
            _ =>
            {
                if (IgnoreCombatLevelRequirement != null)
                {
                    IgnoreCombatLevelRequirement.Value = !IgnoreCombatLevelRequirement.Value;
                    Instance?.Config.Save();
                    AudioHandler.instance?.Play2DSound(12);
                    OpenDebugWindow();
                }
            },
            null,
            win);

        LUI_Core.instance.AddButton(
            "Close",
            Position.topLeft,
            Position.topLeft,
            new Vector2(15f, -368f),
            new Vector2(310f, 28f),
            _ => CloseDebugWindow(),
            null,
            win);
    }

    public static int GetMountShopIdForNpc(NPC npc)
    {
        if (npc == null)
        {
            return 9501;
        }

        if (npc.npcTypeId == 1583)
        {
            return 9508;
        }

        int closestLocationId = 1;
        if (FastTravel.instance != null && FastTravel.instance.locations != null && FastTravel.instance.locations.Count > 0)
        {
            float bestDist = float.MaxValue;
            Vector3 npcPos = npc.transform.position;
            foreach (var loc in FastTravel.instance.locations)
            {
                if (loc == null) continue;
                float dist = Vector3.Distance(npcPos, loc.position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    closestLocationId = loc.id;
                }
            }
        }

        return closestLocationId switch
        {
            1 => 9501,
            2 => 9501,
            3 => 9502,
            4 => 9503,
            5 => 9504,
            6 => 9505,
            7 => 9506,
            8 => 9507,
            _ => 9508
        };
    }

    // =========================================================================
    // 1. Item & Ability Registration + Icons
    // =========================================================================

    [HarmonyPatch(typeof(ItemHandler), nameof(ItemHandler.ManualStart))]
    private static class Patch_ItemHandler_ManualStart
    {
        private static void Postfix(ItemHandler __instance)
        {
            if (__instance.items == null)
            {
                return;
            }

            foreach (var kvp in MountDefinitions)
            {
                var def = kvp.Value;
                int pct = Mathf.RoundToInt(def.SpeedBoost * 100f);
                string speedStr = pct >= 0 ? $"+{pct}%" : $"{pct}%";
                string desc = $"{def.FlavorText}\nRequires Combat Level {def.CombatLevel}.\nMovement Speed: {speedStr} while mounted.";

                __instance.items.RemoveAll(x => x != null && x.typeId == def.ItemId);

                var item = new ItemHandler.Item
                {
                    typeId = def.ItemId,
                    itemName = def.Name,
                    description = desc,
                    itemType = MountSlotType,
                    itemCategory = new List<ItemHandler.ItemCategory>(),
                    skill = SkillsHandler.SkillType.none,
                    level = def.CombatLevel,
                    price = def.Price,
                    copyModel = def.IconItemId,
                    stats = new List<StatsHandler.Stat>()
                };
                ItemToolRef(item) = HarvestablesHandler.Tool.none;

                __instance.items.Add(item);
            }
        }
    }

    [HarmonyPatch(typeof(ItemHandler.Item), nameof(ItemHandler.Item.IsEquippable))]
    private static class Patch_Item_IsEquippable
    {
        private static void Postfix(ItemHandler.Item __instance, ref bool __result)
        {
            if (__instance.itemType == MountSlotType)
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(AbilityHandler), nameof(AbilityHandler.ManualStart))]
    private static class Patch_AbilityHandler_ManualStart
    {
        private static void Postfix(AbilityHandler __instance)
        {
            if (__instance.abilities == null)
            {
                return;
            }

            __instance.abilities.RemoveAll(x => x != null && x.abilityId == SummonMountAbilityId);

            var summonAbility = new AbilityHandler.Ability
            {
                abilityId = SummonMountAbilityId,
                name = "Summon Mount",
                description = "Summon or dismiss your equipped mount.",
                spellBookType = AbilityHandler.SpellBookType.abilities,
                instantCast = true,
                singleCast = true,
                dontShowSelectedAbility = true,
                canCastWhileMoving = true,
                requiresTarget = false,
                cooldown = 0.5f,
                levelRequired = 0
            };

            __instance.abilities.Add(summonAbility);
        }
    }

    private static void RedirectCustomTextureId(ref object tex)
    {
        if (tex is string path)
        {
            if (path == $"Abilities/{SummonMountAbilityId}" ||
                path == $"EquipmentSlotSilhouettes/{MountSlotType}" ||
                path == $"EquipmentSlotSilhouettes/{(int)MountSlotType}" ||
                path == "ItemIcons/9505")
            {
                tex = HorseIconPath;
            }
        }
    }

    [HarmonyPatch(typeof(LUI_TextureLoader), nameof(LUI_TextureLoader.LoadSprite))]
    private static class Patch_LUI_TextureLoader_LoadSprite
    {
        private static void Prefix(ref object tex)
        {
            RedirectCustomTextureId(ref tex);
        }
    }

    [HarmonyPatch(typeof(LUI_TextureLoader), nameof(LUI_TextureLoader.LoadTexture), new[] { typeof(Image), typeof(object), typeof(Vector4), typeof(bool) })]
    private static class Patch_LUI_TextureLoader_LoadTexture
    {
        private static void Prefix(ref object tex)
        {
            RedirectCustomTextureId(ref tex);
        }
    }

    [HarmonyPatch(typeof(UI_Inventory), nameof(UI_Inventory.GetItemIcon))]
    private static class Patch_UI_Inventory_GetItemIcon
    {
        private static void Postfix(int itemId, ref string __result)
        {
            if (TryGetMountDefinition(itemId, out var def))
            {
                if (def.IconItemId == SummonMountAbilityId)
                {
                    __result = $"Abilities/{SummonMountAbilityId}";
                }
                else if (def.IconItemId > 0)
                {
                    __result = $"ItemIcons/{def.IconItemId}";
                }
                else
                {
                    __result = HorseIconPath;
                }
            }
        }
    }

    // =========================================================================
    // 2. Learning "Summon Mount" When Receiving First Mount
    // =========================================================================

    [HarmonyPatch(typeof(UI_Inventory), "AddItem", new[] { typeof(int), typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(Transform) })]
    private static class Patch_UI_Inventory_AddItem
    {
        private static void Postfix(int itemId)
        {
            if (IsMountItem(itemId))
            {
                EnsureSummonMountAbilityLearned(showNotification: true);
            }
        }
    }

    [HarmonyPatch(typeof(UI_Inventory), nameof(UI_Inventory.AddItemToMainInventoryOnly), new[] { typeof(int), typeof(int), typeof(bool) })]
    private static class Patch_UI_Inventory_AddItemToMainInventoryOnly
    {
        private static void Postfix(int itemId)
        {
            if (IsMountItem(itemId))
            {
                EnsureSummonMountAbilityLearned(showNotification: true);
            }
        }
    }

    [HarmonyPatch(typeof(SaveHandler), nameof(SaveHandler.LoadGame))]
    private static class Patch_SaveHandler_LoadGame
    {
        private static void Postfix()
        {
            if (IsMounted)
            {
                Dismount();
            }

            if (GetEquippedMountItemId() != 0)
            {
                EnsureSummonMountAbilityLearned(showNotification: false);
                return;
            }

            if (UI_Inventory.instance != null && UI_Inventory.instance.itemSlots != null)
            {
                foreach (var slot in UI_Inventory.instance.itemSlots)
                {
                    if (slot != null && IsMountItem(slot.itemId))
                    {
                        EnsureSummonMountAbilityLearned(showNotification: false);
                        return;
                    }
                }
            }
        }
    }

    // =========================================================================
    // 3. Summon Mount Ability Execution
    // =========================================================================

    [HarmonyPatch(typeof(UnitAttacks), nameof(UnitAttacks.CanSelectAbility))]
    private static class Patch_UnitAttacks_CanSelectAbility
    {
        private static bool Prefix(int abilityId, ref bool __result)
        {
            if (abilityId == SummonMountAbilityId)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(UnitAttacks), nameof(UnitAttacks.CanUseAbility))]
    private static class Patch_UnitAttacks_CanUseAbility
    {
        private static bool Prefix(int abilityId, ref bool __result)
        {
            if (abilityId == SummonMountAbilityId)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(UnitAttacks), nameof(UnitAttacks.SelectAbility), new[] { typeof(int), typeof(int) })]
    private static class Patch_UnitAttacks_SelectAbility
    {
        private static bool Prefix(UnitAttacks __instance, int abilityId)
        {
            if (abilityId == SummonMountAbilityId && __instance.unit != null && __instance.unit.unitPlayer != null)
            {
                bool onCooldown = false;
                if (__instance.abilityCooldowns != null)
                {
                    foreach (var cd in __instance.abilityCooldowns)
                    {
                        if (cd != null && cd.abilityId == SummonMountAbilityId && cd.cooldown > 0f)
                        {
                            onCooldown = true;
                            break;
                        }
                    }
                }

                if (!onCooldown)
                {
                    __instance.SetAbilityCooldown(SummonMountAbilityId, 0.5f);
                    ToggleMount();
                }
                return false;
            }
            return true;
        }
    }

    // =========================================================================
    // 4. Mount Equipment Slot & Combat Level Requirement
    // =========================================================================

    private static void EnsureMountSlotExists(List<PlayerEquipment.EquipmentSlot> slots)
    {
        if (slots == null)
        {
            return;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].slotType == MountSlotType)
            {
                return;
            }
        }

        slots.Add(new PlayerEquipment.EquipmentSlot
        {
            slotType = MountSlotType,
            itemId = 0
        });
    }

    [HarmonyPatch(typeof(PlayerEquipment), "Start")]
    private static class Patch_PlayerEquipment_Start
    {
        private static void Postfix(PlayerEquipment __instance)
        {
            EnsureMountSlotExists(__instance.equipmentSlots);
        }
    }

    [HarmonyPatch(typeof(PlayerEquipment), nameof(PlayerEquipment.LoadEquipment))]
    private static class Patch_PlayerEquipment_LoadEquipment
    {
        private static void Prefix(PlayerEquipment __instance, List<PlayerEquipment.EquipmentSlot> equipment)
        {
            EnsureMountSlotExists(__instance.equipmentSlots);
            EnsureMountSlotExists(equipment);
        }

        private static void Postfix(PlayerEquipment __instance)
        {
            EnsureMountSlotExists(__instance.equipmentSlots);
            UI_BottomRightTabs_Equipment.instance?.ChangeEquips();
            SanitizeMovementSpeedStat();
        }
    }

    [HarmonyPatch(typeof(PlayerEquipment), nameof(PlayerEquipment.GetSlot), new[] { typeof(ItemHandler.ItemType) })]
    private static class Patch_PlayerEquipment_GetSlot
    {
        private static void Prefix(PlayerEquipment __instance)
        {
            EnsureMountSlotExists(__instance.equipmentSlots);
        }
    }

    [HarmonyPatch(typeof(PlayerEquipment), nameof(PlayerEquipment.CanEquipItem))]
    private static class Patch_PlayerEquipment_CanEquipItem
    {
        private static bool Prefix(int itemId, ref bool __result)
        {
            var item = ItemHandler.instance?.GetItem(itemId);
            if (item == null || item.itemType != MountSlotType)
            {
                return true;
            }

            int playerCombatLevel = Player.instance != null ? Player.instance.GetCombatLevel() : 1;
            if (playerCombatLevel < item.level && (IgnoreCombatLevelRequirement == null || !IgnoreCombatLevelRequirement.Value))
            {
                UI_Notifications.instance?.ShowDefaultNotification(
                    $"You need a Combat Level of {item.level} to equip this mount.", 0f);
                __result = false;
                return false;
            }

            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerEquipment), nameof(PlayerEquipment.EquipItem), new[] { typeof(int), typeof(bool) })]
    private static class Patch_PlayerEquipment_EquipItem
    {
        private static bool Prefix(PlayerEquipment __instance, int itemId, bool skipInventoryCheck)
        {
            var item = ItemHandler.instance?.GetItem(itemId);
            if (item == null || item.itemType != MountSlotType)
            {
                return true;
            }

            EnsureMountSlotExists(__instance.equipmentSlots);

            if (!skipInventoryCheck && !UI_Inventory.instance.HasItem(itemId))
            {
                return false;
            }

            if (!__instance.CanEquipItem(itemId))
            {
                return false;
            }

            bool wasMounted = IsMounted;
            if (wasMounted)
            {
                Dismount();
            }

            var eqSlot = __instance.GetSlot(MountSlotType);
            if (eqSlot == null)
            {
                return false;
            }

            if (!skipInventoryCheck)
            {
                UI_Inventory.instance.RemoveItem(itemId);
            }

            if (eqSlot.itemId != 0)
            {
                __instance.UnequipSlot(MountSlotType, addItemToInventory: !skipInventoryCheck);
            }

            if (eqSlot.itemId != 0)
            {
                return false;
            }

            eqSlot.itemId = itemId;
            EnsureSummonMountAbilityLearned(showNotification: true);

            UI_BottomRightTabs_Equipment.instance?.ChangeEquips();
            AudioHandler.instance?.Play2DSound(14);

            if (wasMounted)
            {
                MountUp();
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerEquipment), nameof(PlayerEquipment.UnequipSlot), new[] { typeof(ItemHandler.ItemType), typeof(bool) })]
    private static class Patch_PlayerEquipment_UnequipSlot
    {
        private static void Prefix(ItemHandler.ItemType slotType)
        {
            if (slotType == MountSlotType && IsMounted)
            {
                Dismount();
            }
        }
    }

    // =========================================================================
    // 5. Equipment Tab UI (Mount Slot Display & Navigation)
    // =========================================================================

    [HarmonyPatch(typeof(UI_BottomRightTabs_Equipment), nameof(UI_BottomRightTabs_Equipment.Draw))]
    private static class Patch_UI_Equipment_Draw
    {
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(UI_BottomRightTabs_Equipment __instance)
        {
            if (__instance == null || __instance.EQSlots == null)
            {
                return;
            }

            LUIRect containerTab = ContainerTabRef(__instance);
            if (containerTab == null)
            {
                return;
            }

            for (int i = 0; i < __instance.EQSlots.Count; i++)
            {
                if (__instance.EQSlots[i].itemType == MountSlotType)
                {
                    __instance.ChangeEquips();
                    return;
                }
            }

            UI_BottomRightTabs_Equipment.EQSlot shieldSlot = null;
            foreach (var s in __instance.EQSlots)
            {
                if (s.itemType == ItemHandler.ItemType.shield)
                {
                    shieldSlot = s;
                    break;
                }
            }

            Vector2 slotSize = new Vector2(50f, 50f);
            Vector2 iconSize = new Vector2(44f, 44f);
            Vector2 slotPos = new Vector2(
                MountSlotPosX != null ? MountSlotPosX.Value : -155f,
                MountSlotPosY != null ? MountSlotPosY.Value : -50f);

            // Add backing frame attached to the left edge of the equipment window
            LUI_Core.instance.AddImage(
                "DefaultUI/DefaultWindowBackground",
                Position.topMiddle,
                Position.topMiddle,
                slotPos,
                new Vector2(58f, 58f),
                containerTab.GetParentTransform(),
                raycast: false);

            var mountEqSlot = new UI_BottomRightTabs_Equipment.EQSlot
            {
                itemType = MountSlotType
            };

            mountEqSlot.background = LUI_Core.instance.AddButton(
                "EquipmentTab/SlotBg",
                "",
                12,
                Position.topMiddle,
                Position.topMiddle,
                slotPos,
                slotSize,
                vars => UnequipSlotMethod?.Invoke(__instance, vars),
                new object[] { mountEqSlot.itemType },
                containerTab.GetParentTransform());

            mountEqSlot.itemIcon = LUI_Core.instance.AddImage(
                null,
                Position.center,
                Position.center,
                Vector2.zero,
                iconSize,
                mountEqSlot.background.GetParentTransform(),
                raycast: false);

            __instance.EQSlots.Add(mountEqSlot);
            __instance.ChangeEquips();
            SetupEquipmentNavigationMethod?.Invoke(__instance);
        }
    }

    [HarmonyPatch(typeof(UI_BottomRightTabs_Equipment), "WireEquipmentNavigation")]
    private static class Patch_UI_Equipment_WireEquipmentNavigation
    {
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(UI_BottomRightTabs_Equipment __instance)
        {
            if (__instance == null || GetEquipmentSlotMethod == null)
            {
                return;
            }

            var mountSlot = GetEquipmentSlotMethod(__instance, MountSlotType);
            var headSlot = GetEquipmentSlotMethod(__instance, ItemHandler.ItemType.head);
            var weaponSlot = GetEquipmentSlotMethod(__instance, ItemHandler.ItemType.weapon);

            if (mountSlot?.navItem != null)
            {
                if (headSlot?.navItem != null)
                {
                    mountSlot.navItem.right = headSlot.navItem;
                    headSlot.navItem.left = mountSlot.navItem;
                }
                if (weaponSlot?.navItem != null)
                {
                    mountSlot.navItem.down = weaponSlot.navItem;
                    weaponSlot.navItem.up = mountSlot.navItem;
                }
            }
        }
    }

    [HarmonyPatch(typeof(UI_BottomRightTabs_Equipment), "UpdateSlotIcons")]
    private static class Patch_UI_Equipment_UpdateSlotIcons
    {
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(UI_BottomRightTabs_Equipment __instance)
        {
            if (__instance == null || __instance.EQSlots == null || Player.instance?.equipment == null)
            {
                return;
            }

            foreach (var uiSlot in __instance.EQSlots)
            {
                if (uiSlot == null || uiSlot.itemType != MountSlotType || uiSlot.itemIcon == null)
                {
                    continue;
                }

                var eqSlot = Player.instance.equipment.GetSlot(MountSlotType);
                if (eqSlot == null || eqSlot.itemId == 0)
                {
                    uiSlot.itemIcon.SetImage(HorseIconPath);
                    uiSlot.itemIcon.SetImageColor(new Color(0f, 0f, 0f, 0.45f));
                    uiSlot.itemIcon.AddRightClickDown(null, null);
                    uiSlot.itemIcon.AddMouseHover(
                        _ => UI_Cursor.instance?.SetUIMouseHoverText("Mount"),
                        null,
                        _ => UI_Cursor.instance?.SetUIMouseHoverText(""),
                        null);
                }
                else
                {
                    uiSlot.itemIcon.SetImageColor(Color.white);
                    int capturedItemId = eqSlot.itemId;
                    uiSlot.itemIcon.AddRightClickDown(
                        _ =>
                        {
                            UI_RightClickMenu.instance?.Activate(new object[]
                            {
                                new UI_RightClickMenu.EquipmentSlotContext
                                {
                                    slotType = MountSlotType,
                                    itemId = capturedItemId
                                }
                            });
                        },
                        null);
                }
            }
        }
    }

    [HarmonyPatch(typeof(UI_RightClickMenu), "EquipmentSlotOptions")]
    private static class Patch_UI_RightClickMenu_EquipmentSlotOptions
    {
        private static void Postfix(UI_RightClickMenu __instance, UI_RightClickMenu.EquipmentSlotContext equipmentSlot, ref int counter)
        {
            if (equipmentSlot == null || equipmentSlot.slotType != MountSlotType || equipmentSlot.itemId == 0)
            {
                return;
            }

            string toggleLabel = IsMounted ? "Dismount" : "Summon Mount";
            __instance.AddButton(
                toggleLabel,
                new Vector2(5f, -5 - counter * 25),
                _ =>
                {
                    ToggleMount();
                    __instance.Deactivate(null);
                },
                null);
            counter++;
        }
    }

    // =========================================================================
    // 6. Movement Speed Boost & Riding Animations
    // =========================================================================

    [HarmonyPatch(typeof(UnitPathFinder), "SetSpeed")]
    private static class Patch_UnitPathFinder_SetSpeed
    {
        private static void Prefix(UnitPathFinder __instance, ref float speed)
        {
            if (IsMounted && ActiveMount != null && Player.instance != null &&
                (__instance == Player.instance.pfinder || (__instance.unit != null && __instance.unit.unitPlayer != null)))
            {
                speed = Mathf.Max(0.5f, speed * (1f + ActiveMount.SpeedBoost));
            }
        }
    }

    [HarmonyPatch(typeof(UnitStats), nameof(UnitStats.GetStatValueString))]
    private static class Patch_UnitStats_GetStatValueString
    {
        private static void Postfix(UnitStats __instance, StatsHandler.StatType statType, ref string __result)
        {
            if (IsMounted && ActiveMount != null && statType == StatsHandler.StatType.movementSpeed &&
                Player.instance != null && __instance == Player.instance.stats)
            {
                if (float.TryParse(__result, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val) ||
                    float.TryParse(__result, out val))
                {
                    float boosted = Mathf.Max(0.5f, val * (1f + ActiveMount.SpeedBoost));
                    __result = $"{boosted:0.#}";
                }
            }
        }
    }

    [HarmonyPatch(typeof(UI_Stats), nameof(UI_Stats.UpdateStat))]
    private static class Patch_UI_Stats_UpdateStat
    {
        private static void Prefix(StatsHandler.StatType statType, ref float value)
        {
            if (IsMounted && ActiveMount != null && statType == StatsHandler.StatType.movementSpeed)
            {
                value = Mathf.Max(0.5f, value * (1f + ActiveMount.SpeedBoost));
            }
        }
    }

    [HarmonyPatch(typeof(PlayerAnimations), nameof(PlayerAnimations.GetIdleAnimation))]
    private static class Patch_PlayerAnimations_GetIdleAnimation
    {
        private static bool Prefix(ref int __result)
        {
            if (IsMounted)
            {
                __result = RidingAnimationId;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(UnitAnimations), nameof(UnitAnimations.GetWalkAnimation))]
    private static class Patch_UnitAnimations_GetWalkAnimation
    {
        private static bool Prefix(UnitAnimations __instance, ref int __result)
        {
            if (IsMounted && __instance.unit != null && __instance.unit.unitPlayer != null)
            {
                __result = RidingAnimationId;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerAnimations), nameof(PlayerAnimations.UpdateAnimations))]
    private static class Patch_PlayerAnimations_UpdateAnimations
    {
        private static void Postfix(PlayerAnimations __instance)
        {
            if (IsMounted)
            {
                __instance.anim_idle = RidingAnimationId;
                __instance.anim_walk = RidingAnimationId;
                __instance.anim_run = RidingAnimationId;
            }
        }
    }

    [HarmonyPatch(typeof(UnitAnimations), nameof(UnitAnimations.PlayAnimation), new[] { typeof(int), typeof(float), typeof(bool) })]
    private static class Patch_UnitAnimations_PlayAnimation
    {
        private static void Prefix(UnitAnimations __instance, ref int id, ref float spd)
        {
            if (IsMounted && __instance.unit != null && __instance.unit.unitPlayer != null && !__instance.unit.dead)
            {
                id = RidingAnimationId;
                spd = 1f;
            }
        }
    }

    // =========================================================================
    // 7. Dismount Triggers (Damage > 0, Attacking, Interacting, Teleport, Death)
    // =========================================================================

    [HarmonyPatch(typeof(Unit), nameof(Unit.TakeDamage))]
    private static class Patch_Unit_TakeDamage
    {
        private static void Prefix(Unit __instance, ref float __state)
        {
            __state = __instance != null ? __instance.currentHealth : 0f;
        }

        private static void Postfix(Unit __instance, DamageHandler.Damage damage, float __state)
        {
            if (!IsMounted || __instance == null || __instance.unitPlayer == null || damage == null)
            {
                return;
            }

            if (damage.damageFlags != null && damage.damageFlags.heal)
            {
                return;
            }

            // Dismount only when positive damage (> 0) was actually dealt (ignores misses and 0 damage)
            float healthLost = __state - __instance.currentHealth;
            if (healthLost > 0f || (!__instance.immuneToDamage && damage.modifiedDamage > 0f))
            {
                Dismount("You were dismounted by the attack!");
            }
        }
    }

    [HarmonyPatch(typeof(UnitAttacks), nameof(UnitAttacks.StartAttack), new[] { typeof(int) })]
    private static class Patch_UnitAttacks_StartAttack
    {
        private static void Prefix(UnitAttacks __instance)
        {
            if (IsMounted && __instance.unit != null && __instance.unit.unitPlayer != null)
            {
                Dismount();
            }
        }
    }

    [HarmonyPatch(typeof(PlayerInteract), nameof(PlayerInteract.AttemptStartInteract), new[] { typeof(object), typeof(Action), typeof(string) })]
    private static class Patch_PlayerInteract_AttemptStartInteract
    {
        private static void Prefix(object target)
        {
            if (IsMounted && (target is Interactable || (UI_Inventory.instance != null && UI_Inventory.instance.usingItem)))
            {
                Dismount();
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Teleport), new[] { typeof(Vector3) })]
    private static class Patch_Player_Teleport
    {
        private static void Prefix()
        {
            if (IsMounted)
            {
                Dismount();
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Death), new[] { typeof(DamageHandler.Damage) })]
    private static class Patch_Player_Death
    {
        private static void Prefix()
        {
            if (IsMounted)
            {
                Dismount();
            }
        }
    }

    [HarmonyPatch(typeof(FastTravelHorse), nameof(FastTravelHorse.Activate), new[] { typeof(Vector3) })]
    private static class Patch_FastTravelHorse_Activate
    {
        private static void Prefix()
        {
            if (IsMounted)
            {
                Dismount();
            }
        }
    }

    // =========================================================================
    // 8. Stablemaster Mount Shops (ShopHandler, Right-Click & Dialogue)
    // =========================================================================

    [HarmonyPatch(typeof(ShopHandler), nameof(ShopHandler.ManualStart))]
    private static class Patch_ShopHandler_ManualStart
    {
        private static void Postfix(ShopHandler __instance)
        {
            if (__instance.shops == null)
            {
                return;
            }

            foreach (var kvp in RegionalMountShops)
            {
                int shopId = kvp.Key;
                int[] mountIds = kvp.Value;

                __instance.shops.RemoveAll(s => s != null && s.id == shopId);
                __instance.shopStocks?.RemoveAll(st => st != null && st.shopId == shopId);

                var shopItems = new List<ShopHandler.ShopItem>();
                var stockItems = new List<ShopHandler.ShopStockItem>();
                foreach (int mountId in mountIds)
                {
                    int price = MountDefinitions.TryGetValue(mountId, out var def) ? def.Price : 10000;
                    shopItems.Add(new ShopHandler.ShopItem
                    {
                        itemTypeId = mountId,
                        price = price,
                        buyprice = price,
                        noSell = true
                    });
                    stockItems.Add(new ShopHandler.ShopStockItem
                    {
                        itemId = mountId,
                        currentAmount = 1,
                        maxAmount = 1,
                        restockTime = 5
                    });
                }

                var shop = new ShopHandler.Shop
                {
                    id = shopId,
                    name = "Stablemaster's Mounts",
                    currencyTypeId = 131,
                    priceModifier = 1f,
                    noSell = true,
                    items = shopItems
                };

                __instance.shops.Add(shop);
                __instance.shopStocks?.Add(new ShopHandler.ShopStock
                {
                    shopId = shopId,
                    shopStockItems = stockItems
                });
            }
        }
    }

    [HarmonyPatch(typeof(UI_RightClickMenu), "NpcExtraOptions")]
    private static class Patch_UI_RightClickMenu_NpcExtraOptions
    {
        private static void Postfix(UI_RightClickMenu __instance, NPC npc, ref int counter)
        {
            if (npc == null || (npc.npcTypeId != 197 && npc.npcTypeId != 1583))
            {
                return;
            }

            int shopId = GetMountShopIdForNpc(npc);
            __instance.AddButton(
                "Buy Mounts - " + npc.npcName,
                new Vector2(5f, -5 - counter * 25),
                _ =>
                {
                    Player.instance.commands.AddCommand(new PlayerCommands.Command_Move
                    {
                        overrideQueue = true,
                        customDistance = 2f,
                        inputPosition = Utils.GetPositionFromObject(npc)
                    });
                    Player.instance.commands.AddCommand(new PlayerCommands.Command_Interact
                    {
                        target = npc,
                        methodToCall = () => ShopHandler.instance?.OpenShop(shopId)
                    });
                    __instance.Deactivate(null);
                },
                null);
            counter++;
        }
    }

    [HarmonyPatch(typeof(DialogueHandler), nameof(DialogueHandler.StartDialogue), new[] { typeof(string), typeof(int), typeof(object) })]
    private static class Patch_DialogueHandler_StartDialogue
    {
        private static void Postfix(DialogueHandler __instance, string dialogueId, int nodeId)
        {
            if (__instance.currentNode == null || __instance.optionsList == null)
            {
                return;
            }

            // Node 1850 = Stablehand greeting, Node 6640 = Cartmaster Wargus greeting
            if (dialogueId == "" && (nodeId == 1850 || nodeId == 6640))
            {
                foreach (var opt in __instance.optionsList)
                {
                    if (opt != null && opt.optionId == MountDialogueOptionId)
                    {
                        return;
                    }
                }

                var targetNodeId = new DialogueHandler.NodeId();
                targetNodeId.dialogueId = "";
                targetNodeId.nodeId = 0;

                var buyMountOption = new DialogueHandler.Option
                {
                    optionId = MountDialogueOptionId,
                    optionText = "I'd like to buy a mount.",
                    nextNode = targetNodeId,
                    xmlChecks = new List<CBActions.CBCheck>()
                };

                int insertIdx = Mathf.Max(0, __instance.optionsList.Count - 1);
                __instance.optionsList.Insert(insertIdx, buyMountOption);
                string npcName = NodeNpcNameRef(__instance.currentNode) ?? (__instance.currentNpc != null ? __instance.currentNpc.npcName : "Stablemaster");
                UI_Dialogue.instance?.StartDialogue(__instance.currentNode, __instance.optionsList, npcName);
            }
        }
    }

    [HarmonyPatch(typeof(DialogueHandler), nameof(DialogueHandler.PlayerPickOption), new[] { typeof(int) })]
    private static class Patch_DialogueHandler_PlayerPickOption
    {
        private static bool Prefix(DialogueHandler __instance, int optionId)
        {
            if (optionId == MountDialogueOptionId &&
                __instance.currentNode != null &&
                (__instance.currentNode.nodeId == 1850 || __instance.currentNode.nodeId == 6640))
            {
                NPC npc = __instance.currentNpc;
                int shopId = GetMountShopIdForNpc(npc);
                __instance.EndDialogue();
                ShopHandler.instance?.OpenShop(shopId);
                return false;
            }
            return true;
        }
    }
}
