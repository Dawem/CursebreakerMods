using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ModConfigMenu;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.mike.theblackgrimoire.modconfigmenu";
    public const string PluginName = "Mod Config Menu";
    public const string PluginVersion = "1.0.0";

    public const int Menu_Mods = 99;

    internal static new ManualLogSource Logger;

    public static ConfigEntry<KeyboardShortcut> OpenHotkey;
    public static ConfigEntry<bool> AutoSaveOnApply;

    private readonly Harmony _harmony = new(PluginGuid);

    private void Awake()
    {
        Logger = base.Logger;

        OpenHotkey = Config.Bind(
            "Keybinds",
            "OpenHotkey",
            new KeyboardShortcut(KeyCode.None),
            "Optional hotkey to directly open the Settings menu to the Mods tab.");

        AutoSaveOnApply = Config.Bind(
            "General",
            "AutoSaveOnApply",
            true,
            "Whether clicking the Options 'Apply' button immediately saves all changed mod configs to disk.");

        _harmony.PatchAll();
        Logger.LogInfo($"{PluginName} ({PluginGuid}) v{PluginVersion} loaded successfully!");
    }

    private void Update()
    {
        // 1. Process keybind capture if an active setting is listening for a key press
        if (UI_ModSettingsView.IsCapturingKeybind)
        {
            UI_ModSettingsView.UpdateKeybindCapture();
            return;
        }

        // 2. Direct hotkey to open the Mods tab
        KeyboardShortcut hotkey = OpenHotkey?.Value ?? KeyboardShortcut.Empty;
        if (hotkey.MainKey != KeyCode.None && IsShortcutDown(in hotkey))
        {
            if (UI_Options.instance != null)
            {
                if (!UI_Options.instance.active)
                {
                    UI_Options.instance.ToggleWindow();
                }
                UI_Options.instance.SelectMenu(new object[] { Menu_Mods });
            }
        }
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
}

