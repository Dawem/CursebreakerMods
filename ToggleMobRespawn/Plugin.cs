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
            "Respawn All Bosses",
            false,
            "When enabled, all boss enemies are included when respawning nearby enemies (both automatically and via the respawn interface). Individual bosses can also be toggled in the respawn interface.");

        ToggleKey = Config.Bind(
            "Keybinds",
            "ToggleKey",
            new KeyboardShortcut(KeyCode.G),
            "Key(s) to press to toggle automatic mob respawn on or off.");

        AutoRespawnEnabled.SettingChanged += (_, _) => OnSettingsChanged();
        RespawnAllBosses.SettingChanged += (_, _) => OnSettingsChanged();

        RespawnManager.LoadSelectedBosses();

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

    internal sealed class SelectedBossEntry
    {
        public string Key;
        public int NpcTypeId;
        public Vector3 Pos;
        public string Name;
    }

    private static readonly Dictionary<string, SelectedBossEntry> SelectedBosses = new(StringComparer.Ordinal);

    internal static void LoadSelectedBosses()
    {
        SelectedBosses.Clear();
        string raw = PlayerPrefs.GetString(SelectedBossesPrefsKey, string.Empty);
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

            if (TryParseBossKey(key, out int npcTypeId, out Vector3 pos))
            {
                SelectedBosses[key] = new SelectedBossEntry
                {
                    Key = key,
                    NpcTypeId = npcTypeId,
                    Pos = pos,
                    Name = name
                };
            }
        }
    }

    private static void SaveSelectedBosses()
    {
        if (SelectedBosses.Count == 0)
        {
            PlayerPrefs.DeleteKey(SelectedBossesPrefsKey);
            PlayerPrefs.Save();
            return;
        }

        var serialized = new List<string>(SelectedBosses.Count);
        foreach (SelectedBossEntry entry in SelectedBosses.Values)
        {
            string safeName = (entry.Name ?? string.Empty).Replace(";", string.Empty).Replace("|", string.Empty);
            serialized.Add($"{entry.Key}|{safeName}");
        }

        PlayerPrefs.SetString(SelectedBossesPrefsKey, string.Join(";", serialized));
        PlayerPrefs.Save();
    }

    internal static string GetBossKey(int npcTypeId, Vector3 pos)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}@{1:F1},{2:F1},{3:F1}",
            npcTypeId,
            pos.x,
            pos.y,
            pos.z);
    }

    private static bool TryParseBossKey(string key, out int npcTypeId, out Vector3 pos)
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

    internal static bool IsBossSelected(NPCHandler.RespawningNpc npc)
    {
        if (npc == null)
        {
            return false;
        }

        return SelectedBosses.ContainsKey(GetBossKey(npc.npcTypeId, npc.pos));
    }

    internal static bool IsBossSelected(string key)
    {
        return !string.IsNullOrEmpty(key) && SelectedBosses.ContainsKey(key);
    }

    internal static void SetBossSelected(int npcTypeId, Vector3 pos, string name, bool selected)
    {
        string key = GetBossKey(npcTypeId, pos);
        if (selected)
        {
            SelectedBosses[key] = new SelectedBossEntry
            {
                Key = key,
                NpcTypeId = npcTypeId,
                Pos = pos,
                Name = name ?? string.Empty
            };
        }
        else
        {
            SelectedBosses.Remove(key);
        }

        SaveSelectedBosses();
    }

    internal static List<SelectedBossEntry> GetNearbyActiveSelectedBosses(List<NPCHandler.RespawningNpc> listedBosses)
    {
        var result = new List<SelectedBossEntry>();
        if (SelectedBosses.Count == 0 || Player.instance == null)
        {
            return result;
        }

        var listedKeys = new HashSet<string>(StringComparer.Ordinal);
        if (listedBosses != null)
        {
            for (int i = 0; i < listedBosses.Count; i++)
            {
                listedKeys.Add(GetBossKey(listedBosses[i].npcTypeId, listedBosses[i].pos));
            }
        }

        foreach (SelectedBossEntry entry in SelectedBosses.Values)
        {
            if (listedKeys.Contains(entry.Key))
            {
                continue;
            }

            if (OlipaRuntimeUtils.DistanceToPlayer(entry.Pos) < RespawnRange)
            {
                result.Add(entry);
            }
        }

        return result;
    }

    internal static void TryAutoRespawn()
    {
        if (Player.instance == null || Player.instance.dead || Player.instance.IsInCombat())
        {
            return;
        }

        int respawned = RespawnNearby(respawnTimeValue: 1, out _);
        if (respawned > 0)
        {
            RespawnUIHelpers.RefreshUI();
        }
    }

    internal static int RespawnNearby(int respawnTimeValue, out int skippedBossCount)
    {
        skippedBossCount = 0;
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

            if (!ShouldRespawnBoss(npc))
            {
                skippedBossCount++;
                continue;
            }

            npc.time = respawnTimeValue;
            respawnedCount++;
        }

        return respawnedCount;
    }

    internal static bool ShouldRespawnBoss(NPCHandler.RespawningNpc npc)
    {
        return Plugin.RespawnAllBosses.Value || IsBossSelected(npc);
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

        return CBActions.instance.CheckCBChecks(matched.visibilityChecks, UI_RespawningNpcs.instance);
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
    [HarmonyPostfix]
    public static void CheckActive_Postfix(UI_RespawningNpcs __instance)
    {
        RespawnUIHelpers.OnCheckActive(__instance);
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

    internal static void OnCheckActive(UI_RespawningNpcs instance)
    {
        if (instance == null || Player.instance == null)
        {
            return;
        }

        if (Plugin.AutoRespawnEnabled.Value && !Player.instance.IsInCombat() && !Player.instance.dead && !instance.active)
        {
            SetActiveDelegate(instance, true);
        }

        UpdateButtonAppearance();
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
        int respawned = RespawnManager.RespawnNearby(
            respawnTimeValue: 2,
            out int skippedBosses);

        if (respawned == 0 && skippedBosses > 0)
        {
            UI_Notifications.instance?.ShowNotification("Boss respawn is disabled. Toggle 'Respawn All Bosses' or select individual bosses.");
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

        var eligibleNpcs = new List<NPCHandler.RespawningNpc>();
        int totalEligibleCount = 0;

        if (NPCHandler.instance?.respawningNpcs != null)
        {
            NPCHandler.instance.respawningNpcs = NPCHandler.instance.respawningNpcs
                .OrderBy(x => x?.time ?? int.MaxValue)
                .ToList();

            List<NPCHandler.RespawningNpc> allRespawning = NPCHandler.instance.respawningNpcs;
            for (int i = 0; i < allRespawning.Count; i++)
            {
                NPCHandler.RespawningNpc npc = allRespawning[i];
                if (!RespawnManager.IsEligibleForRespawn(npc))
                {
                    continue;
                }

                totalEligibleCount++;
                if (eligibleNpcs.Count < MaxDisplayedEntries)
                {
                    eligibleNpcs.Add(npc);
                }
            }
        }

        instance.respawningNpcsCount = totalEligibleCount;
        List<RespawnManager.SelectedBossEntry> activePinnedBosses = RespawnManager.GetNearbyActiveSelectedBosses(eligibleNpcs);

        if (totalEligibleCount == 0 &&
            activePinnedBosses.Count == 0 &&
            (!Plugin.AutoRespawnEnabled.Value || Player.instance == null || Player.instance.IsInCombat() || Player.instance.dead))
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
        int count = totalEligibleCount;
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

        // Every enemy/boss in the respawn interface gets its own selective auto-respawn toggle
        for (int i = 0; i < eligibleNpcs.Count; i++)
        {
            NPCHandler.RespawningNpc npc = eligibleNpcs[i];
            float rowY = -5f - row * RowHeight;
            string npcName = GetNpcDisplayName(npc.npcTypeId);

            int capturedTypeId = npc.npcTypeId;
            Vector3 capturedPos = npc.pos;
            LUI_ToggleButton npcToggle = CreatePanelToggle(
                string.Empty,
                new Vector2(6f, rowY),
                panel.rectTransform,
                RespawnManager.IsBossSelected(npc),
                isOn => OnIndividualBossToggled(capturedTypeId, capturedPos, npcName, isOn));

            if (npcToggle?.navItem != null)
            {
                navItems.Add(npcToggle.navItem);
            }

            LUI_Text npcText = LUI_Core.AddText(
                TextAnchor.MiddleLeft,
                Position.topLeft,
                Position.topLeft,
                new Vector2(30f, rowY),
                new Vector2(500f, 25f),
                panel,
                $"{npc.time} - {npcName}");
            npcText.SetTextColor(Utils.Color255(200f, 200f, 200f));
            maxWidth = Mathf.Max(maxWidth, 30f + npcText.GetPreferredWidth() + 10f);
            row++;
        }

        // Nearby entries pinned for auto-respawn that are currently alive (so user can uncheck them anytime)
        for (int i = 0; i < activePinnedBosses.Count; i++)
        {
            RespawnManager.SelectedBossEntry entry = activePinnedBosses[i];
            float rowY = -5f - row * RowHeight;
            string bossName = !string.IsNullOrEmpty(entry.Name) ? entry.Name : GetNpcDisplayName(entry.NpcTypeId);

            int capturedTypeId = entry.NpcTypeId;
            Vector3 capturedPos = entry.Pos;
            LUI_ToggleButton pinnedToggle = CreatePanelToggle(
                string.Empty,
                new Vector2(6f, rowY),
                panel.rectTransform,
                RespawnManager.IsBossSelected(entry.Key),
                isOn => OnIndividualBossToggled(capturedTypeId, capturedPos, bossName, isOn));

            if (pinnedToggle?.navItem != null)
            {
                navItems.Add(pinnedToggle.navItem);
            }

            LUI_Text pinnedText = LUI_Core.AddText(
                TextAnchor.MiddleLeft,
                Position.topLeft,
                Position.topLeft,
                new Vector2(30f, rowY),
                new Vector2(500f, 25f),
                panel,
                $"Active - {bossName}");
            pinnedText.SetTextColor(Utils.Color255(160f, 225f, 160f));
            maxWidth = Mathf.Max(maxWidth, 30f + pinnedText.GetPreferredWidth() + 10f);
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

    private static void OnIndividualBossToggled(int npcTypeId, Vector3 pos, string bossName, bool isOn)
    {
        RespawnManager.SetBossSelected(npcTypeId, pos, bossName, isOn);
        if (isOn && Plugin.AutoRespawnEnabled.Value)
        {
            RespawnManager.TryAutoRespawn();
        }

        RefreshUI();
    }

    private static string GetNpcDisplayName(int npcTypeId)
    {
        NPCHandler.NPCTemplate template = NPCHandler.instance?.getNpcTemplate(npcTypeId);
        return template != null && !string.IsNullOrEmpty(template.npcName)
            ? template.npcName
            : $"NPC #{npcTypeId}";
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
            "Respawn All Bosses",
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
            $"Enemies respawning nearby\n" +
            $"Auto-respawn ({autoStatus}) [{shortcut}] | Respawn All Bosses ({bossStatus})");
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
