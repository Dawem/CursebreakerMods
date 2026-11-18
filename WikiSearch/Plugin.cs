using System;
using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LUI;
using UnityEngine;

namespace WikiSearch;

public enum WikiSite
{
    [Description("cursebreakerwiki.com")]
    CursebreakerWikiCom,
    [Description("cursebreaker.wiki")]
    CursebreakerWiki
}

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public const string DefaultWikiUrl = "https://cursebreaker.wiki/wiki/<item name>";
    public const string CursebreakerWikiComBaseUrl = "https://cursebreakerwiki.com";

    internal static new ManualLogSource Logger;

    internal static ConfigEntry<WikiSite> SelectedWiki;
    internal static ConfigEntry<bool> OpenInSteamOverlay;
    internal static ConfigEntry<string> MenuText;
    internal static ConfigEntry<bool> EnableInventory;
    internal static ConfigEntry<bool> EnableEquipment;
    internal static ConfigEntry<bool> EnableBank;
    internal static ConfigEntry<bool> EnableNPCs;
    internal static ConfigEntry<bool> EnableMobs;

    private static readonly Regex RichTextRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex SlugNonAlnumRegex = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    private static bool _steamLookupAttempted;
    private static MethodInfo _activateOverlayToWebPageMethod;
    private static object _defaultOverlayWebPageMode;
    private static MethodInfo _isOverlayEnabledMethod;

    private static readonly Func<ItemHandler.Item, string> GetItemNameDelegate =
        AccessTools.MethodDelegate<Func<ItemHandler.Item, string>>(
            AccessTools.Method(typeof(ItemHandler.Item), "GetItemName"));

    private readonly Harmony _harmony = new(MyPluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Logger = base.Logger;

        SelectedWiki = Config.Bind(
            "General",
            "WikiSite",
            WikiSite.CursebreakerWikiCom,
            "Which wiki site to open when searching (CursebreakerWikiCom = https://cursebreakerwiki.com, CursebreakerWiki = https://cursebreaker.wiki).");

        OpenInSteamOverlay = Config.Bind(
            "General",
            "Open in Steam Overlay",
            false,
            "When enabled, opens wiki links inside the in-game Steam Overlay browser instead of your external web browser (falls back to the external browser if the Steam Overlay is unavailable).");

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

        EnableNPCs = Config.Bind(
            "Features",
            "EnableNPCs",
            true,
            "Whether to show the wiki search option when right-clicking NPCs.");

        EnableMobs = Config.Bind(
            "Features",
            "EnableMobs",
            true,
            "Whether to show the wiki search option when right-clicking mobs / combatants.");

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
    /// Builds the target wiki URL by replacing spaces with underscores in the target name
    /// and inserting it into the configured URL template. Rich text tags (e.g. &lt;color&gt;) are stripped.
    /// </summary>
    public static string BuildWikiUrl(string pattern, string rawTargetName)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            pattern = DefaultWikiUrl;

        if (string.IsNullOrWhiteSpace(rawTargetName))
            return pattern;

        string cleanName = StripRichTextTags(rawTargetName).Trim();
        if (string.IsNullOrWhiteSpace(cleanName))
            return pattern;

        string formattedName = Regex.Replace(cleanName, @"\s+", " ").Replace(' ', '_');

        if (pattern.IndexOf("<item name>", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return Regex.Replace(pattern, "<item name>", formattedName, RegexOptions.IgnoreCase);
        }

        if (pattern.IndexOf("<target name>", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return Regex.Replace(pattern, "<target name>", formattedName, RegexOptions.IgnoreCase);
        }

        if (pattern.IndexOf("<npc name>", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return Regex.Replace(pattern, "<npc name>", formattedName, RegexOptions.IgnoreCase);
        }

        if (pattern.IndexOf("<name>", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return Regex.Replace(pattern, "<name>", formattedName, RegexOptions.IgnoreCase);
        }

        if (pattern.Contains("{0}"))
        {
            return string.Format(pattern, formattedName);
        }

        return pattern.TrimEnd('/') + "/" + formattedName;
    }

    /// <summary>
    /// Converts a display name into a cursebreakerwiki.com URL slug.
    /// </summary>
    public static string SlugifyWikiName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return null;

        string cleanName = StripRichTextTags(rawName).Trim().Replace("\u2019", string.Empty);
        if (string.IsNullOrWhiteSpace(cleanName))
            return null;

        string slug = SlugNonAlnumRegex.Replace(cleanName.ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? null : slug;
    }

    /// <summary>
    /// Strips Exceptional (+100000) and Banknote (+1000000) offsets to recover the base item typeId.
    /// </summary>
    public static int GetBaseItemId(int typeId)
    {
        int baseId = typeId;
        if (baseId >= 1000000)
            baseId -= 1000000;
        if (baseId >= 100000)
            baseId -= 100000;
        return baseId;
    }

    /// <summary>
    /// Builds a direct page URL for cursebreakerwiki.com from entity kind ("item" or "npc"), ID, and name.
    /// </summary>
    public static string BuildCursebreakerWikiComUrl(string kind, int id, string rawName)
    {
        string slug = SlugifyWikiName(rawName);
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        return $"{CursebreakerWikiComBaseUrl}/{kind}/{slug}-{id}.html";
    }

    private static void EnsureSteamOverlayMethods()
    {
        if (_steamLookupAttempted)
            return;

        _steamLookupAttempted = true;

        Type steamFriendsType = AccessTools.TypeByName("Steamworks.SteamFriends");
        if (steamFriendsType != null)
        {
            foreach (MethodInfo method in steamFriendsType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name != "ActivateGameOverlayToWebPage")
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                {
                    _activateOverlayToWebPageMethod = method;
                    break;
                }

                if (parameters.Length == 2 &&
                    parameters[0].ParameterType == typeof(string) &&
                    parameters[1].ParameterType.IsEnum)
                {
                    _activateOverlayToWebPageMethod = method;
                    _defaultOverlayWebPageMode = Enum.ToObject(parameters[1].ParameterType, 0);
                    break;
                }
            }
        }

        Type steamUtilsType = AccessTools.TypeByName("Steamworks.SteamUtils");
        if (steamUtilsType != null)
        {
            _isOverlayEnabledMethod = AccessTools.Method(steamUtilsType, "IsOverlayEnabled", Type.EmptyTypes);
        }
    }

    /// <summary>
    /// Attempts to open the specified URL in the Steam Overlay web browser.
    /// Returns false if Steam is not initialized or the Steam Overlay is unavailable.
    /// </summary>
    public static bool TryOpenInSteamOverlay(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            if (!SteamManager.Initialized)
            {
                Logger?.LogDebug("Steam is not initialized; falling back to external browser.");
                return false;
            }

            EnsureSteamOverlayMethods();

            if (_isOverlayEnabledMethod != null && _isOverlayEnabledMethod.Invoke(null, null) is bool isEnabled && !isEnabled)
            {
                Logger?.LogWarning("Steam Overlay is disabled or unavailable; falling back to external browser.");
                return false;
            }

            if (_activateOverlayToWebPageMethod == null)
            {
                Logger?.LogWarning("SteamFriends.ActivateGameOverlayToWebPage was not found; falling back to external browser.");
                return false;
            }

            object[] args = _activateOverlayToWebPageMethod.GetParameters().Length == 2
                ? new[] { url, _defaultOverlayWebPageMode }
                : new object[] { url };

            _activateOverlayToWebPageMethod.Invoke(null, args);
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning($"Failed to open URL in Steam Overlay, falling back to external browser: {ex.Message}");
            return false;
        }
    }

    private static void OpenWikiUrl(string finalUrl, string targetLabel)
    {
        if (string.IsNullOrWhiteSpace(finalUrl))
            return;

        if (OpenInSteamOverlay != null && OpenInSteamOverlay.Value && TryOpenInSteamOverlay(finalUrl))
        {
            Logger?.LogInfo($"Opening wiki in Steam Overlay for '{targetLabel}': {finalUrl}");
            return;
        }

        try
        {
            Logger?.LogInfo($"Opening wiki in external browser for '{targetLabel}': {finalUrl}");
            Application.OpenURL(finalUrl);
        }
        catch (Exception ex)
        {
            Logger?.LogError($"Failed to open wiki URL '{finalUrl}': {ex}");
        }
    }

    /// <summary>
    /// Opens the legacy wiki URL for the specified target name in the default web browser.
    /// </summary>
    public static void OpenWikiForName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return;

        string cleanName = StripRichTextTags(rawName).Trim();
        if (string.IsNullOrWhiteSpace(cleanName))
            return;

        string finalUrl = BuildWikiUrl(DefaultWikiUrl, cleanName);
        OpenWikiUrl(finalUrl, cleanName);
    }

    /// <summary>
    /// Opens the wiki URL for the specified item in the default web browser.
    /// </summary>
    public static void OpenWikiForItem(ItemHandler.Item item)
    {
        if (item == null)
            return;

        WikiSite site = SelectedWiki?.Value ?? WikiSite.CursebreakerWikiCom;
        if (site == WikiSite.CursebreakerWiki)
        {
            string rawName = GetItemDisplayName(item);
            if (string.IsNullOrWhiteSpace(rawName))
                return;

            OpenWikiForName(rawName);
            return;
        }

        int baseId = GetBaseItemId(item.typeId);
        ItemHandler.Item targetItem = baseId != item.typeId && ItemHandler.instance != null
            ? ItemHandler.instance.GetItem(baseId) ?? item
            : item;

        string baseName = GetItemDisplayName(targetItem);
        if (string.IsNullOrWhiteSpace(baseName))
            return;

        string finalUrl = BuildCursebreakerWikiComUrl("item", baseId, baseName);
        OpenWikiUrl(finalUrl, StripRichTextTags(baseName).Trim());
    }

    /// <summary>
    /// Opens the wiki URL for the specified NPC or mob in the default web browser.
    /// </summary>
    public static void OpenWikiForNpc(NPC npc)
    {
        if (npc == null)
            return;

        WikiSite site = SelectedWiki?.Value ?? WikiSite.CursebreakerWikiCom;
        if (site == WikiSite.CursebreakerWiki)
        {
            if (string.IsNullOrWhiteSpace(npc.npcName))
                return;

            OpenWikiForName(npc.npcName);
            return;
        }

        string templateName = NPCHandler.instance != null
            ? NPCHandler.instance.getNpcTemplate(npc.npcTypeId)?.npcName
            : null;
        string rawName = !string.IsNullOrWhiteSpace(templateName) ? templateName : npc.npcName;
        if (string.IsNullOrWhiteSpace(rawName))
            return;

        string finalUrl = BuildCursebreakerWikiComUrl("npc", npc.npcTypeId, rawName);
        OpenWikiUrl(finalUrl, StripRichTextTags(rawName).Trim());
    }

    /// <summary>
    /// Appends the Wiki option to a right-click context menu for an item and increments the layout counter.
    /// </summary>
    private static void AddWikiButton(UI_RightClickMenu menu, ItemHandler.Item item, ref int counter)
    {
        if (menu == null || item == null)
            return;

        string rawName = GetItemDisplayName(item);
        if (string.IsNullOrWhiteSpace(rawName))
            return;

        string template = !string.IsNullOrWhiteSpace(MenuText?.Value) ? MenuText.Value : "Search Wiki";
        string buttonText = template.Contains("{0}") ? string.Format(template, rawName) : template;
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

    /// <summary>
    /// Appends the Wiki option to a right-click context menu for an NPC or mob and increments the layout counter.
    /// </summary>
    private static void AddWikiButtonForNpc(UI_RightClickMenu menu, NPC npc, ref int counter)
    {
        if (menu == null || npc == null)
            return;

        string cleanName = StripRichTextTags(npc.npcName)?.Trim();
        if (string.IsNullOrWhiteSpace(cleanName))
            return;

        string template = !string.IsNullOrWhiteSpace(MenuText?.Value) ? MenuText.Value : "Search Wiki";
        string buttonText;
        if (template.Contains("{0}"))
        {
            buttonText = string.Format(template, cleanName);
        }
        else
        {
            buttonText = $"{template} - {cleanName}";
        }

        Vector2 pos = new Vector2(5f, -5f - counter * 25f);

        LUI_Button btn = menu.AddButton(
            buttonText,
            pos,
            _ =>
            {
                OpenWikiForNpc(npc);
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

    [HarmonyPatch(typeof(UI_RightClickMenu), "NpcExtraOptions")]
    private static class NpcExtraOptions_Patch
    {
        private static void Postfix(UI_RightClickMenu __instance, NPC npc, ref int counter)
        {
            if (__instance == null || npc == null)
                return;

            bool isMob = npc.canFight;
            bool allowMob = EnableMobs == null || EnableMobs.Value;
            bool allowNpc = EnableNPCs == null || EnableNPCs.Value;

            if (isMob && !allowMob && !(npc.interactable && allowNpc))
                return;

            if (!isMob && !allowNpc)
                return;

            AddWikiButtonForNpc(__instance, npc, ref counter);
        }
    }
}
