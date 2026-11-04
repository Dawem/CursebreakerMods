using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace StopStealingMyStuff;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    internal static ConfigEntry<bool> Enabled;

    private readonly Harmony _harmony = new(MyPluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Logger = base.Logger;

        Enabled = Config.Bind(
            "General",
            "Enabled",
            true,
            "When enabled, prevents NPCs (such as Miners, Woodcutters, Fishermen, and Foragers) from damaging or depleting harvestable resource nodes.");

        _harmony.PatchAll();
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_NAME} ({MyPluginInfo.PLUGIN_GUID}) is loaded!");
    }
}

[HarmonyPatch]
public static class PreventNpcResourceDepletionPatch
{
    private static int _npcNextActionDepth;
    private static readonly List<(Interactable_Resource Resource, int Health)> HealthSnapshot = new();

    [HarmonyPatch(typeof(NPCAI), "NextAction")]
    [HarmonyPrefix]
    public static void NextAction_Prefix()
    {
        if (!Plugin.Enabled.Value)
        {
            return;
        }

        if (_npcNextActionDepth == 0)
        {
            HealthSnapshot.Clear();
            List<Interactable_Resource> resources = HarvestablesHandler.instance?.resources;
            if (resources != null)
            {
                for (int i = 0; i < resources.Count; i++)
                {
                    Interactable_Resource res = resources[i];
                    if (res != null)
                    {
                        HealthSnapshot.Add((res, res.health));
                    }
                }
            }
        }

        _npcNextActionDepth++;
    }

    [HarmonyPatch(typeof(NPCAI), "NextAction")]
    [HarmonyFinalizer]
    public static void NextAction_Finalizer()
    {
        if (_npcNextActionDepth <= 0)
        {
            return;
        }

        _npcNextActionDepth--;
        if (_npcNextActionDepth == 0)
        {
            for (int i = 0; i < HealthSnapshot.Count; i++)
            {
                (Interactable_Resource resource, int health) = HealthSnapshot[i];
                if (resource != null)
                {
                    resource.health = health;
                }
            }

            HealthSnapshot.Clear();
        }
    }

    [HarmonyPatch(typeof(Interactable_Resource), nameof(Interactable_Resource.Exhausted))]
    [HarmonyPrefix]
    public static bool Exhausted_Prefix()
    {
        if (Plugin.Enabled.Value && _npcNextActionDepth > 0)
        {
            return false;
        }

        return true;
    }
}

