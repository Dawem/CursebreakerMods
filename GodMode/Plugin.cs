using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LUI;
using UnityEngine;

namespace GodMode;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.cursebreaker.godmode";
    public const string PluginName = "God Mode";
    public const string PluginVersion = "1.0.0";

    internal static new ManualLogSource Logger;

    // General
    internal static ConfigEntry<bool> GodModeEnabled;
    internal static ConfigEntry<bool> EnableDebugMode;
    internal static ConfigEntry<bool> Invincible;
    internal static ConfigEntry<bool> PreventDeath;

    // Vitals
    internal static ConfigEntry<bool> InfiniteHealth;
    internal static ConfigEntry<bool> InfiniteMana;
    internal static ConfigEntry<bool> InfiniteSpecialPower;

    // Combat
    internal static ConfigEntry<bool> NoCooldowns;
    internal static ConfigEntry<bool> OneHitKill;
    internal static ConfigEntry<float> DamageMultiplier;

    // Movement
    internal static ConfigEntry<float> MovementSpeedMultiplier;

    // Keybinds
    internal static ConfigEntry<KeyboardShortcut> ToggleKey;

    private readonly Harmony _harmony = new(PluginGuid);

    private void Awake()
    {
        Logger = base.Logger;

        // General
        GodModeEnabled = Config.Bind(
            "General",
            "GodModeEnabled",
            false,
            "Master toggle for God Mode. When enabled, all active god mode features take effect.");

        EnableDebugMode = Config.Bind(
            "General",
            "EnableDebugMode",
            false,
            "Enables the game's built-in developer and debug mode (GameHandler.devBuild and CBDebug.debug), unlocking developer console commands, debug right-click context menu options (duplicate, salvage, sell, destroy), the debug bottom-right tab, and extra entity/stat info.");

        Invincible = Config.Bind(
            "General",
            "Invincible",
            true,
            "When enabled, completely immunizes the player against all incoming damage from attacks, AOEs, environmental hazards, poison, and curses.");

        PreventDeath = Config.Bind(
            "General",
            "PreventDeath",
            true,
            "When enabled, prevents the player from dying or triggering the death sequence under any circumstance.");

        // Vitals
        InfiniteHealth = Config.Bind(
            "Vitals",
            "InfiniteHealth",
            true,
            "Continuously keeps the player's health locked at maximum.");

        InfiniteMana = Config.Bind(
            "Vitals",
            "InfiniteMana",
            true,
            "Continuously keeps the player's mana locked at maximum, never running out during spellcasting.");

        InfiniteSpecialPower = Config.Bind(
            "Vitals",
            "InfiniteSpecialPower",
            true,
            "Continuously keeps the player's special attack power charged at 100%.");

        // Combat
        NoCooldowns = Config.Bind(
            "Combat",
            "NoCooldowns",
            true,
            "Removes cooldown timers from all player spells and combat abilities.");

        OneHitKill = Config.Bind(
            "Combat",
            "OneHitKill",
            false,
            "Causes all attacks dealt by the player to deal massive lethal damage (999,999) to defeat enemies instantly.");

        DamageMultiplier = Config.Bind(
            "Combat",
            "DamageMultiplier",
            1.0f,
            new ConfigDescription("Multiplier applied to all damage dealt by the player (e.g. 2.0 = 2x damage). Has no effect if OneHitKill is active.", new AcceptableValueRange<float>(1.0f, 100.0f)));

        // Movement
        MovementSpeedMultiplier = Config.Bind(
            "Movement",
            "MovementSpeedMultiplier",
            1.0f,
            new ConfigDescription("Multiplier applied to the player's movement and run speed (e.g. 1.5 = 50% faster, 2.0 = double speed). 1.0 = standard speed.", new AcceptableValueRange<float>(1.0f, 5.0f)));

        // Keybinds
        ToggleKey = Config.Bind(
            "Keybinds",
            "ToggleKey",
            new KeyboardShortcut(KeyCode.F10),
            "Hotkey shortcut to toggle God Mode on or off during gameplay.");

        // Listen for config changes to apply state immediately
        GodModeEnabled.SettingChanged += (_, _) => OnGodModeToggled(GodModeEnabled.Value, playFeedback: false);
        EnableDebugMode.SettingChanged += (_, _) => ApplyDebugModeState(EnableDebugMode.Value);

        if (EnableDebugMode.Value)
        {
            ApplyDebugModeState(true);
        }

        _harmony.PatchAll();
        Logger.LogInfo($"{PluginName} ({PluginGuid}) v{PluginVersion} loaded successfully!");
    }

    private void Update()
    {
        // Check toggle hotkey
        if (ToggleKey.Value.IsDown())
        {
            ToggleGodMode();
        }

        if (EnableDebugMode.Value)
        {
            GameHandler.devBuild = true;
            if (CBDebug.instance != null && !CBDebug.instance.debug)
            {
                CBDebug.instance.debug = true;
            }
        }
        else
        {
            if (GameHandler.devBuild)
            {
                GameHandler.devBuild = false;
            }
            if (CBDebug.instance != null && CBDebug.instance.debug)
            {
                CBDebug.instance.debug = false;
            }
        }

        if (!GodModeEnabled.Value)
        {
            return;
        }

        Player player = Player.instance;
        if (player == null || player.dead)
        {
            return;
        }

        // Maintain native godmode & immune flags
        if (Invincible.Value)
        {
            player.godmode = true;
            player.immuneToDamage = true;
        }

        // Maintain Infinite Health
        if (InfiniteHealth.Value && player.stats != null)
        {
            float maxHp = player.stats.GetStatValue(StatsHandler.StatType.health);
            if (player.currentHealth < maxHp)
            {
                player.currentHealth = maxHp;
                UI_Healthbars.instance?.UpdateUnitHealth(player);
                UI_HUD.instance?.UpdateHealth();
            }
        }

        // Maintain Infinite Mana
        if (InfiniteMana.Value && player.stats != null)
        {
            float maxMana = player.stats.GetStatValue(StatsHandler.StatType.mana);
            if (player.currentMana < maxMana)
            {
                player.currentMana = maxMana;
                UI_Healthbars.instance?.UpdateUnitMana(player);
                UI_HUD.instance?.UpdateMana();
            }
        }

        // Maintain Infinite Special Power
        if (InfiniteSpecialPower.Value)
        {
            if (player.currentSpecialPower < 100f)
            {
                player.currentSpecialPower = 100f;
                UI_Healthbars.instance?.UpdateUnitSpecial(player);
                UI_HUD.instance?.UpdateSpec();
            }
        }

        // Maintain No Cooldowns
        if (NoCooldowns.Value && player.playerAttacks?.abilityCooldowns != null && player.playerAttacks.abilityCooldowns.Count > 0)
        {
            player.playerAttacks.abilityCooldowns.Clear();
        }
    }

    public static void ToggleGodMode()
    {
        SetGodModeState(!GodModeEnabled.Value, playFeedback: true);
    }

    public static void SetGodModeState(bool enabled, bool playFeedback = true)
    {
        GodModeEnabled.Value = enabled;
        OnGodModeToggled(enabled, playFeedback);
    }

    private static void OnGodModeToggled(bool enabled, bool playFeedback)
    {
        Player player = Player.instance;
        if (player != null)
        {
            player.godmode = enabled && Invincible.Value;
            player.immuneToDamage = enabled && Invincible.Value;

            if (enabled)
            {
                if (InfiniteHealth.Value && player.stats != null)
                {
                    player.currentHealth = player.stats.GetStatValue(StatsHandler.StatType.health);
                    UI_Healthbars.instance?.UpdateUnitHealth(player);
                    UI_HUD.instance?.UpdateHealth();
                }

                if (InfiniteMana.Value && player.stats != null)
                {
                    player.currentMana = player.stats.GetStatValue(StatsHandler.StatType.mana);
                    UI_Healthbars.instance?.UpdateUnitMana(player);
                    UI_HUD.instance?.UpdateMana();
                }

                if (InfiniteSpecialPower.Value)
                {
                    player.currentSpecialPower = 100f;
                    UI_Healthbars.instance?.UpdateUnitSpecial(player);
                    UI_HUD.instance?.UpdateSpec();
                }

                if (NoCooldowns.Value && player.playerAttacks?.abilityCooldowns != null)
                {
                    player.playerAttacks.abilityCooldowns.Clear();
                }
            }
        }

        if (playFeedback)
        {
            string message = enabled
                ? "<color=#55FF55>God Mode: ON</color>"
                : "<color=#FF5555>God Mode: OFF</color>";

            UI_Notifications.instance?.ShowNotification(message);
            AudioHandler.instance?.Play2DSound(enabled ? 12 : 13);
        }

        Logger.LogInfo($"God Mode toggled: {(enabled ? "ON" : "OFF")}");
    }

    private static readonly AccessTools.FieldRef<UI_BottomRightTabs, int> ButtonCounterRef =
        AccessTools.FieldRefAccess<UI_BottomRightTabs, int>("buttonCounter");

    private static readonly Action<UI_BottomRightTabs, int> DrawButtonDelegate =
        AccessTools.MethodDelegate<Action<UI_BottomRightTabs, int>>(
            AccessTools.Method(typeof(UI_BottomRightTabs), "DrawButton", new[] { typeof(int) }));

    private static readonly Action<UI_BottomRightTabs> SetupControllerRadialDelegate =
        AccessTools.MethodDelegate<Action<UI_BottomRightTabs>>(
            AccessTools.Method(typeof(UI_BottomRightTabs), "SetupControllerRadial"));

    public static void ApplyDebugModeState(bool enabled)
    {
        GameHandler.devBuild = enabled;
        if (CBDebug.instance != null)
        {
            CBDebug.instance.debug = enabled;
        }

        if (enabled)
        {
            if (UI_BottomRightTabs.instance != null)
            {
                try
                {
                    if (UI_BottomRightTabs.instance.selectedImages != null && !UI_BottomRightTabs.instance.selectedImages.ContainsKey(12))
                    {
                        DrawButtonDelegate?.Invoke(UI_BottomRightTabs.instance, 12);
                        SetupControllerRadialDelegate?.Invoke(UI_BottomRightTabs.instance);
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning($"Failed to dynamically add debug tab button: {ex.Message}");
                }
            }
        }
        else
        {
            // Close the Debug Tab window if open
            if (UI_DebugTab.instance != null)
            {
                try
                {
                    UI_DebugTab.instance.Deactivate();
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning($"Failed to deactivate debug tab: {ex.Message}");
                }
            }

            if (UI_BottomRightTabs.instance != null)
            {
                try
                {
                    // If tab 12 was currently open, close the tab background and reset active tab
                    if (UI_BottomRightTabs.instance.currentTab == 12)
                    {
                        UI_BottomRightTabs.instance.image_tabBackground?.SetActive(false);
                        UI_BottomRightTabs.instance.currentTab = 0;
                    }

                    // Remove tab 12 button from bottom-right HUD
                    if (UI_BottomRightTabs.instance.selectedImages != null &&
                        UI_BottomRightTabs.instance.selectedImages.TryGetValue(12, out LUI_Image debugSelectedImg))
                    {
                        UI_BottomRightTabs.instance.selectedImages.Remove(12);

                        if (debugSelectedImg != null)
                        {
                            GameObject buttonObj = null;
                            if (debugSelectedImg.rectTransform != null && debugSelectedImg.rectTransform.parent != null)
                            {
                                buttonObj = debugSelectedImg.rectTransform.parent.gameObject;
                            }
                            else if (debugSelectedImg.spawnedObject != null && debugSelectedImg.spawnedObject.transform.parent != null)
                            {
                                buttonObj = debugSelectedImg.spawnedObject.transform.parent.gameObject;
                            }

                            if (buttonObj != null)
                            {
                                UnityEngine.Object.Destroy(buttonObj);
                            }
                        }

                        // Decrement buttonCounter so layout and future button additions stay aligned
                        ref int counter = ref ButtonCounterRef(UI_BottomRightTabs.instance);
                        if (counter > 0)
                        {
                            counter--;
                        }

                        SetupControllerRadialDelegate?.Invoke(UI_BottomRightTabs.instance);
                    }

                    // Clear any lingering hover tooltip
                    UI_Cursor.instance?.SetUIMouseHoverText(new object[] { "" });
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning($"Failed to remove debug tab button: {ex.Message}");
                }
            }
        }

        Logger?.LogInfo($"Debug Mode: {(enabled ? "ON" : "OFF")}");
    }
}

