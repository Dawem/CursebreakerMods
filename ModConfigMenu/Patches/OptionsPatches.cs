using System;
using System.Collections.Generic;
using HarmonyLib;
using LUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigMenu.Patches;

[HarmonyPatch(typeof(UI_Options))]
public static class OptionsPatches
{
    public static int ModsButtonIndex { get; private set; } = 6;
    public static int ExitButtonIndex { get; private set; } = 8;

    private static readonly AccessTools.FieldRef<UI_Options, LUI_ScrollView> ScrollViewLeftTabRef =
        AccessTools.FieldRefAccess<UI_Options, LUI_ScrollView>("scrollView_LeftTab");

    private static readonly AccessTools.FieldRef<UI_Options, LUI_ScrollView> ScrollViewRightTabRef =
        AccessTools.FieldRefAccess<UI_Options, LUI_ScrollView>("scrollView_RightTab");

    private static readonly AccessTools.FieldRef<UI_Options, LUIRect> ContainerContentRef =
        AccessTools.FieldRefAccess<UI_Options, LUIRect>("container_content");

    private static readonly AccessTools.FieldRef<UI_Options, LUIRect> ContainerOptionButtonsRef =
        AccessTools.FieldRefAccess<UI_Options, LUIRect>("container_optionButtons");

    private static readonly AccessTools.FieldRef<UI_Options, LUI_Text> TextTitleRef =
        AccessTools.FieldRefAccess<UI_Options, LUI_Text>("text_title");

    private static readonly AccessTools.FieldRef<UI_Options, List<LUI_Button>> ButtonsRef =
        AccessTools.FieldRefAccess<UI_Options, List<LUI_Button>>("buttons");

    private static readonly AccessTools.FieldRef<UI_Options, int> SelectedMenuRef =
        AccessTools.FieldRefAccess<UI_Options, int>("selectedMenu");

    private static readonly AccessTools.FieldRef<UI_Options, LUI_Image> ImageButtonHighlightRef =
        AccessTools.FieldRefAccess<UI_Options, LUI_Image>("image_buttonHighlight");

    private static readonly AccessTools.FieldRef<UI_Options, List<int>> ControllerCycleMenusRef =
        AccessTools.FieldRefAccess<UI_Options, List<int>>("controllerCycleMenus");

    private static readonly Action<UI_Options> RefreshControllerNavigationDelegate =
        AccessTools.MethodDelegate<Action<UI_Options>>(
            AccessTools.Method(typeof(UI_Options), "RefreshOptionsControllerNavigation"));

    [HarmonyPatch(nameof(UI_Options.SetupWindow))]
    [HarmonyPostfix]
    public static void SetupWindow_Postfix(UI_Options __instance)
    {
        LUI_ScrollView leftTab = ScrollViewLeftTabRef(__instance);
        List<LUI_Button> buttons = ButtonsRef(__instance);
        List<int> cycleMenus = ControllerCycleMenusRef(__instance);

        if (leftTab == null || buttons == null || LUI_Core.instance == null)
        {
            return;
        }

        Transform leftTabContent = leftTab.GetContentTransform();
        if (leftTabContent == null)
        {
            return;
        }

        // Add "Mods" tab button
        LUI_Button modsButton = LUI_Core.instance.AddButton(
            "invisible",
            "Mods",
            Position.topLeft,
            Position.topLeft,
            Vector2.zero,
            new Vector2(145f, 30f),
            __instance.SelectMenu,
            new object[] { Plugin.Menu_Mods },
            leftTabContent);

        // Insert at index 6 (right after Online (5), before Pause Game / Restore Backup (now 7) and Exit (now 8))
        int insertIndex = Mathf.Min(6, buttons.Count);
        buttons.Insert(insertIndex, modsButton);

        ModsButtonIndex = buttons.IndexOf(modsButton);
        ExitButtonIndex = buttons.FindIndex(b => b?.spawnedObject != null && b.spawnedObject.name.Contains("Exit"));
        if (ExitButtonIndex < 0)
        {
            ExitButtonIndex = buttons.Count - 1;
        }

        // Add to controller cycle list (LB / RB)
        if (cycleMenus != null && !cycleMenus.Contains(Plugin.Menu_Mods))
        {
            int quitIdx = cycleMenus.IndexOf(UI_Options.Menu_QuitGame);
            if (quitIdx >= 0)
            {
                cycleMenus.Insert(quitIdx, Plugin.Menu_Mods);
            }
            else
            {
                cycleMenus.Add(Plugin.Menu_Mods);
            }
        }

        // Re-layout all buttons evenly inside the LeftTab "Buttons" container
        Transform buttonsContainerTr = leftTabContent.Find("Buttons");
        float availableHeight = 405f;
        Vector2 buttonSize = new Vector2(145f, availableHeight / buttons.Count);
        float currentY = 0f;

        foreach (LUI_Button btn in buttons)
        {
            if (btn == null)
            {
                continue;
            }

            if (buttonsContainerTr != null)
            {
                btn.SetParent(buttonsContainerTr);
            }

            btn.SetSize(buttonSize);
            btn.SetPosition(0f, currentY);
            currentY -= buttonSize.y;

            btn.SetHighlightSprite("whiteHighlight");
            float h = btn.GetSize().y;
            const float maxHeight = 50f;
            if (h > maxHeight)
            {
                btn.SetSize(btn.GetSize().x, maxHeight);
                btn.SetPosition(new Vector2(btn.GetPosition().x, btn.GetPosition().y - 0.5f * (h - maxHeight)));
            }
        }

        leftTab.SetContentHeight(Mathf.Max(400f, buttons.Count * 45f));
        RefreshControllerNavigationDelegate?.Invoke(__instance);
    }

    [HarmonyPatch("GetButtonIndexForMenu")]
    [HarmonyPrefix]
    public static bool GetButtonIndexForMenu_Prefix(int menu, ref int __result)
    {
        if (menu == Plugin.Menu_Mods)
        {
            __result = ModsButtonIndex;
            return false;
        }

        if (menu == UI_Options.Menu_QuitGame)
        {
            __result = ExitButtonIndex;
            return false;
        }

        return true;
    }

    [HarmonyPatch("CreateContent")]
    [HarmonyPostfix]
    public static void CreateContent_Postfix(UI_Options __instance)
    {
        int selected = SelectedMenuRef(__instance);
        List<LUI_Button> buttons = ButtonsRef(__instance);

        if (selected == Plugin.Menu_Mods)
        {
            LUI_Text title = TextTitleRef(__instance);
            if (title != null)
            {
                title.SetText("Mod Settings");
            }

            LUIRect optionButtons = ContainerOptionButtonsRef(__instance);
            if (optionButtons != null)
            {
                optionButtons.SetActive(true);
            }

            LUIRect containerContent = ContainerContentRef(__instance);
            LUI_ScrollView rightTab = ScrollViewRightTabRef(__instance);

            UI_ModSettingsView.Render(__instance, containerContent, rightTab);

            // Highlight the "Mods" button
            if (buttons != null && ModsButtonIndex >= 0 && ModsButtonIndex < buttons.Count && buttons[ModsButtonIndex] != null)
            {
                LUI_Image highlight = ImageButtonHighlightRef(__instance);
                if (highlight != null)
                {
                    highlight.Destroy();
                }

                highlight = LUI_Core.instance.AddImage(
                    "Options/buttonHighlight",
                    Position.center,
                    Position.center,
                    Vector2.zero,
                    Vector2.zero,
                    buttons[ModsButtonIndex]);

                highlight.SetStretch(0f, 1f, 0f, 1f);
                ImageButtonHighlightRef(__instance) = highlight;
            }

            RefreshControllerNavigationDelegate?.Invoke(__instance);
            __instance.RefreshControllerHints();
        }
        else if (selected == UI_Options.Menu_QuitGame)
        {
            // Vanilla CreateContent assigns obj = buttons[7], but Exit is now at ExitButtonIndex!
            if (buttons != null && ExitButtonIndex >= 0 && ExitButtonIndex < buttons.Count && buttons[ExitButtonIndex] != null)
            {
                LUI_Image highlight = ImageButtonHighlightRef(__instance);
                if (highlight != null)
                {
                    highlight.Destroy();
                }

                highlight = LUI_Core.instance.AddImage(
                    "Options/buttonHighlight",
                    Position.center,
                    Position.center,
                    Vector2.zero,
                    Vector2.zero,
                    buttons[ExitButtonIndex]);

                highlight.SetStretch(0f, 1f, 0f, 1f);
                ImageButtonHighlightRef(__instance) = highlight;
            }
        }
    }

    [HarmonyPatch("ApplySettings")]
    [HarmonyPrefix]
    public static bool ApplySettings_Prefix(UI_Options __instance)
    {
        int selected = SelectedMenuRef(__instance);
        if (selected == Plugin.Menu_Mods)
        {
            ModManager.SaveAllConfigs();
            UI_Notifications.instance?.ShowNotification("Mod settings saved!");
            AudioHandler.instance?.Play2DSound(12);
            return false;
        }

        return true;
    }

    [HarmonyPatch("Reset")]
    [HarmonyPrefix]
    public static bool Reset_Prefix(UI_Options __instance)
    {
        int selected = SelectedMenuRef(__instance);
        if (selected == Plugin.Menu_Mods)
        {
            var mods = ModManager.GetInstalledMods();
            int curIdx = UI_ModSettingsView.SelectedModIndex;
            if (mods.Count > 0 && curIdx >= 0 && curIdx < mods.Count)
            {
                ModInfoModel mod = mods[curIdx];
                ModManager.ResetModToDefaults(mod);
                UI_Notifications.instance?.ShowNotification($"Reset {mod.Name} to default settings!");
                AudioHandler.instance?.Play2DSound(12);
                __instance.SelectMenu(new object[] { Plugin.Menu_Mods });
            }
            return false;
        }

        return true;
    }

    [HarmonyPatch(nameof(UI_Options.InputActive))]
    [HarmonyPostfix]
    public static void InputActive_Postfix(ref bool __result)
    {
        if (!__result)
        {
            if (UI_ModSettingsView.IsCapturingKeybind)
            {
                __result = true;
                return;
            }

            GameObject currentSelected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (currentSelected != null && currentSelected.GetComponent<InputField>() != null)
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(nameof(UI_Options.OnDeactivate))]
    [HarmonyPostfix]
    public static void OnDeactivate_Postfix()
    {
        UI_ModSettingsView.ResetState();
    }
}
