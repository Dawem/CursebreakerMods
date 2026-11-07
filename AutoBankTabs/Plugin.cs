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

namespace AutoBankTabs;

public enum BankItemTag
{
    // --- 10 Default Comprehensive Bank Tab Tags ---
    Weapons,
    Armor,
    Jewelry,
    ToolsAndBags,
    Potions,
    FoodAndCooking,
    HerbsAndAlchemy,
    CraftingMaterials,
    BooksAndScrolls,
    QuestAndMisc,

    // --- Additional Granular / Specialized Tags for User Customization ---
    AllEquipment,
    MeleeWeapons,
    RangedWeapons,
    MagicWeapons,
    Shields,
    Headgear,
    BodyArmor,
    Trinkets,
    Bracelets,
    ToolsOnly,
    StorageBags,
    CookedFoodAndDrink,
    RawFoodAndFish,
    OresAndBars,
    WoodAndPlanks,
    HidesAndCloth,
    GemsAndCrystals,
    SalvageAndScraps,
    BonesAndMonsterParts,
    Furnishings,
    KeysAndCurrencies,
    QuestItems,
    Misc,
    Empty,
    Locked
}

public enum BankTabSortMode
{
    SubCategoryAndName,
    SubCategoryAndLevel,
    Name,
    Level,
    UnitValue,
    StackValue,
    Amount,
    PreserveOrder
}

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public const int TotalBankTabs = 10;
    public const int SlotsPerTab = 78;
    private const string AutoBankTabsUsedVar = "AutoBankTabsUsed";

    internal static new ManualLogSource Logger;

    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> ShowConfirmation;
    internal static ConfigEntry<bool> AutoRouteDeposits;
    internal static ConfigEntry<BankTabSortMode> SortMode;
    internal static ConfigEntry<bool> SortDescending;
    internal static ConfigEntry<string> ButtonLabel;

    internal static readonly ConfigEntry<BankItemTag>[] TabTags = new ConfigEntry<BankItemTag>[TotalBankTabs];
    internal static readonly ConfigEntry<string>[] TabNames = new ConfigEntry<string>[TotalBankTabs];

    private static readonly BankItemTag[] DefaultTabTags =
    {
        BankItemTag.Weapons,           // Tab 1
        BankItemTag.Armor,             // Tab 2
        BankItemTag.Jewelry,           // Tab 3
        BankItemTag.ToolsAndBags,      // Tab 4
        BankItemTag.Potions,           // Tab 5
        BankItemTag.FoodAndCooking,    // Tab 6
        BankItemTag.HerbsAndAlchemy,   // Tab 7
        BankItemTag.CraftingMaterials, // Tab 8
        BankItemTag.BooksAndScrolls,   // Tab 9
        BankItemTag.QuestAndMisc       // Tab 10
    };

    private static readonly Dictionary<BankItemTag, string> DefaultTagNames = new()
    {
        { BankItemTag.Weapons, "Weapons & Shields" },
        { BankItemTag.Armor, "Armor & Outfits" },
        { BankItemTag.Jewelry, "Jewelry & Charms" },
        { BankItemTag.ToolsAndBags, "Tools & Bags" },
        { BankItemTag.Potions, "Potions & Elixirs" },
        { BankItemTag.FoodAndCooking, "Food & Cooking" },
        { BankItemTag.HerbsAndAlchemy, "Herbs & Alchemy" },
        { BankItemTag.CraftingMaterials, "Crafting Materials" },
        { BankItemTag.BooksAndScrolls, "Books & Scrolls" },
        { BankItemTag.QuestAndMisc, "Quest, Keys & Misc" },
        { BankItemTag.AllEquipment, "All Equipment" },
        { BankItemTag.MeleeWeapons, "Melee Weapons" },
        { BankItemTag.RangedWeapons, "Ranged Weapons" },
        { BankItemTag.MagicWeapons, "Magic Weapons" },
        { BankItemTag.Shields, "Shields & Offhands" },
        { BankItemTag.Headgear, "Headgear" },
        { BankItemTag.BodyArmor, "Body Armor" },
        { BankItemTag.Trinkets, "Trinkets & Rings" },
        { BankItemTag.Bracelets, "Bracelets" },
        { BankItemTag.ToolsOnly, "Tools" },
        { BankItemTag.StorageBags, "Storage Bags" },
        { BankItemTag.CookedFoodAndDrink, "Food & Drink" },
        { BankItemTag.RawFoodAndFish, "Raw Fish & Ingredients" },
        { BankItemTag.OresAndBars, "Ores & Bars" },
        { BankItemTag.WoodAndPlanks, "Logs & Planks" },
        { BankItemTag.HidesAndCloth, "Hides & Cloth" },
        { BankItemTag.GemsAndCrystals, "Gems & Crystals" },
        { BankItemTag.SalvageAndScraps, "Scraps & Salvage" },
        { BankItemTag.BonesAndMonsterParts, "Bones & Monster Parts" },
        { BankItemTag.Furnishings, "Furnishings" },
        { BankItemTag.KeysAndCurrencies, "Coins & Keys" },
        { BankItemTag.QuestItems, "Quest Items" },
        { BankItemTag.Misc, "Miscellaneous" },
        { BankItemTag.Empty, "Empty" },
        { BankItemTag.Locked, "Locked" }
    };

    private static readonly HashSet<string> KnownDefaultTabNames = new(
        DefaultTagNames.Values,
        StringComparer.OrdinalIgnoreCase);

    internal static LUI_Button AutoTabsButton;

    private static readonly FieldInfo TogglePlaceholdersField =
        AccessTools.Field(typeof(UI_Bank), "toggle_placeholders");

    private static readonly FieldInfo ToggleWithdrawBanknotesField =
        AccessTools.Field(typeof(UI_Bank), "toggle_withdrawBanknotes");

    private static readonly FieldInfo ButtonPresetsField =
        AccessTools.Field(typeof(UI_Bank), "button_presets");

    private static readonly FieldInfo BankTabButtonsField =
        AccessTools.Field(typeof(UI_Bank), "bankTabButtons");

    private static readonly Action<UI_Bank> UpdateTabNamesDelegate =
        CreateMethodDelegate<Action<UI_Bank>>(typeof(UI_Bank), "UpdateTabNames");

    private static readonly Action<UI_Bank, LUI_Button, string> AddBankButtonHoverTextDelegate =
        CreateMethodDelegate<Action<UI_Bank, LUI_Button, string>>(typeof(UI_Bank), "AddBankButtonHoverText");

    private static readonly Action<UI_Bank, HashSet<int>> SetSearchTabHighlightsDelegate =
        CreateMethodDelegate<Action<UI_Bank, HashSet<int>>>(typeof(UI_Bank), "SetSearchTabHighlights");

    private static readonly Action<UI_Bank> ClearSearchTabHighlightsDelegate =
        CreateMethodDelegate<Action<UI_Bank>>(typeof(UI_Bank), "ClearSearchTabHighlights");

    private static readonly Action<LUI_DragWindow, LUI_NavItem> RegisterControllerNavItemDelegate =
        CreateMethodDelegate<Action<LUI_DragWindow, LUI_NavItem>>(typeof(LUI_DragWindow), "RegisterControllerNavItem");

    internal static T CreateMethodDelegate<T>(Type type, string methodName) where T : Delegate
    {
        try
        {
            MethodInfo method = AccessTools.Method(type, methodName);
            return method != null ? AccessTools.MethodDelegate<T>(method) : null;
        }
        catch
        {
            return null;
        }
    }

    private bool _updatingConfigName;
    private readonly Harmony _harmony = new(MyPluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Logger = base.Logger;

        Enabled = Config.Bind(
            "General",
            "Enabled",
            true,
            "Enable the Auto Tabs button in the bank window.");

        ShowConfirmation = Config.Bind(
            "General",
            "ShowConfirmation",
            true,
            "Show a confirmation dialog before renaming bank tabs and moving items.");

        AutoRouteDeposits = Config.Bind(
            "General",
            "AutoRouteDeposits",
            true,
            "When enabled, depositing new items into a bank that has used Auto Tabs automatically places them into their appropriate category tab.");

        SortMode = Config.Bind(
            "General",
            "SortMode",
            BankTabSortMode.SubCategoryAndName,
            "How items should be sorted within each bank tab after being moved.");

        SortDescending = Config.Bind(
            "General",
            "SortDescending",
            false,
            "When true, reverses the primary sort direction (e.g. highest level/value first).");

        ButtonLabel = Config.Bind(
            "General",
            "ButtonLabel",
            "Auto Tabs",
            "Text displayed on the Auto Bank Tabs button in the bank window.");

        for (int i = 0; i < TotalBankTabs; i++)
        {
            int tabNumber = i + 1;
            BankItemTag defaultTag = DefaultTabTags[i];
            string defaultName = GetDefaultTabName(defaultTag);
            string section = $"Tabs";

            ConfigEntry<BankItemTag> tagEntry = Config.Bind(
                section,
                $"Tab{tabNumber}Tag",
                defaultTag,
                $"Item category/tag assigned to Bank Tab {tabNumber}.");

            ConfigEntry<string> nameEntry = Config.Bind(
                section,
                $"Tab{tabNumber}Name",
                defaultName,
                $"Custom name for Bank Tab {tabNumber}. Leave empty or at the tag's default name to automatically use the selected tag's name.");

            int capturedIndex = i;
            tagEntry.SettingChanged += (_, _) => OnTabTagChanged(capturedIndex);

            TabTags[i] = tagEntry;
            TabNames[i] = nameEntry;
        }

        ButtonLabel.SettingChanged += (_, _) =>
        {
            if (AutoTabsButton?.text != null)
            {
                string label = string.IsNullOrWhiteSpace(ButtonLabel.Value) ? "Auto Tabs" : ButtonLabel.Value.Trim();
                AutoTabsButton.text.SetText(label);
            }
        };

        Enabled.SettingChanged += (_, _) =>
        {
            if (AutoTabsButton != null)
            {
                AutoTabsButton.SetActive(Enabled.Value);
            }
        };

        _harmony.PatchAll(typeof(BankPatches));
        Logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME} v{MyPluginInfo.PLUGIN_VERSION} initialized.");
    }

    private void OnTabTagChanged(int tabIndex)
    {
        if (_updatingConfigName || tabIndex < 0 || tabIndex >= TotalBankTabs)
        {
            return;
        }

        ConfigEntry<BankItemTag> tagEntry = TabTags[tabIndex];
        ConfigEntry<string> nameEntry = TabNames[tabIndex];
        if (tagEntry == null || nameEntry == null)
        {
            return;
        }

        string currentName = nameEntry.Value?.Trim() ?? "";
        if (string.IsNullOrEmpty(currentName) || KnownDefaultTabNames.Contains(currentName))
        {
            _updatingConfigName = true;
            try
            {
                nameEntry.Value = GetDefaultTabName(tagEntry.Value);
            }
            finally
            {
                _updatingConfigName = false;
            }
        }
    }

    public static string GetDefaultTabName(BankItemTag tag)
    {
        return DefaultTagNames.TryGetValue(tag, out string name) ? name : tag.ToString();
    }

    public static string GetConfiguredTabName(int tabIndex)
    {
        if (tabIndex < 0 || tabIndex >= TotalBankTabs)
        {
            return $"Tab {tabIndex + 1}";
        }

        BankItemTag tag = TabTags[tabIndex]?.Value ?? DefaultTabTags[tabIndex];
        string rawName = TabNames[tabIndex]?.Value?.Trim() ?? "";

        if (string.IsNullOrEmpty(rawName))
        {
            return GetDefaultTabName(tag);
        }

        // If the user changed the tag in config without changing the old default tab name, use the new tag's default name
        BankItemTag defaultTagForSlot = DefaultTabTags[tabIndex];
        if (tag != defaultTagForSlot &&
            string.Equals(rawName, GetDefaultTabName(defaultTagForSlot), StringComparison.OrdinalIgnoreCase))
        {
            return GetDefaultTabName(tag);
        }

        return rawName.Length > 70 ? rawName.Substring(0, 70) : rawName;
    }

    public static void OnAutoBankTabsClicked(object[] _)
    {
        if (UI_Bank.instance == null || !UI_Bank.instance.active)
        {
            return;
        }

        if (ShowConfirmation != null && ShowConfirmation.Value && UI_Notifications.instance != null)
        {
            UI_Notifications.instance.ShowTwoButtonsConfirmation(
                "Automatically rename bank tabs and move all banked items into their configured tabs?",
                "Organize",
                "Cancel",
                ConfirmAutoBankTabs,
                null);
        }
        else
        {
            ConfirmAutoBankTabs(null);
        }
    }

    private static void ConfirmAutoBankTabs(object[] _)
    {
        try
        {
            OrganizeBankTabs(UI_Bank.instance);
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to auto-organize bank tabs: {ex}");
            UI_Notifications.instance?.ShowDefaultNotification("Failed to organize bank tabs!", -1f);
        }
    }

    public static void OrganizeBankTabs(UI_Bank bank)
    {
        if (bank == null || bank.itemSlots == null || bank.itemSlots.Count == 0)
        {
            return;
        }

        bank.TryCancelControllerMoveItem();
        BankItemClassifier.RebuildRecipeIndex();

        // 1. Rename all non-locked tabs according to configuration and mark bank as using Auto Tabs
        if (Variables.instance != null)
        {
            Variables.instance.SetVarInt(AutoBankTabsUsedVar, 1);

            for (int i = 0; i < TotalBankTabs; i++)
            {
                BankItemTag tag = TabTags[i]?.Value ?? DefaultTabTags[i];
                if (tag == BankItemTag.Locked)
                {
                    continue;
                }

                int tabId = i + 1;
                string newName = GetConfiguredTabName(i);
                Variables.instance.SetVarString("BankTabName" + tabId, newName);
            }

            UpdateTabNamesDelegate?.Invoke(bank);
        }

        // 2. Identify locked tabs vs active organizable tabs
        HashSet<int> lockedTabIds = new();
        for (int i = 0; i < TotalBankTabs; i++)
        {
            if ((TabTags[i]?.Value ?? DefaultTabTags[i]) == BankItemTag.Locked)
            {
                lockedTabIds.Add(i + 1);
            }
        }

        // 3. Collect all occupied slots from non-locked tabs, merging duplicate itemIds safely
        List<BankItemEntry> collectedEntries = new();
        Dictionary<int, BankItemEntry> entryByItemId = new();

        for (int i = 0; i < bank.itemSlots.Count; i++)
        {
            UI_Bank.ItemSlot slot = bank.itemSlots[i];
            if (slot == null || slot.itemId == 0 || lockedTabIds.Contains(slot.tabId))
            {
                continue;
            }

            int itemId = slot.itemId;
            int amount = Mathf.Max(0, slot.itemAmount);

            if (entryByItemId.TryGetValue(itemId, out BankItemEntry existing))
            {
                // Merge amounts if both or either had real stack; if both were placeholders (0), stays 0
                if (amount > 0 && existing.Amount > 0)
                {
                    long sum = (long)existing.Amount + amount;
                    existing.Amount = sum > ItemHandler.MaxStack ? ItemHandler.MaxStack : (int)sum;
                }
                else if (amount > 0)
                {
                    existing.Amount = amount;
                }
            }
            else
            {
                ItemHandler.Item resolvedItem = BankItemClassifier.ResolveItem(itemId);
                BankItemEntry entry = new()
                {
                    ItemId = itemId,
                    BaseItemId = BankItemClassifier.GetBaseItemId(itemId),
                    Amount = amount,
                    OriginalOrder = collectedEntries.Count,
                    OriginalTabId = slot.tabId,
                    Item = resolvedItem,
                    ItemName = BankItemClassifier.GetCleanItemName(resolvedItem, itemId),
                    SubCategoryOrder = BankItemClassifier.GetSubCategorySortOrder(resolvedItem, itemId)
                };
                entryByItemId[itemId] = entry;
                collectedEntries.Add(entry);
            }
        }

        // 4. Prepare per-tab item lists (tabs 0..9)
        List<BankItemEntry>[] tabBuckets = new List<BankItemEntry>[TotalBankTabs];
        for (int i = 0; i < TotalBankTabs; i++)
        {
            tabBuckets[i] = new List<BankItemEntry>(SlotsPerTab);
        }

        // Build candidate tab order sorted by tag specificity (most specific tags checked first, broad/fallback tags later)
        List<int> activeTabIndices = new(TotalBankTabs);
        for (int i = 0; i < TotalBankTabs; i++)
        {
            BankItemTag tag = TabTags[i]?.Value ?? DefaultTabTags[i];
            if (tag != BankItemTag.Locked && tag != BankItemTag.Empty)
            {
                activeTabIndices.Add(i);
            }
        }

        List<int> specificityOrderedTabs = activeTabIndices
            .OrderBy(idx => BankItemClassifier.GetTagSpecificityTier(TabTags[idx]?.Value ?? DefaultTabTags[idx]))
            .ThenBy(idx => idx)
            .ToList();

        // Pre-sort items by subcategory & name before assigning to tabs so if a category overflows 78 items,
        // the primary tab gets a clean, deterministic subset.
        BankTabSortMode sortMode = SortMode?.Value ?? BankTabSortMode.SubCategoryAndName;
        bool descending = SortDescending?.Value ?? false;
        if (sortMode != BankTabSortMode.PreserveOrder)
        {
            collectedEntries.Sort((a, b) => CompareEntries(a, b, sortMode, descending));
        }

        int overflowCount = 0;
        foreach (BankItemEntry entry in collectedEntries)
        {
            int targetTabIndex = FindTargetTabForEntry(entry, specificityOrderedTabs, tabBuckets, lockedTabIds, out bool wasOverflow);
            if (wasOverflow)
            {
                overflowCount++;
            }
            tabBuckets[targetTabIndex].Add(entry);
        }

        // 5. Sort items within each tab bucket
        for (int i = 0; i < TotalBankTabs; i++)
        {
            if (lockedTabIds.Contains(i + 1) || tabBuckets[i].Count <= 1)
            {
                continue;
            }

            if (sortMode != BankTabSortMode.PreserveOrder)
            {
                tabBuckets[i].Sort((a, b) => CompareEntries(a, b, sortMode, descending));
            }
        }

        // 6. Write organized items back into the bank slots for all non-locked tabs
        for (int tabIndex = 0; tabIndex < TotalBankTabs; tabIndex++)
        {
            int tabId = tabIndex + 1;
            if (lockedTabIds.Contains(tabId))
            {
                continue;
            }

            List<UI_Bank.ItemSlot> tabSlots = bank.itemSlots
                .Where(s => s != null && s.tabId == tabId)
                .OrderBy(s => s.slotId)
                .ToList();

            for (int slotIdx = 0; slotIdx < tabSlots.Count; slotIdx++)
            {
                tabSlots[slotIdx].ClearSlot();
            }

            List<BankItemEntry> bucket = tabBuckets[tabIndex];
            for (int slotIdx = 0; slotIdx < bucket.Count && slotIdx < tabSlots.Count; slotIdx++)
            {
                BankItemEntry entry = bucket[slotIdx];
                UI_Bank.ItemSlot slot = tabSlots[slotIdx];
                slot.AddItem(entry.ItemId, entry.Amount);
                slot.text_amount?.BringToFront();
                slot.button?.SortChildren();
            }
        }

        // 7. Re-apply active search filter if the user has a search query typed in
        RefreshBankSearchState(bank);

        if (bank.HasControllerFocus())
        {
            bank.FocusControllerNavigation();
        }

        AudioHandler.instance?.Play2DSound(14);
        string message = overflowCount > 0
            ? $"Organized {collectedEntries.Count} items across bank tabs ({overflowCount} overflowed to free tabs)."
            : $"Organized {collectedEntries.Count} items across bank tabs!";
        UI_Notifications.instance?.ShowDefaultNotification(message, -1f);
        Logger.LogInfo(message);
    }

    private static int FindTargetTabForEntry(
        BankItemEntry entry,
        List<int> specificityOrderedTabs,
        List<BankItemEntry>[] tabBuckets,
        HashSet<int> lockedTabIds,
        out bool wasOverflow)
    {
        wasOverflow = false;

        // 1. Check all configured tabs in specificity order for a direct tag match with free space
        foreach (int tabIndex in specificityOrderedTabs)
        {
            if (tabBuckets[tabIndex].Count >= SlotsPerTab)
            {
                continue;
            }

            BankItemTag tag = TabTags[tabIndex]?.Value ?? DefaultTabTags[tabIndex];
            if (BankItemClassifier.MatchesTag(entry.Item, entry.ItemId, tag))
            {
                return tabIndex;
            }
        }

        wasOverflow = true;

        // 2. Fallback: any non-full tab with QuestAndMisc or Misc tag
        for (int i = TotalBankTabs - 1; i >= 0; i--)
        {
            if (lockedTabIds.Contains(i + 1) || tabBuckets[i].Count >= SlotsPerTab)
            {
                continue;
            }

            BankItemTag tag = TabTags[i]?.Value ?? DefaultTabTags[i];
            if (tag == BankItemTag.QuestAndMisc || tag == BankItemTag.Misc)
            {
                return i;
            }
        }

        // 3. Second fallback: any non-locked, non-full tab (preferring later tabs first, including Empty tabs if needed)
        for (int i = TotalBankTabs - 1; i >= 0; i--)
        {
            if (!lockedTabIds.Contains(i + 1) && tabBuckets[i].Count < SlotsPerTab)
            {
                return i;
            }
        }

        return 0;
    }

    public static bool IsAutoTabsActiveForBank()
    {
        if (Variables.instance == null)
        {
            return false;
        }

        if (Variables.instance.GetVarIntValue(AutoBankTabsUsedVar) == 1)
        {
            return true;
        }

        // Also detect if Auto Tabs was already used on this save by checking if existing bank tab names match our configured/default names
        int matchingTabs = 0;
        for (int i = 0; i < TotalBankTabs; i++)
        {
            string savedTabName = Variables.instance.GetVarStringValue("BankTabName" + (i + 1))?.Trim() ?? "";
            if (string.IsNullOrEmpty(savedTabName))
            {
                continue;
            }

            string configuredName = GetConfiguredTabName(i);
            if (string.Equals(savedTabName, configuredName, StringComparison.OrdinalIgnoreCase) ||
                KnownDefaultTabNames.Contains(savedTabName))
            {
                matchingTabs++;
            }
        }

        if (matchingTabs >= 3)
        {
            Variables.instance.SetVarInt(AutoBankTabsUsedVar, 1);
            return true;
        }

        return false;
    }

    public static UI_Bank.ItemSlot FindBestEmptySlotForDepositedItem(UI_Bank bank, int bankItemId)
    {
        if (bank == null || bankItemId <= 0)
        {
            return null;
        }

        BankItemClassifier.RebuildRecipeIndex();
        ItemHandler.Item resolvedItem = BankItemClassifier.ResolveItem(bankItemId);
        if (resolvedItem == null)
        {
            return bank.FindNextEmptySlot();
        }

        // Build candidate tabs ordered by tag specificity (most specific first, then tab order 1..10)
        List<int> activeTabIndices = new(TotalBankTabs);
        HashSet<int> lockedTabIds = new();
        for (int i = 0; i < TotalBankTabs; i++)
        {
            BankItemTag tag = TabTags[i]?.Value ?? DefaultTabTags[i];
            if (tag == BankItemTag.Locked)
            {
                lockedTabIds.Add(i + 1);
            }
            else if (tag != BankItemTag.Empty)
            {
                activeTabIndices.Add(i);
            }
        }

        List<int> specificityOrderedTabs = activeTabIndices
            .OrderBy(idx => BankItemClassifier.GetTagSpecificityTier(TabTags[idx]?.Value ?? DefaultTabTags[idx]))
            .ThenBy(idx => idx)
            .ToList();

        // 1. Check all matching tabs in specificity order for an empty slot
        foreach (int tabIndex in specificityOrderedTabs)
        {
            BankItemTag tag = TabTags[tabIndex]?.Value ?? DefaultTabTags[tabIndex];
            if (BankItemClassifier.MatchesTag(resolvedItem, bankItemId, tag))
            {
                UI_Bank.ItemSlot emptySlot = bank.FindNextEmptySlot(tabIndex + 1);
                if (emptySlot != null)
                {
                    return emptySlot;
                }
            }
        }

        // 2. Fallback: any non-locked tab configured as QuestAndMisc or Misc
        for (int i = TotalBankTabs - 1; i >= 0; i--)
        {
            if (lockedTabIds.Contains(i + 1))
            {
                continue;
            }

            BankItemTag tag = TabTags[i]?.Value ?? DefaultTabTags[i];
            if (tag == BankItemTag.QuestAndMisc || tag == BankItemTag.Misc)
            {
                UI_Bank.ItemSlot fallbackSlot = bank.FindNextEmptySlot(i + 1);
                if (fallbackSlot != null)
                {
                    return fallbackSlot;
                }
            }
        }

        // 3. Second fallback: any non-locked tab with an empty slot
        for (int i = 0; i < TotalBankTabs; i++)
        {
            if (!lockedTabIds.Contains(i + 1))
            {
                UI_Bank.ItemSlot anySlot = bank.FindNextEmptySlot(i + 1);
                if (anySlot != null)
                {
                    return anySlot;
                }
            }
        }

        return bank.FindNextEmptySlot();
    }

    private static int CompareEntries(BankItemEntry a, BankItemEntry b, BankTabSortMode mode, bool descending)
    {
        int cmp = 0;
        switch (mode)
        {
            case BankTabSortMode.SubCategoryAndName:
                cmp = a.SubCategoryOrder.CompareTo(b.SubCategoryOrder);
                if (cmp != 0)
                {
                    return cmp;
                }
                cmp = string.Compare(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0)
                {
                    return descending ? -cmp : cmp;
                }
                break;

            case BankTabSortMode.SubCategoryAndLevel:
                cmp = a.SubCategoryOrder.CompareTo(b.SubCategoryOrder);
                if (cmp != 0)
                {
                    return cmp;
                }
                cmp = (a.Item?.level ?? 0).CompareTo(b.Item?.level ?? 0);
                if (cmp != 0)
                {
                    return descending ? -cmp : cmp;
                }
                break;

            case BankTabSortMode.Name:
                cmp = string.Compare(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0)
                {
                    return descending ? -cmp : cmp;
                }
                break;

            case BankTabSortMode.Level:
                cmp = (a.Item?.level ?? 0).CompareTo(b.Item?.level ?? 0);
                if (cmp != 0)
                {
                    return descending ? -cmp : cmp;
                }
                break;

            case BankTabSortMode.UnitValue:
                cmp = (a.Item?.price ?? 0).CompareTo(b.Item?.price ?? 0);
                if (cmp != 0)
                {
                    return descending ? -cmp : cmp;
                }
                break;

            case BankTabSortMode.StackValue:
                long stackA = (long)(a.Item?.price ?? 0) * Mathf.Max(1, a.Amount);
                long stackB = (long)(b.Item?.price ?? 0) * Mathf.Max(1, b.Amount);
                cmp = stackA.CompareTo(stackB);
                if (cmp != 0)
                {
                    return descending ? -cmp : cmp;
                }
                break;

            case BankTabSortMode.Amount:
                cmp = a.Amount.CompareTo(b.Amount);
                if (cmp != 0)
                {
                    return descending ? -cmp : cmp;
                }
                break;

            case BankTabSortMode.PreserveOrder:
                return a.OriginalOrder.CompareTo(b.OriginalOrder);
        }

        int nameTie = string.Compare(a.ItemName, b.ItemName, StringComparison.OrdinalIgnoreCase);
        if (nameTie != 0)
        {
            return nameTie;
        }

        int idTie = a.ItemId.CompareTo(b.ItemId);
        if (idTie != 0)
        {
            return idTie;
        }

        return a.OriginalOrder.CompareTo(b.OriginalOrder);
    }

    private static void RefreshBankSearchState(UI_Bank bank)
    {
        if (bank == null)
        {
            return;
        }

        string keyword = bank.input_search != null ? bank.input_search.GetText()?.Trim().ToLowerInvariant() ?? "" : "";
        if (!string.IsNullOrEmpty(keyword))
        {
            bank.currentKeyword = keyword;
            int matches = 0;
            HashSet<int> matchedTabs = new();

            foreach (UI_Bank.ItemSlot slot in bank.itemSlots)
            {
                if (slot == null)
                {
                    continue;
                }

                ItemHandler.Item item = slot.GetItem();
                if (item != null && item.typeId != 0 && item.itemName != null && item.itemName.ToLowerInvariant().Contains(keyword))
                {
                    slot.ApplyVisualAlpha(1f);
                    matchedTabs.Add(slot.tabId);
                    matches++;
                }
                else
                {
                    slot.ApplyVisualAlpha(0.1f);
                }
            }

            SetSearchTabHighlightsDelegate?.Invoke(bank, matchedTabs);
            bank.text_search?.SetText($"Search ({matches})");
        }
        else
        {
            bank.currentKeyword = "";
            bank.text_search?.SetText("Search");
            foreach (UI_Bank.ItemSlot slot in bank.itemSlots)
            {
                slot?.ApplyVisualAlpha(1f);
            }
            ClearSearchTabHighlightsDelegate?.Invoke(bank);
        }
    }

    internal sealed class BankItemEntry
    {
        public int ItemId;
        public int BaseItemId;
        public int Amount;
        public int OriginalOrder;
        public int OriginalTabId;
        public ItemHandler.Item Item;
        public string ItemName;
        public int SubCategoryOrder;
    }

    internal static class BankPatches
    {
        [HarmonyPatch(typeof(UI_Bank), nameof(UI_Bank.AddItem), new[] { typeof(int), typeof(int), typeof(int) })]
        [HarmonyPrefix]
        private static bool UI_Bank_AddItem_Prefix(UI_Bank __instance, int i, int itemAmount, int slotId)
        {
            try
            {
                if (__instance == null || slotId != -1)
                {
                    return true;
                }

                if (!(Enabled?.Value ?? true) || !(AutoRouteDeposits?.Value ?? true))
                {
                    return true;
                }

                if (SaveHandler.instance == null || !SaveHandler.instance.loaded)
                {
                    return true;
                }

                if (!IsAutoTabsActiveForBank())
                {
                    return true;
                }

                int bankItemId = i > 1000000 ? i - 1000000 : i;
                if (ItemHandler.instance == null || ItemHandler.instance.GetItem(bankItemId) == null)
                {
                    return false;
                }

                // If the item (or its placeholder) already exists in the bank, let vanilla AddItem add to that existing slot
                UI_Bank.ItemSlot existingSlot = __instance.FindSlotByItemId(bankItemId);
                if (existingSlot != null)
                {
                    return true;
                }

                if (UI_CharacterCreation.instance == null || !UI_CharacterCreation.instance.active)
                {
                    AudioHandler.instance?.Play2DSound(3);
                }

                UI_Bank.ItemSlot targetSlot = FindBestEmptySlotForDepositedItem(__instance, bankItemId);
                if (targetSlot != null)
                {
                    targetSlot.AddItem(bankItemId, itemAmount);
                    if (!string.IsNullOrEmpty(__instance.currentKeyword))
                    {
                        RefreshBankSearchState(__instance);
                    }
                }
                else
                {
                    UI_Notifications.instance?.ShowDefaultNotification("Not enough space!", -1f);
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error routing deposited bank item to auto tab: {ex}");
                return true;
            }
        }

        [HarmonyPatch(typeof(UI_Bank), nameof(UI_Bank.SetupUI))]
        [HarmonyPostfix]
        private static void UI_Bank_SetupUI_Postfix(UI_Bank __instance)
        {
            try
            {
                if (__instance == null || __instance.image_windowbackground == null)
                {
                    return;
                }

                // Shift Placeholders toggle slightly left (from x=360 to x=335) and narrow Presets button (from 120 to 80)
                // so the new "Auto Tabs" button fits cleanly between Placeholders and Presets with zero overlap.
                if (TogglePlaceholdersField?.GetValue(__instance) is LUI_ToggleButton placeholdersToggle &&
                    placeholdersToggle.rectTransform != null)
                {
                    placeholdersToggle.rectTransform.anchoredPosition = new Vector2(335f, 10f);
                }

                if (ButtonPresetsField?.GetValue(__instance) is LUI_Button presetsButton &&
                    presetsButton.rectTransform != null)
                {
                    presetsButton.rectTransform.anchoredPosition = new Vector2(-130f, 10f);
                    presetsButton.rectTransform.sizeDelta = new Vector2(80f, 35f);
                }

                string label = string.IsNullOrWhiteSpace(ButtonLabel?.Value) ? "Auto Tabs" : ButtonLabel.Value.Trim();
                AutoTabsButton = LUI_Core.instance.AddButton(
                    label,
                    Position.bottomRight,
                    Position.bottomRight,
                    new Vector2(-215f, 10f),
                    new Vector2(85f, 35f),
                    OnAutoBankTabsClicked,
                    null,
                    __instance.image_windowbackground.GetParentTransform());

                if (AutoTabsButton != null)
                {
                    if (AutoTabsButton.text?.text != null)
                    {
                        AutoTabsButton.text.text.resizeTextForBestFit = true;
                        AutoTabsButton.text.text.resizeTextMinSize = 10;
                        AutoTabsButton.text.text.resizeTextMaxSize = 14;
                    }

                    AddBankButtonHoverTextDelegate?.Invoke(
                        __instance,
                        AutoTabsButton,
                        "Automatically rename tabs and move banked items to their configured category tabs");

                    AutoTabsButton.SetActive(Enabled?.Value ?? true);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error adding Auto Tabs button to UI_Bank: {ex}");
            }
        }

        [HarmonyPatch(typeof(UI_Bank), "RefreshBankControllerNavigation")]
        [HarmonyPostfix]
        private static void UI_Bank_RefreshBankControllerNavigation_Postfix(UI_Bank __instance)
        {
            try
            {
                if (__instance == null || AutoTabsButton?.navItem == null || !(Enabled?.Value ?? true))
                {
                    return;
                }

                RegisterControllerNavItemDelegate?.Invoke(__instance, AutoTabsButton.navItem);

                LUI_ToggleButton togglePlaceholders = TogglePlaceholdersField?.GetValue(__instance) as LUI_ToggleButton;
                LUI_ToggleButton toggleBanknotes = ToggleWithdrawBanknotesField?.GetValue(__instance) as LUI_ToggleButton;
                LUI_Button presetsButton = ButtonPresetsField?.GetValue(__instance) as LUI_Button;
                List<LUI_Button> tabButtons = BankTabButtonsField?.GetValue(__instance) as List<LUI_Button>;

                LUI_NavItem upNav = tabButtons != null && tabButtons.Count > 0
                    ? tabButtons[tabButtons.Count - 1]?.navItem
                    : null;

                LUI_NavItem leftNav = togglePlaceholders?.navItem ?? toggleBanknotes?.navItem;
                LUI_NavItem rightNav = presetsButton?.navItem;

                AutoTabsButton.navItem.up = upNav;
                AutoTabsButton.navItem.left = leftNav;
                AutoTabsButton.navItem.right = rightNav;

                if (leftNav != null)
                {
                    leftNav.right = AutoTabsButton.navItem;
                }

                if (rightNav != null)
                {
                    rightNav.left = AutoTabsButton.navItem;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error wiring controller navigation for Auto Tabs button: {ex}");
            }
        }
    }
}

internal static class BankItemClassifier
{
    private static readonly Func<ItemHandler.Item, string> GetItemNameDelegate =
        Plugin.CreateMethodDelegate<Func<ItemHandler.Item, string>>(typeof(ItemHandler.Item), "GetItemName");

    private static readonly AccessTools.FieldRef<ItemHandler.Item, bool> ItemStorageAllItemsRef =
        AccessTools.FieldRefAccess<ItemHandler.Item, bool>("storageAllItems");

    private static readonly AccessTools.FieldRef<ItemHandler.Item, HarvestablesHandler.Tool> ItemToolRef =
        AccessTools.FieldRefAccess<ItemHandler.Item, HarvestablesHandler.Tool>("tool");

    private static readonly AccessTools.FieldRef<ItemHandler.Item, int> ItemFoodAmountRef =
        AccessTools.FieldRefAccess<ItemHandler.Item, int>("foodAmount");

    private static readonly HashSet<int> AlchemyIngredientIds = new();
    private static readonly HashSet<int>AlchemyProductIds = new();
    private static readonly HashSet<int> CookingIngredientIds = new();
    private static readonly HashSet<int> CookingProductIds = new();
    private static readonly HashSet<int> SmithingIngredientIds = new();
    private static readonly HashSet<int> TailoringIngredientIds = new();
    private static readonly HashSet<int> CarpentryIngredientIds = new();
    private static int _lastIndexedItemCount;

    // Known dedicated tool base IDs (matching ToolSlots & game mechanics)
    private static readonly HashSet<int> KnownToolBaseIds = new()
    {
        234,  // Pestle and Mortar
        268,  // Broom
        467,  // Broken Shovel
        482,  // Dave's Trusty Hunting Knife
        489,  // Knife
        490,  // Hammer
        491,  // Harpoon
        493,  // Fishing Spear
        494,  // Fishing Net
        673,  // Arakite Hammer
        676,  // Torch
        718,  // Needle and Thread
        791,  // Scissors
        794,  // Saw
        919,  // Ancient Torch
        931,  // Miner's Chisel
        934,  // Gutting Knife
        942,  // Splitting Axe
        961,  // Shovel
        964,  // Chitin Torch
        988,  // Shears
        1003, // Lantern of Soulsoothing
        1005, // The Priest's Lantern
        1124, // High Striker's Hammer
        2108  // Salvaging Toolkit
    };

    // Known salvage scrap & essence base IDs from SalvagingHandler
    private static readonly HashSet<int> KnownSalvageBaseIds = new()
    {
        1008, // Magical Essence / Salvage Essence
        1185, // Metal Scraps
        1194, // Cloth / Leather Scraps
        1195, // Wood Scraps
        1198  // Trinket / Jewelry Scraps
    };

    public static void RebuildRecipeIndex()
    {
        if (ItemHandler.instance?.items == null)
        {
            return;
        }

        int count = ItemHandler.instance.items.Count;
        if (count == _lastIndexedItemCount && count > 0)
        {
            return;
        }

        _lastIndexedItemCount = count;
        AlchemyIngredientIds.Clear();
        AlchemyProductIds.Clear();
        CookingIngredientIds.Clear();
        CookingProductIds.Clear();
        SmithingIngredientIds.Clear();
        TailoringIngredientIds.Clear();
        CarpentryIngredientIds.Clear();

        foreach (ItemHandler.Item item in ItemHandler.instance.items)
        {
            if (item == null || item.typeId <= 0 || item.typeId >= 100000 || item.craftingRecipe == null)
            {
                continue;
            }

            foreach (CraftingHandler.CraftingRecipe recipe in item.craftingRecipe)
            {
                if (recipe == null)
                {
                    continue;
                }

                switch (recipe.skill)
                {
                    case SkillsHandler.SkillType.alchemy:
                        AlchemyProductIds.Add(item.typeId);
                        if (recipe.items != null)
                        {
                            foreach (CraftingHandler.CraftingRecipeItem ing in recipe.items)
                            {
                                if (ing != null && ing.itemId > 0)
                                {
                                    AlchemyIngredientIds.Add(GetBaseItemId(ing.itemId));
                                }
                            }
                        }
                        break;

                    case SkillsHandler.SkillType.cooking:
                        CookingProductIds.Add(item.typeId);
                        if (recipe.items != null)
                        {
                            foreach (CraftingHandler.CraftingRecipeItem ing in recipe.items)
                            {
                                if (ing != null && ing.itemId > 0)
                                {
                                    CookingIngredientIds.Add(GetBaseItemId(ing.itemId));
                                }
                            }
                        }
                        break;

                    case SkillsHandler.SkillType.blacksmithy:
                        if (recipe.items != null)
                        {
                            foreach (CraftingHandler.CraftingRecipeItem ing in recipe.items)
                            {
                                if (ing != null && ing.itemId > 0)
                                {
                                    SmithingIngredientIds.Add(GetBaseItemId(ing.itemId));
                                }
                            }
                        }
                        break;

                    case SkillsHandler.SkillType.tailoring:
                        if (recipe.items != null)
                        {
                            foreach (CraftingHandler.CraftingRecipeItem ing in recipe.items)
                            {
                                if (ing != null && ing.itemId > 0)
                                {
                                    TailoringIngredientIds.Add(GetBaseItemId(ing.itemId));
                                }
                            }
                        }
                        break;

                    case SkillsHandler.SkillType.carpentry:
                        if (recipe.items != null)
                        {
                            foreach (CraftingHandler.CraftingRecipeItem ing in recipe.items)
                            {
                                if (ing != null && ing.itemId > 0)
                                {
                                    CarpentryIngredientIds.Add(GetBaseItemId(ing.itemId));
                                }
                            }
                        }
                        break;
                }
            }
        }
    }

    public static int GetBaseItemId(int itemId)
    {
        int id = itemId;
        if (id > 1100000)
        {
            id -= 1100000;
        }
        else if (id > 1000000)
        {
            id -= 1000000;
        }
        else if (id > 100000 && id < 200000)
        {
            id -= 100000;
        }
        return id;
    }

    public static ItemHandler.Item ResolveItem(int itemId)
    {
        if (ItemHandler.instance == null)
        {
            return null;
        }

        ItemHandler.Item item = ItemHandler.instance.GetItem(itemId);
        if (item != null)
        {
            return item;
        }

        int baseId = GetBaseItemId(itemId);
        return ItemHandler.instance.GetItem(baseId);
    }

    public static string GetCleanItemName(ItemHandler.Item item, int fallbackId)
    {
        if (item == null)
        {
            return $"Item {fallbackId}";
        }

        string name = item.itemName;
        if (string.IsNullOrEmpty(name))
        {
            try
            {
                name = GetItemNameDelegate != null ? GetItemNameDelegate(item) : null;
            }
            catch
            {
                name = $"Item {fallbackId}";
            }
        }

        return name ?? $"Item {fallbackId}";
    }

    public static int GetTagSpecificityTier(BankItemTag tag)
    {
        return tag switch
        {
            // Tier 1: Most specific sub-category tags
            BankItemTag.MeleeWeapons => 1,
            BankItemTag.RangedWeapons => 1,
            BankItemTag.MagicWeapons => 1,
            BankItemTag.Shields => 1,
            BankItemTag.Headgear => 1,
            BankItemTag.BodyArmor => 1,
            BankItemTag.Trinkets => 1,
            BankItemTag.Bracelets => 1,
            BankItemTag.StorageBags => 1,
            BankItemTag.ToolsOnly => 1,
            BankItemTag.CookedFoodAndDrink => 1,
            BankItemTag.RawFoodAndFish => 1,
            BankItemTag.OresAndBars => 1,
            BankItemTag.WoodAndPlanks => 1,
            BankItemTag.HidesAndCloth => 1,
            BankItemTag.GemsAndCrystals => 1,
            BankItemTag.SalvageAndScraps => 1,
            BankItemTag.BonesAndMonsterParts => 1,
            BankItemTag.Furnishings => 1,
            BankItemTag.KeysAndCurrencies => 1,
            BankItemTag.QuestItems => 1,

            // Tier 2: Default primary category tags
            BankItemTag.Weapons => 2,
            BankItemTag.Armor => 2,
            BankItemTag.Jewelry => 2,
            BankItemTag.ToolsAndBags => 2,
            BankItemTag.Potions => 2,
            BankItemTag.FoodAndCooking => 2,
            BankItemTag.HerbsAndAlchemy => 2,
            BankItemTag.CraftingMaterials => 2,
            BankItemTag.BooksAndScrolls => 2,

            // Tier 3: Broad umbrella tags
            BankItemTag.AllEquipment => 3,

            // Tier 4: Catch-all / fallback tags
            BankItemTag.QuestAndMisc => 4,
            BankItemTag.Misc => 5,
            _ => 6
        };
    }

    public static bool MatchesTag(ItemHandler.Item item, int rawItemId, BankItemTag tag)
    {
        if (item == null)
        {
            return tag == BankItemTag.QuestAndMisc || tag == BankItemTag.Misc;
        }

        int baseId = GetBaseItemId(rawItemId > 0 ? rawItemId : item.typeId);
        ItemHandler.Item baseItem = (item.typeId == baseId) ? item : (ResolveItem(baseId) ?? item);

        return tag switch
        {
            // --- 10 Primary Default Tags ---
            BankItemTag.Weapons => IsCombatWeaponOrShield(baseItem, baseId),
            BankItemTag.Armor => IsArmorOrHeadgear(baseItem),
            BankItemTag.Jewelry => IsJewelry(baseItem),
            BankItemTag.ToolsAndBags => IsStorageBag(baseItem, baseId) || IsTool(baseItem, baseId),
            BankItemTag.Potions => IsPotionOrAlchemyProduct(baseItem, baseId),
            BankItemTag.FoodAndCooking => IsCookedFoodOrDrink(baseItem, baseId) || IsRawFoodOrCookingIngredient(baseItem, baseId),
            BankItemTag.HerbsAndAlchemy => IsHerbOrAlchemyIngredient(baseItem, baseId),
            BankItemTag.CraftingMaterials => IsCraftingMaterial(baseItem, baseId),
            BankItemTag.BooksAndScrolls => IsBookScrollOrMagicItem(baseItem, baseId),
            BankItemTag.QuestAndMisc => IsQuestKeyCurrencyFurnishingOrMisc(baseItem, baseId),

            // --- Granular / Specialized Tags ---
            BankItemTag.AllEquipment => IsCombatWeaponOrShield(baseItem, baseId) || IsArmorOrHeadgear(baseItem) || IsJewelry(baseItem),
            BankItemTag.MeleeWeapons => IsMeleeWeapon(baseItem, baseId),
            BankItemTag.RangedWeapons => IsRangedWeapon(baseItem, baseId),
            BankItemTag.MagicWeapons => IsMagicWeapon(baseItem, baseId),
            BankItemTag.Shields => IsShield(baseItem, baseId),
            BankItemTag.Headgear => baseItem.itemType == ItemHandler.ItemType.head,
            BankItemTag.BodyArmor => baseItem.itemType == ItemHandler.ItemType.armor,
            BankItemTag.Trinkets => baseItem.itemType == ItemHandler.ItemType.trinket,
            BankItemTag.Bracelets => baseItem.itemType == ItemHandler.ItemType.bracelet,
            BankItemTag.ToolsOnly => IsTool(baseItem, baseId),
            BankItemTag.StorageBags => IsStorageBag(baseItem, baseId),
            BankItemTag.CookedFoodAndDrink => IsCookedFoodOrDrink(baseItem, baseId),
            BankItemTag.RawFoodAndFish => IsRawFoodOrCookingIngredient(baseItem, baseId),
            BankItemTag.OresAndBars => IsOreOrBar(baseItem, baseId),
            BankItemTag.WoodAndPlanks => IsWoodOrPlank(baseItem, baseId),
            BankItemTag.HidesAndCloth => IsHideLeatherOrCloth(baseItem, baseId),
            BankItemTag.GemsAndCrystals => IsGemOrCrystal(baseItem, baseId),
            BankItemTag.SalvageAndScraps => IsSalvageOrScrap(baseItem, baseId),
            BankItemTag.BonesAndMonsterParts => IsBoneOrMonsterPart(baseItem, baseId),
            BankItemTag.Furnishings => IsFurnishing(baseItem),
            BankItemTag.KeysAndCurrencies => IsKeyOrCurrency(baseItem, baseId),
            BankItemTag.QuestItems => IsQuestOrLoreItem(baseItem, baseId),
            BankItemTag.Misc => IsUnmatchedMisc(baseItem, baseId),
            _ => false
        };
    }

    public static int GetSubCategorySortOrder(ItemHandler.Item item, int rawItemId)
    {
        if (item == null)
        {
            return 999;
        }

        int baseId = GetBaseItemId(rawItemId > 0 ? rawItemId : item.typeId);
        ItemHandler.Item baseItem = (item.typeId == baseId) ? item : (ResolveItem(baseId) ?? item);

        // Equipment sub-ordering
        if (IsStorageBag(baseItem, baseId)) return 10;
        if (IsTool(baseItem, baseId)) return 20;
        if (IsMeleeWeapon(baseItem, baseId)) return 30;
        if (IsRangedWeapon(baseItem, baseId)) return 40;
        if (IsMagicWeapon(baseItem, baseId)) return 50;
        if (IsShield(baseItem, baseId)) return 60;
        if (baseItem.itemType == ItemHandler.ItemType.head) return 70;
        if (baseItem.itemType == ItemHandler.ItemType.armor) return 80;
        if (baseItem.itemType == ItemHandler.ItemType.trinket) return 90;
        if (baseItem.itemType == ItemHandler.ItemType.bracelet) return 100;

        // Consumables & Food sub-ordering
        if (IsPotionOrAlchemyProduct(baseItem, baseId)) return 110;
        if (IsCookedFoodOrDrink(baseItem, baseId)) return 120;
        if (IsRawFoodOrCookingIngredient(baseItem, baseId)) return 130;
        if (IsHerbOrAlchemyIngredient(baseItem, baseId)) return 140;

        // Crafting Materials sub-ordering
        if (IsOreOrBar(baseItem, baseId)) return 150;
        if (IsWoodOrPlank(baseItem, baseId)) return 160;
        if (IsHideLeatherOrCloth(baseItem, baseId)) return 170;
        if (IsGemOrCrystal(baseItem, baseId)) return 180;
        if (IsSalvageOrScrap(baseItem, baseId)) return 190;
        if (IsBoneOrMonsterPart(baseItem, baseId)) return 200;
        if (IsCraftingMaterial(baseItem, baseId)) return 210;

        // Books, Scrolls & Magic sub-ordering
        if (IsBookScrollOrMagicItem(baseItem, baseId)) return 220;

        // Quest, Keys, Furnishings & Misc sub-ordering
        if (IsKeyOrCurrency(baseItem, baseId)) return 230;
        if (IsQuestOrLoreItem(baseItem, baseId)) return 240;
        if (IsFurnishing(baseItem)) return 250;

        return 300;
    }

    public static bool IsStorageBag(ItemHandler.Item item, int baseId)
    {
        if (item == null)
        {
            return false;
        }

        if (item.storageSize > 0 || (ItemStorageAllItemsRef != null && ItemStorageAllItemsRef(item)))
        {
            return true;
        }

        return ItemHandler.instance != null && ItemHandler.instance.IsStorageItem(baseId);
    }

    public static bool IsTool(ItemHandler.Item item, int baseId)
    {
        if (item == null || IsStorageBag(item, baseId))
        {
            return false;
        }

        // Support ToolSlots custom ItemType values (100..114)
        int itemTypeInt = (int)item.itemType;
        if (itemTypeInt >= 100 && itemTypeInt <= 120)
        {
            return true;
        }

        if (KnownToolBaseIds.Contains(baseId))
        {
            return true;
        }

        if (ItemToolRef != null && ItemToolRef(item) != HarvestablesHandler.Tool.none)
        {
            return true;
        }

        if (item.itemCategory != null)
        {
            if (item.itemCategory.Contains(ItemHandler.ItemCategory.hatchet) ||
                item.itemCategory.Contains(ItemHandler.ItemCategory.pickaxe) ||
                item.itemCategory.Contains(ItemHandler.ItemCategory.fishingrod) ||
                item.itemCategory.Contains(ItemHandler.ItemCategory.shears) ||
                item.itemCategory.Contains(ItemHandler.ItemCategory.hammer) ||
                item.itemCategory.Contains(ItemHandler.ItemCategory.blacksmithhammer) ||
                item.itemCategory.Contains(ItemHandler.ItemCategory.torch))
            {
                return true;
            }
        }

        string name = GetLowerName(item);
        if (name.Contains("pickaxe") ||
            name.Contains("hatchet") ||
            name.Contains("fishing rod") ||
            name.Contains("fishing net") ||
            name.Contains("harpoon") ||
            name.Contains("lobster cage") ||
            name.Contains("tinderbox") ||
            name.Contains("pestle and mortar") ||
            name.Contains("needle and thread") ||
            name.Contains("salvaging toolkit") ||
            name.Contains("watering can") ||
            name.Contains("lockpick") ||
            name.Contains("crowbar") ||
            name.Contains("secateurs"))
        {
            return true;
        }

        return false;
    }

    public static bool IsCombatWeaponOrShield(ItemHandler.Item item, int baseId)
    {
        if (item == null || IsTool(item, baseId) || IsStorageBag(item, baseId))
        {
            return false;
        }

        return item.itemType == ItemHandler.ItemType.weapon || item.itemType == ItemHandler.ItemType.shield;
    }

    public static bool IsShield(ItemHandler.Item item, int baseId)
    {
        if (item == null || IsTool(item, baseId) || IsStorageBag(item, baseId))
        {
            return false;
        }

        return item.itemType == ItemHandler.ItemType.shield ||
               (item.itemCategory != null && item.itemCategory.Contains(ItemHandler.ItemCategory.shield));
    }

    public static bool IsRangedWeapon(ItemHandler.Item item, int baseId)
    {
        if (!IsCombatWeaponOrShield(item, baseId) || IsShield(item, baseId))
        {
            return false;
        }

        if (item.itemCategory != null &&
            (item.itemCategory.Contains(ItemHandler.ItemCategory.bow) ||
             item.itemCategory.Contains(ItemHandler.ItemCategory.crossbow)))
        {
            return true;
        }

        if (item.skill == SkillsHandler.SkillType.archery)
        {
            return true;
        }

        string name = GetLowerName(item);
        return name.Contains("bow") || name.Contains("crossbow") || name.Contains("longbow") || name.Contains("shortbow");
    }

    public static bool IsMagicWeapon(ItemHandler.Item item, int baseId)
    {
        if (!IsCombatWeaponOrShield(item, baseId) || IsShield(item, baseId) || IsRangedWeapon(item, baseId))
        {
            return false;
        }

        if (item.itemCategory != null &&
            (item.itemCategory.Contains(ItemHandler.ItemCategory.staff) ||
             item.itemCategory.Contains(ItemHandler.ItemCategory.wand)))
        {
            return true;
        }

        if (item.skill == SkillsHandler.SkillType.magic)
        {
            return true;
        }

        string name = GetLowerName(item);
        return name.Contains("staff") || name.Contains("wand") || name.Contains("scepter") || name.Contains("sceptre");
    }

    public static bool IsMeleeWeapon(ItemHandler.Item item, int baseId)
    {
        if (!IsCombatWeaponOrShield(item, baseId) || IsShield(item, baseId))
        {
            return false;
        }

        return !IsRangedWeapon(item, baseId) && !IsMagicWeapon(item, baseId);
    }

    public static bool IsArmorOrHeadgear(ItemHandler.Item item)
    {
        if (item == null)
        {
            return false;
        }

        return item.itemType == ItemHandler.ItemType.head || item.itemType == ItemHandler.ItemType.armor;
    }

    public static bool IsJewelry(ItemHandler.Item item)
    {
        if (item == null)
        {
            return false;
        }

        return item.itemType == ItemHandler.ItemType.trinket || item.itemType == ItemHandler.ItemType.bracelet;
    }

    public static bool IsBookScrollOrMagicItem(ItemHandler.Item item, int baseId)
    {
        if (item == null || item.IsEquippable() || IsTool(item, baseId) || IsStorageBag(item, baseId))
        {
            return false;
        }

        if (item.bookId != 0 ||
            item.learnAbilityId != 0 ||
            item.abilityId == 87 ||
            item.abilityId == 124 ||
            (item.permanentStatBoosts != null && item.permanentStatBoosts.Count > 0))
        {
            return true;
        }

        string name = GetLowerName(item);
        string desc = GetLowerDescription(item);

        // Exclude quest letters/notes/journals unless they teach abilities or are magical scrolls/tomes/spellbooks
        if (name.Contains("spellbook") ||
            name.Contains("grimoire") ||
            name.Contains("tome") ||
            name.Contains("scroll") ||
            name.Contains("tablet") ||
            name.Contains("rune") ||
            name.Contains("codex") ||
            name.Contains("manual") ||
            name.Contains("guidebook") ||
            name.Contains("skill book") ||
            name.Contains("book of ") ||
            name.Contains("recall crystal") ||
            name.Contains("replenishment crystal") ||
            name.Contains("teleport"))
        {
            return true;
        }

        if (desc.Contains("teaches the") ||
            desc.Contains("learn the") ||
            desc.Contains("spellbook") ||
            desc.Contains("teleport to"))
        {
            return true;
        }

        return false;
    }

    public static bool IsPotionOrAlchemyProduct(ItemHandler.Item item, int baseId)
    {
        if (item == null || item.IsEquippable() || IsTool(item, baseId) || IsStorageBag(item, baseId) || IsBookScrollOrMagicItem(item, baseId))
        {
            return false;
        }

        if (AlchemyProductIds.Contains(baseId))
        {
            return true;
        }

        if (item.craftingRecipe != null)
        {
            foreach (CraftingHandler.CraftingRecipe recipe in item.craftingRecipe)
            {
                if (recipe != null && recipe.skill == SkillsHandler.SkillType.alchemy)
                {
                    return true;
                }
            }
        }

        string name = GetLowerName(item);
        if (name.Contains("potion") ||
            name.Contains("elixir") ||
            name.Contains("tonic") ||
            name.Contains("draught") ||
            name.Contains("ampoule") ||
            name.Contains("antipoison") ||
            name.Contains("antivenom") ||
            name.Contains("weapon poison") ||
            name.Contains("poison") ||
            name.Contains("vial") ||
            name.Contains("flask") ||
            name.Contains("bomb") ||
            name.Contains("decoction") ||
            name.Contains("philter") ||
            name.Contains("philtre"))
        {
            // Exclude monster glands/parts that happen to have poison in the name if they are ingredients
            if (!name.Contains("gland") && !name.Contains("sac") && !name.Contains("fang") && !name.Contains("stinger") && !name.Contains("ivy") && !name.Contains("mushroom") && !name.Contains("shroom") && !name.Contains("berry") && !name.Contains("flower") && !name.Contains("leaf") && !name.Contains("herb"))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsCookedFoodOrDrink(ItemHandler.Item item, int baseId)
    {
        if (item == null || item.IsEquippable() || IsTool(item, baseId) || IsStorageBag(item, baseId) ||
            IsPotionOrAlchemyProduct(item, baseId) || IsBookScrollOrMagicItem(item, baseId))
        {
            return false;
        }

        string name = GetLowerName(item);
        if (name.StartsWith("raw ") || name.Contains(" raw "))
        {
            return false;
        }

        if (item.abilityId == 1 || (ItemFoodAmountRef != null && ItemFoodAmountRef(item) > 0) || CookingProductIds.Contains(baseId))
        {
            return true;
        }

        if (item.craftingRecipe != null)
        {
            foreach (CraftingHandler.CraftingRecipe recipe in item.craftingRecipe)
            {
                if (recipe != null && recipe.skill == SkillsHandler.SkillType.cooking)
                {
                    return true;
                }
            }
        }

        if (name.Contains("cooked ") ||
            name.Contains("roasted ") ||
            name.Contains("baked ") ||
            name.Contains("fried ") ||
            name.Contains("grilled ") ||
            name.Contains("stew") ||
            name.Contains("soup") ||
            name.Contains("pie") ||
            name.Contains("cake") ||
            name.Contains("bread") ||
            name.Contains("sandwich") ||
            name.Contains("cheese") ||
            name.Contains("beer") ||
            name.Contains("ale") ||
            name.Contains("wine") ||
            name.Contains("whiskey") ||
            name.Contains("mead") ||
            name.Contains("cider") ||
            name.Contains("rum") ||
            name.Contains("grog"))
        {
            return true;
        }

        return false;
    }

    public static bool IsRawFoodOrCookingIngredient(ItemHandler.Item item, int baseId)
    {
        if (item == null || item.IsEquippable() || IsTool(item, baseId) || IsStorageBag(item, baseId) ||
            IsPotionOrAlchemyProduct(item, baseId) || IsCookedFoodOrDrink(item, baseId) || IsBookScrollOrMagicItem(item, baseId))
        {
            return false;
        }

        if (item.itemCategory != null && item.itemCategory.Contains(ItemHandler.ItemCategory.fish))
        {
            return true;
        }

        if (item.foodLevel > 0)
        {
            return true;
        }

        string name = GetLowerName(item);
        if (name.StartsWith("raw ") ||
            name.Contains("raw fish") ||
            name.Contains("raw meat") ||
            name.Contains("crab meat") ||
            name.Contains("snail meat") ||
            name.Contains("flour") ||
            name.Contains("dough") ||
            name.Contains("pastry") ||
            name.Contains("butter") ||
            name.Contains("sugar") ||
            name.Contains("salt") ||
            name.Contains("spice") ||
            name.Contains("garlic") ||
            name.Contains("onion") ||
            name.Contains("potato") ||
            name.Contains("cabbage") ||
            name.Contains("carrot") ||
            name.Contains("tomato") ||
            name.Contains("pumpkin") ||
            name.Contains("wheat") ||
            name.Contains("barley") ||
            name.Contains("grain") ||
            name.Contains("hops") ||
            name.Contains("apple") ||
            name.Contains("banana") ||
            name.Contains("orange") ||
            name.Contains("lemon") ||
            name.Contains("strawberry") ||
            name.Contains("grapes") ||
            name.Contains("milk") ||
            name.Contains("cream") ||
            name.Contains("honey") ||
            name.Contains("cooking ") ||
            name.Contains("fish bait") ||
            name.Contains("fishing bait") ||
            name.Contains("worm"))
        {
            return true;
        }

        if (CookingIngredientIds.Contains(baseId) && !AlchemyIngredientIds.Contains(baseId))
        {
            return true;
        }

        return false;
    }

    public static bool IsHerbOrAlchemyIngredient(ItemHandler.Item item, int baseId)
    {
        if (item == null || item.IsEquippable() || IsTool(item, baseId) || IsStorageBag(item, baseId) ||
            IsPotionOrAlchemyProduct(item, baseId) || IsCookedFoodOrDrink(item, baseId) ||
            IsBookScrollOrMagicItem(item, baseId) || IsFurnishing(item))
        {
            return false;
        }

        string name = GetLowerName(item);
        string desc = GetLowerDescription(item);

        if (name.Contains("herb") ||
            name.Contains("leaf") ||
            name.Contains("leaves") ||
            name.Contains("flower") ||
            name.Contains("petal") ||
            name.Contains("blossom") ||
            name.Contains("lily") ||
            name.Contains("lotus") ||
            name.Contains("rose") ||
            name.Contains("orchid") ||
            name.Contains("thistle") ||
            name.Contains("nettle") ||
            name.Contains("weed") ||
            name.Contains("wort") ||
            name.Contains("moss") ||
            name.Contains("lichen") ||
            name.Contains("mushroom") ||
            name.Contains("shroom") ||
            name.Contains("toadstool") ||
            name.Contains("fungus") ||
            name.Contains("fungi") ||
            name.Contains("spore") ||
            name.Contains("root") ||
            name.Contains("bulb") ||
            name.Contains("berry") ||
            name.Contains("berries") ||
            name.Contains("nightshade") ||
            name.Contains("mandrake") ||
            name.Contains("ginseng") ||
            name.Contains("aloe") ||
            name.Contains("sage") ||
            name.Contains("thyme") ||
            name.Contains("mint") ||
            name.Contains("lavender") ||
            name.Contains("chamomile") ||
            name.Contains("valerian") ||
            name.Contains("belladonna") ||
            name.Contains("hemlock") ||
            name.Contains("wolfsbane") ||
            name.Contains("bloodgrass") ||
            name.Contains("silverleaf") ||
            name.Contains("peacebloom") ||
            name.Contains("earthroot") ||
            name.Contains("mageroyal") ||
            name.Contains("briarthorn") ||
            name.Contains("stranglekelp") ||
            name.Contains("bruiseweed") ||
            name.Contains("grave moss") ||
            name.Contains("kingsblood") ||
            name.Contains("liferoot") ||
            name.Contains("fadeleaf") ||
            name.Contains("goldthorn") ||
            name.Contains("khadgar") ||
            name.Contains("wintersbite") ||
            name.Contains("firebloom") ||
            name.Contains("purple lotus") ||
            name.Contains("arthas") ||
            name.Contains("sungrass") ||
            name.Contains("blindweed") ||
            name.Contains("ghost mushroom") ||
            name.Contains("gromsblood") ||
            name.Contains("golden sansam") ||
            name.Contains("dreamfoil") ||
            name.Contains("mountain silversage") ||
            name.Contains("plaguebloom") ||
            name.Contains("icecap") ||
            name.Contains("black lotus") ||
            name.Contains("bone dust") ||
            name.Contains("alchemical") ||
            name.Contains("alchemy"))
        {
            return true;
        }

        if (desc.Contains("alchemy") || desc.Contains("brew") || desc.Contains("potion ingredient") || desc.Contains("herbal"))
        {
            return true;
        }

        // If the item is used in an Alchemy recipe and is not an ore/bar/log/plank/cloth/gem/coin
        if (AlchemyIngredientIds.Contains(baseId) &&
            !IsOreOrBar(item, baseId) &&
            !IsWoodOrPlank(item, baseId) &&
            !IsHideLeatherOrCloth(item, baseId) &&
            !IsKeyOrCurrency(item, baseId))
        {
            return true;
        }

        return false;
    }

    public static bool IsOreOrBar(ItemHandler.Item item, int baseId)
    {
        if (item == null || item.IsEquippable() || IsTool(item, baseId) || IsStorageBag(item, baseId))
        {
            return false;
        }

        string name = GetLowerName(item);
        if (name.EndsWith(" ore") ||
            name.StartsWith("ore ") ||
            name.Contains("iron ore") ||
            name.Contains("copper ore") ||
            name.Contains("tin ore") ||
            name.Contains("silver ore") ||
            name.Contains("gold ore") ||
            name.Contains("mithril ore") ||
            name.Contains("adamant") && name.Contains("ore") ||
            name.Contains("runite ore") ||
            name.Contains("orichalcum ore") ||
            name.Contains("arakite ore") ||
            name.Contains("platinum ore") ||
            name.Contains("cobalt ore") ||
            name.Contains("titanium ore") ||
            name == "coal" ||
            name.EndsWith(" coal") ||
            name == "clay" ||
            name.EndsWith(" clay") ||
            name.EndsWith(" bar") ||
            name.EndsWith(" bars") ||
            name.EndsWith(" ingot") ||
            name.EndsWith(" ingots") ||
            name.Contains("bronze bar") ||
            name.Contains("iron bar") ||
            name.Contains("steel bar") ||
            name.Contains("silver bar") ||
            name.Contains("gold bar") ||
            name.Contains("mithril bar") ||
            name.Contains("adamant bar") ||
            name.Contains("runite bar") ||
            name.Contains("arakite bar") ||
            name.Contains("orichalcum bar") ||
            name.Contains("molten glass") ||
            name.Contains("sandstone") ||
            name.Contains("limestone") ||
            name.Contains("sulfur") ||
            name.Contains("sulphur") ||
            name.Contains("saltpeter") ||
            name.Contains("charcoal"))
        {
            return true;
        }

        return false;
    }

    public static bool IsWoodOrPlank(ItemHandler.Item item, int baseId)
    {
        if (item == null || item.IsEquippable() || IsTool(item, baseId) || IsStorageBag(item, baseId) || IsFurnishing(item))
        {
            return false;
        }

        string name = GetLowerName(item);
        if (name == "logs" ||
            name.EndsWith(" logs") ||
            name.EndsWith(" log") ||
            name.EndsWith(" plank") ||
            name.EndsWith(" planks") ||
            name.Contains("wooden plank") ||
            name.Contains("oak plank") ||
            name.Contains("willow plank") ||
            name.Contains("maple plank") ||
            name.Contains("yew plank") ||
            name.Contains("magic plank") ||
            name.Contains("mahogany plank") ||
            name.Contains("teak plank") ||
            name.Contains("pine") && (name.Contains("log") || name.Contains("plank")) ||
            name.Contains("birch") && (name.Contains("log") || name.Contains("plank")) ||
            name.Contains("ash") && (name.Contains("log") || name.Contains("plank")) ||
            name.Contains("firewood") ||
            name.Contains("timber") ||
            name.Contains("lumber") ||
            name.Contains("wood pole") ||
            name.Contains("wooden pole") ||
            name.Contains("bowstring") ||
            name.Contains("bow string") ||
            name.Contains("arrow shaft") ||
            name.Contains("javelin shaft") ||
            name.Contains("wooden dowel") ||
            name.Contains("sawdust") ||
            name.Contains("tree bark"))
        {
            return true;
        }

        return false;
    }

    public static bool IsHideLeatherOrCloth(ItemHandler.Item item, int baseId)
    {
        if (item == null || item.IsEquippable() || IsTool(item, baseId) || IsStorageBag(item, baseId) || IsFurnishing(item))
        {
            return false;
        }

        string name = GetLowerName(item);
        if (name.Contains("hide") ||
            name.Contains("pelt") ||
            name.Contains("leather") ||
            name.Contains("suede") ||
            name.Contains("fur") && !name.Contains("furni") && !name.Contains("furnace") ||
            name.Contains("fleece") ||
            name.Contains("wool") ||
            name.Contains("cotton") ||
            name.Contains("flax") ||
            name.Contains("linen") ||
            name.Contains("cloth") ||
            name.Contains("fabric") ||
            name.Contains("silk") ||
            name.Contains("velvet") ||
            name.Contains("thread") ||
            name.Contains("yarn") ||
            name.Contains("string") ||
            name.Contains("sinew") ||
            name.Contains("tendon") ||
            name.Contains("rope") ||
            name.Contains("twine") ||
            name.Contains("fiber") ||
            name.Contains("fibre") ||
            name.Contains("canvas") ||
            name.Contains("padding") ||
            name.Contains("strap"))
        {
            return true;
        }

        return false;
    }

    public static bool IsGemOrCrystal(ItemHandler.Item item, int baseId)
    {
        if (item == null || item.IsEquippable() || IsTool(item, baseId) || IsStorageBag(item, baseId) ||
            IsBookScrollOrMagicItem(item, baseId) || IsFurnishing(item))
        {
            return false;
        }

        string name = GetLowerName(item);
        if (name.Contains("sapphire") ||
            name.Contains("emerald") ||
            name.Contains("ruby") ||
            name.Contains("diamond") ||
            name.Contains("dragonstone") ||
            name.Contains("onyx") ||
            name.Contains("ZENYTE".ToLowerInvariant()) ||
            name.Contains("amethyst") ||
            name.Contains("topaz") ||
            name.Contains("opal") ||
            name.Contains("jade") ||
            name.Contains("garnet") ||
            name.Contains("aquamarine") ||
            name.Contains("peridot") ||
            name.Contains("citrine") ||
            name.Contains("quartz") ||
            name.Contains("amber") ||
            name.Contains("pearl") ||
            name.Contains("gem") ||
            name.Contains("jewel") ||
            name.Contains("crystal") ||
            name.Contains("shard"))
        {
            return true;
        }

        return false;
    }

    public static bool IsSalvageOrScrap(ItemHandler.Item item, int baseId)
    {
        if (item == null || item.IsEquippable() || IsTool(item, baseId) || IsStorageBag(item, baseId))
        {
            return false;
        }

        if (KnownSalvageBaseIds.Contains(baseId))
        {
            return true;
        }

        string name = GetLowerName(item);
        if (name.Contains("scrap") ||
            name.Contains("salvage") ||
            name.Contains("shred") ||
            name.Contains("remnant") ||
            name.Contains("fragment") ||
            name.Contains("debris") ||
            name.Contains("essence") ||
            name.Contains("dust") && !IsHerbOrAlchemyIngredient(item, baseId))
        {
            return true;
        }

        return false;
    }

    public static bool IsBoneOrMonsterPart(ItemHandler.Item item, int baseId)
    {
        if (item == null || item.IsEquippable() || IsTool(item, baseId) || IsStorageBag(item, baseId) ||
            IsCookedFoodOrDrink(item, baseId) || IsRawFoodOrCookingIngredient(item, baseId))
        {
            return false;
        }

        if (item.itemCategory != null && item.itemCategory.Contains(ItemHandler.ItemCategory.bone))
        {
            return true;
        }

        string name = GetLowerName(item);
        if (name.Contains("bone") ||
            name.Contains("skull") ||
            name.Contains("rib") ||
            name.Contains("spine") ||
            name.Contains("ashes") ||
            name.Contains("claw") ||
            name.Contains("talon") ||
            name.Contains("fang") ||
            name.Contains("tooth") ||
            name.Contains("teeth") ||
            name.Contains("tusk") ||
            name.Contains("horn") ||
            name.Contains("antler") ||
            name.Contains("scale") ||
            name.Contains("shell") ||
            name.Contains("carapace") ||
            name.Contains("chitin") ||
            name.Contains("feather") ||
            name.Contains("plume") ||
            name.Contains("wing") ||
            name.Contains("tail") ||
            name.Contains("stinger") ||
            name.Contains("mandible") ||
            name.Contains("pincer") ||
            name.Contains("beak") ||
            name.Contains("hoof") ||
            name.Contains("paw") ||
            name.Contains("eye of ") ||
            name.Contains(" spider ") ||
            name.Contains("venom sac") ||
            name.Contains("gland") ||
            name.Contains("heart of ") ||
            name.Contains("blood of ") ||
            name.Contains("ectoplasm") ||
            name.Contains("ichor") ||
            name.Contains("slime") ||
            name.Contains("goo") ||
            name.Contains("mucus") ||
            name.Contains("viscera") ||
            name.Contains("entrails"))
        {
            return true;
        }

        return false;
    }

    public static bool IsCraftingMaterial(ItemHandler.Item item, int baseId)
    {
        if (item == null || item.IsEquippable() || IsTool(item, baseId) || IsStorageBag(item, baseId) ||
            IsPotionOrAlchemyProduct(item, baseId) || IsCookedFoodOrDrink(item, baseId) ||
            IsRawFoodOrCookingIngredient(item, baseId) || IsBookScrollOrMagicItem(item, baseId) ||
            IsFurnishing(item))
        {
            return false;
        }

        if (IsOreOrBar(item, baseId) ||
            IsWoodOrPlank(item, baseId) ||
            IsHideLeatherOrCloth(item, baseId) ||
            IsGemOrCrystal(item, baseId) ||
            IsSalvageOrScrap(item, baseId) ||
            IsBoneOrMonsterPart(item, baseId))
        {
            return true;
        }

        if (SmithingIngredientIds.Contains(baseId) ||
            TailoringIngredientIds.Contains(baseId) ||
            CarpentryIngredientIds.Contains(baseId))
        {
            if (!IsKeyOrCurrency(item, baseId))
            {
                return true;
            }
        }

        string name = GetLowerName(item);
        string desc = GetLowerDescription(item);
        if (name.Contains("nail") ||
            name.Contains("rivet") ||
            name.Contains("bolt") ||
            name.Contains("arrow") ||
            name.Contains("arrowhead") ||
            name.Contains("dart") ||
            name.Contains("feather") ||
            name.Contains("buckle") ||
            name.Contains("hinge") ||
            name.Contains("wire") ||
            name.Contains("chain link") ||
            name.Contains("stud") ||
            name.Contains("wax") ||
            name.Contains("resin") ||
            name.Contains("pitch") ||
            name.Contains("tar") ||
            name.Contains("glue") ||
            name.Contains("oil") ||
            name.Contains("dye") ||
            name.Contains("pigment") ||
            name.Contains("ink") ||
            name.Contains("parchment") ||
            name.Contains("paper") ||
            name.Contains("glass") ||
            name.Contains("brick") ||
            name.Contains("stone block") ||
            name.Contains("marble") ||
            name.Contains("granite") ||
            name.Contains("obsidian") ||
            name.Contains("flint") ||
            name.Contains("pebble") ||
            name.Contains("cog") ||
            name.Contains("gear") ||
            name.Contains("spring") ||
            name.Contains("lens") ||
            desc.Contains("crafting material") ||
            desc.Contains("used in smithing") ||
            desc.Contains("used in tailoring") ||
            desc.Contains("used in carpentry") ||
            desc.Contains("used for crafting"))
        {
            return true;
        }

        return false;
    }

    public static bool IsFurnishing(ItemHandler.Item item)
    {
        if (item == null)
        {
            return false;
        }

        return item.itemCategory != null && item.itemCategory.Contains(ItemHandler.ItemCategory.constructable);
    }

    public static bool IsKeyOrCurrency(ItemHandler.Item item, int baseId)
    {
        if (item == null || item.IsEquippable() || IsTool(item, baseId) || IsStorageBag(item, baseId))
        {
            return false;
        }

        if (baseId == 131) // Coins
        {
            return true;
        }

        string name = GetLowerName(item);
        if (name == "coins" ||
            name.EndsWith(" coin") ||
            name.EndsWith(" coins") ||
            name.Contains("gold coin") ||
            name.Contains("silver coin") ||
            name.Contains("ancient coin") ||
            name.Contains("token") ||
            name.Contains("voucher") ||
            name.Contains("ticket") ||
            name.Contains("currency") ||
            name.Contains("bullion") ||
            name.Contains("doubloon") ||
            name == "key" ||
            name.EndsWith(" key") ||
            name.StartsWith("key ") ||
            name.Contains("keyring") ||
            name.Contains("keycard") ||
            name.Contains("skeleton key") ||
            name.Contains("chest key") ||
            name.Contains("door key") ||
            name.Contains("prison key") ||
            name.Contains("cell key") ||
            name.Contains("dungeon key") ||
            name.Contains("gate key") ||
            name.Contains("vault key"))
        {
            return true;
        }

        return false;
    }

    public static bool IsQuestOrLoreItem(ItemHandler.Item item, int baseId)
    {
        if (item == null || item.IsEquippable() || IsTool(item, baseId) || IsStorageBag(item, baseId) ||
            IsBookScrollOrMagicItem(item, baseId))
        {
            return false;
        }

        if (item.itemCategory != null && item.itemCategory.Contains(ItemHandler.ItemCategory.questitem))
        {
            return true;
        }

        if (item.undroppable)
        {
            return true;
        }

        string name = GetLowerName(item);
        string desc = GetLowerDescription(item);
        if (name.Contains("letter") ||
            name.Contains("note") ||
            name.Contains("journal") ||
            name.Contains("diary") ||
            name.Contains("contract") ||
            name.Contains("deed") ||
            name.Contains("manifest") ||
            name.Contains("ledger") ||
            name.Contains("report") ||
            name.Contains("orders") ||
            name.Contains("bounty") ||
            name.Contains("warrant") ||
            name.Contains("certificate") ||
            name.Contains("permit") ||
            name.Contains("pass") ||
            name.Contains("invitation") ||
            name.Contains("message") ||
            name.Contains("package") ||
            name.Contains("parcel") ||
            name.Contains("crate") ||
            name.Contains("relic") ||
            name.Contains("artifact") ||
            name.Contains("artefact") ||
            name.Contains("emblem") ||
            name.Contains("insignia") ||
            name.Contains("badge") ||
            name.Contains("medallion") ||
            name.Contains("seal") ||
            name.Contains("crest") ||
            name.Contains("signet") ||
            name.Contains("trophy") ||
            name.Contains("statuette") ||
            name.Contains("idol") ||
            name.Contains("effigy") ||
            name.Contains("totem") ||
            name.Contains("chalice") ||
            name.Contains("goblet") ||
            name.Contains("locket") ||
            name.Contains("heirloom") ||
            name.Contains("keepsake") ||
            name.Contains("memento") ||
            name.Contains("clue") ||
            name.Contains("map") ||
            name.Contains("compass") ||
            desc.Contains("quest"))
        {
            return true;
        }

        return false;
    }

    public static bool IsQuestKeyCurrencyFurnishingOrMisc(ItemHandler.Item item, int baseId)
    {
        if (IsKeyOrCurrency(item, baseId) ||
            IsQuestOrLoreItem(item, baseId) ||
            IsFurnishing(item))
        {
            return true;
        }

        return IsUnmatchedMisc(item, baseId);
    }

    public static bool IsUnmatchedMisc(ItemHandler.Item item, int baseId)
    {
        if (item == null)
        {
            return true;
        }

        return !IsCombatWeaponOrShield(item, baseId) &&
               !IsArmorOrHeadgear(item) &&
               !IsJewelry(item) &&
               !IsStorageBag(item, baseId) &&
               !IsTool(item, baseId) &&
               !IsPotionOrAlchemyProduct(item, baseId) &&
               !IsCookedFoodOrDrink(item, baseId) &&
               !IsRawFoodOrCookingIngredient(item, baseId) &&
               !IsHerbOrAlchemyIngredient(item, baseId) &&
               !IsCraftingMaterial(item, baseId) &&
               !IsBookScrollOrMagicItem(item, baseId);
    }

    private static string GetLowerName(ItemHandler.Item item)
    {
        return item?.itemName?.ToLowerInvariant() ?? "";
    }

    private static string GetLowerDescription(ItemHandler.Item item)
    {
        return item?.description?.ToLowerInvariant() ?? "";
    }
}
