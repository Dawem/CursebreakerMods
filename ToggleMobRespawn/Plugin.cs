using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LUI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ToggleMobRespawn;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    internal static ConfigEntry<bool> AutoRespawnEnabled;
    internal static ConfigEntry<bool> RespawnAllBosses;
    internal static ConfigEntry<bool> AlwaysShowRespawnButton;
    internal static ConfigEntry<string> KnownBossTypeIdsConfig;
    internal static ConfigEntry<KeyboardShortcut> ToggleKey;

    private readonly Harmony _harmony = new(MyPluginInfo.PLUGIN_GUID);

    private static readonly Func<KeyboardInputs, bool> TextInputActiveForShortcutsDelegate =
        AccessTools.MethodDelegate<Func<KeyboardInputs, bool>>(
            AccessTools.Method(typeof(KeyboardInputs), "TextInputActiveForShortcuts"));

    private void Awake()
    {
        Logger = base.Logger;

        AutoRespawnEnabled = Config.Bind(
            "General",
            "AutoRespawnEnabled",
            false,
            "When enabled, nearby dead mobs automatically respawn as soon as the player is out of combat.");

        RespawnAllBosses = Config.Bind(
            "General",
            "Respawn Bosses",
            true,
            "When enabled, all boss enemies are included when respawning nearby enemies (both automatically and via the respawn interface). Individual bosses can also be toggled in the respawn interface.");

        AlwaysShowRespawnButton = Config.Bind(
            "General",
            "Always Show Respawn Button",
            false,
            "When enabled, the respawn interface button remains visible on the HUD even when auto-respawn is off and no nearby mobs are respawning.");

        KnownBossTypeIdsConfig = Config.Bind(
            "General",
            "Known Boss Type IDs",
            "118,191,238,482,489,598,624,748,844,908,1538,9074,9200,9203",
            "Comma-separated list of NPC type IDs treated as boss enemies in addition to the game's boss flag and boss health bar.");

        ToggleKey = Config.Bind(
            "Keybinds",
            "ToggleKey",
            new KeyboardShortcut(KeyCode.G),
            "Key(s) to press to toggle automatic mob respawn on or off.");

        AutoRespawnEnabled.SettingChanged += (_, _) => OnSettingsChanged();
        RespawnAllBosses.SettingChanged += (_, _) => OnSettingsChanged();
        AlwaysShowRespawnButton.SettingChanged += (_, _) => OnSettingsChanged();
        KnownBossTypeIdsConfig.SettingChanged += (_, _) =>
        {
            RespawnManager.LoadKnownBossTypeIds();
            OnSettingsChanged();
        };

        RespawnManager.LoadKnownBossTypeIds();
        RespawnManager.LoadSavedSelections();

        _harmony.PatchAll();
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_NAME} ({MyPluginInfo.PLUGIN_GUID}) is loaded!");
    }

    private void Update()
    {
        if (!IsGameplayActive())
        {
            return;
        }

        if (CanHandleToggleHotkey())
        {
            KeyboardShortcut shortcut = ToggleKey?.Value ?? KeyboardShortcut.Empty;
            if (IsShortcutDown(in shortcut))
            {
                ToggleAutoRespawn(playFeedback: true);
            }
        }

        if (AutoRespawnEnabled.Value)
        {
            RespawnManager.TryAutoRespawn();
        }
    }

    internal static void ToggleAutoRespawn(bool playFeedback = true)
    {
        SetAutoRespawn(!AutoRespawnEnabled.Value, playFeedback);
    }

    internal static void SetAutoRespawn(bool enabled, bool playFeedback = true)
    {
        AutoRespawnEnabled.Value = enabled;

        if (playFeedback)
        {
            string message = enabled
                ? "<color=#55FF55>Auto Mob Respawn: ON</color>"
                : "<color=#FF5555>Auto Mob Respawn: OFF</color>";

            UI_Notifications.instance?.ShowNotification(message);
            AudioHandler.instance?.Play2DSound(enabled ? 12 : 13);
        }
    }

    private static void OnSettingsChanged()
    {
        if (AutoRespawnEnabled.Value)
        {
            RespawnManager.TryAutoRespawn();
        }

        RespawnUIHelpers.RefreshUI();
    }

    private static bool IsGameplayActive()
    {
        return SaveHandler.instance != null &&
               SaveHandler.instance.loaded &&
               Player.instance != null;
    }

    private static bool CanHandleToggleHotkey()
    {
        if (UI_DevConsole.instance != null && UI_DevConsole.instance.active)
        {
            return false;
        }

        if (UI_CharacterCreation.instance != null && UI_CharacterCreation.instance.active)
        {
            return false;
        }

        if (UI_Options.instance != null && UI_Options.instance.active)
        {
            return false;
        }

        if (CinematicMaster3000.instance != null && CinematicMaster3000.instance.CinematicActive())
        {
            return false;
        }

        if (Assets._GameAssets.Scripts.UI.UI_WorldMap.instance != null &&
            Assets._GameAssets.Scripts.UI.UI_WorldMap.instance.InputActive())
        {
            return false;
        }

        if (KeyboardInputs.instance != null &&
            (KeyboardInputs.instance.changingInputs || TextInputActiveForShortcutsDelegate(KeyboardInputs.instance)))
        {
            return false;
        }

        GameObject selected = EventSystem.current?.currentSelectedGameObject;
        if (selected != null &&
            (selected.GetComponent<InputField>() != null || selected.GetComponent<TMP_InputField>() != null))
        {
            return false;
        }

        return true;
    }

    private static bool IsShortcutDown(in KeyboardShortcut shortcut)
    {
        KeyboardShortcut copy = shortcut;
        if (copy.MainKey == KeyCode.None || !Input.GetKeyDown(copy.MainKey))
        {
            return false;
        }

        foreach (KeyCode modifier in copy.Modifiers)
        {
            if (!IsModifierHeld(modifier))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsModifierHeld(KeyCode key)
    {
        return key switch
        {
            KeyCode.LeftControl or KeyCode.RightControl =>
                Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
            KeyCode.LeftShift or KeyCode.RightShift =>
                Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
            KeyCode.LeftAlt or KeyCode.RightAlt =>
                Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt),
            _ => Input.GetKey(key)
        };
    }

    internal static string FormatShortcut(in KeyboardShortcut shortcut)
    {
        KeyboardShortcut copy = shortcut;
        return copy.MainKey == KeyCode.None ? "None" : copy.ToString();
    }
}

internal static class RespawnManager
{
    internal const float RespawnRange = 15f;
    private const string SelectedBossesPrefsKey = "ToggleMobRespawn_SelectedBosses";
    private const string DisabledMobsPrefsKey = "ToggleMobRespawn_DisabledMobs";

    // Dedicated boss encounter NPCs configured via KnownBossTypeIdsConfig, supplementing NPCTemplate.bossLoot
    private static readonly HashSet<int> KnownBossTypeIds = new();

    internal static void LoadKnownBossTypeIds()
    {
        KnownBossTypeIds.Clear();
        string raw = Plugin.KnownBossTypeIdsConfig?.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        string[] parts = raw.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            if (int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int typeId))
            {
                KnownBossTypeIds.Add(typeId);
            }
        }
    }

    internal sealed class SavedMobEntry
    {
        public string Key;
        public int NpcTypeId;
        public Vector3 Pos;
        public string Name;
    }

    internal sealed class NearbyMobEntry
    {
        public string Key;
        public int NpcTypeId;
        public Vector3 Pos;
        public string Name;
        public bool IsBoss;
        public float Distance;
        public NPCHandler.RespawningNpc RespawningNpc;
    }

    private static readonly Dictionary<string, SavedMobEntry> EnabledBosses = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, SavedMobEntry> DisabledMobs = new(StringComparer.Ordinal);

    internal static void LoadSavedSelections()
    {
        LoadEntryDictionary(SelectedBossesPrefsKey, EnabledBosses);
        LoadEntryDictionary(DisabledMobsPrefsKey, DisabledMobs);
    }

    private static void LoadEntryDictionary(string prefsKey, Dictionary<string, SavedMobEntry> target)
    {
        target.Clear();
        string raw = PlayerPrefs.GetString(prefsKey, string.Empty);
        if (string.IsNullOrEmpty(raw))
        {
            return;
        }

        string[] entries = raw.Split(';');
        for (int i = 0; i < entries.Length; i++)
        {
            string entry = entries[i];
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            int pipeIdx = entry.IndexOf('|');
            string key = pipeIdx >= 0 ? entry.Substring(0, pipeIdx) : entry;
            string name = pipeIdx >= 0 ? entry.Substring(pipeIdx + 1) : string.Empty;

            if (TryParseMobKey(key, out int npcTypeId, out Vector3 pos))
            {
                target[key] = new SavedMobEntry
                {
                    Key = key,
                    NpcTypeId = npcTypeId,
                    Pos = pos,
                    Name = name
                };
            }
        }
    }

    private static void SaveEntryDictionary(string prefsKey, Dictionary<string, SavedMobEntry> source)
    {
        if (source.Count == 0)
        {
            PlayerPrefs.DeleteKey(prefsKey);
            PlayerPrefs.Save();
            return;
        }

        var serialized = new List<string>(source.Count);
        foreach (SavedMobEntry entry in source.Values)
        {
            string safeName = (entry.Name ?? string.Empty).Replace(";", string.Empty).Replace("|", string.Empty);
            serialized.Add($"{entry.Key}|{safeName}");
        }

        PlayerPrefs.SetString(prefsKey, string.Join(";", serialized));
        PlayerPrefs.Save();
    }

    internal static string GetMobKey(int npcTypeId, Vector3 pos)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}@{1:F1},{2:F1},{3:F1}",
            npcTypeId,
            pos.x,
            pos.y,
            pos.z);
    }

    private static bool TryParseMobKey(string key, out int npcTypeId, out Vector3 pos)
    {
        npcTypeId = 0;
        pos = Vector3.zero;
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        int atIdx = key.IndexOf('@');
        if (atIdx <= 0)
        {
            return false;
        }

        if (!int.TryParse(key.Substring(0, atIdx), NumberStyles.Integer, CultureInfo.InvariantCulture, out npcTypeId))
        {
            return false;
        }

        string[] coords = key.Substring(atIdx + 1).Split(',');
        if (coords.Length != 3)
        {
            return false;
        }

        if (!float.TryParse(coords[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(coords[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(coords[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            return false;
        }

        pos = new Vector3(x, y, z);
        return true;
    }

    internal static bool IsMobRespawnEnabled(int npcTypeId, Vector3 pos)
    {
        string key = GetMobKey(npcTypeId, pos);
        if (IsBoss(npcTypeId))
        {
            return Plugin.RespawnAllBosses.Value
                ? !DisabledMobs.ContainsKey(key)
                : EnabledBosses.ContainsKey(key);
        }

        return !DisabledMobs.ContainsKey(key);
    }

    internal static void SetMobRespawnEnabled(int npcTypeId, Vector3 pos, string name, bool enabled)
    {
        string key = GetMobKey(npcTypeId, pos);
        var entry = new SavedMobEntry
        {
            Key = key,
            NpcTypeId = npcTypeId,
            Pos = pos,
            Name = name ?? string.Empty
        };

        if (IsBoss(npcTypeId))
        {
            if (enabled)
            {
                DisabledMobs.Remove(key);
                if (!Plugin.RespawnAllBosses.Value)
                {
                    EnabledBosses[key] = entry;
                }
            }
            else
            {
                EnabledBosses.Remove(key);
                if (Plugin.RespawnAllBosses.Value)
                {
                    DisabledMobs[key] = entry;
                }
            }

            SaveEntryDictionary(SelectedBossesPrefsKey, EnabledBosses);
            SaveEntryDictionary(DisabledMobsPrefsKey, DisabledMobs);
            return;
        }

        if (enabled)
        {
            DisabledMobs.Remove(key);
        }
        else
        {
            DisabledMobs[key] = entry;
        }

        SaveEntryDictionary(DisabledMobsPrefsKey, DisabledMobs);
    }

    internal static List<NearbyMobEntry> GetNearbyMobs(out int totalRespawningCount)
    {
        totalRespawningCount = 0;
        var result = new List<NearbyMobEntry>();
        if (NPCHandler.instance == null || Player.instance == null)
        {
            return result;
        }

        var respawningByKey = new Dictionary<string, NPCHandler.RespawningNpc>(StringComparer.Ordinal);
        List<NPCHandler.RespawningNpc> allRespawning = NPCHandler.instance.respawningNpcs;
        if (allRespawning != null)
        {
            for (int i = 0; i < allRespawning.Count; i++)
            {
                NPCHandler.RespawningNpc npc = allRespawning[i];
                if (!IsEligibleForRespawn(npc))
                {
                    continue;
                }

                totalRespawningCount++;
                string key = GetMobKey(npc.npcTypeId, npc.pos);
                if (!respawningByKey.ContainsKey(key))
                {
                    respawningByKey[key] = npc;
                }
            }
        }

        var activeByUniqueId = new Dictionary<int, NPC>();
        List<NPC> activeNpcs = NPCHandler.instance.activeNpcs;
        if (activeNpcs != null)
        {
            for (int i = 0; i < activeNpcs.Count; i++)
            {
                NPC activeNpc = activeNpcs[i];
                if (activeNpc != null && !activeNpc.instantiated)
                {
                    activeByUniqueId[activeNpc.uniqueId] = activeNpc;
                }
            }
        }

        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        List<NPCHandler.NPCSpawner> spawners = NPCHandler.instance.npcSpawners;
        if (spawners != null)
        {
            for (int i = 0; i < spawners.Count; i++)
            {
                NPCHandler.NPCSpawner spawner = spawners[i];
                if (spawner == null)
                {
                    continue;
                }

                string key = GetMobKey(spawner.typeId, spawner.startingPosition);
                if (seenKeys.Contains(key))
                {
                    continue;
                }

                respawningByKey.TryGetValue(key, out NPCHandler.RespawningNpc respawningNpc);
                NPCHandler.NPCTemplate template = NPCHandler.instance.getNpcTemplate(spawner.typeId);
                if (!IsMobTemplate(template, respawningNpc != null))
                {
                    continue;
                }

                activeByUniqueId.TryGetValue(spawner.uniqueId, out NPC activeNpc);
                float startDist = OlipaRuntimeUtils.DistanceToPlayer(spawner.startingPosition);
                float liveDist = activeNpc != null && !activeNpc.dead
                    ? OlipaRuntimeUtils.DistanceToPlayer(activeNpc.transform.position)
                    : float.MaxValue;
                float dist = Mathf.Min(startDist, liveDist);

                if (respawningNpc == null && dist >= RespawnRange)
                {
                    continue;
                }

                if (respawningNpc == null && !IsLiveSpawnerVisible(spawner))
                {
                    continue;
                }

                seenKeys.Add(key);
                string mobName = !string.IsNullOrEmpty(spawner.npcName)
                    ? spawner.npcName
                    : GetNpcDisplayName(spawner.typeId);

                result.Add(new NearbyMobEntry
                {
                    Key = key,
                    NpcTypeId = spawner.typeId,
                    Pos = spawner.startingPosition,
                    Name = mobName,
                    IsBoss = IsBoss(spawner.typeId),
                    Distance = respawningNpc != null ? startDist : dist,
                    RespawningNpc = respawningNpc
                });
            }
        }

        // Fallback for any eligible respawning entry not matched to a spawner above
        foreach (KeyValuePair<string, NPCHandler.RespawningNpc> kvp in respawningByKey)
        {
            if (seenKeys.Contains(kvp.Key))
            {
                continue;
            }

            NPCHandler.RespawningNpc npc = kvp.Value;
            result.Add(new NearbyMobEntry
            {
                Key = kvp.Key,
                NpcTypeId = npc.npcTypeId,
                Pos = npc.pos,
                Name = GetNpcDisplayName(npc.npcTypeId),
                IsBoss = IsBoss(npc.npcTypeId),
                Distance = OlipaRuntimeUtils.DistanceToPlayer(npc.pos),
                RespawningNpc = npc
            });
        }

        result.Sort((a, b) =>
        {
            bool aDead = a.RespawningNpc != null;
            bool bDead = b.RespawningNpc != null;
            if (aDead != bDead)
            {
                return aDead ? -1 : 1;
            }

            if (aDead)
            {
                int timeCmp = a.RespawningNpc.time.CompareTo(b.RespawningNpc.time);
                if (timeCmp != 0)
                {
                    return timeCmp;
                }
            }

            int distCmp = a.Distance.CompareTo(b.Distance);
            return distCmp != 0
                ? distCmp
                : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return result;
    }

    private static bool IsMobTemplate(NPCHandler.NPCTemplate template, bool isCurrentlyRespawning)
    {
        if (template == null)
        {
            return isCurrentlyRespawning;
        }

        if (!template.canFight || template.unitTags.Contains(21))
        {
            return false;
        }

        return template.respawnTime > 0 || isCurrentlyRespawning;
    }

    private static bool IsLiveSpawnerVisible(NPCHandler.NPCSpawner spawner)
    {
        if (spawner == null || NPCHandler.instance == null || CBActions.instance == null)
        {
            return false;
        }

        return NPCHandler.instance.NpcSpawnerIsVisible(spawner) &&
               CBActions.instance.CheckCBChecks(spawner.spawnerChecks, spawner);
    }

    private static string GetNpcDisplayName(int npcTypeId)
    {
        NPCHandler.NPCTemplate template = NPCHandler.instance?.getNpcTemplate(npcTypeId);
        return template != null && !string.IsNullOrEmpty(template.npcName)
            ? template.npcName
            : $"NPC #{npcTypeId}";
    }

    internal static void TryAutoRespawn()
    {
        if (Player.instance == null || Player.instance.dead || Player.instance.IsInCombat())
        {
            return;
        }

        int respawned = RespawnNearby(respawnTimeValue: 1, out _, out _);
        if (respawned > 0)
        {
            RespawnUIHelpers.RefreshUI();
        }
    }

    internal static int RespawnNearby(int respawnTimeValue, out int skippedBossCount, out int skippedDisabledCount)
    {
        skippedBossCount = 0;
        skippedDisabledCount = 0;
        if (NPCHandler.instance == null || NPCHandler.instance.respawningNpcs == null || Player.instance == null)
        {
            return 0;
        }

        int respawnedCount = 0;
        List<NPCHandler.RespawningNpc> list = NPCHandler.instance.respawningNpcs;
        for (int i = 0; i < list.Count; i++)
        {
            NPCHandler.RespawningNpc npc = list[i];
            if (!IsEligibleForRespawn(npc))
            {
                continue;
            }

            if (!IsMobRespawnEnabled(npc.npcTypeId, npc.pos))
            {
                if (IsBoss(npc) && !Plugin.RespawnAllBosses.Value)
                {
                    skippedBossCount++;
                }
                else
                {
                    skippedDisabledCount++;
                }

                continue;
            }

            npc.time = respawnTimeValue;
            respawnedCount++;
        }

        return respawnedCount;
    }

    internal static bool IsBoss(NPCHandler.RespawningNpc npc)
    {
        return npc != null && IsBoss(npc.npcTypeId);
    }

    internal static bool IsBoss(int npcTypeId)
    {
        NPCHandler.NPCTemplate template = NPCHandler.instance?.getNpcTemplate(npcTypeId);
        if (template != null && template.bossLoot)
        {
            return true;
        }

        if (KnownBossTypeIds.Contains(npcTypeId))
        {
            return true;
        }

        if (UI_BossHealthBar.instance != null && UI_BossHealthBar.instance.bossTypeId == npcTypeId)
        {
            return true;
        }

        return false;
    }

    internal static bool IsEligibleForRespawn(NPCHandler.RespawningNpc respawningNpc)
    {
        if (respawningNpc == null || respawningNpc.time <= 2)
        {
            return false;
        }

        if (OlipaRuntimeUtils.DistanceToPlayer(respawningNpc.pos) >= RespawnRange)
        {
            return false;
        }

        return IsSpawnerVisible(respawningNpc);
    }

    internal static bool IsSpawnerVisible(NPCHandler.RespawningNpc respawningNpc)
    {
        if (NPCHandler.instance == null || CBActions.instance == null)
        {
            return false;
        }

        List<NPCHandler.NPCSpawner> spawners = NPCHandler.instance.npcSpawners;
        if (spawners == null)
        {
            return false;
        }

        NPCHandler.NPCSpawner matched = null;
        for (int i = 0; i < spawners.Count; i++)
        {
            NPCHandler.NPCSpawner spawner = spawners[i];
            if (spawner != null && spawner.typeId == respawningNpc.npcTypeId)
            {
                matched = spawner;
                if (spawner.startingPosition == respawningNpc.pos)
                {
                    break;
                }
            }
        }

        if (matched == null)
        {
            return false;
        }

        NPCHandler.NPCTemplate template = NPCHandler.instance.getNpcTemplate(respawningNpc.npcTypeId);
        if (template != null &&
            template.visibilityChecks != null &&
            template.visibilityChecks.Count > 0 &&
            !CBActions.instance.CheckCBChecks(template.visibilityChecks, template))
        {
            return false;
        }

        return CBActions.instance.CheckCBChecks(matched.visibilityChecks, (object)UI_RespawningNpcs.instance ?? matched);
    }
}

[HarmonyPatch(typeof(UI_RespawningNpcs))]
public static class RespawnUIPatch
{
    [HarmonyPatch(nameof(UI_RespawningNpcs.ManualStart))]
    [HarmonyPostfix]
    public static void ManualStart_Postfix(UI_RespawningNpcs __instance)
    {
        RespawnUIHelpers.OnManualStart(__instance);
    }

    [HarmonyPatch("CheckActive")]
    [HarmonyPrefix]
    public static bool CheckActive_Prefix(UI_RespawningNpcs __instance)
    {
        return RespawnUIHelpers.OnCheckActivePrefix(__instance);
    }

    [HarmonyPatch("Tick")]
    [HarmonyPrefix]
    public static bool Tick_Prefix(UI_RespawningNpcs __instance)
    {
        return RespawnUIHelpers.OnTickPrefix(__instance);
    }

    [HarmonyPatch("Respawn")]
    [HarmonyPrefix]
    public static bool Respawn_Prefix(UI_RespawningNpcs __instance)
    {
        return RespawnUIHelpers.OnRespawn(__instance);
    }
}

internal static class RespawnUIHelpers
{
    private const float RowHeight = 25f;
    private const float ToggleRowHeight = 26f;
    private const float MinPanelWidth = 215f;
    private const int MaxDisplayedEntries = 10;

    private static readonly Color ActiveTintColor = new(1f, 0.85f, 0.25f, 1f);
    private static readonly Color InactiveTintColor = Color.white;

    private static readonly AccessTools.FieldRef<UI_RespawningNpcs, LUI_NavGroup> NavGroupRef =
        AccessTools.FieldRefAccess<UI_RespawningNpcs, LUI_NavGroup>("navGroup");

    private static readonly Action<UI_RespawningNpcs> CheckActiveDelegate =
        AccessTools.MethodDelegate<Action<UI_RespawningNpcs>>(
            AccessTools.Method(typeof(UI_RespawningNpcs), "CheckActive"));

    private static readonly Action<UI_RespawningNpcs, bool> SetActiveDelegate =
        AccessTools.MethodDelegate<Action<UI_RespawningNpcs, bool>>(
            AccessTools.Method(typeof(UI_RespawningNpcs), "SetActive", new[] { typeof(bool) }));

    private static readonly Action<UI_RespawningNpcs> HideDelegate =
        AccessTools.MethodDelegate<Action<UI_RespawningNpcs>>(
            AccessTools.Method(typeof(UI_RespawningNpcs), "Hide"));

    private static readonly Action<UI_RespawningNpcs> TickDelegate =
        AccessTools.MethodDelegate<Action<UI_RespawningNpcs>>(
            AccessTools.Method(typeof(UI_RespawningNpcs), "Tick"));

    private static Image _hudButtonImage;

    internal static void OnManualStart(UI_RespawningNpcs instance)
    {
        if (instance == null || instance.container == null || instance.container.rectTransform == null)
        {
            return;
        }

        LUI_EventTrigger_MouseEnter hover = instance.container.rectTransform.GetComponentInChildren<LUI_EventTrigger_MouseEnter>(true);
        if (hover != null)
        {
            _hudButtonImage = hover.GetComponent<Image>();
            hover.enterAction = _ => UpdateHoverText();
            hover.enterVars = null;
            hover.exitAction = _ => UI_Cursor.instance?.SetUIMouseHoverText("");
            hover.exitVars = null;
        }

        UpdateButtonAppearance();
    }

    internal static bool OnCheckActivePrefix(UI_RespawningNpcs instance)
    {
        if (instance == null || Player.instance == null)
        {
            return false;
        }

        bool alwaysShow = Plugin.AlwaysShowRespawnButton.Value;
        if (Player.instance.dead || (!alwaysShow && Player.instance.IsInCombat()))
        {
            instance.respawningNpcsCount = 0;
            SetActiveDelegate(instance, false);
            UpdateButtonAppearance();
            return false;
        }

        int eligibleCount = 0;
        List<NPCHandler.RespawningNpc> allRespawning = NPCHandler.instance?.respawningNpcs;
        if (allRespawning != null)
        {
            for (int i = 0; i < allRespawning.Count; i++)
            {
                if (RespawnManager.IsEligibleForRespawn(allRespawning[i]))
                {
                    eligibleCount++;
                }
            }
        }

        instance.respawningNpcsCount = eligibleCount;

        bool shouldBeActive = alwaysShow || eligibleCount > 0 || Plugin.AutoRespawnEnabled.Value;
        SetActiveDelegate(instance, shouldBeActive);
        UpdateButtonAppearance();
        return false;
    }

    internal static bool OnTickPrefix(UI_RespawningNpcs instance)
    {
        if (instance == null || !instance.showing)
        {
            return true;
        }

        RenderPanel(instance);
        return false;
    }

    internal static bool OnRespawn(UI_RespawningNpcs instance)
    {
        if (Player.instance != null && Player.instance.IsInCombat())
        {
            UI_Chatbox.instance?.AddChatMessage("You cannot respawn nearby enemies while in combat!", UI_Chatbox.MessageType.gameplay);
            return false;
        }

        int respawned = RespawnManager.RespawnNearby(
            respawnTimeValue: 2,
            out int skippedBosses,
            out int skippedDisabled);

        if (respawned == 0)
        {
            if (skippedBosses > 0 && skippedDisabled == 0)
            {
                UI_Notifications.instance?.ShowNotification("Boss respawn is disabled. Toggle 'Respawn Bosses' or enable individual bosses.");
            }
            else if (skippedDisabled > 0 || skippedBosses > 0)
            {
                UI_Notifications.instance?.ShowNotification("No enabled mobs to respawn nearby. Toggle mobs on to respawn them.");
            }
        }

        HideDelegate(instance);
        CheckActiveDelegate(instance);
        return false;
    }

    internal static void RefreshUI()
    {
        UI_RespawningNpcs instance = UI_RespawningNpcs.instance;
        if (instance == null)
        {
            return;
        }

        if (instance.container != null)
        {
            CheckActiveDelegate(instance);
            if (instance.showing)
            {
                TickDelegate(instance);
            }
        }

        UpdateButtonAppearance();
    }

    private static void RenderPanel(UI_RespawningNpcs instance)
    {
        if (instance.container_npcs != null)
        {
            instance.container_npcs.Destroy();
        }

        List<RespawnManager.NearbyMobEntry> nearbyMobs = RespawnManager.GetNearbyMobs(out int totalRespawningCount);
        instance.respawningNpcsCount = totalRespawningCount;

        bool alwaysShow = Plugin.AlwaysShowRespawnButton.Value;
        if (Player.instance == null ||
            Player.instance.dead ||
            (!alwaysShow && (Player.instance.IsInCombat() || (totalRespawningCount == 0 && !Plugin.AutoRespawnEnabled.Value))))
        {
            HideDelegate(instance);
            SetActiveDelegate(instance, false);
            return;
        }

        LUIRect panel = LUI_Core.instance.AddImage(
            "RespawningNpcs/Bg",
            Position.topLeft,
            Position.topLeft,
            new Vector2(0f, -125f),
            new Vector2(150f, 0f),
            instance.container);
        instance.container_npcs = panel;

        int row = 0;
        int count = totalRespawningCount;
        string header = $"{count} {(count == 1 ? "enemy" : "enemies")} respawning nearby:";

        LUI_Text headerText = LUI_Core.AddText(
            TextAnchor.MiddleLeft,
            Position.topLeft,
            Position.topLeft,
            new Vector2(5f, -5f),
            new Vector2(500f, 25f),
            panel,
            header);
        headerText.AddOutline();

        float maxWidth = Mathf.Max(headerText.GetPreferredWidth() + 10f, MinPanelWidth);
        row++;

        var navItems = new List<LUI_NavItem>();
        const float textX = 30f;

        int displayCount = Mathf.Min(nearbyMobs.Count, MaxDisplayedEntries);
        for (int i = 0; i < displayCount; i++)
        {
            RespawnManager.NearbyMobEntry entry = nearbyMobs[i];
            float rowY = -5f - row * RowHeight;
            bool isEnabled = RespawnManager.IsMobRespawnEnabled(entry.NpcTypeId, entry.Pos);

            string bossSuffix = entry.IsBoss ? " [Boss]" : string.Empty;
            string labelText = entry.RespawningNpc != null
                ? $"{entry.RespawningNpc.time} - {entry.Name}{bossSuffix}"
                : $"Alive - {entry.Name}{bossSuffix}";

            Color textColor;
            if (!isEnabled)
            {
                textColor = Utils.Color255(135f, 135f, 135f);
            }
            else if (entry.IsBoss)
            {
                textColor = Utils.Color255(255f, 215f, 120f);
            }
            else if (entry.RespawningNpc == null)
            {
                textColor = Utils.Color255(160f, 225f, 160f);
            }
            else
            {
                textColor = Utils.Color255(200f, 200f, 200f);
            }

            maxWidth = Mathf.Max(
                maxWidth,
                AddMobSelectionRow(
                    panel,
                    rowY,
                    textX,
                    entry.NpcTypeId,
                    entry.Pos,
                    entry.Name,
                    isEnabled,
                    labelText,
                    textColor,
                    navItems));
            row++;
        }

        // Bottom global toggles
        float toggleStartY = -5f - row * RowHeight;
        AddPanelToggles(panel, toggleStartY, navItems);

        float panelHeight = 10f + row * RowHeight + ToggleRowHeight * 2f;
        panel.SetSize(maxWidth, panelHeight);

        LUI_Button respawnNowButton = null;
        if (count > 0)
        {
            respawnNowButton = LUI_Core.instance.AddButton(
                "Respawn now",
                Position.topLeft,
                Position.topLeft,
                new Vector2(0f, -(panelHeight + 5f)),
                new Vector2(100f, 25f),
                _ => OnRespawn(instance),
                null,
                panel);
        }

        LUI_NavGroup navGroup = NavGroupRef(instance);
        if (navGroup != null)
        {
            LUI_NavigationManager.instance?.ClearActiveGroup(navGroup);
            navGroup.Clear();

            if (respawnNowButton?.navItem != null)
            {
                navGroup.Register(respawnNowButton.navItem);
            }

            for (int i = 0; i < navItems.Count; i++)
            {
                navGroup.Register(navItems[i]);
            }

            LUI_NavigationManager.instance?.SetActiveGroup(navGroup);
        }
    }

    private static float AddMobSelectionRow(
        LUIRect panel,
        float rowY,
        float textX,
        int npcTypeId,
        Vector3 pos,
        string mobName,
        bool isSelected,
        string labelText,
        Color textColor,
        List<LUI_NavItem> navItems)
    {
        LUI_ToggleButton toggle = CreatePanelToggle(
            string.Empty,
            new Vector2(6f, rowY),
            panel.rectTransform,
            isSelected,
            isOn => OnIndividualMobToggled(npcTypeId, pos, mobName, isOn));

        if (toggle?.navItem != null)
        {
            navItems.Add(toggle.navItem);
        }

        LUI_Text text = LUI_Core.AddText(
            TextAnchor.MiddleLeft,
            Position.topLeft,
            Position.topLeft,
            new Vector2(textX, rowY),
            new Vector2(500f, 25f),
            panel,
            labelText);
        text.SetTextColor(textColor);
        return textX + text.GetPreferredWidth() + 10f;
    }

    private static void OnIndividualMobToggled(int npcTypeId, Vector3 pos, string mobName, bool isOn)
    {
        RespawnManager.SetMobRespawnEnabled(npcTypeId, pos, mobName, isOn);
        if (isOn && Plugin.AutoRespawnEnabled.Value)
        {
            RespawnManager.TryAutoRespawn();
        }

        RefreshUI();
    }

    private static void AddPanelToggles(LUIRect panel, float startY, List<LUI_NavItem> navItems)
    {
        string shortcutLabel = Plugin.FormatShortcut(Plugin.ToggleKey.Value);
        string autoLabel = shortcutLabel == "None" ? "Auto-respawn" : $"Auto-respawn ({shortcutLabel})";

        LUI_ToggleButton autoToggle = CreatePanelToggle(
            autoLabel,
            new Vector2(6f, startY),
            panel.rectTransform,
            Plugin.AutoRespawnEnabled.Value,
            isOn => Plugin.SetAutoRespawn(isOn, playFeedback: false));

        LUI_ToggleButton bossToggle = CreatePanelToggle(
            "Respawn Bosses",
            new Vector2(6f, startY - ToggleRowHeight),
            panel.rectTransform,
            Plugin.RespawnAllBosses.Value,
            isOn => Plugin.RespawnAllBosses.Value = isOn);

        if (autoToggle?.navItem != null)
        {
            navItems.Add(autoToggle.navItem);
        }

        if (bossToggle?.navItem != null)
        {
            navItems.Add(bossToggle.navItem);
        }
    }

    private static LUI_ToggleButton CreatePanelToggle(
        string label,
        Vector2 position,
        Transform parent,
        bool initialValue,
        Action<bool> onChanged)
    {
        // AddToggleButton internally offsets Y by -25f, so compensate by +25f
        LUI_ToggleButton toggle = LUI_Core.instance.AddToggleButton(
            label,
            Position.topLeft,
            Position.topLeft,
            new Vector2(position.x, position.y + 25f),
            parent,
            vars =>
            {
                var btn = (LUI_ToggleButton)vars[0];
                btn.Toggle();
                onChanged?.Invoke(btn.isOn);
            });

        Text labelText = toggle.spawnedObject?.GetComponentInChildren<Text>();
        if (labelText != null)
        {
            if (string.IsNullOrEmpty(label))
            {
                labelText.gameObject.SetActive(false);
            }
            else
            {
                labelText.rectTransform.anchoredPosition = new Vector2(26f, 0f);
                labelText.rectTransform.sizeDelta = new Vector2(210f, 22f);
                labelText.fontSize = 15;
            }
        }

        if (initialValue)
        {
            toggle.Toggle(playSound: false);
        }

        return toggle;
    }

    private static void UpdateHoverText()
    {
        if (UI_Cursor.instance == null)
        {
            return;
        }

        string autoStatus = Plugin.AutoRespawnEnabled.Value ? "<color=#55FF55>ON</color>" : "<color=#AAAAAA>OFF</color>";
        string bossStatus = Plugin.RespawnAllBosses.Value ? "<color=#55FF55>ON</color>" : "<color=#AAAAAA>OFF</color>";
        string shortcut = Plugin.FormatShortcut(Plugin.ToggleKey.Value);

        UI_Cursor.instance.SetUIMouseHoverText(
            $"Nearby mobs & respawn controls\n" +
            $"Auto-respawn ({autoStatus}) [{shortcut}] | Respawn Bosses ({bossStatus})");
    }

    private static void UpdateButtonAppearance()
    {
        if (_hudButtonImage == null)
        {
            return;
        }

        _hudButtonImage.color = Plugin.AutoRespawnEnabled.Value ? ActiveTintColor : InactiveTintColor;
    }
}
