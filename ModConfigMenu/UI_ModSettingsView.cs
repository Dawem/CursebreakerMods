using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using LUI;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigMenu;

public static class UI_ModSettingsView
{
    private static int _selectedModIndex;
    private static string _searchFilter = string.Empty;
    private static bool _showModPicker;

    // Keybind capturing state
    private static bool _isCapturingKeybind;
    private static float _captureStartupTimer;
    private static KeyCode _candidateModifier = KeyCode.None;
    private static KeyCode _pendingKey = KeyCode.None;
    private static KeyCode[] _pendingModifiers = null;
    private static ConfigEntryBase _capturingEntry;
    private static LUI_Button _capturingButton;
    private static LUIRect _keybindOverlay;
    private static LUI_Text _keybindOverlayText;

    // Mod picker overlay
    private static LUIRect _modPickerOverlay;

    public static int SelectedModIndex => _selectedModIndex;

    public static void ResetState()
    {
        _searchFilter = string.Empty;
        _showModPicker = false;
        CancelKeybindCapture();
        CloseModPicker();
    }

    public static void Render(UI_Options options, LUIRect containerContent, LUI_ScrollView scrollView)
    {
        if (options == null || containerContent == null || scrollView == null)
        {
            return;
        }

        CloseModPicker();
        CancelKeybindCapture();

        Transform parent = containerContent.GetParentTransform();
        List<ModInfoModel> mods = ModManager.GetInstalledMods();

        if (mods == null || mods.Count == 0)
        {
            LUI_Core.AddText(
                16,
                TextAnchor.MiddleCenter,
                Position.center,
                Position.center,
                new Vector2(0f, 0f),
                new Vector2(380f, 60f),
                parent,
                "No configurable BepInEx mods detected.");
            scrollView.SetContentHeight(360f);
            return;
        }

        if (_selectedModIndex < 0 || _selectedModIndex >= mods.Count)
        {
            _selectedModIndex = 0;
        }

        ModInfoModel currentMod = mods[_selectedModIndex];

        float currentY = -5f;

        // 1. Top Navigation Bar: [ < ] [ Mod Name (1/N) ] [ > ]
        LUI_Core.instance.AddButton(
            "<",
            Position.topLeft,
            Position.topLeft,
            new Vector2(5f, currentY),
            new Vector2(32f, 32f),
            _ =>
            {
                _selectedModIndex = (_selectedModIndex - 1 + mods.Count) % mods.Count;
                _searchFilter = string.Empty;
                AudioHandler.instance?.Play2DSound(12);
                RebuildContent(options);
            },
            null,
            parent);

        string modTitle = $"{currentMod.Name} ({_selectedModIndex + 1}/{mods.Count})";
        if (modTitle.Length > 36)
        {
            modTitle = modTitle.Substring(0, 33) + "...";
        }

        LUI_Core.instance.AddButton(
            modTitle,
            Position.topLeft,
            Position.topLeft,
            new Vector2(42f, currentY),
            new Vector2(306f, 32f),
            _ =>
            {
                AudioHandler.instance?.Play2DSound(12);
                ToggleModPicker(options, mods);
            },
            null,
            parent);

        LUI_Core.instance.AddButton(
            ">",
            Position.topLeft,
            Position.topLeft,
            new Vector2(353f, currentY),
            new Vector2(32f, 32f),
            _ =>
            {
                _selectedModIndex = (_selectedModIndex + 1) % mods.Count;
                _searchFilter = string.Empty;
                AudioHandler.instance?.Play2DSound(12);
                RebuildContent(options);
            },
            null,
            parent);

        currentY -= 36f;

        // 2. Mod Metadata details & settings count
        string versionStr = !string.IsNullOrEmpty(currentMod.Version) ? $"v{currentMod.Version} | " : "";
        string countStr = $"{currentMod.TotalSettingsCount} setting{(currentMod.TotalSettingsCount == 1 ? "" : "s")}";
        LUI_Core.AddText(
            13,
            TextAnchor.MiddleLeft,
            Position.topLeft,
            Position.topLeft,
            new Vector2(8f, currentY),
            new Vector2(380f, 20f),
            parent,
            $"<color=#AAAAAA>{versionStr}{currentMod.Guid} ({countStr})</color>");

        currentY -= 26f;

        // 3. Search Bar (if mod has more than 3 settings or a search filter is already active)
        if (currentMod.TotalSettingsCount > 3 || !string.IsNullOrEmpty(_searchFilter))
        {
            LUI_Core.AddText(
                14,
                TextAnchor.MiddleLeft,
                Position.topLeft,
                Position.topLeft,
                new Vector2(8f, currentY),
                new Vector2(60f, 25f),
                parent,
                "<color=#DDDDDD>Search:</color>");

            LUI_InputField searchBox = LUI_Core.instance.AddInputField(
                Position.topLeft,
                Position.topLeft,
                new Vector2(70f, currentY),
                new Vector2(275f, 26f),
                parent,
                TextAnchor.MiddleLeft);

            searchBox.SetText(_searchFilter);
            searchBox.inputField.onValueChanged.AddListener(newVal =>
            {
                _searchFilter = newVal;
                RebuildContent(options);
            });

            if (!string.IsNullOrEmpty(_searchFilter))
            {
                LUI_Core.instance.AddButton(
                    "x",
                    Position.topLeft,
                    Position.topLeft,
                    new Vector2(352f, currentY),
                    new Vector2(26f, 26f),
                    _ =>
                    {
                        _searchFilter = string.Empty;
                        RebuildContent(options);
                    },
                    null,
                    parent);
            }

            currentY -= 32f;
        }

        // 4. If current mod has no settings
        if (currentMod.Sections.Count == 0 || currentMod.TotalSettingsCount == 0)
        {
            currentY -= 20f;
            LUI_Core.AddText(
                15,
                TextAnchor.MiddleCenter,
                Position.topLeft,
                Position.topLeft,
                new Vector2(5f, currentY),
                new Vector2(380f, 40f),
                parent,
                "<color=#BBBBBB>This mod does not have any configurable BepInEx options.</color>");
            currentY -= 60f;
            scrollView.SetContentHeight(Mathf.Max(360f, Mathf.Abs(currentY) + 20f));
            return;
        }

        // 5. Render Sections and Config Entries
        int renderedSettingsCount = 0;
        string query = _searchFilter.Trim().ToLowerInvariant();
        bool isFirstSection = true;

        foreach (ConfigSectionGroup section in currentMod.Sections)
        {
            // Filter entries according to search query
            List<ConfigEntryBase> matchingEntries = section.Entries
                .Where(e =>
                {
                    if (string.IsNullOrEmpty(query)) return true;
                    string key = e.Definition?.Key?.ToLowerInvariant() ?? "";
                    string desc = e.Description?.Description?.ToLowerInvariant() ?? "";
                    string sec = section.SectionName?.ToLowerInvariant() ?? "";
                    return key.Contains(query) || desc.Contains(query) || sec.Contains(query);
                })
                .ToList();

            if (matchingEntries.Count == 0)
            {
                continue;
            }

            // Generous spacing before subsequent section headers so controls aren't squished
            if (!isFirstSection)
            {
                currentY -= 18f;
            }
            else
            {
                currentY -= 6f;
            }
            isFirstSection = false;

            LUI_Core.AddText(
                16,
                TextAnchor.MiddleLeft,
                Position.topLeft,
                Position.topLeft,
                new Vector2(8f, currentY),
                new Vector2(380f, 26f),
                parent,
                $"<color=#E5B73B><b>── {section.SectionName} ──</b></color>");

            currentY -= 32f;

            foreach (ConfigEntryBase entry in matchingEntries)
            {
                RenderConfigEntry(entry, parent, ref currentY);
                renderedSettingsCount++;
            }
        }

        if (renderedSettingsCount == 0 && !string.IsNullOrEmpty(_searchFilter))
        {
            currentY -= 15f;
            LUI_Core.AddText(
                15,
                TextAnchor.MiddleCenter,
                Position.topLeft,
                Position.topLeft,
                new Vector2(5f, currentY),
                new Vector2(380f, 40f),
                parent,
                $"<color=#BBBBBB>No settings matching \"{_searchFilter}\"</color>");
            currentY -= 50f;
        }

        currentY -= 40f;
        scrollView.SetContentHeight(Mathf.Max(360f, Mathf.Abs(currentY) + 20f));
    }

    private static void RenderConfigEntry(ConfigEntryBase entry, Transform parent, ref float currentY)
    {
        Type settingType = entry.SettingType;
        string keyName = entry.Definition?.Key ?? "Unknown";

        // Info tooltip icon
        void AddTooltipIcon(float xPos, float yPos)
        {
            string tooltip = FormatTooltip(entry);
            if (!string.IsNullOrEmpty(tooltip))
            {
                LUI_Image infoImg = LUI_Core.instance.AddImage(
                    "Info",
                    Position.topLeft,
                    Position.topLeft,
                    new Vector2(xPos, yPos),
                    new Vector2(16f, 16f),
                    parent,
                    raycast: true);

                infoImg.AddMouseHover(
                    _ => UI_Cursor.instance?.SetUIMouseHoverText(tooltip),
                    null,
                    _ => UI_Cursor.instance?.SetUIMouseHoverText(""),
                    null);
            }
        }

        // Case 0: Action Button (entry has an Action or Action<ConfigEntryBase> in Tags)
        Action buttonAction = entry.Description?.Tags?.OfType<Action>().FirstOrDefault();
        if (buttonAction != null)
        {
            LUI_Core.instance.AddButton(
                keyName,
                Position.topLeft,
                Position.topLeft,
                new Vector2(8f, currentY),
                new Vector2(350f, 28f),
                _ =>
                {
                    AudioHandler.instance?.Play2DSound(12);
                    buttonAction();
                },
                null,
                parent);

            AddTooltipIcon(365f, currentY - 5f);
            currentY -= 36f;
            return;
        }

        Action<ConfigEntryBase> buttonWithEntryAction = entry.Description?.Tags?.OfType<Action<ConfigEntryBase>>().FirstOrDefault();
        if (buttonWithEntryAction != null)
        {
            LUI_Core.instance.AddButton(
                keyName,
                Position.topLeft,
                Position.topLeft,
                new Vector2(8f, currentY),
                new Vector2(350f, 28f),
                _ =>
                {
                    AudioHandler.instance?.Play2DSound(12);
                    buttonWithEntryAction(entry);
                },
                null,
                parent);

            AddTooltipIcon(365f, currentY - 5f);
            currentY -= 36f;
            return;
        }

        // Case 1: Boolean Toggle
        if (settingType == typeof(bool))
        {
            bool currentVal = (bool)entry.BoxedValue;
            // AddToggleButton internally offsets Y by -25f, so pass currentY + 25f to align it at currentY
            LUI_ToggleButton toggle = LUI_Core.instance.AddToggleButton(
                keyName,
                Position.topLeft,
                Position.topLeft,
                new Vector2(8f, currentY + 25f),
                parent,
                vars =>
                {
                    var btn = (LUI_ToggleButton)vars[0];
                    btn.Toggle();
                    entry.BoxedValue = btn.isOn;
                    ModManager.MarkConfigDirty(entry.ConfigFile);
                });

            Text labelText = toggle.spawnedObject?.GetComponentInChildren<Text>();
            if (labelText != null)
            {
                labelText.rectTransform.anchoredPosition = new Vector2(28f, 0f);
                labelText.rectTransform.sizeDelta = new Vector2(320f, 22f);
            }

            if (currentVal)
            {
                toggle.Toggle(false);
            }

            AddTooltipIcon(365f, currentY - 2f);
            currentY -= 32f;
            return;
        }

        // Case 2: KeyboardShortcut Keybind
        if (settingType == typeof(KeyboardShortcut))
        {
            KeyboardShortcut shortcut = (KeyboardShortcut)entry.BoxedValue;
            string shortcutLabel = shortcut.MainKey == KeyCode.None ? "None" : shortcut.ToString();

            LUI_Core.AddText(
                15,
                TextAnchor.MiddleLeft,
                Position.topLeft,
                Position.topLeft,
                new Vector2(8f, currentY),
                new Vector2(180f, 25f),
                parent,
                keyName);

            LUI_Button keyBtn = null;
            keyBtn = LUI_Core.instance.AddButton(
                shortcutLabel,
                Position.topLeft,
                Position.topLeft,
                new Vector2(195f, currentY),
                new Vector2(135f, 26f),
                _ => StartKeybindCapture(entry, keyBtn),
                null,
                parent);

            // Clear button
            LUI_Core.instance.AddButton(
                "x",
                Position.topLeft,
                Position.topLeft,
                new Vector2(335f, currentY),
                new Vector2(25f, 26f),
                _ =>
                {
                    entry.BoxedValue = KeyboardShortcut.Empty;
                    keyBtn.text.SetText("None");
                    ModManager.MarkConfigDirty(entry.ConfigFile);
                    AudioHandler.instance?.Play2DSound(13);
                },
                null,
                parent);

            AddTooltipIcon(368f, currentY - 5f);
            currentY -= 34f;
            return;
        }

        // Case 3: Numeric with Range (AcceptableValueRange<T>)
        if (entry.Description?.AcceptableValues is AcceptableValueBase rangeBase &&
            rangeBase.GetType().IsGenericType &&
            rangeBase.GetType().GetGenericTypeDefinition() == typeof(AcceptableValueRange<>))
        {
            object minObj = rangeBase.GetType().GetProperty("MinValue")?.GetValue(rangeBase);
            object maxObj = rangeBase.GetType().GetProperty("MaxValue")?.GetValue(rangeBase);

            if (minObj != null && maxObj != null)
            {
                float minVal = Convert.ToSingle(minObj);
                float maxVal = Convert.ToSingle(maxObj);
                float curVal = Convert.ToSingle(entry.BoxedValue);
                bool isInteger = settingType == typeof(int) || settingType == typeof(long) || settingType == typeof(short);

                string formattedVal = isInteger ? $"{Mathf.RoundToInt(curVal)}" : $"{curVal:0.##}";
                LUI_Text label = LUI_Core.AddText(
                    15,
                    TextAnchor.MiddleLeft,
                    Position.topLeft,
                    Position.topLeft,
                    new Vector2(8f, currentY),
                    new Vector2(350f, 22f),
                    parent,
                    $"{keyName}: <color=#FFFFFF>{formattedVal}</color>");

                AddTooltipIcon(365f, currentY - 2f);
                currentY -= 24f;

                LUI_Slider slider = LUI_Core.instance.AddSlider(
                    "",
                    minVal,
                    maxVal,
                    Position.topLeft,
                    Position.topLeft,
                    new Vector2(8f, currentY),
                    new Vector2(345f, 20f),
                    parent);

                slider.SetValue(curVal);
                slider.slider.wholeNumbers = isInteger;

                slider.slider.onValueChanged.AddListener(val =>
                {
                    if (isInteger)
                    {
                        int intVal = Mathf.RoundToInt(val);
                        entry.BoxedValue = intVal;
                        label.SetText($"{keyName}: <color=#FFFFFF>{intVal}</color>");
                    }
                    else
                    {
                        float rounded = (float)Math.Round(val, 2);
                        entry.BoxedValue = rounded;
                        label.SetText($"{keyName}: <color=#FFFFFF>{rounded:0.##}</color>");
                    }
                    ModManager.MarkConfigDirty(entry.ConfigFile);
                });

                currentY -= 34f;
                return;
            }
        }

        // Case 4: Enum or AcceptableValueList<T>
        if (settingType.IsEnum)
        {
            Array enumValues = Enum.GetValues(settingType);
            string[] names = Enum.GetNames(settingType);
            int curIndex = Array.IndexOf(names, entry.BoxedValue.ToString());
            if (curIndex < 0) curIndex = 0;

            LUI_Core.AddText(
                15,
                TextAnchor.MiddleLeft,
                Position.topLeft,
                Position.topLeft,
                new Vector2(8f, currentY),
                new Vector2(175f, 26f),
                parent,
                keyName);

            string currentDisplayName = names[curIndex];
            LUI_Button cycleBtn = null;
            cycleBtn = LUI_Core.instance.AddButton(
                currentDisplayName,
                Position.topLeft,
                Position.topLeft,
                new Vector2(190f, currentY),
                new Vector2(165f, 26f),
                _ =>
                {
                    curIndex = (curIndex + 1) % enumValues.Length;
                    object nextVal = enumValues.GetValue(curIndex);
                    entry.BoxedValue = nextVal;
                    cycleBtn.text.SetText(names[curIndex]);
                    ModManager.MarkConfigDirty(entry.ConfigFile);
                    AudioHandler.instance?.Play2DSound(12);
                },
                null,
                parent);

            AddTooltipIcon(365f, currentY - 5f);
            currentY -= 34f;
            return;
        }

        // Case 5: General String / Numeric / Unsupported fallback
        {
            LUI_Core.AddText(
                15,
                TextAnchor.MiddleLeft,
                Position.topLeft,
                Position.topLeft,
                new Vector2(8f, currentY),
                new Vector2(175f, 26f),
                parent,
                keyName);

            LUI_InputField inputField = LUI_Core.instance.AddInputField(
                Position.topLeft,
                Position.topLeft,
                new Vector2(190f, currentY),
                new Vector2(165f, 28f),
                parent,
                TextAnchor.MiddleLeft);

            inputField.SetText(entry.GetSerializedValue() ?? "");
            inputField.inputField.onEndEdit.AddListener(newText =>
            {
                entry.SetSerializedValue(newText);
                inputField.SetText(entry.GetSerializedValue() ?? "");
                ModManager.MarkConfigDirty(entry.ConfigFile);
            });

            AddTooltipIcon(365f, currentY - 6f);
            currentY -= 36f;
        }
    }

    private static string FormatTooltip(ConfigEntryBase entry)
    {
        string desc = entry.Description?.Description;
        string defStr = entry.DefaultValue != null
            ? TomlTypeConverter.ConvertToString(entry.DefaultValue, entry.SettingType)
            : "none";

        string result = "";
        if (!string.IsNullOrEmpty(desc))
        {
            result += desc.Trim() + "\n";
        }

        result += $"<color=#F5C542>Type:</color> {entry.SettingType.Name}  |  <color=#F5C542>Default:</color> {defStr}";
        return result;
    }

    private static void RebuildContent(UI_Options options)
    {
        // Calling SelectMenu re-triggers CreateContent and updates UI cleanly
        options.SelectMenu(new object[] { Plugin.Menu_Mods });
    }

    #region Keybind Capture Modal

    public static bool IsCapturingKeybind => _isCapturingKeybind;

    private static void StartKeybindCapture(ConfigEntryBase entry, LUI_Button button)
    {
        _isCapturingKeybind = true;
        _captureStartupTimer = 0.25f;
        _candidateModifier = KeyCode.None;
        _pendingKey = KeyCode.None;
        _pendingModifiers = null;
        _capturingEntry = entry;
        _capturingButton = button;

        if (KeyboardInputs.instance != null)
        {
            KeyboardInputs.instance.changingInputs = true;
        }

        button.text.SetText("<color=yellow>Press key...</color>");

        UI_Options options = UI_Options.instance;
        if (options == null || options.container == null)
        {
            return;
        }

        CloseKeybindOverlay();

        _keybindOverlay = LUI_Core.instance.AddImage(
            "DefaultUI/DefaultWindowBackground",
            Position.center,
            Position.center,
            new Vector2(0f, 0f),
            new Vector2(460f, 180f),
            options.container,
            raycast: true);

        string entryName = entry.Definition?.Key ?? "Keybind";
        _keybindOverlayText = LUI_Core.AddText(
            17,
            TextAnchor.MiddleCenter,
            Position.center,
            Position.center,
            new Vector2(0f, 20f),
            new Vector2(440f, 80f),
            _keybindOverlay,
            $"Press any key or combination for\n<color=#F5C542><b>'{entryName}'</b></color>\n\n<size=13><color=#AAAAAA>[ESC] Cancel  |  [Backspace / Delete] Clear</color></size>");

        LUI_Core.instance.AddButton(
            "Cancel",
            Position.bottomMiddle,
            Position.bottomMiddle,
            new Vector2(0f, 15f),
            new Vector2(120f, 30f),
            _ => CancelKeybindCapture(),
            null,
            _keybindOverlay);
    }

    public static void UpdateKeybindCapture()
    {
        if (!_isCapturingKeybind || _capturingEntry == null)
        {
            return;
        }

        // Keep game inputs suppressed while capturing
        if (KeyboardInputs.instance != null)
        {
            KeyboardInputs.instance.changingInputs = true;
        }

        // If we detected a key, wait until all pressed keys are released before committing BoxedValue
        // This ensures the action (like opening Combined Inventory) never fires on the frame it was assigned!
        if (_pendingKey != KeyCode.None)
        {
            if (Input.GetKey(_pendingKey))
            {
                return;
            }

            if (_pendingModifiers != null && _pendingModifiers.Any(m => Input.GetKey(m)))
            {
                return;
            }

            // Keys are released; now safely commit the new shortcut
            var newShortcut = new KeyboardShortcut(_pendingKey, _pendingModifiers ?? Array.Empty<KeyCode>());
            _capturingEntry.BoxedValue = newShortcut;
            ModManager.MarkConfigDirty(_capturingEntry.ConfigFile);

            FinishKeybindCapture();
            return;
        }

        // 1. Debounce timer: wait after click before accepting any new inputs
        if (_captureStartupTimer > 0f)
        {
            _captureStartupTimer -= Time.unscaledDeltaTime;
            return;
        }

        // 2. Ignore if mouse button 0 is still held down from the initial click
        if (Input.GetMouseButton(0))
        {
            return;
        }

        // 3. Escape cancels rebind
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CancelKeybindCapture();
            return;
        }

        // 4. Backspace or Delete clears the keybind
        if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete))
        {
            _capturingEntry.BoxedValue = KeyboardShortcut.Empty;
            _capturingButton?.text.SetText("None");
            ModManager.MarkConfigDirty(_capturingEntry.ConfigFile);
            AudioHandler.instance?.Play2DSound(13);
            FinishKeybindCapture();
            return;
        }

        // 5. Track standalone modifier keys (e.g. binding just LeftControl, LeftShift, etc.)
        KeyCode[] modifierCandidates = new[]
        {
            KeyCode.LeftControl, KeyCode.RightControl,
            KeyCode.LeftShift, KeyCode.RightShift,
            KeyCode.LeftAlt, KeyCode.RightAlt
        };

        foreach (KeyCode modKey in modifierCandidates)
        {
            if (Input.GetKeyDown(modKey))
            {
                _candidateModifier = modKey;
            }
        }

        // 6. Detect currently held modifiers for combinations
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        var activeModifiers = new List<KeyCode>();
        if (ctrl) activeModifiers.Add(KeyCode.LeftControl);
        if (shift) activeModifiers.Add(KeyCode.LeftShift);
        if (alt) activeModifiers.Add(KeyCode.LeftAlt);

        // 7. Check for non-modifier key presses
        KeyCode detectedKey = KeyCode.None;
        foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
        {
            // Skip Mouse0 (left click) and modifiers from being detected as main non-modifier keys
            if (k == KeyCode.Mouse0 ||
                k is KeyCode.LeftControl or KeyCode.RightControl or
                     KeyCode.LeftShift or KeyCode.RightShift or
                     KeyCode.LeftAlt or KeyCode.RightAlt or
                     KeyCode.None)
            {
                continue;
            }

            if (Input.GetKeyDown(k))
            {
                detectedKey = k;
                break;
            }
        }

        if (detectedKey != KeyCode.None)
        {
            _candidateModifier = KeyCode.None;
            _pendingKey = detectedKey;
            _pendingModifiers = activeModifiers.ToArray();
            var displayShortcut = new KeyboardShortcut(_pendingKey, _pendingModifiers);
            _capturingButton?.text.SetText(displayShortcut.ToString());
            CloseKeybindOverlay();
            AudioHandler.instance?.Play2DSound(12);
            return;
        }

        // 8. If a modifier key was tapped and released without pressing another key, bind it standalone
        if (_candidateModifier != KeyCode.None && Input.GetKeyUp(_candidateModifier))
        {
            _pendingKey = _candidateModifier;
            _pendingModifiers = Array.Empty<KeyCode>();
            var displayShortcut = new KeyboardShortcut(_pendingKey);
            _capturingButton?.text.SetText(displayShortcut.ToString());
            CloseKeybindOverlay();
            AudioHandler.instance?.Play2DSound(12);
        }
    }

    public static void CancelKeybindCapture()
    {
        if (_isCapturingKeybind && _capturingEntry != null && _capturingButton != null)
        {
            var s = (KeyboardShortcut)_capturingEntry.BoxedValue;
            _capturingButton.text.SetText(s.MainKey == KeyCode.None ? "None" : s.ToString());
        }
        FinishKeybindCapture();
    }

    private static void CloseKeybindOverlay()
    {
        if (_keybindOverlay != null)
        {
            _keybindOverlay.Destroy();
            _keybindOverlay = null;
        }
    }

    private static void FinishKeybindCapture()
    {
        _isCapturingKeybind = false;
        _captureStartupTimer = 0f;
        _candidateModifier = KeyCode.None;
        _pendingKey = KeyCode.None;
        _pendingModifiers = null;
        _capturingEntry = null;
        _capturingButton = null;

        if (KeyboardInputs.instance != null)
        {
            KeyboardInputs.instance.changingInputs = false;
        }

        CloseKeybindOverlay();
    }

    #endregion

    #region Mod Picker Quick Switcher Modal

    private static void ToggleModPicker(UI_Options options, List<ModInfoModel> mods)
    {
        if (_showModPicker)
        {
            CloseModPicker();
        }
        else
        {
            OpenModPicker(options, mods);
        }
    }

    private static void OpenModPicker(UI_Options options, List<ModInfoModel> mods)
    {
        CloseModPicker();
        _showModPicker = true;

        if (options?.container == null)
        {
            return;
        }

        _modPickerOverlay = LUI_Core.instance.AddImage(
            "DefaultUI/DefaultWindowBackground",
            Position.center,
            Position.center,
            new Vector2(0f, 0f),
            new Vector2(460f, 440f),
            options.container,
            raycast: true);

        LUI_Core.AddText(
            18,
            TextAnchor.MiddleCenter,
            Position.topMiddle,
            Position.topMiddle,
            new Vector2(0f, -20f),
            new Vector2(400f, 30f),
            _modPickerOverlay,
            "<color=#F5C542><b>Select Mod</b></color>");

        LUI_ScrollView pickerScroll = LUI_Core.instance.AddScrollView(
            null,
            Position.topLeft,
            Position.topLeft,
            new Vector2(20f, -60f),
            new Vector2(420f, 320f),
            new Vector2(400f, Mathf.Max(320f, mods.Count * 38f + 10f)),
            _modPickerOverlay);

        Transform contentParent = pickerScroll.GetContentTransform();
        float y = -5f;

        for (int i = 0; i < mods.Count; i++)
        {
            int index = i;
            ModInfoModel m = mods[index];
            bool isCurrent = index == _selectedModIndex;

            string label = $"{m.Name} ({m.TotalSettingsCount})";
            if (isCurrent)
            {
                label = $"<color=#55FF55>► {label}</color>";
            }

            LUI_Core.instance.AddButton(
                label,
                Position.topLeft,
                Position.topLeft,
                new Vector2(5f, y),
                new Vector2(390f, 32f),
                _ =>
                {
                    _selectedModIndex = index;
                    _searchFilter = string.Empty;
                    CloseModPicker();
                    AudioHandler.instance?.Play2DSound(12);
                    RebuildContent(options);
                },
                null,
                contentParent);

            y -= 36f;
        }

        LUI_Core.instance.AddButton(
            "Close",
            Position.bottomMiddle,
            Position.bottomMiddle,
            new Vector2(0f, 15f),
            new Vector2(120f, 30f),
            _ => CloseModPicker(),
            null,
            _modPickerOverlay);
    }

    public static void CloseModPicker()
    {
        _showModPicker = false;
        if (_modPickerOverlay != null)
        {
            _modPickerOverlay.Destroy();
            _modPickerOverlay = null;
        }
    }

    #endregion
}
