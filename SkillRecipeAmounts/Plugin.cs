using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LUI;
using UnityEngine;

namespace SkillRecipeAmounts;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> AlwaysShowAmount;
    internal static ConfigEntry<bool> ColorCodeByInventory;

    private readonly Harmony _harmony = new(MyPluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Logger = base.Logger;

        Enabled = Config.Bind(
            "General",
            "Enabled",
            true,
            "Whether to display item amount requirements on crafting recipes in the skill menu.");

        AlwaysShowAmount = Config.Bind(
            "General",
            "AlwaysShowAmount",
            true,
            "Whether to show the amount requirement even when only 1 item is required (e.g. '1'). If false, only amounts greater than 1 are displayed.");

        ColorCodeByInventory = Config.Bind(
            "General",
            "ColorCodeByInventory",
            false,
            "Whether to color-code amount text based on whether the player currently has enough of that item in inventory (green if sufficient, red if insufficient). Defaults to false (native white text with outline).");

        _harmony.PatchAll();

        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_NAME} ({MyPluginInfo.PLUGIN_GUID}) v{MyPluginInfo.PLUGIN_VERSION} loaded!");
    }

    /// <summary>
    /// Attaches the item amount text overlay to the ingredient image in the skill milestone list.
    /// </summary>
    public static void AddRecipeItemAmount(LUI_Image image, CraftingHandler.CraftingRecipeItem recipeItem)
    {
        if (Enabled == null || !Enabled.Value || image == null || recipeItem == null || recipeItem.amount <= 0)
            return;

        int amount = recipeItem.amount;
        if (amount <= 1 && AlwaysShowAmount != null && !AlwaysShowAmount.Value)
            return;

        try
        {
            LUI_Text text = LUI_Core.AddText(
                15,
                (TextAnchor)4, // MiddleCenter
                Position.topLeft,
                Position.topLeft,
                new Vector2(5f, -5f),
                new Vector2(20f, 20f),
                image,
                amount.ToString());

            if (text == null)
                return;

            text.AddOutline();

            if (ColorCodeByInventory != null && ColorCodeByInventory.Value && UI_Inventory.instance != null)
            {
                bool hasEnough = UI_Inventory.instance.HasItem(recipeItem.itemId, amount);
                text.SetTextColor(hasEnough ? new Color(0.35f, 1f, 0.35f) : new Color(1f, 0.35f, 0.35f));
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError($"Error adding recipe item amount: {ex}");
        }
    }
}

[HarmonyPatch(typeof(UI_Skills), "ActivateMilestoneList")]
public static class UI_Skills_ActivateMilestoneList_Patch
{
    private static readonly FieldInfo ItemIdField = AccessTools.Field(
        typeof(CraftingHandler.CraftingRecipeItem),
        nameof(CraftingHandler.CraftingRecipeItem.itemId));

    private static readonly MethodInfo RegisterMilestoneButtonMethod = AccessTools.Method(
        typeof(UI_Skills),
        "RegisterMilestoneButton",
        new[] { typeof(LUI_Button), typeof(bool) });

    private static readonly MethodInfo AddButtonMethod = AccessTools.Method(
        typeof(LUI_Core),
        nameof(LUI_Core.AddButton),
        new[]
        {
            typeof(string),
            typeof(string),
            typeof(Position),
            typeof(Position),
            typeof(Vector2),
            typeof(Vector2),
            typeof(Action<object[]>),
            typeof(object[]),
            typeof(object)
        });

    private static readonly MethodInfo HelperMethod = AccessTools.Method(
        typeof(Plugin),
        nameof(Plugin.AddRecipeItemAmount),
        new[] { typeof(LUI_Image), typeof(CraftingHandler.CraftingRecipeItem) });

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);

        CodeInstruction loadItem4Inst = null;
        CodeInstruction loadImage3Inst = null;
        bool patched = false;

        for (int i = 0; i < list.Count; i++)
        {
            var inst = list[i];

            // 1. Identify the instruction that loads item4 (CraftingRecipeItem) by finding the read of CraftingRecipeItem.itemId
            if (inst.opcode == OpCodes.Ldfld && inst.operand is FieldInfo f && f == ItemIdField && i > 0)
            {
                loadItem4Inst = list[i - 1].Clone();
            }

            // 2. Identify the instruction that loads lUI_Image3 (LUI_Image) as the last parameter to LUI_Core.AddButton
            if (loadItem4Inst != null && (inst.opcode == OpCodes.Call || inst.opcode == OpCodes.Callvirt) &&
                inst.operand is MethodInfo m && m == AddButtonMethod && i > 0)
            {
                loadImage3Inst = list[i - 1].Clone();
            }

            // 3. Right after RegisterMilestoneButton is called in this loop, inject our helper call
            if (loadItem4Inst != null && loadImage3Inst != null &&
                (inst.opcode == OpCodes.Call || inst.opcode == OpCodes.Callvirt) &&
                inst.operand is MethodInfo regMethod && regMethod == RegisterMilestoneButtonMethod)
            {
                // Inject right after this instruction (i + 1)
                list.Insert(i + 1, new CodeInstruction(loadImage3Inst.opcode, loadImage3Inst.operand));
                list.Insert(i + 2, new CodeInstruction(loadItem4Inst.opcode, loadItem4Inst.operand));
                list.Insert(i + 3, new CodeInstruction(OpCodes.Call, HelperMethod));

                patched = true;
                Plugin.Logger?.LogInfo("Successfully injected AddRecipeItemAmount into UI_Skills.ActivateMilestoneList");
                break;
            }
        }

        if (!patched)
        {
            Plugin.Logger?.LogWarning("Could not find injection point for AddRecipeItemAmount in UI_Skills.ActivateMilestoneList; returning unmodified IL.");
        }

        return list;
    }
}
