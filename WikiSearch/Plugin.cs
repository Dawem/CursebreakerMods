using System;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LUI;
using UnityEngine;

namespace WikiSearch;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public const string DefaultWikiUrl = "https://cursebreaker.wiki/wiki/<item name>";

    internal static new ManualLogSource Logger;

    internal static ConfigEntry<string> WikiUrl;
    internal static ConfigEntry<string> MenuText;
    internal static ConfigEntry<bool> EnableInventory;
    internal static ConfigEntry<bool> EnableEquipment;
    internal static ConfigEntry<bool> EnableBank;

    private static readonly Regex RichTextRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    private static readonly Func<ItemHandler.Item, string> GetItemNameDelegate =
        AccessTools.MethodDelegate<Func<ItemHandler.Item, string>>(
            AccessTools.Method(typeof(ItemHandler.Item), "GetItemName"));

    private readonly Harmony _harmony = new(MyPluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Logger = base.Logger;

        WikiUrl = Config.Bind(
            "General",
            "WikiUrl",
            DefaultWikiUrl,
            "The wiki URL template used to search for items. '<item name>' is replaced with the item's name with spaces converted to underscores.");

        MenuText = Config.Bind(
            "General",
            "MenuText",
            "Search Wiki",
            "The text displayed on the right-click context menu button.");

        EnableInventory = Config.Bind(
            "Features",
            "EnableInventory",
            true,
            "Whether to show the wiki search option when right-clicking items in the inventory.");

        EnableEquipment = Config.Bind(
            "Features",
            "EnableEquipment",
            true,
            "Whether to show the wiki search option when right-clicking equipped items in the equipment panel.");

        EnableBank = Config.Bind(
            "Features",
            "EnableBank",
            true,
            "Whether to show the wiki search option when right-clicking items in the bank.");

        _harmony.PatchAll();

        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_NAME} ({MyPluginInfo.PLUGIN_GUID}) v{MyPluginInfo.PLUGIN_VERSION} loaded!");
    }

    /// <summary>
    /// Strips rich text tags (such as &lt;color&gt;, &lt;b&gt;, etc.) from the input string.
    /// </summary>
    public static string StripRichTextTags(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return RichTextRegex.Replace(input, string.Empty);
    }

    /// <summary>
    /// Retrieves the name of the item, preferring the base itemName and falling back to GetItemName(),
    /// with any rich text tags (such as &lt;color&gt;) stripped.
    /// </summary>
    public static string GetItemDisplayName(ItemHandler.Item item)
    {
        if (item == null)
            return null;

        string name = null;
        if (!string.IsNullOrWhiteSpace(item.itemName))
            name = item.itemName;
        else
        {
            try
            {
                name = GetItemNameDelegate?.Invoke(item);
            }
            catch
            {
                name = null;
            }
        }

        if (string.IsNullOrWhiteSpace(name))
            return null;

        string cleanName = StripRichTextTags(name).Trim();
        return string.IsNullOrWhiteSpace(cleanName) ? null : cleanName;
    }

    /// <summary>
    /// Builds the target wiki URL by replacing spaces with underscores in the item name
    /// and inserting it into the configured URL template. Rich text tags (e.g. &lt;color&gt;) are stripped.
    /// </summary>
    public static string BuildWikiUrl(string pattern, string rawItemName)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            pattern = DefaultWikiUrl;

        if (string.IsNullOrWhiteSpace(rawItemName))
            return pattern;

        string cleanName = StripRichTextTags(rawItemName).Trim();
        if (string.IsNullOrWhiteSpace(cleanName))
            return pattern;

        string formattedName = Regex.Replace(cleanName, @"\s+", " ").Replace(' ', '_');

        if (pattern.IndexOf("<item name>", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return Regex.Replace(pattern, "<item name>", formattedName, RegexOptions.IgnoreCase);
        }

        if (pattern.Contains("{0}"))
        {
            return string.Format(pattern, formattedName);
        }

        return pattern.TrimEnd('/') + "/" + formattedName;
    }

    /// <summary>
    /// Opens the wiki URL for the specified item in the default web browser.
    /// </summary>
    public static void OpenWikiForItem(ItemHandler.Item item)
    {
        if (item == null)
            return;

        string rawName = GetItemDisplayName(item);
        if (string.IsNullOrWhiteSpace(rawName))
            return;

        string finalUrl = BuildWikiUrl(WikiUrl?.Value, rawName);
        try
        {
            Logger?.LogInfo($"Opening wiki for '{rawName}': {finalUrl}");
            Application.OpenURL(finalUrl);
        }
        catch (Exception ex)
        {
            Logger?.LogError($"Failed to open wiki URL '{finalUrl}': {ex}");
        }
    }

    /// <summary>
    /// Appends the Wiki option to a right-click context menu and increments the layout counter.
    /// </summary>
    private static void AddWikiButton(UI_RightClickMenu menu, ItemHandler.Item item, ref int counter)
    {
        if (menu == null || item == null)
            return;

        string rawName = GetItemDisplayName(item);
        if (string.IsNullOrWhiteSpace(rawName))
            return;

        string buttonText = !string.IsNullOrWhiteSpace(MenuText?.Value) ? MenuText.Value : "Search Wiki";
        Vector2 pos = new Vector2(5f, -5f - counter * 25f);

        LUI_Button btn = menu.AddButton(
            buttonText,
            pos,
            _ =>
            {
                OpenWikiForItem(item);
                try
                {
                    menu.Deactivate();
                }
                catch (Exception ex)
                {
                    Logger?.LogError($"Failed to deactivate right-click menu: {ex}");
                }
            },
            null);

        if (btn != null && btn.text != null && btn.text.text != null)
        {
            btn.text.text.supportRichText = true;
        }

        counter++;
    }

    [HarmonyPatch(typeof(UI_RightClickMenu), "InventoryItemOptions")]
    private static class InventoryItemOptions_Patch
    {
        private static void Postfix(UI_RightClickMenu __instance, UI_Inventory.ItemSlot itemslot, ref int counter)
        {
            if (EnableInventory != null && !EnableInventory.Value)
                return;

            if (__instance == null || itemslot == null || itemslot.itemId <= 0)
                return;

            ItemHandler.Item item = itemslot.GetItem();
            if (item == null && ItemHandler.instance != null)
            {
                item = ItemHandler.instance.GetItem(itemslot.itemId);
            }

            if (item == null)
                return;

            AddWikiButton(__instance, item, ref counter);
        }
    }

    [HarmonyPatch(typeof(UI_RightClickMenu), "EquipmentSlotOptions")]
    private static class EquipmentSlotOptions_Patch
    {
        private static void Postfix(UI_RightClickMenu __instance, UI_RightClickMenu.EquipmentSlotContext equipmentSlot, ref int counter)
        {
            if (EnableEquipment != null && !EnableEquipment.Value)
                return;

            if (__instance == null || equipmentSlot == null || equipmentSlot.itemId <= 0)
                return;

            if (ItemHandler.instance == null)
                return;

            ItemHandler.Item item = ItemHandler.instance.GetItem(equipmentSlot.itemId);
            if (item == null)
                return;

            AddWikiButton(__instance, item, ref counter);
        }
    }

    [HarmonyPatch(typeof(UI_RightClickMenu), "BankItemOptions")]
    private static class BankItemOptions_Patch
    {
        private static void Postfix(UI_RightClickMenu __instance, UI_Bank.ItemSlot itemslot, ref int counter)
        {
            if (EnableBank != null && !EnableBank.Value)
                return;

            if (__instance == null || itemslot == null || itemslot.itemId <= 0)
                return;

            ItemHandler.Item item = itemslot.GetItem();
            if (item == null && ItemHandler.instance != null)
            {
                item = ItemHandler.instance.GetItem(itemslot.itemId);
            }

            if (item == null)
                return;

            AddWikiButton(__instance, item, ref counter);
        }
    }
}
