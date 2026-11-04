using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using LUI;
using UnityEngine;

namespace ToolSlots
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.mike.theblackgrimoire.toolslots";
        public const string PluginName = "Tool Slots";
        public const string PluginVersion = "1.1.0";

        // Original 4 tool slot IDs (preserved for save compatibility)
        public const ItemHandler.ItemType ToolSlotAxe = (ItemHandler.ItemType)100;
        public const ItemHandler.ItemType ToolSlotFishingRod = (ItemHandler.ItemType)101;
        public const ItemHandler.ItemType ToolSlotHammer = (ItemHandler.ItemType)102;
        public const ItemHandler.ItemType ToolSlotPickaxe = (ItemHandler.ItemType)103;

        // Additional 11 tool slot IDs
        public const ItemHandler.ItemType ToolSlotBroom = (ItemHandler.ItemType)104;
        public const ItemHandler.ItemType ToolSlotShears = (ItemHandler.ItemType)105;
        public const ItemHandler.ItemType ToolSlotTorch = (ItemHandler.ItemType)106;
        public const ItemHandler.ItemType ToolSlotShovel = (ItemHandler.ItemType)107;
        public const ItemHandler.ItemType ToolSlotSalvagingToolkit = (ItemHandler.ItemType)108;
        public const ItemHandler.ItemType ToolSlotKnife = (ItemHandler.ItemType)109;
        public const ItemHandler.ItemType ToolSlotChisel = (ItemHandler.ItemType)110;
        public const ItemHandler.ItemType ToolSlotSaw = (ItemHandler.ItemType)111;
        public const ItemHandler.ItemType ToolSlotPestle = (ItemHandler.ItemType)112;
        public const ItemHandler.ItemType ToolSlotNeedle = (ItemHandler.ItemType)113;
        public const ItemHandler.ItemType ToolSlotFishingGear = (ItemHandler.ItemType)114;

        // Ordered in 3 rows of 5 slots for the Equipment UI grid
        public static readonly ItemHandler.ItemType[] CustomToolSlotTypes =
        {
            // Row 0: Primary gathering & utility tools
            ToolSlotAxe,
            ToolSlotPickaxe,
            ToolSlotFishingRod,
            ToolSlotHammer,
            ToolSlotTorch,

            // Row 1: Secondary gathering & processing tools
            ToolSlotShovel,
            ToolSlotBroom,
            ToolSlotShears,
            ToolSlotSaw,
            ToolSlotChisel,

            // Row 2: Crafting, salvaging & specialized fishing tools
            ToolSlotKnife,
            ToolSlotPestle,
            ToolSlotNeedle,
            ToolSlotFishingGear,
            ToolSlotSalvagingToolkit
        };

        internal static ManualLogSource Log;
        private Harmony _harmony;

        private static readonly AccessTools.FieldRef<ItemHandler.Item, HarvestablesHandler.Tool> ItemToolRef =
            AccessTools.FieldRefAccess<ItemHandler.Item, HarvestablesHandler.Tool>("tool");

        private static readonly AccessTools.FieldRef<UI_BottomRightTab, LUIRect> ContainerTabRef =
            AccessTools.FieldRefAccess<UI_BottomRightTab, LUIRect>("container_tab");

        private static readonly AccessTools.FieldRef<UI_BottomRightTabs_Equipment, LUI_ScrollView> ScrollViewStatsRef =
            AccessTools.FieldRefAccess<UI_BottomRightTabs_Equipment, LUI_ScrollView>("scrollview_stats");

        private static readonly AccessTools.FieldRef<UI_Inventory, UI_Inventory.ItemSlot> CurrentConsumableItemRef =
            AccessTools.FieldRefAccess<UI_Inventory, UI_Inventory.ItemSlot>("currentConsumableItem");

        private static readonly Action<UI_BottomRightTabs_Equipment, object[]> UnequipSlotMethod =
            AccessTools.MethodDelegate<Action<UI_BottomRightTabs_Equipment, object[]>>(
                AccessTools.Method(typeof(UI_BottomRightTabs_Equipment), "UnequipSlot", new[] { typeof(object[]) }));

        private static readonly Action<UI_BottomRightTabs_Equipment> SetupEquipmentNavigationMethod =
            AccessTools.MethodDelegate<Action<UI_BottomRightTabs_Equipment>>(
                AccessTools.Method(typeof(UI_BottomRightTabs_Equipment), "SetupEquipmentNavigation"));

        private static readonly Func<UI_BottomRightTabs_Equipment, ItemHandler.ItemType, UI_BottomRightTabs_Equipment.EQSlot> GetEquipmentSlotMethod =
            AccessTools.MethodDelegate<Func<UI_BottomRightTabs_Equipment, ItemHandler.ItemType, UI_BottomRightTabs_Equipment.EQSlot>>(
                AccessTools.Method(typeof(UI_BottomRightTabs_Equipment), "GetEquipmentSlot", new[] { typeof(ItemHandler.ItemType) }));

        private static readonly Action<UI_BottomRightTabs_Equipment, UI_BottomRightTabs_Equipment.EQSlot, UI_BottomRightTabs_Equipment.EQSlot, UI_BottomRightTabs_Equipment.EQSlot, UI_BottomRightTabs_Equipment.EQSlot, UI_BottomRightTabs_Equipment.EQSlot> SetSlotNavigationMethod =
            AccessTools.MethodDelegate<Action<UI_BottomRightTabs_Equipment, UI_BottomRightTabs_Equipment.EQSlot, UI_BottomRightTabs_Equipment.EQSlot, UI_BottomRightTabs_Equipment.EQSlot, UI_BottomRightTabs_Equipment.EQSlot, UI_BottomRightTabs_Equipment.EQSlot>>(
                AccessTools.Method(
                    typeof(UI_BottomRightTabs_Equipment),
                    "SetSlotNavigation",
                    new[]
                    {
                        typeof(UI_BottomRightTabs_Equipment.EQSlot),
                        typeof(UI_BottomRightTabs_Equipment.EQSlot),
                        typeof(UI_BottomRightTabs_Equipment.EQSlot),
                        typeof(UI_BottomRightTabs_Equipment.EQSlot),
                        typeof(UI_BottomRightTabs_Equipment.EQSlot)
                    }));

        private static readonly Action<PlayerEquipment, int> OnEquipItemMethod =
            AccessTools.MethodDelegate<Action<PlayerEquipment, int>>(
                AccessTools.Method(typeof(PlayerEquipment), "OnEquipItem", new[] { typeof(int) }));

        private static readonly Func<UI_RightClickMenu, string, Vector2, Action<object[]>, object[], LUI_Button> AddButtonMethod =
            AccessTools.MethodDelegate<Func<UI_RightClickMenu, string, Vector2, Action<object[]>, object[], LUI_Button>>(
                AccessTools.Method(
                    typeof(UI_RightClickMenu),
                    "AddButton",
                    new[] { typeof(string), typeof(Vector2), typeof(Action<object[]>), typeof(object[]) }));

        private static readonly Action<UI_RightClickMenu, object[]> DeactivateRightClickMenuMethod =
            AccessTools.MethodDelegate<Action<UI_RightClickMenu, object[]>>(
                AccessTools.Method(typeof(UI_RightClickMenu), "Deactivate", new[] { typeof(object[]) }));

        private static readonly Action<UI_Inventory, UI_Inventory.ItemSlot> ConsumeItemEffectsMethod =
            AccessTools.MethodDelegate<Action<UI_Inventory, UI_Inventory.ItemSlot>>(
                AccessTools.Method(typeof(UI_Inventory), "ConsumeItemEffects", new[] { typeof(UI_Inventory.ItemSlot) }));

        private static readonly Action<UI_Inventory, UI_Inventory.ItemSlot, UI_Inventory.ItemSlot> UseItemOnItemMethod =
            AccessTools.MethodDelegate<Action<UI_Inventory, UI_Inventory.ItemSlot, UI_Inventory.ItemSlot>>(
                AccessTools.Method(typeof(UI_Inventory), "UseItemOnItem", new[] { typeof(UI_Inventory.ItemSlot), typeof(UI_Inventory.ItemSlot) }));

        private static readonly Type SalvagingHandlerType = AccessTools.TypeByName("Handlers.SalvagingHandler");
        private static readonly System.Reflection.FieldInfo SalvagingHandlerInstanceField =
            SalvagingHandlerType != null ? AccessTools.Field(SalvagingHandlerType, "instance") : null;
        private static readonly System.Reflection.MethodInfo CanSalvageMethod =
            SalvagingHandlerType != null ? AccessTools.Method(SalvagingHandlerType, "CanSalvage", new[] { typeof(ItemHandler.Item) }) : null;
        private static readonly System.Reflection.MethodInfo IsSalvageLockedMethod =
            SalvagingHandlerType != null ? AccessTools.Method(SalvagingHandlerType, "IsSalvageLocked", new[] { typeof(int) }) : null;

        public static bool CanSalvageItemUnlocked(ItemHandler.Item item)
        {
            if (item == null || SalvagingHandlerInstanceField == null || CanSalvageMethod == null || IsSalvageLockedMethod == null)
                return false;

            object instance = SalvagingHandlerInstanceField.GetValue(null);
            if (instance == null)
                return false;

            bool canSalvage = (bool)CanSalvageMethod.Invoke(instance, new object[] { item });
            if (!canSalvage)
                return false;

            bool isLocked = (bool)IsSalvageLockedMethod.Invoke(instance, new object[] { item.typeId });
            return !isLocked;
        }

        private static readonly Stack<List<HarvestablesHandler.Tool>> ActiveResourceToolStack = new Stack<List<HarvestablesHandler.Tool>>();
        private static readonly Stack<ItemHandler.ItemType> ActiveForcedToolSlotStack = new Stack<ItemHandler.ItemType>();
        private static int _suppressInventoryToolCheckDepth;
        private static bool _suppressWeaponRedirect;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{PluginName} v{PluginVersion} loaded! Added {CustomToolSlotTypes.Length} dedicated tool equipment slots.");
        }

        public static int GetBaseItemId(int itemId)
        {
            if (itemId >= 100000 && itemId < 200000)
                return itemId - 100000;
            return itemId;
        }

        public static HarvestablesHandler.Tool GetItemHarvestTool(ItemHandler.Item item)
        {
            return item != null ? ItemToolRef(item) : HarvestablesHandler.Tool.none;
        }

        public static bool IsToolSlotType(ItemHandler.ItemType itemType)
        {
            int val = (int)itemType;
            return val >= (int)ToolSlotAxe && val <= (int)ToolSlotFishingGear;
        }

        public static bool TryGetToolSlotType(ItemHandler.Item item, out ItemHandler.ItemType slotType)
        {
            slotType = ItemHandler.ItemType.weapon;
            if (item == null || item.typeId <= 0 || item.typeId >= 1000000)
                return false;

            int baseId = GetBaseItemId(item.typeId);
            HarvestablesHandler.Tool tool = GetItemHarvestTool(item);
            List<ItemHandler.ItemCategory> categories = item.itemCategory;

            // 1. Axe / Hatchet
            if (tool == HarvestablesHandler.Tool.hatchet
                || (categories != null && categories.Contains(ItemHandler.ItemCategory.hatchet)))
            {
                slotType = ToolSlotAxe;
                return true;
            }

            // 2. Fishing Rod
            if (tool == HarvestablesHandler.Tool.fishingrod
                || (categories != null && categories.Contains(ItemHandler.ItemCategory.fishingrod)))
            {
                slotType = ToolSlotFishingRod;
                return true;
            }

            // 3. Pickaxe
            if (tool == HarvestablesHandler.Tool.pickaxe
                || (categories != null && categories.Contains(ItemHandler.ItemCategory.pickaxe)))
            {
                slotType = ToolSlotPickaxe;
                return true;
            }

            // 4. Broom (268 Broom)
            if (tool == HarvestablesHandler.Tool.broom || baseId == 268)
            {
                slotType = ToolSlotBroom;
                return true;
            }

            // 5. Hammer (490 Hammer, 673 Arakite Hammer, 1124 High Striker's Hammer, or hammer/blacksmithhammer category)
            if ((categories != null
                 && (categories.Contains(ItemHandler.ItemCategory.hammer)
                     || categories.Contains(ItemHandler.ItemCategory.blacksmithhammer)))
                || baseId == 490
                || baseId == 673
                || baseId == 1124)
            {
                slotType = ToolSlotHammer;
                return true;
            }

            // 6. Shears / Scissors (988 Shears, 791 Scissors)
            if ((categories != null && categories.Contains(ItemHandler.ItemCategory.shears))
                || baseId == 988
                || baseId == 791)
            {
                slotType = ToolSlotShears;
                return true;
            }

            // 7. Torch / Lantern (676 Torch, 919 Ancient Torch, 964 Chitin Torch, 1003 Lantern of Soulsoothing, 1005 The Priest's Lantern)
            if ((categories != null && categories.Contains(ItemHandler.ItemCategory.torch))
                || baseId == 676
                || baseId == 919
                || baseId == 964
                || baseId == 1003
                || baseId == 1005)
            {
                slotType = ToolSlotTorch;
                return true;
            }

            // 8. Shovel (961 Shovel, 467 Broken Shovel)
            if (baseId == 961 || baseId == 467)
            {
                slotType = ToolSlotShovel;
                return true;
            }

            // 9. Salvaging Toolkit (2108 Salvaging Toolkit)
            if (baseId == 2108)
            {
                slotType = ToolSlotSalvagingToolkit;
                return true;
            }

            // 10. Knife (934 Gutting Knife, 489 Knife, 482 Dave's Trusty Hunting Knife)
            if (baseId == 934 || baseId == 489 || baseId == 482)
            {
                slotType = ToolSlotKnife;
                return true;
            }

            // 11. Chisel (931 Miner's Chisel)
            if (baseId == 931)
            {
                slotType = ToolSlotChisel;
                return true;
            }

            // 12. Saw / Carpentry (794 Saw, 942 Splitting Axe)
            if (baseId == 794 || baseId == 942)
            {
                slotType = ToolSlotSaw;
                return true;
            }

            // 13. Pestle & Mortar (234 Pestle and Mortar)
            if (baseId == 234)
            {
                slotType = ToolSlotPestle;
                return true;
            }

            // 14. Needle & Thread (718 Needle and Thread)
            if (baseId == 718)
            {
                slotType = ToolSlotNeedle;
                return true;
            }

            // 15. Fishing Gear (491 Harpoon, 493 Fishing Spear, 494 Fishing Net)
            if (baseId == 491 || baseId == 493 || baseId == 494)
            {
                slotType = ToolSlotFishingGear;
                return true;
            }

            return false;
        }

        public static bool TryMapHarvestToolToSlotType(HarvestablesHandler.Tool tool, out ItemHandler.ItemType slotType)
        {
            switch (tool)
            {
                case HarvestablesHandler.Tool.hatchet:
                    slotType = ToolSlotAxe;
                    return true;
                case HarvestablesHandler.Tool.fishingrod:
                    slotType = ToolSlotFishingRod;
                    return true;
                case HarvestablesHandler.Tool.pickaxe:
                    slotType = ToolSlotPickaxe;
                    return true;
                case HarvestablesHandler.Tool.broom:
                    slotType = ToolSlotBroom;
                    return true;
                default:
                    slotType = ItemHandler.ItemType.weapon;
                    return false;
            }
        }

        public static string GetToolSlotDisplayName(ItemHandler.ItemType slotType)
        {
            switch (slotType)
            {
                case ToolSlotAxe:
                    return "Axe Slot";
                case ToolSlotFishingRod:
                    return "Fishing Rod Slot";
                case ToolSlotHammer:
                    return "Hammer Slot";
                case ToolSlotPickaxe:
                    return "Pickaxe Slot";
                case ToolSlotBroom:
                    return "Broom Slot";
                case ToolSlotShears:
                    return "Shears / Scissors Slot";
                case ToolSlotTorch:
                    return "Torch / Lantern Slot";
                case ToolSlotShovel:
                    return "Shovel Slot";
                case ToolSlotSalvagingToolkit:
                    return "Salvaging Toolkit Slot";
                case ToolSlotKnife:
                    return "Knife Slot";
                case ToolSlotChisel:
                    return "Chisel Slot";
                case ToolSlotSaw:
                    return "Saw / Carpentry Slot";
                case ToolSlotPestle:
                    return "Pestle & Mortar Slot";
                case ToolSlotNeedle:
                    return "Needle & Thread Slot";
                case ToolSlotFishingGear:
                    return "Fishing Gear Slot (Harpoon / Spear / Net)";
                default:
                    return "Tool Slot";
            }
        }

        public static int GetEmptyToolSlotIconItemId(ItemHandler.ItemType slotType)
        {
            switch (slotType)
            {
                case ToolSlotAxe:
                    return 139; // Copper Hatchet
                case ToolSlotFishingRod:
                    return 167; // Fishing Rod
                case ToolSlotHammer:
                    return 490; // Hammer
                case ToolSlotPickaxe:
                    return 141; // Copper Pickaxe
                case ToolSlotBroom:
                    return 268; // Broom
                case ToolSlotShears:
                    return 988; // Shears
                case ToolSlotTorch:
                    return 676; // Torch
                case ToolSlotShovel:
                    return 961; // Shovel
                case ToolSlotSalvagingToolkit:
                    return 2108; // Salvaging Toolkit
                case ToolSlotKnife:
                    return 934; // Gutting Knife
                case ToolSlotChisel:
                    return 931; // Miner's Chisel
                case ToolSlotSaw:
                    return 794; // Saw
                case ToolSlotPestle:
                    return 234; // Pestle and Mortar
                case ToolSlotNeedle:
                    return 718; // Needle and Thread
                case ToolSlotFishingGear:
                    return 491; // Harpoon
                default:
                    return 139;
            }
        }

        public static UI_Inventory.ItemSlot CreateVirtualToolItemSlot(ItemHandler.ItemType slotType, int itemId)
        {
            return new UI_Inventory.ItemSlot
            {
                slotId = -100 - (int)slotType,
                itemId = itemId,
                itemAmount = 1
            };
        }

        public static void EnsureToolSlots(PlayerEquipment equipment)
        {
            if (equipment == null || equipment.equipmentSlots == null)
                return;

            foreach (ItemHandler.ItemType slotType in CustomToolSlotTypes)
            {
                bool exists = false;
                for (int i = 0; i < equipment.equipmentSlots.Count; i++)
                {
                    if (equipment.equipmentSlots[i].slotType == slotType)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    equipment.equipmentSlots.Add(new PlayerEquipment.EquipmentSlot
                    {
                        slotType = slotType,
                        itemId = 0
                    });
                }
            }
        }

        public static PlayerEquipment.EquipmentSlot GetRawSlot(PlayerEquipment equipment, ItemHandler.ItemType slotType)
        {
            if (equipment == null || equipment.equipmentSlots == null)
                return null;

            EnsureToolSlots(equipment);
            for (int i = 0; i < equipment.equipmentSlots.Count; i++)
            {
                if (equipment.equipmentSlots[i].slotType == slotType)
                    return equipment.equipmentSlots[i];
            }

            return null;
        }

        public static bool IsEquippedInAnyToolSlot(PlayerEquipment equipment, int itemId, bool includeExceptional = true)
        {
            if (equipment == null || itemId <= 0)
                return false;

            EnsureToolSlots(equipment);
            int targetBaseId = GetBaseItemId(itemId);

            foreach (ItemHandler.ItemType slotType in CustomToolSlotTypes)
            {
                PlayerEquipment.EquipmentSlot slot = GetRawSlot(equipment, slotType);
                if (slot != null && slot.itemId != 0)
                {
                    if (slot.itemId == itemId || (includeExceptional && GetBaseItemId(slot.itemId) == targetBaseId))
                        return true;
                }
            }

            return false;
        }

        internal static bool TryResolveRedirectedToolSlot(PlayerEquipment equipment, out PlayerEquipment.EquipmentSlot redirectedSlot)
        {
            redirectedSlot = null;
            if (_suppressWeaponRedirect || equipment == null)
                return false;

            EnsureToolSlots(equipment);

            // 1. Explicit forced tool slot context (MiningTunnelHandler, MiningRareProcHandler, TrollVillageHandler, HighStriker, Tooltips)
            if (ActiveForcedToolSlotStack.Count > 0)
            {
                ItemHandler.ItemType forcedType = ActiveForcedToolSlotStack.Peek();
                PlayerEquipment.EquipmentSlot toolSlot = GetRawSlot(equipment, forcedType);
                if (toolSlot != null && toolSlot.itemId != 0)
                {
                    PlayerEquipment.EquipmentSlot weaponSlot = GetRawSlot(equipment, ItemHandler.ItemType.weapon);
                    if (weaponSlot != null && weaponSlot.itemId != 0)
                    {
                        ItemHandler.Item weaponItem = weaponSlot.GetItem();
                        ItemHandler.Item toolItem = toolSlot.GetItem();
                        if (TryGetToolSlotType(weaponItem, out ItemHandler.ItemType weaponToolType)
                            && weaponToolType == forcedType
                            && weaponItem.level > toolItem.level)
                        {
                            return false;
                        }
                    }

                    redirectedSlot = toolSlot;
                    return true;
                }
            }

            // 2. Resource harvesting context (Interactable_Resource.CanInteract / Interact)
            if (ActiveResourceToolStack.Count > 0)
            {
                List<HarvestablesHandler.Tool> requiredTools = ActiveResourceToolStack.Peek();
                if (requiredTools != null && requiredTools.Count > 0)
                {
                    PlayerEquipment.EquipmentSlot weaponSlot = GetRawSlot(equipment, ItemHandler.ItemType.weapon);
                    ItemHandler.Item weaponItem = weaponSlot != null && weaponSlot.itemId != 0 ? weaponSlot.GetItem() : null;
                    bool weaponIsValidTool = weaponItem != null && requiredTools.Contains(GetItemHarvestTool(weaponItem));

                    PlayerEquipment.EquipmentSlot bestToolSlot = null;
                    int bestToolLevel = -1;

                    for (int i = 0; i < requiredTools.Count; i++)
                    {
                        if (TryMapHarvestToolToSlotType(requiredTools[i], out ItemHandler.ItemType candidateSlotType))
                        {
                            PlayerEquipment.EquipmentSlot candidateSlot = GetRawSlot(equipment, candidateSlotType);
                            if (candidateSlot != null && candidateSlot.itemId != 0)
                            {
                                ItemHandler.Item candidateItem = candidateSlot.GetItem();
                                if (candidateItem != null
                                    && requiredTools.Contains(GetItemHarvestTool(candidateItem))
                                    && candidateItem.level > bestToolLevel)
                                {
                                    bestToolSlot = candidateSlot;
                                    bestToolLevel = candidateItem.level;
                                }
                            }
                        }
                    }

                    if (bestToolSlot != null)
                    {
                        if (!weaponIsValidTool || bestToolLevel >= weaponItem.level)
                        {
                            redirectedSlot = bestToolSlot;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        // =====================================================================
        // ItemHandler.Item Patches
        // =====================================================================

        [HarmonyPatch(typeof(ItemHandler.Item), nameof(ItemHandler.Item.IsEquippable))]
        private static class Item_IsEquippable_Patch
        {
            private static void Postfix(ItemHandler.Item __instance, ref bool __result)
            {
                if (!__result && TryGetToolSlotType(__instance, out _))
                {
                    __result = true;
                }
            }
        }

        // =====================================================================
        // PlayerEquipment Patches
        // =====================================================================

        [HarmonyPatch(typeof(PlayerEquipment), "Start")]
        private static class PlayerEquipment_Start_Patch
        {
            private static void Postfix(PlayerEquipment __instance)
            {
                EnsureToolSlots(__instance);
            }
        }

        [HarmonyPatch(typeof(PlayerEquipment), nameof(PlayerEquipment.LoadEquipment))]
        private static class PlayerEquipment_LoadEquipment_Patch
        {
            private static void Prefix(PlayerEquipment __instance)
            {
                EnsureToolSlots(__instance);
            }

            private static void Postfix(PlayerEquipment __instance)
            {
                EnsureToolSlots(__instance);
                if (UI_BottomRightTabs_Equipment.instance != null)
                {
                    UI_BottomRightTabs_Equipment.instance.ChangeEquips();
                }
            }
        }

        [HarmonyPatch(typeof(PlayerEquipment), nameof(PlayerEquipment.GetSlot))]
        private static class PlayerEquipment_GetSlot_Patch
        {
            private static void Prefix(PlayerEquipment __instance)
            {
                EnsureToolSlots(__instance);
            }

            private static void Postfix(PlayerEquipment __instance, ItemHandler.ItemType itemType, ref PlayerEquipment.EquipmentSlot __result)
            {
                if (itemType == ItemHandler.ItemType.weapon && TryResolveRedirectedToolSlot(__instance, out PlayerEquipment.EquipmentSlot redirected))
                {
                    __result = redirected;
                }
            }
        }

        [HarmonyPatch(typeof(PlayerEquipment), nameof(PlayerEquipment.GetEquippedItem))]
        private static class PlayerEquipment_GetEquippedItem_Patch
        {
            private static void Postfix(PlayerEquipment __instance, ItemHandler.ItemType itemType, ref ItemHandler.Item __result)
            {
                if (itemType == ItemHandler.ItemType.weapon && TryResolveRedirectedToolSlot(__instance, out PlayerEquipment.EquipmentSlot redirected))
                {
                    __result = ItemHandler.instance.GetItem(redirected.itemId);
                }
            }
        }

        [HarmonyPatch(typeof(PlayerEquipment), nameof(PlayerEquipment.GetEquippedItemId))]
        private static class PlayerEquipment_GetEquippedItemId_Patch
        {
            private static void Postfix(PlayerEquipment __instance, ItemHandler.ItemType itemType, ref int __result)
            {
                if (itemType == ItemHandler.ItemType.weapon && TryResolveRedirectedToolSlot(__instance, out PlayerEquipment.EquipmentSlot redirected))
                {
                    __result = redirected.itemId;
                }
            }
        }

        [HarmonyPatch(typeof(PlayerEquipment), "HasItemCategoryEquipped")]
        private static class PlayerEquipment_HasItemCategoryEquipped_Patch
        {
            private static void Postfix(PlayerEquipment __instance, ItemHandler.ItemCategory c, ref bool __result)
            {
                if (__result || __instance == null)
                    return;

                // Allow Lanterns (1003 Lantern of Soulsoothing, 1005 The Priest's Lantern) equipped in the Torch/Lantern slot
                // to also satisfy light/torch checks in dark environments.
                if (c == ItemHandler.ItemCategory.torch)
                {
                    PlayerEquipment.EquipmentSlot torchSlot = GetRawSlot(__instance, ToolSlotTorch);
                    if (torchSlot != null && torchSlot.itemId != 0)
                    {
                        int baseId = GetBaseItemId(torchSlot.itemId);
                        if (baseId == 1003 || baseId == 1005)
                        {
                            __result = true;
                        }
                    }
                }
                else if (c == ItemHandler.ItemCategory.shears)
                {
                    PlayerEquipment.EquipmentSlot shearsSlot = GetRawSlot(__instance, ToolSlotShears);
                    if (shearsSlot != null && shearsSlot.itemId != 0)
                    {
                        int baseId = GetBaseItemId(shearsSlot.itemId);
                        if (baseId == 988 || baseId == 791)
                        {
                            __result = true;
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(PlayerEquipment), nameof(PlayerEquipment.EquipItem), typeof(int), typeof(bool))]
        private static class PlayerEquipment_EquipItem_Patch
        {
            private static bool Prefix(PlayerEquipment __instance, int itemId, bool skipInventoryCheck)
            {
                if (itemId <= 0 || ItemHandler.instance == null)
                    return true;

                ItemHandler.Item item = ItemHandler.instance.GetItem(itemId);
                if (!TryGetToolSlotType(item, out ItemHandler.ItemType toolSlotType))
                    return true;

                EnsureToolSlots(__instance);

                _suppressInventoryToolCheckDepth++;
                bool hasInInventory;
                try
                {
                    hasInInventory = skipInventoryCheck || (UI_Inventory.instance != null && UI_Inventory.instance.HasItem(itemId));
                }
                finally
                {
                    _suppressInventoryToolCheckDepth--;
                }

                if (!hasInInventory)
                {
                    return false;
                }

                bool originalTwoHanded = item.twoHanded;
                bool canEquip;
                try
                {
                    item.twoHanded = false;
                    canEquip = __instance.CanEquipItem(item.typeId);
                }
                finally
                {
                    item.twoHanded = originalTwoHanded;
                }

                if (!canEquip)
                {
                    return false;
                }

                PlayerEquipment.EquipmentSlot slot = GetRawSlot(__instance, toolSlotType);
                if (slot == null)
                {
                    return false;
                }

                if (!skipInventoryCheck)
                {
                    UI_Inventory.instance.RemoveItem(itemId);
                }

                if (slot.itemId != 0)
                {
                    __instance.UnequipSlot(toolSlotType, true);
                }

                if (slot.itemId != 0)
                {
                    return false;
                }

                slot.itemId = itemId;

                if (AudioHandler.instance != null)
                {
                    AudioHandler.instance.Play2DSound(ItemHandler.instance.GetItemEquipSound(itemId));
                }

                if (UI_BottomRightTabs_Equipment.instance != null)
                {
                    UI_BottomRightTabs_Equipment.instance.ChangeEquips();
                }

                OnEquipItemMethod?.Invoke(__instance, itemId);

                return false;
            }
        }

        [HarmonyPatch(typeof(PlayerEquipment), nameof(PlayerEquipment.UnequipSlot), typeof(ItemHandler.ItemType), typeof(bool))]
        private static class PlayerEquipment_UnequipSlot_Patch
        {
            private static bool Prefix(PlayerEquipment __instance, ItemHandler.ItemType slotType, bool addItemToInventory)
            {
                if (!IsToolSlotType(slotType))
                    return true;

                EnsureToolSlots(__instance);
                PlayerEquipment.EquipmentSlot slot = GetRawSlot(__instance, slotType);
                if (slot == null || slot.itemId == 0)
                    return false;

                if (!UI_Inventory.instance.CanAddItem(slot.itemId, 1) && addItemToInventory)
                {
                    if (UI_Notifications.instance != null)
                    {
                        UI_Notifications.instance.ShowDefaultNotification("Inventory full!", -1f);
                    }
                    return false;
                }

                if (addItemToInventory && !slot.GetItem().unequipDestroy)
                {
                    UI_Inventory.instance.disableItemNotifications = true;
                    UI_Inventory.instance.AddItem(slot.itemId, 1, true);
                    UI_Inventory.instance.disableItemNotifications = false;
                }

                slot.itemId = 0;

                if (UI_BottomRightTabs_Equipment.instance != null)
                {
                    UI_BottomRightTabs_Equipment.instance.ChangeEquips();
                }

                if (AudioHandler.instance != null)
                {
                    AudioHandler.instance.Play2DSound(48);
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(PlayerEquipment), nameof(PlayerEquipment.UnequipAll))]
        private static class PlayerEquipment_UnequipAll_Patch
        {
            private static void Postfix(PlayerEquipment __instance)
            {
                foreach (ItemHandler.ItemType slotType in CustomToolSlotTypes)
                {
                    __instance.UnequipSlot(slotType, true);
                }
            }
        }

        [HarmonyPatch(typeof(PlayerEquipment), nameof(PlayerEquipment.GetAllEquippedItemsList))]
        private static class PlayerEquipment_GetAllEquippedItemsList_Patch
        {
            private static void Postfix(PlayerEquipment __instance, ref List<ItemHandler.Item> __result)
            {
                if (__instance == null || __result == null)
                    return;

                EnsureToolSlots(__instance);
                foreach (ItemHandler.ItemType slotType in CustomToolSlotTypes)
                {
                    PlayerEquipment.EquipmentSlot slot = GetRawSlot(__instance, slotType);
                    if (slot != null && slot.itemId != 0)
                    {
                        ItemHandler.Item item = slot.GetItem();
                        if (item != null && !__result.Contains(item))
                        {
                            __result.Add(item);
                        }
                    }
                }
            }
        }

        // =====================================================================
        // Equipment Screen UI & Right-Click Menu Patches
        // =====================================================================

        [HarmonyPatch(typeof(UI_BottomRightTabs_Equipment), nameof(UI_BottomRightTabs_Equipment.Draw))]
        private static class UI_BottomRightTabs_Equipment_Draw_Patch
        {
            private static void Postfix(UI_BottomRightTabs_Equipment __instance)
            {
                if (__instance == null || __instance.EQSlots == null)
                    return;

                LUIRect containerTab = ContainerTabRef(__instance);
                if (containerTab == null)
                    return;

                if (Player.instance != null && Player.instance.equipment != null)
                {
                    EnsureToolSlots(Player.instance.equipment);
                }

                // Compact and shift the 6 vanilla equipment slots slightly upward so their bottom edge ends at y = -186
                // (in vanilla, armor spans y = -120..-220 and trinket/bracelet span y = -145..-205).
                for (int i = 0; i < __instance.EQSlots.Count; i++)
                {
                    UI_BottomRightTabs_Equipment.EQSlot slot = __instance.EQSlots[i];
                    if (slot == null || slot.background == null || slot.itemIcon == null)
                        continue;

                    switch (slot.itemType)
                    {
                        case ItemHandler.ItemType.head:
                            slot.background.SetPosition(0f, -48f);
                            slot.background.SetSize(52f, 52f);
                            slot.itemIcon.SetPosition(0f, 0f);
                            slot.itemIcon.SetSize(52f, 52f);
                            break;
                        case ItemHandler.ItemType.weapon:
                            slot.background.SetPosition(-82f, -64f);
                            slot.background.SetSize(54f, 54f);
                            slot.itemIcon.SetPosition(4f, -4f);
                            slot.itemIcon.SetSize(46f, 46f);
                            break;
                        case ItemHandler.ItemType.shield:
                            slot.background.SetPosition(82f, -64f);
                            slot.background.SetSize(54f, 54f);
                            slot.itemIcon.SetPosition(0f, 0f);
                            slot.itemIcon.SetSize(54f, 54f);
                            break;
                        case ItemHandler.ItemType.armor:
                            slot.background.SetPosition(0f, -104f);
                            slot.background.SetSize(72f, 82f);
                            slot.itemIcon.SetPosition(0f, -7f);
                            slot.itemIcon.SetSize(68f, 68f);
                            break;
                        case ItemHandler.ItemType.trinket:
                            slot.background.SetPosition(-82f, -124f);
                            slot.background.SetSize(54f, 54f);
                            slot.itemIcon.SetPosition(0f, 0f);
                            slot.itemIcon.SetSize(54f, 54f);
                            break;
                        case ItemHandler.ItemType.bracelet:
                            slot.background.SetPosition(82f, -124f);
                            slot.background.SetSize(54f, 54f);
                            slot.itemIcon.SetPosition(0f, 0f);
                            slot.itemIcon.SetSize(54f, 54f);
                            break;
                    }
                }

                // Place the 3x5 tool slot grid from y = -190 to y = -310, and size the stats scrollview below it (y = -315 to -415).
                LUI_ScrollView scrollViewStats = ScrollViewStatsRef(__instance);
                if (scrollViewStats != null)
                {
                    scrollViewStats.SetSize(245f, 100f);
                }

                Transform parentTransform = containerTab.GetParentTransform();
                float[] xPositions = { -88f, -44f, 0f, 44f, 88f };
                float[] yPositions = { -190f, -231f, -272f };
                Vector2 slotSize = new Vector2(38f, 38f);
                Vector2 iconSize = new Vector2(34f, 34f);

                for (int i = 0; i < CustomToolSlotTypes.Length; i++)
                {
                    ItemHandler.ItemType toolSlotType = CustomToolSlotTypes[i];

                    bool alreadyExists = false;
                    for (int j = 0; j < __instance.EQSlots.Count; j++)
                    {
                        if (__instance.EQSlots[j].itemType == toolSlotType)
                        {
                            alreadyExists = true;
                            break;
                        }
                    }
                    if (alreadyExists)
                        continue;

                    int col = i % 5;
                    int row = i / 5;

                    UI_BottomRightTabs_Equipment.EQSlot eqSlot = new UI_BottomRightTabs_Equipment.EQSlot();
                    eqSlot.itemType = toolSlotType;
                    eqSlot.background = LUI_Core.instance.AddButton(
                        "EquipmentTab/SlotBg",
                        "",
                        12,
                        Position.topMiddle,
                        Position.topMiddle,
                        new Vector2(xPositions[col], yPositions[row]),
                        slotSize,
                        vars => UnequipSlotMethod?.Invoke(__instance, vars),
                        new object[] { eqSlot.itemType },
                        parentTransform);

                    eqSlot.itemIcon = LUI_Core.instance.AddImage(
                        null,
                        Position.center,
                        Position.center,
                        Vector2.zero,
                        iconSize,
                        eqSlot.background.GetParentTransform(),
                        false);

                    __instance.EQSlots.Add(eqSlot);
                }

                __instance.ChangeEquips();
                SetupEquipmentNavigationMethod?.Invoke(__instance);
            }
        }

        [HarmonyPatch(typeof(UI_BottomRightTabs_Equipment), "WireEquipmentNavigation")]
        private static class UI_BottomRightTabs_Equipment_WireEquipmentNavigation_Patch
        {
            private static void Postfix(UI_BottomRightTabs_Equipment __instance)
            {
                if (__instance == null || GetEquipmentSlotMethod == null || SetSlotNavigationMethod == null)
                    return;

                var head = GetEquipmentSlotMethod(__instance, ItemHandler.ItemType.head);
                var armor = GetEquipmentSlotMethod(__instance, ItemHandler.ItemType.armor);
                var weapon = GetEquipmentSlotMethod(__instance, ItemHandler.ItemType.weapon);
                var shield = GetEquipmentSlotMethod(__instance, ItemHandler.ItemType.shield);
                var trinket = GetEquipmentSlotMethod(__instance, ItemHandler.ItemType.trinket);
                var bracelet = GetEquipmentSlotMethod(__instance, ItemHandler.ItemType.bracelet);

                UI_BottomRightTabs_Equipment.EQSlot[] toolSlots = new UI_BottomRightTabs_Equipment.EQSlot[CustomToolSlotTypes.Length];
                for (int i = 0; i < CustomToolSlotTypes.Length; i++)
                {
                    toolSlots[i] = GetEquipmentSlotMethod(__instance, CustomToolSlotTypes[i]);
                    if (toolSlots[i] == null)
                        return;
                }

                // Connect bottom vanilla equipment row down into Row 0 of tool slots
                SetSlotNavigationMethod(__instance, trinket, weapon, toolSlots[0], null, armor);
                SetSlotNavigationMethod(__instance, armor, head, toolSlots[2], weapon, shield);
                SetSlotNavigationMethod(__instance, bracelet, shield, toolSlots[4], trinket, null);

                // Wire 3x5 tool slot grid navigation
                const int cols = 5;
                int rows = CustomToolSlotTypes.Length / cols;
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        int idx = r * cols + c;
                        UI_BottomRightTabs_Equipment.EQSlot up = r == 0
                            ? (c <= 1 ? trinket : (c == 2 ? armor : bracelet))
                            : toolSlots[(r - 1) * cols + c];
                        UI_BottomRightTabs_Equipment.EQSlot down = r < rows - 1
                            ? toolSlots[(r + 1) * cols + c]
                            : null;
                        UI_BottomRightTabs_Equipment.EQSlot left = c > 0
                            ? toolSlots[r * cols + (c - 1)]
                            : null;
                        UI_BottomRightTabs_Equipment.EQSlot right = c < cols - 1
                            ? toolSlots[r * cols + (c + 1)]
                            : null;

                        SetSlotNavigationMethod(__instance, toolSlots[idx], up, down, left, right);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(UI_BottomRightTabs_Equipment), "UpdateSlotIcons")]
        private static class UI_BottomRightTabs_Equipment_UpdateSlotIcons_Patch
        {
            private static void Postfix(UI_BottomRightTabs_Equipment __instance)
            {
                if (__instance == null || __instance.EQSlots == null || Player.instance == null || Player.instance.equipment == null)
                    return;

                EnsureToolSlots(Player.instance.equipment);

                for (int i = 0; i < __instance.EQSlots.Count; i++)
                {
                    UI_BottomRightTabs_Equipment.EQSlot eqSlot = __instance.EQSlots[i];
                    if (eqSlot == null || !IsToolSlotType(eqSlot.itemType) || eqSlot.itemIcon == null)
                        continue;

                    PlayerEquipment.EquipmentSlot slot = GetRawSlot(Player.instance.equipment, eqSlot.itemType);
                    if (slot == null || slot.itemId == 0)
                    {
                        if (UI_Inventory.instance != null)
                        {
                            int placeholderItemId = GetEmptyToolSlotIconItemId(eqSlot.itemType);
                            eqSlot.itemIcon.SetImage(UI_Inventory.instance.GetItemIcon(placeholderItemId));
                        }

                        eqSlot.itemIcon.SetImageColor(new Color(0f, 0f, 0f, 0.45f));
                        eqSlot.itemIcon.AddRightClickDown(null, null);

                        string hoverLabel = GetToolSlotDisplayName(eqSlot.itemType);
                        eqSlot.itemIcon.AddMouseHover(
                            _ =>
                            {
                                if (UI_Cursor.instance != null)
                                    UI_Cursor.instance.SetUIMouseHoverText(hoverLabel);
                            },
                            null,
                            _ =>
                            {
                                if (UI_Cursor.instance != null)
                                    UI_Cursor.instance.SetUIMouseHoverText("");
                            },
                            null);
                    }
                    else
                    {
                        eqSlot.itemIcon.SetImageColor(Color.white);
                        ItemHandler.ItemType capturedSlotType = eqSlot.itemType;
                        int capturedItemId = slot.itemId;
                        eqSlot.itemIcon.AddRightClickDown(
                            _ =>
                            {
                                if (UI_RightClickMenu.instance != null)
                                {
                                    UI_RightClickMenu.instance.Activate(new object[]
                                    {
                                        new UI_RightClickMenu.EquipmentSlotContext
                                        {
                                            slotType = capturedSlotType,
                                            itemId = capturedItemId
                                        }
                                    });
                                }
                            },
                            null);
                    }
                }
            }
        }

        private static void AddCustomMenuButton(UI_RightClickMenu menu, ref int counter, string text, Action<object[]> onClick)
        {
            if (menu == null || AddButtonMethod == null)
                return;

            Vector2 pos = new Vector2(5f, -5f - counter * 25f);
            AddButtonMethod(
                menu,
                text,
                pos,
                vars =>
                {
                    onClick?.Invoke(vars);
                    DeactivateRightClickMenuMethod?.Invoke(menu, null);
                },
                null);
            counter++;
        }

        [HarmonyPatch(typeof(UI_RightClickMenu), "EquipmentSlotOptions")]
        private static class UI_RightClickMenu_EquipmentSlotOptions_Patch
        {
            private static void Postfix(UI_RightClickMenu __instance, UI_RightClickMenu.EquipmentSlotContext equipmentSlot, ref int counter)
            {
                if (__instance == null
                    || equipmentSlot == null
                    || equipmentSlot.itemId <= 0
                    || !IsToolSlotType(equipmentSlot.slotType)
                    || AddButtonMethod == null
                    || ItemHandler.instance == null
                    || UI_Inventory.instance == null)
                {
                    return;
                }

                ItemHandler.Item item = ItemHandler.instance.GetItem(equipmentSlot.itemId);
                if (item == null)
                    return;

                UI_Inventory.ItemSlot virtualSlot = CreateVirtualToolItemSlot(equipmentSlot.slotType, equipmentSlot.itemId);

                if (item.abilityId != 0 && ConsumeItemEffectsMethod != null)
                {
                    AddCustomMenuButton(
                        __instance,
                        ref counter,
                        "Activate <color=#c9c9c9>" + item.itemName,
                        _ => ConsumeItemEffectsMethod(UI_Inventory.instance, virtualSlot));
                }

                AddCustomMenuButton(
                    __instance,
                    ref counter,
                    "Use <color=#c9c9c9>" + item.itemName,
                    _ =>
                    {
                        UI_Inventory.instance.usingItemSlot = virtualSlot;
                        UI_Inventory.instance.usingItem = true;
                    });
            }
        }

        [HarmonyPatch(typeof(UI_RightClickMenu), "InventoryItemOptions")]
        private static class UI_RightClickMenu_InventoryItemOptions_Patch
        {
            private static void Postfix(UI_RightClickMenu __instance, UI_Inventory.ItemSlot itemslot, ref int counter)
            {
                if (__instance == null
                    || itemslot == null
                    || itemslot.itemId <= 0
                    || AddButtonMethod == null
                    || UseItemOnItemMethod == null
                    || Player.instance == null
                    || Player.instance.equipment == null
                    || UI_Inventory.instance == null)
                {
                    return;
                }

                ItemHandler.Item targetItem = itemslot.GetItem();
                if (targetItem == null)
                    return;

                // If Salvaging Toolkit (2108) is equipped in its tool slot, add a direct "Salvage" option on salvageable items
                PlayerEquipment.EquipmentSlot salvageSlot = GetRawSlot(Player.instance.equipment, ToolSlotSalvagingToolkit);
                if (salvageSlot != null
                    && salvageSlot.itemId != 0
                    && CanSalvageItemUnlocked(targetItem))
                {
                    UI_Inventory.ItemSlot virtualSalvageSlot = CreateVirtualToolItemSlot(ToolSlotSalvagingToolkit, salvageSlot.itemId);
                    AddCustomMenuButton(
                        __instance,
                        ref counter,
                        "Salvage <color=#c9c9c9>" + targetItem.itemName,
                        _ => UseItemOnItemMethod(UI_Inventory.instance, virtualSalvageSlot, itemslot));
                }

                // If Pestle and Mortar (234) is equipped in its tool slot, add a direct "Crush bones" option on bone items
                PlayerEquipment.EquipmentSlot pestleSlot = GetRawSlot(Player.instance.equipment, ToolSlotPestle);
                if (pestleSlot != null
                    && pestleSlot.itemId != 0
                    && targetItem.itemCategory != null
                    && targetItem.itemCategory.Contains(ItemHandler.ItemCategory.bone))
                {
                    UI_Inventory.ItemSlot virtualPestleSlot = CreateVirtualToolItemSlot(ToolSlotPestle, pestleSlot.itemId);
                    AddCustomMenuButton(
                        __instance,
                        ref counter,
                        "Crush <color=#c9c9c9>" + targetItem.itemName,
                        _ => UseItemOnItemMethod(UI_Inventory.instance, virtualPestleSlot, itemslot));
                }
            }
        }

        [HarmonyPatch(typeof(UI_Inventory), "ActivateUsingOverlay")]
        private static class UI_Inventory_ActivateUsingOverlay_Patch
        {
            private static bool Prefix(UI_Inventory.ItemSlot slot)
            {
                // Virtual tool slots have negative slotId and do not correspond to an inventory grid cell.
                return slot != null && slot.slotId >= 0;
            }
        }

        [HarmonyPatch(typeof(UI_Inventory), nameof(UI_Inventory.RemoveCurrentConsumableItem))]
        private static class UI_Inventory_RemoveCurrentConsumableItem_Patch
        {
            private static bool Prefix(UI_Inventory __instance)
            {
                if (__instance == null)
                    return true;

                UI_Inventory.ItemSlot currentSlot = CurrentConsumableItemRef(__instance);
                return currentSlot == null || currentSlot.slotId >= 0;
            }
        }

        [HarmonyPatch(typeof(UI_ItemTooltips), nameof(UI_ItemTooltips.ItemTooltip), typeof(int), typeof(LUI_Image), typeof(UI_Inventory.InventorySlot))]
        private static class UI_ItemTooltips_ItemTooltip_Patch
        {
            private static void Prefix(int originalItemId, out bool __state)
            {
                __state = false;
                if (originalItemId > 0 && ItemHandler.instance != null)
                {
                    ItemHandler.Item item = ItemHandler.instance.GetItem(originalItemId);
                    if (TryGetToolSlotType(item, out ItemHandler.ItemType toolSlotType))
                    {
                        ActiveForcedToolSlotStack.Push(toolSlotType);
                        __state = true;
                    }
                }
            }

            private static void Finalizer(bool __state)
            {
                if (__state && ActiveForcedToolSlotStack.Count > 0)
                {
                    ActiveForcedToolSlotStack.Pop();
                }
            }
        }

        // =====================================================================
        // Resource Harvesting, Mining, Woodcutting & Tool Check Patches
        // =====================================================================

        [HarmonyPatch(typeof(Interactable_Resource), nameof(Interactable_Resource.CanInteract))]
        private static class Interactable_Resource_CanInteract_Patch
        {
            private static void Prefix(Interactable_Resource __instance)
            {
                ActiveResourceToolStack.Push(__instance != null ? __instance.requiredTools : null);
            }

            private static void Finalizer()
            {
                if (ActiveResourceToolStack.Count > 0)
                {
                    ActiveResourceToolStack.Pop();
                }
            }
        }

        [HarmonyPatch(typeof(Interactable_Resource), nameof(Interactable_Resource.Interact))]
        private static class Interactable_Resource_Interact_Patch
        {
            private static void Prefix(Interactable_Resource __instance)
            {
                ActiveResourceToolStack.Push(__instance != null ? __instance.requiredTools : null);
            }

            private static void Finalizer()
            {
                if (ActiveResourceToolStack.Count > 0)
                {
                    ActiveResourceToolStack.Pop();
                }
            }
        }

        [HarmonyPatch(typeof(PlayerInteract), nameof(PlayerInteract.AttemptStartInteract))]
        private static class PlayerInteract_AttemptStartInteract_Patch
        {
            private static void Postfix(PlayerInteract __instance)
            {
                if (__instance == null
                    || !(__instance.interactingObject is Interactable_Resource resource)
                    || __instance.interactTimer <= 0f
                    || resource.requiredTools == null
                    || resource.requiredTools.Count == 0
                    || Player.instance == null
                    || Player.instance.equipment == null)
                {
                    return;
                }

                // If the player is harvesting a resource using a tool from a tool slot while holding a different weapon,
                // hide the combat weapon for the duration of the harvest and show the tool hand model if applicable.
                _suppressWeaponRedirect = true;
                PlayerEquipment.EquipmentSlot mainWeaponSlot;
                try
                {
                    mainWeaponSlot = GetRawSlot(Player.instance.equipment, ItemHandler.ItemType.weapon);
                }
                finally
                {
                    _suppressWeaponRedirect = false;
                }

                ItemHandler.Item mainWeaponItem = mainWeaponSlot != null && mainWeaponSlot.itemId != 0 ? mainWeaponSlot.GetItem() : null;
                bool mainWeaponIsMatchingTool = mainWeaponItem != null && resource.requiredTools.Contains(GetItemHarvestTool(mainWeaponItem));
                if (mainWeaponIsMatchingTool)
                    return;

                for (int i = 0; i < resource.requiredTools.Count; i++)
                {
                    HarvestablesHandler.Tool requiredTool = resource.requiredTools[i];
                    if (TryMapHarvestToolToSlotType(requiredTool, out ItemHandler.ItemType toolSlotType))
                    {
                        PlayerEquipment.EquipmentSlot toolSlot = GetRawSlot(Player.instance.equipment, toolSlotType);
                        if (toolSlot != null && toolSlot.itemId != 0)
                        {
                            Player.instance.equipment.HideWeapon(__instance.interactTimer);
                            if (requiredTool == HarvestablesHandler.Tool.hatchet)
                            {
                                Player.instance.ShowTemporaryHandItem("Hatchet", __instance.interactTimer);
                            }
                            else if (requiredTool == HarvestablesHandler.Tool.pickaxe)
                            {
                                Player.instance.ShowTemporaryHandItem("Pickaxe", __instance.interactTimer);
                            }
                            break;
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(MiningTunnelHandler), "HasValidPickaxe")]
        private static class MiningTunnelHandler_HasValidPickaxe_Patch
        {
            private static void Prefix()
            {
                ActiveForcedToolSlotStack.Push(ToolSlotPickaxe);
            }

            private static void Finalizer()
            {
                if (ActiveForcedToolSlotStack.Count > 0)
                {
                    ActiveForcedToolSlotStack.Pop();
                }
            }
        }

        [HarmonyPatch(typeof(MiningTunnelHandler), "GetEquippedPickaxeLevelForSpeed")]
        private static class MiningTunnelHandler_GetEquippedPickaxeLevelForSpeed_Patch
        {
            private static void Prefix()
            {
                ActiveForcedToolSlotStack.Push(ToolSlotPickaxe);
            }

            private static void Finalizer()
            {
                if (ActiveForcedToolSlotStack.Count > 0)
                {
                    ActiveForcedToolSlotStack.Pop();
                }
            }
        }

        [HarmonyPatch(typeof(MiningTunnelHandler), "TryRollMiningRareDrop")]
        private static class MiningTunnelHandler_TryRollMiningRareDrop_Patch
        {
            private static void Prefix()
            {
                ActiveForcedToolSlotStack.Push(ToolSlotPickaxe);
            }

            private static void Finalizer()
            {
                if (ActiveForcedToolSlotStack.Count > 0)
                {
                    ActiveForcedToolSlotStack.Pop();
                }
            }
        }

        [HarmonyPatch(typeof(MiningRareProcHandler), "GetPickaxeBonusChance")]
        private static class MiningRareProcHandler_GetPickaxeBonusChance_Patch
        {
            private static void Prefix()
            {
                ActiveForcedToolSlotStack.Push(ToolSlotPickaxe);
            }

            private static void Finalizer()
            {
                if (ActiveForcedToolSlotStack.Count > 0)
                {
                    ActiveForcedToolSlotStack.Pop();
                }
            }
        }

        [HarmonyPatch(typeof(TrollVillageHandler), "Root")]
        private static class TrollVillageHandler_Root_Patch
        {
            private static void Prefix()
            {
                ActiveForcedToolSlotStack.Push(ToolSlotAxe);
            }

            private static void Finalizer()
            {
                if (ActiveForcedToolSlotStack.Count > 0)
                {
                    ActiveForcedToolSlotStack.Pop();
                }
            }
        }

        [HarmonyPatch(typeof(Interactable_HighStriker), "Interactable_HighStriker_OnInteract")]
        private static class Interactable_HighStriker_OnInteract_Patch
        {
            private static void Prefix()
            {
                ActiveForcedToolSlotStack.Push(ToolSlotHammer);
            }

            private static void Finalizer()
            {
                if (ActiveForcedToolSlotStack.Count > 0)
                {
                    ActiveForcedToolSlotStack.Pop();
                }
            }
        }

        [HarmonyPatch(typeof(UI_Inventory), "ConfirmSplit")]
        private static class UI_Inventory_ConfirmSplit_Patch
        {
            private static void Prefix()
            {
                _suppressInventoryToolCheckDepth++;
            }

            private static void Finalizer()
            {
                if (_suppressInventoryToolCheckDepth > 0)
                {
                    _suppressInventoryToolCheckDepth--;
                }
            }
        }

        public static bool CanPlayerEquipToolSilently(ItemHandler.Item item)
        {
            if (item == null || Player.instance == null)
                return false;

            if (item.skill != SkillsHandler.SkillType.none)
            {
                int playerLevel = Player.instance.levels?.GetSkill(item.skill)?.GetLevel() ?? 0;
                if (playerLevel < item.level)
                    return false;
            }

            return true;
        }

        public static bool ShouldAutoReplaceEquippedTool(ItemHandler.Item currentItem, ItemHandler.Item newItem)
        {
            if (currentItem == null || currentItem.typeId <= 0)
                return true;

            if (newItem == null || newItem.typeId <= 0)
                return false;

            if (newItem.level > currentItem.level)
                return true;

            int currentBaseId = GetBaseItemId(currentItem.typeId);
            int newBaseId = GetBaseItemId(newItem.typeId);

            // Upgrade normal tool to Exceptional version of the same tool
            if (newItem.level == currentItem.level && newBaseId == currentBaseId && newItem.typeId > currentItem.typeId)
                return true;

            // Upgrade Broken Shovel (467) to Shovel (961)
            if (currentBaseId == 467 && newBaseId == 961)
                return true;

            return false;
        }

        public static bool CanAutoEquipGroundToolLoot(Interactable_Loot loot)
        {
            if (loot == null
                || loot.itemId <= 0
                || loot.amount != 1
                || ItemHandler.instance == null
                || Player.instance == null
                || Player.instance.equipment == null)
            {
                return false;
            }

            ItemHandler.Item item = ItemHandler.instance.GetItem(loot.itemId);
            if (!TryGetToolSlotType(item, out ItemHandler.ItemType toolSlotType))
                return false;

            if (!CanPlayerEquipToolSilently(item))
                return false;

            EnsureToolSlots(Player.instance.equipment);
            PlayerEquipment.EquipmentSlot slot = GetRawSlot(Player.instance.equipment, toolSlotType);
            if (slot == null)
                return false;

            if (slot.itemId == 0)
                return true;

            ItemHandler.Item currentItem = slot.GetItem();
            if (!ShouldAutoReplaceEquippedTool(currentItem, item))
                return false;

            return UI_Inventory.instance != null && UI_Inventory.instance.CanAddItem(slot.itemId, 1);
        }

        [HarmonyPatch(typeof(LootHandler), "CanAutoEquipDeathDroppedLoot")]
        private static class LootHandler_CanAutoEquipDeathDroppedLoot_Patch
        {
            private static void Postfix(Interactable_Loot l, ref bool __result)
            {
                if (!__result && CanAutoEquipGroundToolLoot(l))
                {
                    __result = true;
                }
            }
        }

        [HarmonyPatch(typeof(UI_Inventory), nameof(UI_Inventory.HasItem), typeof(int), typeof(int), typeof(bool))]
        private static class UI_Inventory_HasItem_Patch
        {
            private static void Postfix(int itemId, int amount, bool includeExceptionals, ref bool __result)
            {
                if (!__result
                    && _suppressInventoryToolCheckDepth <= 0
                    && amount <= 1
                    && Player.instance != null
                    && Player.instance.equipment != null
                    && IsEquippedInAnyToolSlot(Player.instance.equipment, itemId, includeExceptionals))
                {
                    __result = true;
                }
            }
        }
    }
}
