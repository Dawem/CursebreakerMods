using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LUI;
using UnityEngine;

namespace QuickMove;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    internal static ConfigEntry<KeyboardShortcut> MoveStackKey;
    internal static ConfigEntry<KeyboardShortcut> MoveAllMatchingKey;

    private readonly Harmony _harmony = new(MyPluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Logger = base.Logger;

        MoveStackKey = Config.Bind(
            "Keybinds",
            "MoveStackKey",
            new KeyboardShortcut(KeyCode.LeftControl),
            "Key(s) to hold while left-clicking an item slot to transfer that stack.");

        MoveAllMatchingKey = Config.Bind(
            "Keybinds",
            "MoveAllMatchingKey",
            new KeyboardShortcut(KeyCode.LeftControl, KeyCode.LeftShift),
            "Key(s) to hold while left-clicking an item slot to transfer all matching item stacks.");

        _harmony.PatchAll();
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }
}

[HarmonyPatch(typeof(UI_Inventory), nameof(UI_Inventory.LeftClickItemSlot))]
public static class InventoryQuickMovePatch
{
    private static readonly AccessTools.FieldRef<UI_StorageBag, int> StorageBagIdRef =
        AccessTools.FieldRefAccess<UI_StorageBag, int>("storageBagId");

    private static readonly AccessTools.FieldRef<UI_StorageBag, List<UI_StorageBag.ItemSlot>> RenderedSlotsRef =
        AccessTools.FieldRefAccess<UI_StorageBag, List<UI_StorageBag.ItemSlot>>("renderedSlots");

    private static readonly AccessTools.FieldRef<ItemHandler.Item, bool> StorageAllItemsRef =
        AccessTools.FieldRefAccess<ItemHandler.Item, bool>("storageAllItems");

    private static readonly Func<UI_Inventory, int, int, int> GetAmountThatWouldNotFitMainInventory =
        AccessTools.MethodDelegate<Func<UI_Inventory, int, int, int>>(
            AccessTools.Method(typeof(UI_Inventory), "GetAmountThatWouldNotFitMainInventory", new[] { typeof(int), typeof(int) }));

    [HarmonyPrefix]
    public static bool Prefix(UI_Inventory __instance, UI_Inventory.ItemSlot slot)
    {
        if (slot == null || slot.itemId == 0 || slot.itemAmount <= 0 || slot.bankSlot)
        {
            return true;
        }

        if (LUI_DragHandler.instance != null && LUI_DragHandler.instance.IsDragging())
        {
            return true;
        }

        if (__instance.HasControllerMoveItem() || __instance.usingItem)
        {
            return true;
        }

        if (UI_Bank.instance != null && UI_Bank.instance.active)
        {
            return true;
        }

        if (UI_Trade.instance != null && UI_Trade.instance.CanAddInventorySlot(slot))
        {
            return true;
        }

        if (!TryGetTransferAction(out bool transferAllMatching))
        {
            return true;
        }

        List<int> openBagIds = GetOpenAccessibleBagIds();
        if (openBagIds.Count == 0)
        {
            return true;
        }

        // Quick-move inside an open bag or horsy inventory
        if (slot.IsStorageBagSlot)
        {
            int sourceBagId = slot.SourceStorageBagId;
            if (sourceBagId != StorageBagHandler.HorsyStorageBagId && openBagIds.Contains(StorageBagHandler.HorsyStorageBagId))
            {
                MoveFromBagToHorsy(slot, transferAllMatching);
            }
            else
            {
                MoveFromBagToMainInventory(__instance, slot, transferAllMatching);
            }

            UI_ItemTooltips.instance?.Deactivate();
            return false;
        }

        // Quick-move in main inventory -> move into an open bag or open horsy inventory
        MoveFromMainInventoryToOpenBags(__instance, slot, openBagIds, transferAllMatching);
        UI_ItemTooltips.instance?.Deactivate();
        return false;
    }

    private static bool TryGetTransferAction(out bool transferAllMatching)
    {
        KeyboardShortcut moveStack = Plugin.MoveStackKey?.Value ?? KeyboardShortcut.Empty;
        KeyboardShortcut moveAll = Plugin.MoveAllMatchingKey?.Value ?? KeyboardShortcut.Empty;

        int moveStackCount = CountKeys(in moveStack);
        int moveAllCount = CountKeys(in moveAll);

        // Check the shortcut with more keys first so multi-key chords (e.g. Ctrl+Shift) take precedence over single-key chords (e.g. Ctrl)
        if (moveAllCount >= moveStackCount)
        {
            if (IsShortcutHeld(in moveAll))
            {
                transferAllMatching = true;
                return true;
            }
            if (IsShortcutHeld(in moveStack))
            {
                transferAllMatching = false;
                return true;
            }
        }
        else
        {
            if (IsShortcutHeld(in moveStack))
            {
                transferAllMatching = false;
                return true;
            }
            if (IsShortcutHeld(in moveAll))
            {
                transferAllMatching = true;
                return true;
            }
        }

        transferAllMatching = false;
        return false;
    }

    private static int CountKeys(in KeyboardShortcut shortcut)
    {
        KeyboardShortcut copy = shortcut;
        if (copy.MainKey == KeyCode.None)
        {
            return 0;
        }

        return 1 + copy.Modifiers.Count();
    }

    private static bool IsShortcutHeld(in KeyboardShortcut shortcut)
    {
        KeyboardShortcut copy = shortcut;
        if (copy.MainKey == KeyCode.None)
        {
            return false;
        }

        if (!IsKeyHeld(copy.MainKey))
        {
            return false;
        }

        foreach (KeyCode modifier in copy.Modifiers)
        {
            if (!IsKeyHeld(modifier))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsKeyHeld(KeyCode key)
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

    private static List<int> GetOpenAccessibleBagIds()
    {
        var result = new List<int>();
        if (UI_StorageBagHandler.instance?.listOfStorageBagUIs == null || StorageBagHandler.instance == null)
        {
            return result;
        }

        foreach (UI_StorageBag bagUi in UI_StorageBagHandler.instance.listOfStorageBagUIs)
        {
            if (bagUi != null && bagUi.gameObject != null && bagUi.active)
            {
                int bagId = StorageBagIdRef(bagUi);
                if (bagId != 0 && StorageBagHandler.instance.IsBagAccessible(bagId) && !result.Contains(bagId))
                {
                    result.Add(bagId);
                }
            }
        }

        // Also include held storage bags if the Combined Inventory window is open
        try
        {
            Type combinedWindowType = AccessTools.TypeByName("CombinedInventory.UI_CombinedInventoryWindow");
            if (combinedWindowType != null)
            {
                object instance = AccessTools.Field(combinedWindowType, "instance")?.GetValue(null);
                if (instance != null)
                {
                    MethodInfo isOpenMethod = AccessTools.Method(combinedWindowType, "IsOpen");
                    if (isOpenMethod != null && (bool)isOpenMethod.Invoke(instance, null))
                    {
                        var heldBags = StorageBagHandler.instance.GetAccessibleBagIds(false);
                        if (heldBags != null)
                        {
                            foreach (int bagId in heldBags)
                            {
                                if (!result.Contains(bagId))
                                {
                                    result.Add(bagId);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return result;
    }

    private static void MoveFromMainInventoryToOpenBags(
        UI_Inventory inventory,
        UI_Inventory.ItemSlot clickedSlot,
        List<int> openBagIds,
        bool transferAllMatching)
    {
        int itemId = clickedSlot.itemId;

        List<int> orderedBagIds;
        if (openBagIds.Contains(StorageBagHandler.HorsyStorageBagId))
        {
            orderedBagIds = new List<int> { StorageBagHandler.HorsyStorageBagId };
        }
        else
        {
            // Prioritize specialized bags (e.g., Ore Bag) over general-purpose bags
            orderedBagIds = new List<int>(openBagIds.Count);
            foreach (int bagId in openBagIds)
            {
                ItemHandler.Item bagItem = ItemHandler.instance?.GetItem(bagId);
                if (bagItem != null && !StorageAllItemsRef(bagItem))
                {
                    orderedBagIds.Add(bagId);
                }
            }
            foreach (int bagId in openBagIds)
            {
                if (!orderedBagIds.Contains(bagId))
                {
                    orderedBagIds.Add(bagId);
                }
            }
        }

        bool movedAny = false;
        int firstCompatibleBagId = 0;

        // Always transfer the clicked slot first
        TransferSlotToBags(clickedSlot, itemId, orderedBagIds, ref movedAny, ref firstCompatibleBagId);

        if (transferAllMatching && inventory.itemSlots != null)
        {
            foreach (UI_Inventory.ItemSlot otherSlot in inventory.itemSlots)
            {
                if (otherSlot == null || otherSlot == clickedSlot || otherSlot.IsStorageBagSlot || otherSlot.bankSlot)
                {
                    continue;
                }

                if (otherSlot.itemId != itemId || otherSlot.itemAmount <= 0)
                {
                    continue;
                }

                bool movedFromSlot = TransferSlotToBags(
                    otherSlot,
                    itemId,
                    orderedBagIds,
                    ref movedAny,
                    ref firstCompatibleBagId);

                if (!movedFromSlot)
                {
                    // All compatible open bags are now full
                    break;
                }
            }
        }

        // If nothing could be moved at all, trigger the game's standard chat feedback on the most relevant open bag
        if (!movedAny)
        {
            int fallbackBagId = firstCompatibleBagId != 0 ? firstCompatibleBagId : orderedBagIds[0];
            StorageBagHandler.instance.AddItemToStorageBag(fallbackBagId, clickedSlot, clickedSlot.itemAmount);
        }
    }

    private static void MoveFromBagToHorsy(UI_Inventory.ItemSlot clickedSlot, bool transferAllMatching)
    {
        int itemId = clickedSlot.itemId;
        int sourceBagId = clickedSlot.SourceStorageBagId;
        var horsyTargetList = new List<int> { StorageBagHandler.HorsyStorageBagId };

        bool movedAny = false;
        int firstCompatibleBagId = 0;

        TransferSlotToBags(clickedSlot, itemId, horsyTargetList, ref movedAny, ref firstCompatibleBagId);

        if (transferAllMatching && UI_StorageBagHandler.instance?.listOfStorageBagUIs != null)
        {
            foreach (UI_StorageBag bagUi in UI_StorageBagHandler.instance.listOfStorageBagUIs)
            {
                if (bagUi == null || bagUi.gameObject == null || !bagUi.active || StorageBagIdRef(bagUi) != sourceBagId)
                {
                    continue;
                }

                List<UI_StorageBag.ItemSlot> renderedSlots = RenderedSlotsRef(bagUi);
                if (renderedSlots == null)
                {
                    break;
                }

                foreach (UI_StorageBag.ItemSlot otherSlot in renderedSlots)
                {
                    if (otherSlot == null || otherSlot == clickedSlot || otherSlot.itemId != itemId || otherSlot.itemAmount <= 0)
                    {
                        continue;
                    }

                    bool movedFromSlot = TransferSlotToBags(
                        otherSlot,
                        itemId,
                        horsyTargetList,
                        ref movedAny,
                        ref firstCompatibleBagId);

                    if (!movedFromSlot)
                    {
                        break;
                    }
                }

                break;
            }
        }

        if (!movedAny)
        {
            StorageBagHandler.instance.AddItemToStorageBag(
                StorageBagHandler.HorsyStorageBagId,
                clickedSlot,
                clickedSlot.itemAmount);
        }
    }

    private static bool TransferSlotToBags(
        UI_Inventory.ItemSlot slot,
        int itemId,
        List<int> orderedBagIds,
        ref bool movedAny,
        ref int firstCompatibleBagId)
    {
        bool movedFromThisSlot = false;

        foreach (int bagId in orderedBagIds)
        {
            if (slot.itemId == 0 || slot.itemAmount <= 0)
            {
                break;
            }

            if (!StorageBagHandler.instance.CanBagStoreItem(bagId, itemId))
            {
                continue;
            }

            if (firstCompatibleBagId == 0)
            {
                firstCompatibleBagId = bagId;
            }

            var bag = new StorageBagHandler.StorageBag(bagId);
            int space = bag.CountHowManyItemsCanBeAdded(itemId);
            if (space <= 0)
            {
                continue;
            }

            StorageBagHandler.instance.AddItemToStorageBag(bagId, slot, slot.itemAmount);
            movedAny = true;
            movedFromThisSlot = true;
        }

        return movedFromThisSlot;
    }

    private static void MoveFromBagToMainInventory(
        UI_Inventory inventory,
        UI_Inventory.ItemSlot clickedSlot,
        bool transferAllMatching)
    {
        int itemId = clickedSlot.itemId;
        int sourceBagId = clickedSlot.SourceStorageBagId;

        bool previousDisableNotifications = inventory.disableItemNotifications;
        inventory.disableItemNotifications = true;
        try
        {
            bool canContinue = TransferBagSlotToMainInventory(inventory, clickedSlot);

            if (transferAllMatching && canContinue && UI_StorageBagHandler.instance?.listOfStorageBagUIs != null)
            {
                foreach (UI_StorageBag bagUi in UI_StorageBagHandler.instance.listOfStorageBagUIs)
                {
                    if (bagUi == null || bagUi.gameObject == null || !bagUi.active || StorageBagIdRef(bagUi) != sourceBagId)
                    {
                        continue;
                    }

                    List<UI_StorageBag.ItemSlot> renderedSlots = RenderedSlotsRef(bagUi);
                    if (renderedSlots == null)
                    {
                        break;
                    }

                    foreach (UI_StorageBag.ItemSlot otherSlot in renderedSlots)
                    {
                        if (otherSlot == null || otherSlot == clickedSlot || otherSlot.itemId != itemId || otherSlot.itemAmount <= 0)
                        {
                            continue;
                        }

                        if (!TransferBagSlotToMainInventory(inventory, otherSlot))
                        {
                            return;
                        }
                    }

                    break;
                }

                // If Combined Inventory is open, transfer matching items from its rendered bag slots as well
                try
                {
                    Type combinedWindowType = AccessTools.TypeByName("CombinedInventory.UI_CombinedInventoryWindow");
                    if (combinedWindowType != null)
                    {
                        object instance = AccessTools.Field(combinedWindowType, "instance")?.GetValue(null);
                        if (instance != null)
                        {
                            MethodInfo isOpenMethod = AccessTools.Method(combinedWindowType, "IsOpen");
                            if (isOpenMethod != null && (bool)isOpenMethod.Invoke(instance, null))
                            {
                                var renderedBagSlots = AccessTools.Field(combinedWindowType, "renderedBagSlots")?.GetValue(instance) as List<UI_StorageBag.ItemSlot>;
                                if (renderedBagSlots != null)
                                {
                                    foreach (UI_StorageBag.ItemSlot otherSlot in renderedBagSlots)
                                    {
                                        if (otherSlot == null || otherSlot == clickedSlot || otherSlot.bagId != sourceBagId || otherSlot.itemId != itemId || otherSlot.itemAmount <= 0)
                                        {
                                            continue;
                                        }

                                        if (!TransferBagSlotToMainInventory(inventory, otherSlot))
                                        {
                                            return;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }
        finally
        {
            inventory.disableItemNotifications = previousDisableNotifications;
        }
    }

    private static bool TransferBagSlotToMainInventory(UI_Inventory inventory, UI_Inventory.ItemSlot slot)
    {
        int itemId = slot.itemId;
        int amount = slot.itemAmount;

        int overflow = GetAmountThatWouldNotFitMainInventory(inventory, itemId, amount);
        int amountThatFits = amount - overflow;

        if (amountThatFits <= 0)
        {
            UI_Chatbox.instance?.AddChatMessage("Inventory full!", UI_Chatbox.MessageType.gameplay);
            return false;
        }

        inventory.AddItemToMainInventoryOnly(itemId, amountThatFits);
        slot.AddAmount(-amountThatFits);

        if (overflow > 0)
        {
            UI_Chatbox.instance?.AddChatMessage("Inventory full!", UI_Chatbox.MessageType.gameplay);
            return false;
        }

        return true;
    }
}
