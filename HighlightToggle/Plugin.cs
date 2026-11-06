using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using HighlightPlus;
using LUI;
using UnityEngine;

namespace HighlightToggle;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    internal static ConfigEntry<bool> ToggleEnabled;
    internal static ConfigEntry<KeyboardShortcut> ToggleKey;

    internal static ConfigEntry<bool> HighlightLoot;
    internal static ConfigEntry<bool> HighlightResources;
    internal static ConfigEntry<bool> HighlightOnlyHarvestableResources;
    internal static ConfigEntry<bool> HighlightContainersAndStations;
    internal static ConfigEntry<bool> HighlightDoorsAndObstacles;
    internal static ConfigEntry<bool> HighlightOtherInteractables;
    internal static ConfigEntry<bool> HighlightFriendlyNpcs;
    internal static ConfigEntry<bool> HighlightEnemyNpcs;
    internal static ConfigEntry<bool> HighlightPlayers;

    private readonly Harmony _harmony = new(MyPluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Logger = base.Logger;

        ToggleEnabled = Config.Bind(
            "General",
            "ToggleEnabled",
            false,
            "Whether persistent highlighting of interactable objects is currently toggled on.");

        ToggleKey = Config.Bind(
            "Keybinds",
            "ToggleKey",
            new KeyboardShortcut(KeyCode.H),
            "Key(s) to press to toggle persistent object highlighting on or off.");

        HighlightLoot = Config.Bind(
            "Filters",
            "HighlightLoot",
            true,
            "Highlight dropped loot items on the ground when toggle is enabled.");

        HighlightResources = Config.Bind(
            "Filters",
            "HighlightResources",
            true,
            "Highlight harvestable resources (ore veins, trees, herbs, fishing spots) when toggle is enabled.");

        HighlightOnlyHarvestableResources = Config.Bind(
            "Filters",
            "HighlightOnlyHarvestableResources",
            false,
            "When enabled, only highlight resource nodes that can currently be harvested (not depleted, sufficient skill level, and valid tool owned).");

        HighlightContainersAndStations = Config.Bind(
            "Filters",
            "HighlightContainersAndStations",
            true,
            "Highlight chests, bank chests, workbenches, and wayshrines/portals when toggle is enabled.");

        HighlightDoorsAndObstacles = Config.Bind(
            "Filters",
            "HighlightDoorsAndObstacles",
            true,
            "Highlight doors, stairs, and agility/skill obstacles when toggle is enabled.");

        HighlightOtherInteractables = Config.Bind(
            "Filters",
            "HighlightOtherInteractables",
            true,
            "Highlight all other general interactable objects when toggle is enabled.");

        HighlightFriendlyNpcs = Config.Bind(
            "Filters",
            "HighlightFriendlyNpcs",
            true,
            "Highlight friendly / non-combat NPCs when toggle is enabled.");

        HighlightEnemyNpcs = Config.Bind(
            "Filters",
            "HighlightEnemyNpcs",
            true,
            "Highlight attackable / enemy NPCs when toggle is enabled.");

        HighlightPlayers = Config.Bind(
            "Filters",
            "HighlightPlayers",
            true,
            "Highlight players when toggle is enabled.");

        ToggleEnabled.SettingChanged += (_, _) => MinimapHighlightButtonPatch.UpdateButtonAppearance();

        _harmony.PatchAll();
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    internal static void SetToggleState(bool enabled, bool playSound = true)
    {
        ToggleEnabled.Value = enabled;
        if (playSound && AudioHandler.instance != null)
        {
            AudioHandler.instance.Play2DSound(enabled ? 12 : 13);
        }

        MinimapHighlightButtonPatch.UpdateButtonAppearance();
    }

    internal static void ToggleHighlightState(bool playSound = true)
    {
        SetToggleState(!ToggleEnabled.Value, playSound);
    }
}

[HarmonyPatch(typeof(UI_Minimap))]
public static class MinimapHighlightButtonPatch
{
    private static readonly Color ActiveTintColor = new(1f, 0.85f, 0.25f, 1f);
    private static readonly Color InactiveTintColor = Color.white;

    [HarmonyPatch(nameof(UI_Minimap.SetupUI))]
    [HarmonyPostfix]
    public static void SetupUI_Postfix(UI_Minimap __instance)
    {
        if (__instance.image_highlightInteractables == null)
        {
            return;
        }

        __instance.image_highlightInteractables.AddClickUp(_ =>
        {
            Plugin.ToggleHighlightState(playSound: true);
            if (__instance.highlightButtonActive)
            {
                UpdateHoverTooltip();
            }
        }, null);

        UpdateButtonAppearance();
    }

    [HarmonyPatch("Button_HighlightInteractablesEnter")]
    [HarmonyPostfix]
    public static void ButtonEnter_Postfix()
    {
        UpdateHoverTooltip();
        UpdateButtonAppearance();
    }

    [HarmonyPatch("Button_HighlightInteractablesExit")]
    [HarmonyPostfix]
    public static void ButtonExit_Postfix()
    {
        UpdateButtonAppearance();
    }

    internal static void UpdateHoverTooltip()
    {
        if (UI_Cursor.instance == null || KeyboardInputs.instance == null)
        {
            return;
        }

        string status = Plugin.ToggleEnabled.Value ? "<color=#55FF55>ON</color>" : "<color=#AAAAAA>OFF</color>";
        string toggleShortcut = FormatShortcut(Plugin.ToggleKey.Value);
        string holdKey = KeyboardInputs.instance.GetBoundKeyName(KeyboardInputs.HighlightObjects);

        UI_Cursor.instance.SetUIMouseHoverText(
            $"Highlight interactables ({status})\n" +
            $"Left-click or [{toggleShortcut}]: Toggle | Hold [{holdKey}]: Show All");
    }

    internal static void UpdateButtonAppearance()
    {
        UI_Minimap minimap = UI_Minimap.instance;
        if (minimap == null || minimap.image_highlightInteractables == null)
        {
            return;
        }

        bool toggled = Plugin.ToggleEnabled.Value;
        minimap.image_highlightInteractables.SetImageColor(toggled ? ActiveTintColor : InactiveTintColor);

        if (minimap.highlightButtonActive)
        {
            minimap.image_highlightInteractables.SetImageAlpha(0.55f);
        }
        else
        {
            minimap.image_highlightInteractables.SetImageAlpha(1f);
        }
    }

    private static string FormatShortcut(in KeyboardShortcut shortcut)
    {
        KeyboardShortcut copy = shortcut;
        return copy.MainKey == KeyCode.None ? "None" : copy.ToString();
    }
}

[HarmonyPatch(typeof(UI_HighlightInteractables))]
public static class HighlightInteractablesPatch
{
    private static readonly AccessTools.FieldRef<UI_HighlightInteractables, LUIRect> ContainerRef =
        AccessTools.FieldRefAccess<UI_HighlightInteractables, LUIRect>("container");

    private static readonly Func<UI_HighlightInteractables, bool> CanShowHighlightsDelegate =
        AccessTools.MethodDelegate<Func<UI_HighlightInteractables, bool>>(
            AccessTools.Method(typeof(UI_HighlightInteractables), "CanShowHighlights"));

    private static readonly Func<UI_HighlightInteractables, UI_HighlightInteractables.HightlightObject, bool> IsHighlightObjectUsableDelegate =
        AccessTools.MethodDelegate<Func<UI_HighlightInteractables, UI_HighlightInteractables.HightlightObject, bool>>(
            AccessTools.Method(typeof(UI_HighlightInteractables), "IsHighlightObjectUsable"));

    private static readonly Action<UI_HighlightInteractables, HighlightEffect, bool> SetHighlightedDelegate =
        AccessTools.MethodDelegate<Action<UI_HighlightInteractables, HighlightEffect, bool>>(
            AccessTools.Method(typeof(UI_HighlightInteractables), "SetHighlighted"));

    private static readonly Func<UI_HighlightInteractables, UI_HighlightInteractables.HightlightObject, InteractableHandler.NearestInteractionTarget, bool> IsNearestTargetDelegate =
        AccessTools.MethodDelegate<Func<UI_HighlightInteractables, UI_HighlightInteractables.HightlightObject, InteractableHandler.NearestInteractionTarget, bool>>(
            AccessTools.Method(typeof(UI_HighlightInteractables), "IsNearestTarget"));

    private static readonly Action<UI_HighlightInteractables, Transform, string, Color, bool> ShowLabelDelegate =
        AccessTools.MethodDelegate<Action<UI_HighlightInteractables, Transform, string, Color, bool>>(
            AccessTools.Method(typeof(UI_HighlightInteractables), "ShowLabel"));

    private static readonly Func<KeyboardInputs, bool> TextInputActiveForShortcutsDelegate =
        AccessTools.MethodDelegate<Func<KeyboardInputs, bool>>(
            AccessTools.Method(typeof(KeyboardInputs), "TextInputActiveForShortcuts"));

    private static readonly Func<Interactable_Resource, ItemHandler.Item, bool> IsValidToolDelegate =
        AccessTools.MethodDelegate<Func<Interactable_Resource, ItemHandler.Item, bool>>(
            AccessTools.Method(typeof(Interactable_Resource), "IsValidTool"));

    [HarmonyPatch("Update")]
    [HarmonyPrefix]
    public static void Update_Prefix(UI_HighlightInteractables __instance)
    {
        if (ContainerRef(__instance) == null || !CanShowHighlightsDelegate(__instance))
        {
            return;
        }

        if (KeyboardInputs.instance != null &&
            (KeyboardInputs.instance.changingInputs || TextInputActiveForShortcutsDelegate(KeyboardInputs.instance)))
        {
            return;
        }

        KeyboardShortcut toggleShortcut = Plugin.ToggleKey?.Value ?? KeyboardShortcut.Empty;
        if (IsShortcutDown(in toggleShortcut))
        {
            Plugin.ToggleHighlightState(playSound: true);
        }
    }

    [HarmonyPatch("ShouldShowAllHighlights")]
    [HarmonyPostfix]
    public static void ShouldShowAllHighlights_Postfix(ref bool __result)
    {
        if (!__result && Plugin.ToggleEnabled.Value)
        {
            __result = true;
        }
    }

    [HarmonyPatch("ShowNearbyHighlightObjects")]
    [HarmonyPrefix]
    public static bool ShowNearbyHighlightObjects_Prefix(
        UI_HighlightInteractables __instance,
        InteractableHandler.NearestInteractionTarget nearestTarget)
    {
        // When the vanilla hold-key, minimap hover, controller button, or timer is active, show all unfiltered highlights
        if (IsVanillaHighlightActive(__instance))
        {
            return true;
        }

        if (!Plugin.ToggleEnabled.Value)
        {
            return true;
        }

        if (Player.instance == null)
        {
            return false;
        }

        Vector3 playerPos = Player.instance.transform.position;

        for (int i = __instance.highlightObjects.Count - 1; i >= 0; i--)
        {
            UI_HighlightInteractables.HightlightObject highlightObject = __instance.highlightObjects[i];
            if (!IsHighlightObjectUsableDelegate(__instance, highlightObject))
            {
                __instance.highlightObjects.RemoveAt(i);
                continue;
            }

            if (Vector3.Distance(playerPos, highlightObject.transform.position) <= 20f &&
                ShouldHighlightObject(highlightObject))
            {
                SetHighlightedDelegate(__instance, highlightObject.outline, true);
                if (!string.IsNullOrEmpty(highlightObject.name) &&
                    !IsNearestTargetDelegate(__instance, highlightObject, nearestTarget))
                {
                    ShowLabelDelegate(__instance, highlightObject.transform, highlightObject.name, highlightObject.color, false);
                }
            }
        }

        return false;
    }

    private static bool IsVanillaHighlightActive(UI_HighlightInteractables instance)
    {
        bool keyHeld = KeyboardInputs.instance != null &&
                       ((KeyboardInputs.instance.GetKey(KeyboardInputs.HighlightObjects) &&
                         !KeyboardInputs.instance.changingInputs &&
                         !TextInputActiveForShortcutsDelegate(KeyboardInputs.instance)) ||
                        Input.GetKey((KeyCode)338));

        bool hoverActive = UI_Minimap.instance != null && UI_Minimap.instance.highlightButtonActive;
        bool timerActive = instance.highlightTimer > 0f;

        return keyHeld || hoverActive || timerActive;
    }

    private static bool ShouldHighlightObject(UI_HighlightInteractables.HightlightObject obj)
    {
        if (obj == null)
        {
            return false;
        }

        if (obj.interactable != null)
        {
            Interactable interactable = obj.interactable;

            if (interactable.GetComponent<Interactable_Loot>() != null)
            {
                return Plugin.HighlightLoot.Value;
            }

            Interactable_Resource resource = interactable.GetComponent<Interactable_Resource>();
            if (resource != null)
            {
                if (!Plugin.HighlightResources.Value)
                {
                    return false;
                }

                if (Plugin.HighlightOnlyHarvestableResources.Value && !CanHarvestResource(resource))
                {
                    return false;
                }

                return true;
            }

            if (IsContainerOrStation(interactable))
            {
                return Plugin.HighlightContainersAndStations.Value;
            }

            if (IsDoorOrObstacle(interactable))
            {
                return Plugin.HighlightDoorsAndObstacles.Value;
            }

            return Plugin.HighlightOtherInteractables.Value;
        }

        if (obj.npc != null)
        {
            return obj.npc.canFight
                ? Plugin.HighlightEnemyNpcs.Value
                : Plugin.HighlightFriendlyNpcs.Value;
        }

        // Player / NetworkPlayer
        return Plugin.HighlightPlayers.Value;
    }

    private static bool CanHarvestResource(Interactable_Resource resource)
    {
        if (resource == null || resource.disabled || resource.health <= 0)
        {
            return false;
        }

        if (resource.model_normal != null && !resource.model_normal.activeInHierarchy)
        {
            return false;
        }

        if (resource.model_exhausted != null && resource.model_exhausted.activeSelf)
        {
            return false;
        }

        if (Player.instance == null)
        {
            return false;
        }

        if (resource.requiredSkill != SkillsHandler.SkillType.none)
        {
            int playerLevel = Player.instance.levels?.GetSkill(resource.requiredSkill)?.GetLevel() ?? 0;
            if (playerLevel < resource.requiredLevel)
            {
                return false;
            }
        }

        if (resource.requiredTools != null && resource.requiredTools.Count > 0)
        {
            ItemHandler.Item equipped = Player.instance.equipment?.GetEquippedItem(ItemHandler.ItemType.weapon);
            if (equipped != null && equipped.typeId != 0 && IsValidToolDelegate(resource, equipped))
            {
                return true;
            }

            if (Player.instance.equipment?.equipmentSlots != null)
            {
                foreach (PlayerEquipment.EquipmentSlot equipSlot in Player.instance.equipment.equipmentSlots)
                {
                    if (equipSlot == null || equipSlot.itemId == 0)
                    {
                        continue;
                    }

                    ItemHandler.Item equippedItem = equipSlot.GetItem();
                    if (equippedItem != null && equippedItem.typeId != 0 && IsValidToolDelegate(resource, equippedItem))
                    {
                        return true;
                    }
                }
            }

            if (UI_Inventory.instance?.itemSlots != null)
            {
                foreach (UI_Inventory.ItemSlot slot in UI_Inventory.instance.itemSlots)
                {
                    if (slot == null || slot.itemId == 0 || slot.itemAmount <= 0 || slot.bankSlot)
                    {
                        continue;
                    }

                    ItemHandler.Item item = slot.GetItem();
                    if (item != null && item.typeId != 0 && IsValidToolDelegate(resource, item))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        return true;
    }

    private static bool IsContainerOrStation(Interactable interactable)
    {
        if (interactable is Interactable_Workbench or
            Interactable_SalvagingWorkbench or
            Interactable_Wayshrine or
            Assets.Scripts.Interactable_BankChest or
            Assets.Scripts.Interactable_FastTravelPortal)
        {
            return true;
        }

        string typeName = interactable.GetType().Name;
        if (typeName.IndexOf("Chest", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("Bank", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("Stash", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("DisplayCase", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("TeleportTable", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("Workbench", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("Anvil", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("Furnace", StringComparison.OrdinalIgnoreCase) >= 0 ||
            typeName.IndexOf("Cooking", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        string objName = interactable.objectName;
        if (!string.IsNullOrEmpty(objName) &&
            (objName.IndexOf("Chest", StringComparison.OrdinalIgnoreCase) >= 0 ||
             objName.IndexOf("Bank", StringComparison.OrdinalIgnoreCase) >= 0 ||
             objName.IndexOf("Crate", StringComparison.OrdinalIgnoreCase) >= 0 ||
             objName.IndexOf("Cache", StringComparison.OrdinalIgnoreCase) >= 0 ||
             objName.IndexOf("Stash", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return true;
        }

        return false;
    }

    private static bool IsDoorOrObstacle(Interactable interactable)
    {
        if (interactable is Interactable_Door or
            Interactable_DoorOpener or
            Interactable_DoorStaysOpen or
            Interactable_TeleporterDoor or
            Assets._GameAssets.Scripts.Interactable_DoorItemRequired or
            Interactable_SkillObstacle or
            Interactable_ClimbingObstacle or
            Interactable_LedgeJump or
            Interactable_SteppingStones or
            Interactable_SteppingPillars or
            Interactable_SteppingDebrisWater or
            Interactable_SteppingDebrisExtended or
            Interactable_RopeSwing or
            Interactable_RopeBridge or
            Interactable_Stairs)
        {
            return true;
        }

        string typeName = interactable.GetType().Name;
        return typeName.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0 ||
               typeName.IndexOf("Obstacle", StringComparison.OrdinalIgnoreCase) >= 0 ||
               typeName.IndexOf("Stair", StringComparison.OrdinalIgnoreCase) >= 0 ||
               typeName.IndexOf("Ladder", StringComparison.OrdinalIgnoreCase) >= 0;
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
                Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt),
            _ => Input.GetKey(key)
        };
    }
}
