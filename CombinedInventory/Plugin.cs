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
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CombinedInventory;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    internal static ConfigEntry<KeyboardShortcut> ToggleKey;
    internal static ConfigEntry<bool> ShowInventoryButton;
    internal static ConfigEntry<int> GridColumns;
    internal static ConfigEntry<float> WindowWidth;
    internal static ConfigEntry<float> WindowHeight;
    internal static ConfigEntry<bool> SeamlessInventory;
    internal static ConfigEntry<string> HiddenBagIds;
    internal static ConfigEntry<bool> PreventBagOpenOnPickup;

    private readonly Harmony _harmony = new(MyPluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Logger = base.Logger;

        ToggleKey = Config.Bind(
            "Keybinds",
            "ToggleKey",
            new KeyboardShortcut(KeyCode.B),
            "Key to toggle the Combined Inventory window.");

        ShowInventoryButton = Config.Bind(
            "UI",
            "ShowInventoryButton",
            true,
            "Whether to show a 'Combined' toggle button inside the vanilla inventory header.");

        GridColumns = Config.Bind(
            "UI",
            "GridColumns",
            5,
            "Default number of slot columns in the Combined Inventory window (resizing window will dynamically change this).");

        WindowWidth = Config.Bind(
            "UI",
            "WindowWidth",
            312f,
            "Default width of the Combined Inventory window.");

        WindowHeight = Config.Bind(
            "UI",
            "WindowHeight",
            450f,
            "Default height of the Combined Inventory window.");

        SeamlessInventory = Config.Bind(
            "UI",
            "SeamlessInventory",
            false,
            "Whether to display all slots as one seamless grid without per-bag row breaks.");

        HiddenBagIds = Config.Bind(
            "UI",
            "HiddenBagIds",
            "",
            "Comma-separated list of bag item IDs or names to hide from the combined inventory.");

        PreventBagOpenOnPickup = Config.Bind(
            "General",
            "PreventBagOpenOnPickup",
            true,
            "Whether to prevent storage bags from opening automatically when picking up items that go into a bag.");

        _harmony.PatchAll();

        // Optional patch for QuickMove mod if loaded
        PatchQuickMoveIfPresent(_harmony);

        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    public static HashSet<int> GetHiddenBagIdSet()
    {
        var set = new HashSet<int>();
        string val = HiddenBagIds?.Value;
        if (string.IsNullOrWhiteSpace(val)) return set;
        var parts = val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            string trimmed = part.Trim();
            if (int.TryParse(trimmed, out int id))
            {
                set.Add(id);
            }
            else if (ItemHandler.instance != null && ItemHandler.instance.items != null)
            {
                foreach (var item in ItemHandler.instance.items)
                {
                    if (item != null && string.Equals(item.itemName, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        set.Add(item.typeId);
                    }
                }
            }
        }
        return set;
    }

    public static bool IsBagHidden(int bagId)
    {
        return GetHiddenBagIdSet().Contains(bagId);
    }

    public static void ToggleBagHidden(int bagId)
    {
        var set = GetHiddenBagIdSet();
        if (set.Contains(bagId))
        {
            set.Remove(bagId);
        }
        else
        {
            set.Add(bagId);
        }
        HiddenBagIds.Value = string.Join(",", set);
    }

    private static readonly Func<KeyboardInputs, bool> TextInputActiveForShortcutsDelegate =
        AccessTools.MethodDelegate<Func<KeyboardInputs, bool>>(
            AccessTools.Method(typeof(KeyboardInputs), "TextInputActiveForShortcuts"));

    private void Update()
    {
        if (ToggleKey != null && ToggleKey.Value.IsDown())
        {
            if (CanToggleInventory())
            {
                UI_CombinedInventoryWindow.Toggle();
            }
        }
    }

    private static bool CanToggleInventory()
    {
        // 1. Must be in game with a valid player and inventory instance
        if (Player.instance == null || UI_Inventory.instance == null)
        {
            return false;
        }

        // 2. Must not be in the MainMenu scene
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu")
        {
            return false;
        }

        // 3. Must not be remapping keys or typing in chat / input fields
        if (KeyboardInputs.instance != null)
        {
            if (KeyboardInputs.instance.changingInputs ||
                (TextInputActiveForShortcutsDelegate != null && TextInputActiveForShortcutsDelegate(KeyboardInputs.instance)))
            {
                return false;
            }
        }

        // 4. Must not be open while Options/Settings window is active
        if (UI_Options.instance != null && UI_Options.instance.active)
        {
            return false;
        }

        // 5. Must not be focused on any UI text input field
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null)
        {
            return false;
        }

        return true;
    }

    private static void PatchQuickMoveIfPresent(Harmony harmony)
    {
        try
        {
            Type quickMoveType = AccessTools.TypeByName("QuickMove.InventoryQuickMovePatch");
            if (quickMoveType != null)
            {
                MethodInfo getOpenBagsMethod = AccessTools.Method(quickMoveType, "GetOpenAccessibleBagIds");
                if (getOpenBagsMethod != null)
                {
                    MethodInfo postfix = typeof(QuickMoveIntegrationPatch).GetMethod(
                        nameof(QuickMoveIntegrationPatch.GetOpenAccessibleBagIds_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(getOpenBagsMethod, postfix: new HarmonyMethod(postfix));
                    Logger.LogInfo("Successfully patched QuickMove for Combined Inventory integration.");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"QuickMove integration patch skipped or failed: {ex.Message}");
        }
    }
}

public static class QuickMoveIntegrationPatch
{
    public static void GetOpenAccessibleBagIds_Postfix(ref List<int> __result)
    {
        if (UI_CombinedInventoryWindow.instance != null && UI_CombinedInventoryWindow.instance.IsOpen())
        {
            var heldBags = StorageBagHandler.instance?.GetAccessibleBagIds(false);
            if (heldBags != null)
            {
                var hiddenSet = Plugin.GetHiddenBagIdSet();
                foreach (int bagId in heldBags)
                {
                    if (hiddenSet.Contains(bagId)) continue;
                    if (!__result.Contains(bagId))
                    {
                        __result.Add(bagId);
                    }
                }
            }
        }
    }
}

public enum ResizeEdge
{
    Right,
    Bottom,
    BottomRight,
    Left,
    BottomLeft
}

public class WindowResizeHandle : MonoBehaviour, IPointerDownHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
{
    public ResizeEdge edge;
    public UI_CombinedInventoryWindow window;

    private Vector2 startPointerPos;
    private Vector2 startWindowSize;
    private Vector2 startWindowPos;
    private Image handleImage;

    private void Awake()
    {
        handleImage = GetComponent<Image>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (window == null) return;
        startPointerPos = eventData.position;
        startWindowSize = window.GetWindowSize();
        startWindowPos = window.GetWindowPos();
        window.BringToFront();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (window == null || CBUI.instance?.canvasRect == null) return;

        Vector2 deltaPx = eventData.position - startPointerPos;
        Vector2 canvasSize = CBUI.instance.canvasRect.sizeDelta;
        Vector2 canvasDelta = new Vector2(
            deltaPx.x / Screen.width * canvasSize.x,
            deltaPx.y / Screen.height * canvasSize.y
        );

        float newWidth = startWindowSize.x;
        float newHeight = startWindowSize.y;
        float newPosX = startWindowPos.x;
        bool moveX = false;

        switch (edge)
        {
            case ResizeEdge.Right:
                newWidth = startWindowSize.x + canvasDelta.x;
                break;
            case ResizeEdge.Bottom:
                newHeight = startWindowSize.y - canvasDelta.y;
                break;
            case ResizeEdge.BottomRight:
                newWidth = startWindowSize.x + canvasDelta.x;
                newHeight = startWindowSize.y - canvasDelta.y;
                break;
            case ResizeEdge.Left:
                newWidth = startWindowSize.x - canvasDelta.x;
                newPosX = startWindowPos.x + canvasDelta.x;
                moveX = true;
                break;
            case ResizeEdge.BottomLeft:
                newWidth = startWindowSize.x - canvasDelta.x;
                newHeight = startWindowSize.y - canvasDelta.y;
                newPosX = startWindowPos.x + canvasDelta.x;
                moveX = true;
                break;
        }

        // Min width for 3 columns: ~210px
        float minW = 210f;
        float maxW = Mathf.Max(minW, canvasSize.x - 40f);
        float minH = 180f;
        float maxH = Mathf.Max(minH, canvasSize.y - 40f);

        if (newWidth < minW)
        {
            if (moveX)
            {
                newPosX -= (minW - newWidth);
            }
            newWidth = minW;
        }
        else if (newWidth > maxW)
        {
            if (moveX)
            {
                newPosX += (newWidth - maxW);
            }
            newWidth = maxW;
        }

        newHeight = Mathf.Clamp(newHeight, minH, maxH);

        window.ApplyResize(new Vector2(newWidth, newHeight), newPosX, moveX);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (window != null)
        {
            window.OnResizeComplete();
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (handleImage != null)
        {
            handleImage.color = new Color(0.35f, 0.65f, 1f, 0.25f);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (handleImage != null)
        {
            handleImage.color = new Color(0f, 0f, 0f, 0f);
        }
    }
}

public class CombinedSlotView
{
    public bool isBagSlot;
    public int mainSlotId;
    public int bagId;
    public int bagSlotIndex;
    public UI_StorageBag.ItemSlot bagItemSlot;

    public LUIRect container;
    public LUI_Image bgImage;
    public LUI_Image iconImage;
    public LUI_Text amountText;
    public LUI_Image usingOverlay;
    public LUI_Image frameImage;

    public UI_Inventory.ItemSlot GetItemSlot()
    {
        if (isBagSlot)
        {
            return bagItemSlot;
        }
        return UI_Inventory.instance?.GetSlotByIndex(mainSlotId);
    }

    public void Refresh()
    {
        if (!isBagSlot)
        {
            var realSlot = UI_Inventory.instance?.GetSlotByIndex(mainSlotId);
            int itemId = realSlot != null ? realSlot.itemId : 0;
            int amount = realSlot != null ? realSlot.itemAmount : 0;
            bool hasItem = itemId > 0 && amount > 0;

            if (iconImage != null)
            {
                iconImage.SetImage(hasItem && UI_Inventory.instance != null
                    ? UI_Inventory.instance.GetItemIcon(itemId)
                    : "invisible");
                iconImage.RaycastImage(hasItem);
            }

            if (frameImage != null)
            {
                frameImage.SetActive(hasItem);
            }

            if (amountText != null)
            {
                if (amount > 1)
                {
                    amountText.SetText(amount.ToString());
                    amountText.SetActive(true);
                }
                else
                {
                    amountText.SetActive(false);
                }
            }

            if (usingOverlay != null)
            {
                bool isUsing = UI_Inventory.instance != null
                    && UI_Inventory.instance.usingItem
                    && UI_Inventory.instance.usingItemSlot == realSlot;
                usingOverlay.SetActive(isUsing);
            }
        }
        else if (bagItemSlot != null)
        {
            int itemId = 0;
            int amount = 0;
            var bag = new StorageBagHandler.StorageBag(bagId);
            if (bag.slots != null && bagSlotIndex >= 0 && bagSlotIndex < bag.slots.Count)
            {
                var s = bag.slots[bagSlotIndex];
                if (s != null && s.itemId > 0 && s.amount > 0)
                {
                    itemId = s.itemId;
                    amount = s.amount;
                }
            }

            bagItemSlot.itemId = itemId;
            bagItemSlot.itemAmount = amount;
            bool hasItem = itemId > 0 && amount > 0;

            if (iconImage != null)
            {
                iconImage.SetImage(hasItem && UI_Inventory.instance != null
                    ? UI_Inventory.instance.GetItemIcon(itemId)
                    : "invisible");
                iconImage.RaycastImage(hasItem);
            }

            bagItemSlot.SetBanknote(hasItem && itemId > 1000000);

            if (frameImage != null)
            {
                frameImage.SetActive(hasItem);
            }

            if (amountText != null)
            {
                if (amount > 1)
                {
                    amountText.SetText(amount.ToString());
                    amountText.SetActive(true);
                }
                else
                {
                    amountText.SetActive(false);
                }
            }

            if (usingOverlay != null)
            {
                bool isUsing = UI_Inventory.instance != null
                    && UI_Inventory.instance.usingItem
                    && UI_Inventory.instance.usingItemSlot == bagItemSlot;
                usingOverlay.SetActive(isUsing);
            }
        }
    }
}

public class UI_CombinedInventoryWindow : LUI_DragWindow
{
    public static UI_CombinedInventoryWindow instance;

    private static readonly Action<UI_Inventory> InventoryUpdateAmountsDelegate =
        AccessTools.MethodDelegate<Action<UI_Inventory>>(AccessTools.Method(typeof(UI_Inventory), "UpdateAmounts"));

    private static readonly Action<UI_StorageBagHandler, int, int[]> StorageBagHandlerRefreshSlotsDelegate =
        AccessTools.MethodDelegate<Action<UI_StorageBagHandler, int, int[]>>(AccessTools.Method(typeof(UI_StorageBagHandler), "RefreshSlots", new[] { typeof(int), typeof(int[]) }));

    private static readonly Action<StorageBagHandler, int> UpdateBagInventorySlotAmountDelegate =
        AccessTools.MethodDelegate<Action<StorageBagHandler, int>>(AccessTools.Method(typeof(StorageBagHandler), "UpdateBagInventorySlotAmount", new[] { typeof(int) }));

    private static readonly AccessTools.FieldRef<UI_BottomRightTabs, LUIRect> BottomRightTabsContainerRef =
        AccessTools.FieldRefAccess<UI_BottomRightTabs, LUIRect>("container");

    public static LUIRect GetBottomRightTabsContainer()
    {
        if (UI_BottomRightTabs.instance == null) return null;
        try
        {
            var c = BottomRightTabsContainerRef(UI_BottomRightTabs.instance);
            if (c != null) return c;
        }
        catch { }
        return UI_BottomRightTabs.instance.image_tabBackground;
    }

    public static void BringBottomRightTabsToFront()
    {
        var brContainer = GetBottomRightTabsContainer();
        brContainer?.BringToFront();
        UI_BottomRightTabs.instance?.image_tabBackground?.BringToFront();
    }

    private LUI_ScrollView scrollView_Inventory;
    private LUIRect container_inventory;
    private LUI_Image activeDragGhost;
    private bool cancelDragEndAfterClose;
    public bool suppressClickUntilMouseRelease;

    public readonly List<UI_StorageBag.ItemSlot> renderedBagSlots = new();
    public readonly List<CombinedSlotView> allSlotViews = new();
    private readonly List<int> currentHeldBagIds = new();

    private Vector2 currentWindowSize = Vector2.zero;
    private int currentColumns = 5;
    private bool handlesAttached = false;

    private LUI_Button btnSortLocal;
    private LUI_Button btnSortGlobal;
    private LUI_Button btnViewToggle;
    private LUI_Button btnBagsFilter;
    private LUIRect bagsMenuContainer;

    private ItemSortMode lastLocalSortMode = ItemSortMode.Auto;
    private bool lastLocalSortDescending = UI_ItemSortUtility.GetDefaultDescending(ItemSortMode.Auto);
    private bool hasLocalSortMode = false;

    private ItemSortMode lastGlobalSortMode = ItemSortMode.Auto;
    private bool lastGlobalSortDescending = UI_ItemSortUtility.GetDefaultDescending(ItemSortMode.Auto);
    private bool hasGlobalSortMode = false;

    private class StoredItemEntry
    {
        public int itemId;
        public int amount;
        public ItemHandler.Item item;
        public string itemName;
        public int originalIndex;
    }

    public static void Toggle()
    {
        if (Player.instance == null || UI_Inventory.instance == null || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu")
        {
            return;
        }

        EnsureInstance();
        if (instance != null)
        {
            if (instance.IsOpen())
            {
                instance.Close(null);
            }
            else
            {
                instance.DrawItems();
                instance.Open();
                instance.BringToFront();
            }
        }
    }

    public static void OpenWindow()
    {
        if (Player.instance == null || UI_Inventory.instance == null || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu")
        {
            return;
        }

        EnsureInstance();
        if (instance != null)
        {
            instance.DrawItems();
            instance.Open();
            instance.BringToFront();
        }
    }

    private static void EnsureInstance()
    {
        if (Player.instance == null || UI_Inventory.instance == null || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu")
        {
            return;
        }

        if (instance == null)
        {
            var go = new GameObject("UI_CombinedInventoryWindow");
            UnityEngine.Object.DontDestroyOnLoad(go);
            instance = go.AddComponent<UI_CombinedInventoryWindow>();
            instance.SetupWindow();
        }
    }

    public bool IsOpen()
    {
        return active && gameObject != null && gameObject.activeInHierarchy;
    }

    public override void BringToFront()
    {
        base.BringToFront();
        EnsureBehindBottomRightTabs();
    }

    public override void BringToFront(object[] vars)
    {
        base.BringToFront(vars);
        EnsureBehindBottomRightTabs();
    }

    public static void EnsureBehindBottomRightTabs()
    {
        if (instance == null || instance.dragBar?.spawnedObject == null) return;

        Transform combinedTransform = instance.dragBar.spawnedObject.transform;

        // 1. Ensure behind UI_BottomRightTabs container (skills, houses, spells, equipment, quests, traits, etc.)
        var brContainer = GetBottomRightTabsContainer();
        if (brContainer?.spawnedObject != null)
        {
            Transform brTabsTransform = brContainer.spawnedObject.transform;
            if (combinedTransform.parent == brTabsTransform.parent)
            {
                int combinedIndex = combinedTransform.GetSiblingIndex();
                int brTabsIndex = brTabsTransform.GetSiblingIndex();
                if (combinedIndex > brTabsIndex)
                {
                    combinedTransform.SetSiblingIndex(brTabsIndex);
                }
            }
        }

        // 2. Ensure behind UI_Options if active
        if (UI_Options.instance != null && UI_Options.instance.active && UI_Options.instance.dragBar?.spawnedObject != null)
        {
            Transform optionsTransform = UI_Options.instance.dragBar.spawnedObject.transform;
            if (combinedTransform.parent == optionsTransform.parent)
            {
                int combinedIndex = combinedTransform.GetSiblingIndex();
                int optionsIndex = optionsTransform.GetSiblingIndex();
                if (combinedIndex > optionsIndex)
                {
                    combinedTransform.SetSiblingIndex(optionsIndex);
                }
            }
        }
    }

    public Vector2 GetWindowSize()
    {
        return currentWindowSize;
    }

    public Vector2 GetWindowPos()
    {
        return dragBar != null ? dragBar.GetPosition() : Vector2.zero;
    }

    public static int CalculateColumns(float width)
    {
        return CalculateColumns(width, Plugin.SeamlessInventory?.Value ?? false);
    }

    public static int CalculateColumns(float width, bool isSeamless)
    {
        float slotsStartX = isSeamless ? 10f : 42f;
        float rightMargin = 25f;
        float spacing = 5f;
        float slotStep = 50f;

        float available = width - slotsStartX - rightMargin;
        int cols = Mathf.FloorToInt((available + spacing) / slotStep);
        return Mathf.Clamp(cols, 3, 15);
    }

    public override void SetupWindow()
    {
        if (image_windowbackground != null && image_windowbackground.spawnedObject != null)
        {
            return;
        }

        LUI_Core.instance?.Setup();

        // Restore saved window size or use default
        string savedSize = Variables.instance?.GetVarStringValue("CombinedInvSize");
        if (!string.IsNullOrEmpty(savedSize))
        {
            string[] parts = savedSize.Split(',');
            if (parts.Length == 2 && float.TryParse(parts[0], out float w) && float.TryParse(parts[1], out float h))
            {
                currentWindowSize = new Vector2(Mathf.Clamp(w, 210f, 1600f), Mathf.Clamp(h, 180f, 1200f));
            }
        }

        if (currentWindowSize == Vector2.zero)
        {
            int defaultCols = Mathf.Clamp(Plugin.GridColumns?.Value ?? 5, 3, 15);
            float defaultW = Mathf.Max(Plugin.WindowWidth?.Value ?? 312f, 50f * defaultCols + 62f);
            float defaultH = Plugin.WindowHeight?.Value ?? 450f;
            currentWindowSize = new Vector2(defaultW, defaultH);
        }

        currentColumns = CalculateColumns(currentWindowSize.x);

        SetupDragWindow("Combined Inventory", currentWindowSize);

        if (text_windowTitle != null)
        {
            text_windowTitle.SetHorizontalStretch(0f, 1f, 35f, 35f);
        }

        if (closeButton != null)
        {
            closeButton.SetPosition(0f, -5f);
        }

        // Restore saved window position if exists
        string savedPos = Variables.instance?.GetVarStringValue("CombinedInvPos");
        if (!string.IsNullOrEmpty(savedPos))
        {
            Vector2 pos = OlipaRuntimeUtils.StringToVector2(savedPos);
            dragBar?.SetPosition(pos);
            dragBar?.ClampRectToScreen();
        }

        AttachResizeHandles();
        CreateToolbarButtons();
        DrawItems();
    }

    public new void SetWindowSize(Vector2 size)
    {
        base.SetWindowSize(size);
        currentWindowSize = size;
        if (dragBar != null)
        {
            dragBar.SetSize(Mathf.Max(50f, size.x - 50f), 45f);
        }
    }

    public void ApplyResize(Vector2 newSize, float newPosX, bool moveX)
    {
        SetWindowSize(newSize);

        if (moveX && dragBar != null)
        {
            Vector2 pos = dragBar.GetPosition();
            dragBar.SetPosition(new Vector2(newPosX, pos.y));
        }

        UpdateToolbarLayout();

        int newCols = CalculateColumns(newSize.x, Plugin.SeamlessInventory?.Value ?? false);
        if (newCols != currentColumns)
        {
            currentColumns = newCols;
            DrawItems();
        }
    }

    public void OnResizeComplete()
    {
        dragBar?.ClampRectToScreen();

        if (Variables.instance != null)
        {
            Variables.instance.SetVarString("CombinedInvSize", $"{(int)currentWindowSize.x},{(int)currentWindowSize.y}");
            if (dragBar != null)
            {
                Vector2 pos = dragBar.GetPosition();
                Variables.instance.SetVarString("CombinedInvPos", $"{(int)pos.x},{(int)pos.y}");
            }
        }
    }

    private void AttachResizeHandles()
    {
        if (handlesAttached || image_windowbackground?.spawnedObject == null) return;
        handlesAttached = true;

        Transform bgTransform = image_windowbackground.spawnedObject.transform;

        // 1. Right edge handle
        CreateHandle("Resize_Right", ResizeEdge.Right, bgTransform,
            new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f),
            new Vector2(10f, 0f), new Vector2(-4f, 0f));

        // 2. Bottom edge handle
        CreateHandle("Resize_Bottom", ResizeEdge.Bottom, bgTransform,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 1f),
            new Vector2(0f, 10f), new Vector2(0f, 4f));

        // 3. Bottom-Right corner handle
        var brHandle = CreateHandle("Resize_BottomRight", ResizeEdge.BottomRight, bgTransform,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0.5f),
            new Vector2(20f, 20f), Vector2.zero);

        // Add subtle visible grip marker in the bottom-right corner
        CreateGripMarker(brHandle.transform);

        // 4. Left edge handle
        CreateHandle("Resize_Left", ResizeEdge.Left, bgTransform,
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0.5f),
            new Vector2(10f, 0f), new Vector2(4f, 0f));

        // 5. Bottom-Left corner handle
        CreateHandle("Resize_BottomLeft", ResizeEdge.BottomLeft, bgTransform,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0.5f, 0.5f),
            new Vector2(20f, 20f), Vector2.zero);
    }

    private GameObject CreateHandle(
        string name,
        ResizeEdge edge,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 sizeDelta,
        Vector2 anchoredPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = anchoredPos;

        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0f);
        img.raycastTarget = true;

        var handle = go.AddComponent<WindowResizeHandle>();
        handle.edge = edge;
        handle.window = this;

        return go;
    }

    private void CreateGripMarker(Transform parent)
    {
        var grip = new GameObject("GripMarker");
        grip.transform.SetParent(parent, false);

        var rt = grip.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(10f, 10f);
        rt.anchoredPosition = new Vector2(-2f, 2f);

        var img = grip.AddComponent<Image>();
        img.color = new Color(0.8f, 0.8f, 0.8f, 0.35f);
        img.raycastTarget = false;
    }

    public void DrawItems()
    {
        if (container_inventory != null)
        {
            container_inventory.Destroy();
            container_inventory = null;
        }

        renderedBagSlots.Clear();
        allSlotViews.Clear();
        currentHeldBagIds.Clear();

        if (image_windowbackground == null)
        {
            return;
        }

        bool isSeamless = Plugin.SeamlessInventory?.Value ?? false;
        currentColumns = CalculateColumns(currentWindowSize.x, isSeamless);

        container_inventory = LUI_Core.instance.AddEmptyContainer("CombinedInvContainer", image_windowbackground);
        scrollView_Inventory = LUI_Core.instance.AddScrollView(
            null,
            Position.center,
            Position.center,
            Vector2.zero,
            Vector2.zero,
            container_inventory);

        // Top: 40f (room for drag bar / title), Bottom: 35f (room for toolbar buttons)
        scrollView_Inventory.SetStretch(0f, 1f, 0f, 1f, 0f, -20f, 40f, 35f);

        int columns = currentColumns;
        float slotSize = 45f;
        float spacing = 5f;
        float startY = -10f;
        var hiddenSet = Plugin.GetHiddenBagIdSet();
        var accessibleBags = StorageBagHandler.instance?.GetAccessibleBagIds(false);
        if (accessibleBags != null)
        {
            currentHeldBagIds.AddRange(accessibleBags);
        }

        if (isSeamless)
        {
            float slotsStartX = 10f;
            int globalSlotIndex = 0;

            // 1. Add 35 main inventory slots (1..35)
            for (int i = 0; i < 35; i++)
            {
                int col = globalSlotIndex % columns;
                int row = globalSlotIndex / columns;
                float posX = slotsStartX + col * (slotSize + spacing);
                float posY = startY - row * (slotSize + spacing);

                int slotId = i + 1;
                CreateSlotUI(posX, posY, slotSize, false, slotId, 0, 0);
                globalSlotIndex++;
            }

            // 2. Add held storage bag slots continuously
            if (accessibleBags != null)
            {
                foreach (int bagId in accessibleBags)
                {
                    if (hiddenSet.Contains(bagId)) continue;

                    var bag = new StorageBagHandler.StorageBag(bagId);
                    int bagSize = 0;
                    var itemDef = ItemHandler.instance?.GetItem(bagId);
                    if (itemDef != null)
                    {
                        bagSize = itemDef.storageSize;
                    }
                    if (bag.slots != null && bag.slots.Count > bagSize)
                    {
                        bagSize = bag.slots.Count;
                    }

                    for (int slotIdx = 0; slotIdx < bagSize; slotIdx++)
                    {
                        int col = globalSlotIndex % columns;
                        int row = globalSlotIndex / columns;
                        float posX = slotsStartX + col * (slotSize + spacing);
                        float posY = startY - row * (slotSize + spacing);

                        CreateSlotUI(posX, posY, slotSize, true, 0, bagId, slotIdx);
                        globalSlotIndex++;
                    }
                }
            }

            int totalRows = Mathf.CeilToInt((float)globalSlotIndex / columns);
            float contentHeight = totalRows * (slotSize + spacing) + 20f;
            scrollView_Inventory.SetContentHeight(Mathf.Max(200f, contentHeight));
        }
        else
        {
            float slotsStartX = 42f; // room for section bag indicator on the left
            int currentGlobalRow = 0;

            // 1. Add 35 main inventory slots (1..35)
            int mainStartRow = currentGlobalRow;
            for (int i = 0; i < 35; i++)
            {
                int col = i % columns;
                int row = mainStartRow + (i / columns);
                float posX = slotsStartX + col * (slotSize + spacing);
                float posY = startY - row * (slotSize + spacing);

                int slotId = i + 1;
                CreateSlotUI(posX, posY, slotSize, false, slotId, 0, 0);
            }

            // Indicator on the left side for Main Inventory
            float mainIconY = startY - mainStartRow * (slotSize + spacing) - 8f;
            CreateSectionIndicator(6f, mainIconY, 28f, "BottomRightTabs/Inventory", "Main Inventory (35 slots)", 0);

            int mainRowCount = Mathf.CeilToInt(35f / columns);
            currentGlobalRow += mainRowCount;

            // 2. Add held storage bag slots
            if (accessibleBags != null)
            {
                foreach (int bagId in accessibleBags)
                {
                    if (hiddenSet.Contains(bagId)) continue;

                    var bag = new StorageBagHandler.StorageBag(bagId);
                    int bagSize = 0;
                    var itemDef = ItemHandler.instance?.GetItem(bagId);
                    if (itemDef != null)
                    {
                        bagSize = itemDef.storageSize;
                    }
                    if (bag.slots != null && bag.slots.Count > bagSize)
                    {
                        bagSize = bag.slots.Count;
                    }

                    int bagStartRow = currentGlobalRow;
                    for (int slotIdx = 0; slotIdx < bagSize; slotIdx++)
                    {
                        int col = slotIdx % columns;
                        int row = bagStartRow + (slotIdx / columns);
                        float posX = slotsStartX + col * (slotSize + spacing);
                        float posY = startY - row * (slotSize + spacing);

                        CreateSlotUI(posX, posY, slotSize, true, 0, bagId, slotIdx);
                    }

                    // Add small icon on the left side of the inventory representing this bag
                    float bagIconY = startY - bagStartRow * (slotSize + spacing) - 8f;
                    string bagIconName = UI_Inventory.instance != null ? UI_Inventory.instance.GetItemIcon(bagId) : "invisible";
                    string bagName = itemDef != null ? itemDef.itemName : "Storage Bag";
                    string tooltipText = $"{bagName} ({bagSize} slots)\nRight-click to hide from combined view";

                    CreateSectionIndicator(6f, bagIconY, 28f, bagIconName, tooltipText, bagId);

                    int bagRowCount = Mathf.CeilToInt((float)bagSize / columns);
                    currentGlobalRow += bagRowCount;
                }
            }

            float contentHeight = currentGlobalRow * (slotSize + spacing) + 20f;
            scrollView_Inventory.SetContentHeight(Mathf.Max(200f, contentHeight));
        }

        if (bagsMenuContainer?.spawnedObject != null)
        {
            bagsMenuContainer.spawnedObject.transform.SetAsLastSibling();
        }

        RefreshAllSlots();
    }

    private void CreateSectionIndicator(float posX, float posY, float size, string iconName, string tooltipText, int bagId)
    {
        LUIRect iconContainer = LUI_Core.instance.AddContainer(
            "SectionIndicator",
            Position.topLeft,
            Position.topLeft,
            new Vector2(posX, posY),
            new Vector2(size, size),
            scrollView_Inventory.GetContentTransform());

        LUI_Image iconImg = LUI_Core.instance.AddImage(
            iconName,
            Position.topLeft,
            Position.topLeft,
            Vector2.zero,
            new Vector2(size, size),
            iconContainer);

        iconImg.RaycastImage(true);

        if (bagId > 0)
        {
            iconImg.AddMouseHover(
                _ =>
                {
                    if (UI_ItemTooltips.instance != null)
                    {
                        UI_ItemTooltips.instance.customMode = UI_ItemTooltips.TooltipMode.inventory;
                        UI_ItemTooltips.instance.ItemTooltip(bagId, iconImg, null);
                    }
                },
                null,
                _ => UI_ItemTooltips.instance?.Deactivate(),
                null);

            // Left-Click opens/focuses that bag's standalone window
            iconImg.AddClickUp(
                _ =>
                {
                    UI_StorageBagHandler.instance?.OpenBag(bagId);
                },
                null);

            // Right-Click hides this bag from the combined inventory view
            iconImg.AddRightClickDown(
                _ =>
                {
                    Plugin.ToggleBagHidden(bagId);
                    DrawItems();
                },
                null);
        }
        else
        {
            iconImg.AddMouseHover(
                _ => UI_TextToolTips.instance?.Tooltip(tooltipText, iconImg),
                null,
                _ => UI_TextToolTips.instance?.Deactivate(),
                null);
        }
    }

    private void CreateSlotUI(
        float posX,
        float posY,
        float slotSize,
        bool isBagSlot,
        int mainSlotId,
        int bagId,
        int bagSlotIndex)
    {
        LUIRect slotContainer = LUI_Core.instance.AddContainer(
            "Item",
            Position.topLeft,
            Position.topLeft,
            new Vector2(posX, posY),
            new Vector2(slotSize, slotSize),
            scrollView_Inventory.GetContentTransform());

        LUI_Image bgImage = LUI_Core.instance.AddImage(
            "InventorySlot",
            Position.topLeft,
            Position.topLeft,
            Vector2.zero,
            new Vector2(slotSize, slotSize),
            slotContainer);

        bgImage.AddDragToArea();
        if (bgImage.image != null)
        {
            bgImage.image.raycastTarget = true;
        }

        LUI_Image iconImage = LUI_Core.instance.AddImage(
            "invisible",
            Position.topLeft,
            Position.topLeft,
            new Vector2(1.5f, -1.5f),
            new Vector2(42f, 42f),
            bgImage.GetParentTransform());

        iconImage.RaycastImage(false);
        iconImage.AddDragToArea();

        LUI_Text textAmount = LUI_Core.AddText(
            15,
            TextAnchor.UpperLeft,
            Position.topLeft,
            Position.topLeft,
            new Vector2(3f, -3f),
            new Vector2(slotSize, slotSize),
            iconImage,
            "");
        textAmount.AddOutline();

        LUI_Image usingOverlay = LUI_Core.instance.AddImage(
            113,
            Position.topLeft,
            Position.topLeft,
            Vector2.zero,
            new Vector2(slotSize, slotSize),
            bgImage.GetParentTransform());
        usingOverlay.SetActive(false);

        LUI_Image frameImage = LUI_Core.instance.AddImage(
            "Common/SkillIconFrame",
            Position.topLeft,
            Position.topLeft,
            Vector2.zero,
            new Vector2(slotSize, slotSize),
            bgImage.GetParentTransform());
        frameImage.SetActive(false);

        var view = new CombinedSlotView
        {
            isBagSlot = isBagSlot,
            mainSlotId = mainSlotId,
            bagId = bagId,
            bagSlotIndex = bagSlotIndex,
            container = slotContainer,
            bgImage = bgImage,
            iconImage = iconImage,
            amountText = textAmount,
            usingOverlay = usingOverlay,
            frameImage = frameImage
        };

        if (!isBagSlot)
        {
            var dropTarget1 = bgImage.spawnedObject.AddComponent<UI_InventorySlot>();
            dropTarget1.slotId = mainSlotId;
            var dropTarget2 = iconImage.spawnedObject.AddComponent<UI_InventorySlot>();
            dropTarget2.slotId = mainSlotId;

            iconImage.AddMouseHover(
                OnSlotMouseEnter,
                new object[] { view, iconImage },
                OnSlotMouseExit,
                null);

            iconImage.AddRightClickDown(OnSlotRightClick, new object[] { view });
            iconImage.AddClickUp(OnSlotLeftClick, new object[] { view });
            iconImage.AddDrag(OnDragStart, OnDragEnd, new object[] { iconImage, view });
        }
        else
        {
            var bagItemSlot = new UI_StorageBag.ItemSlot();
            bagItemSlot.bagId = bagId;
            bagItemSlot.storageSlotIndex = bagSlotIndex;
            bagItemSlot.slotId = -100000 - bagId * 1000 - bagSlotIndex;
            bagItemSlot.button = bgImage;
            bagItemSlot.image_itemIcon = iconImage;
            bagItemSlot.text_amount = textAmount;
            bagItemSlot.usingSlotOverlay = usingOverlay;
            bagItemSlot.image_itemFrame = frameImage;

            view.bagItemSlot = bagItemSlot;
            renderedBagSlots.Add(bagItemSlot);

            var dropTarget1 = bgImage.spawnedObject.AddComponent<UI_StorageInventorySlot>();
            dropTarget1.bagId = bagId;
            dropTarget1.storageSlotIndex = bagSlotIndex;

            var dropTarget2 = iconImage.spawnedObject.AddComponent<UI_StorageInventorySlot>();
            dropTarget2.bagId = bagId;
            dropTarget2.storageSlotIndex = bagSlotIndex;

            iconImage.AddMouseHover(
                OnSlotMouseEnter,
                new object[] { view, iconImage },
                OnSlotMouseExit,
                null);

            iconImage.AddRightClickDown(OnSlotRightClick, new object[] { view });
            iconImage.AddClickUp(OnSlotLeftClick, new object[] { view });
            iconImage.AddDrag(OnDragStart, OnDragEnd, new object[] { iconImage, view });
        }

        allSlotViews.Add(view);
    }

    public UI_StorageBag.ItemSlot FindBagSlot(int bagId, int storageSlotIndex)
    {
        for (int i = 0; i < renderedBagSlots.Count; i++)
        {
            var s = renderedBagSlots[i];
            if (s != null && s.bagId == bagId && s.storageSlotIndex == storageSlotIndex)
            {
                return s;
            }
        }
        return null;
    }

    public UI_StorageBag.ItemSlot FindBagSlotByItemId(int itemId)
    {
        for (int i = 0; i < renderedBagSlots.Count; i++)
        {
            var s = renderedBagSlots[i];
            if (s != null && s.itemId == itemId && s.itemAmount > 0)
            {
                return s;
            }
        }
        return null;
    }

    public void RefreshAllSlots()
    {
        for (int i = 0; i < allSlotViews.Count; i++)
        {
            allSlotViews[i]?.Refresh();
        }
    }

    public void RefreshMainSlots()
    {
        for (int i = 0; i < allSlotViews.Count; i++)
        {
            var view = allSlotViews[i];
            if (view != null && !view.isBagSlot)
            {
                view.Refresh();
            }
        }
    }

    public void RefreshBagSlots(int bagId, int[] slotIndexes)
    {
        for (int i = 0; i < allSlotViews.Count; i++)
        {
            var view = allSlotViews[i];
            if (view != null && view.isBagSlot && view.bagId == bagId)
            {
                if (slotIndexes == null || slotIndexes.Length == 0 || Array.IndexOf(slotIndexes, view.bagSlotIndex) >= 0)
                {
                    view.Refresh();
                }
            }
        }
    }

    private void CreateToolbarButtons()
    {
        if (image_windowbackground == null) return;
        var parent = image_windowbackground.GetParentTransform();

        // 1. Sort Bags (Local)
        btnSortLocal = LUI_Core.instance.AddButton(
            "Sort Bags",
            Position.bottomLeft,
            Position.bottomLeft,
            Vector2.zero,
            new Vector2(70f, 24f),
            _ => SortLocal(lastLocalSortMode),
            null,
            parent);
        btnSortLocal?.image?.AddRightClickDown(ShowSortLocalMenu, null);
        btnSortLocal?.AddMouseHoverText("Sort Bags (Local)\nLeft-Click: Auto-sort within each container.\nRight-Click: Select sort mode.");

        // 2. Sort All (Global)
        btnSortGlobal = LUI_Core.instance.AddButton(
            "Sort All",
            Position.bottomLeft,
            Position.bottomLeft,
            Vector2.zero,
            new Vector2(65f, 24f),
            _ => SortGlobal(lastGlobalSortMode),
            null,
            parent);
        btnSortGlobal?.image?.AddRightClickDown(ShowSortGlobalMenu, null);
        btnSortGlobal?.AddMouseHoverText("Sort All (Global)\nLeft-Click: Auto-sort across all bags and inventory.\nRight-Click: Select sort mode.");

        // 3. View Mode Toggle (Split / Seamless)
        string viewLabel = (Plugin.SeamlessInventory?.Value ?? false) ? "Seamless" : "Split";
        btnViewToggle = LUI_Core.instance.AddButton(
            viewLabel,
            Position.bottomLeft,
            Position.bottomLeft,
            Vector2.zero,
            new Vector2(65f, 24f),
            _ =>
            {
                Plugin.SeamlessInventory.Value = !Plugin.SeamlessInventory.Value;
                UpdateToolbarLayout();
                DrawItems();
            },
            null,
            parent);
        btnViewToggle?.AddMouseHoverText("View Mode\nClick to switch between Split by Bag and Seamless combined layout.");

        // 4. Bags Filter
        btnBagsFilter = LUI_Core.instance.AddButton(
            "Bags",
            Position.bottomLeft,
            Position.bottomLeft,
            Vector2.zero,
            new Vector2(50f, 24f),
            _ => ToggleBagsMenu(),
            null,
            parent);
        btnBagsFilter?.AddMouseHoverText("Filter Bags\nClick to choose which bags appear in the combined inventory.");

        UpdateToolbarLayout();
    }

    private void UpdateToolbarLayout()
    {
        if (btnSortLocal == null || btnSortGlobal == null || btnViewToggle == null || btnBagsFilter == null) return;

        float w = currentWindowSize.x;
        float startX = 6f;
        float gap = 4f;
        float posY = 5f;
        float totalPadding = startX * 2f + gap * 3f;
        float btnWidth = Mathf.Clamp((w - totalPadding) / 4f, 40f, 95f);

        btnSortLocal.SetSize(btnWidth, 24f);
        btnSortLocal.SetPosition(startX, posY);

        btnSortGlobal.SetSize(btnWidth, 24f);
        btnSortGlobal.SetPosition(startX + btnWidth + gap, posY);

        btnViewToggle.SetSize(btnWidth, 24f);
        btnViewToggle.SetPosition(startX + (btnWidth + gap) * 2f, posY);

        btnBagsFilter.SetSize(btnWidth, 24f);
        btnBagsFilter.SetPosition(startX + (btnWidth + gap) * 3f, posY);

        bool wide = btnWidth >= 62f;
        btnSortLocal.text?.SetText(wide ? "Sort Bags" : "Sort");
        btnSortGlobal.text?.SetText(wide ? "Sort All" : "All");
        btnViewToggle.text?.SetText((Plugin.SeamlessInventory?.Value ?? false) ? (wide ? "Seamless" : "Seam") : "Split");
        btnBagsFilter.text?.SetText(wide ? "Bags" : "Filter");
    }

    private void ShowSortLocalMenu(object[] vars)
    {
        if (UI_RightClickMenu.instance == null || btnSortLocal == null || btnSortLocal.image == null) return;
        var context = new UI_RightClickMenu.SortMenuContext
        {
            targetLabel = "Bags (Local)",
            getNextDescending = GetNextLocalSortDescending,
            selectMode = mode => SortLocal(mode)
        };
        UI_RightClickMenu.instance.ActivateSortMenu(context, btnSortLocal.image, vars);
    }

    private void ShowSortGlobalMenu(object[] vars)
    {
        if (UI_RightClickMenu.instance == null || btnSortGlobal == null || btnSortGlobal.image == null) return;
        var context = new UI_RightClickMenu.SortMenuContext
        {
            targetLabel = "All (Global)",
            getNextDescending = GetNextGlobalSortDescending,
            selectMode = mode => SortGlobal(mode)
        };
        UI_RightClickMenu.instance.ActivateSortMenu(context, btnSortGlobal.image, vars);
    }

    private bool GetNextLocalSortDescending(ItemSortMode mode)
    {
        if (hasLocalSortMode && lastLocalSortMode == mode)
        {
            return !lastLocalSortDescending;
        }
        return UI_ItemSortUtility.GetDefaultDescending(mode);
    }

    private bool GetNextGlobalSortDescending(ItemSortMode mode)
    {
        if (hasGlobalSortMode && lastGlobalSortMode == mode)
        {
            return !lastGlobalSortDescending;
        }
        return UI_ItemSortUtility.GetDefaultDescending(mode);
    }

    public void SortLocal(ItemSortMode mode)
    {
        if (Player.instance == null || UI_Inventory.instance == null) return;

        bool nextDescending = GetNextLocalSortDescending(mode);
        hasLocalSortMode = true;
        lastLocalSortMode = mode;
        lastLocalSortDescending = nextDescending;

        if (UI_Inventory.instance != null)
        {
            UI_Inventory.instance.CancelUsingItem();
            UI_Inventory.instance.TryCancelControllerMoveItem();
        }

        // 1. Sort main inventory
        ConsolidateInventorySlots(UI_Inventory.instance.itemSlots);
        UI_ItemSortUtility.SortInventorySlots(UI_Inventory.instance.itemSlots, mode, nextDescending);

        // 2. Sort each accessible, non-hidden bag
        var accessibleBags = StorageBagHandler.instance?.GetAccessibleBagIds(false);
        if (accessibleBags != null)
        {
            var hiddenSet = Plugin.GetHiddenBagIdSet();
            foreach (int bagId in accessibleBags)
            {
                if (hiddenSet.Contains(bagId)) continue;
                var bag = new StorageBagHandler.StorageBag(bagId);
                if (bag.slots != null)
                {
                    ConsolidateBagSlots(bag.slots, bagId);
                    UI_ItemSortUtility.SortStorageBagSlots(bag.slots, mode, nextDescending);
                    bag.Save();

                    if (UI_StorageBagHandler.instance != null)
                    {
                        StorageBagHandlerRefreshSlotsDelegate?.Invoke(UI_StorageBagHandler.instance, bagId, new int[0]);
                    }
                    if (StorageBagHandler.instance != null)
                    {
                        UpdateBagInventorySlotAmountDelegate?.Invoke(StorageBagHandler.instance, bagId);
                    }
                }
            }
        }

        InventoryUpdateAmountsDelegate?.Invoke(UI_Inventory.instance);
        RefreshAllSlots();
        AudioHandler.instance?.Play2DSound(12);
    }

    public void SortGlobal(ItemSortMode mode)
    {
        if (Player.instance == null || UI_Inventory.instance == null) return;

        bool nextDescending = GetNextGlobalSortDescending(mode);
        hasGlobalSortMode = true;
        lastGlobalSortMode = mode;
        lastGlobalSortDescending = nextDescending;

        if (UI_Inventory.instance != null)
        {
            UI_Inventory.instance.CancelUsingItem();
            UI_Inventory.instance.TryCancelControllerMoveItem();
        }

        var accessibleBags = StorageBagHandler.instance?.GetAccessibleBagIds(false);
        var activeBagIds = new List<int>();
        if (accessibleBags != null)
        {
            var hiddenSet = Plugin.GetHiddenBagIdSet();
            foreach (int bagId in accessibleBags)
            {
                if (!hiddenSet.Contains(bagId))
                {
                    activeBagIds.Add(bagId);
                }
            }
        }

        // 1. Collect all items from main inventory and active bags
        var rawItems = new List<StoredItemEntry>();
        if (UI_Inventory.instance.itemSlots != null)
        {
            foreach (var slot in UI_Inventory.instance.itemSlots)
            {
                if (slot == null || slot.bankSlot || slot.slotId < 1 || slot.slotId > 35) continue;
                if (slot.itemId > 0 && slot.itemAmount > 0)
                {
                    rawItems.Add(new StoredItemEntry { itemId = slot.itemId, amount = slot.itemAmount });
                }
            }
        }

        var bagObjects = new Dictionary<int, StorageBagHandler.StorageBag>();
        foreach (int bagId in activeBagIds)
        {
            var bag = new StorageBagHandler.StorageBag(bagId);
            bagObjects[bagId] = bag;
            if (bag.slots != null)
            {
                foreach (var slot in bag.slots)
                {
                    if (slot != null && slot.itemId > 0 && slot.amount > 0)
                    {
                        rawItems.Add(new StoredItemEntry { itemId = slot.itemId, amount = slot.amount });
                    }
                }
            }
        }

        // 2. Consolidate stackable items
        var consolidated = new List<StoredItemEntry>();
        foreach (var entry in rawItems)
        {
            if (ItemHandler.instance != null && ItemHandler.instance.IsStackable(entry.itemId))
            {
                var itemDef = ItemHandler.instance.GetItem(entry.itemId);
                int maxStack = (itemDef != null && itemDef.maxStack > 0) ? itemDef.maxStack : int.MaxValue;
                int remaining = entry.amount;

                foreach (var existing in consolidated)
                {
                    if (existing.itemId == entry.itemId && existing.amount < maxStack)
                    {
                        int canAdd = maxStack - existing.amount;
                        int toAdd = Mathf.Min(remaining, canAdd);
                        existing.amount += toAdd;
                        remaining -= toAdd;
                        if (remaining <= 0) break;
                    }
                }

                while (remaining > 0)
                {
                    int toAdd = Mathf.Min(remaining, maxStack);
                    consolidated.Add(new StoredItemEntry { itemId = entry.itemId, amount = toAdd });
                    remaining -= toAdd;
                }
            }
            else
            {
                consolidated.Add(entry);
            }
        }

        // 3. Resolve item definitions and sort
        for (int i = 0; i < consolidated.Count; i++)
        {
            consolidated[i].originalIndex = i;
            consolidated[i].item = ResolveSortItem(consolidated[i].itemId);
            consolidated[i].itemName = consolidated[i].item != null ? consolidated[i].item.itemName : "";
        }

        consolidated.Sort((a, b) => CompareEntries(a, b, mode, nextDescending));

        // 4. Clear all slots
        foreach (var slot in UI_Inventory.instance.itemSlots)
        {
            if (slot != null && !slot.bankSlot && slot.slotId >= 1 && slot.slotId <= 35)
            {
                slot.ClearSlot();
            }
        }

        foreach (var kvp in bagObjects)
        {
            if (kvp.Value.slots != null)
            {
                for (int i = 0; i < kvp.Value.slots.Count; i++)
                {
                    kvp.Value.slots[i].itemId = 0;
                    kvp.Value.slots[i].amount = 0;
                }
            }
        }

        // 5. Partition bags into restricted and general
        var restrictedBags = new List<int>();
        var generalBags = new List<int>();
        foreach (int bagId in activeBagIds)
        {
            var itemDef = ItemHandler.instance?.GetItem(bagId);
            if (itemDef != null && itemDef.storageItem != null && itemDef.storageItem.Count > 0)
            {
                restrictedBags.Add(bagId);
            }
            else
            {
                generalBags.Add(bagId);
            }
        }

        var remainingItems = new List<StoredItemEntry>(consolidated);

        // Pass A: Fill restricted bags first with compatible items
        foreach (int bagId in restrictedBags)
        {
            var bag = bagObjects[bagId];
            if (bag.slots == null) continue;

            int capacity = bag.slots.Count;
            var bagAssigned = new List<StoredItemEntry>();

            for (int i = remainingItems.Count - 1; i >= 0; i--)
            {
                var item = remainingItems[i];
                if (StorageBagHandler.instance != null && StorageBagHandler.instance.CanBagStoreItem(bagId, item.itemId))
                {
                    bagAssigned.Add(item);
                    remainingItems.RemoveAt(i);
                    if (bagAssigned.Count >= capacity) break;
                }
            }

            // Sort items assigned to this bag
            bagAssigned.Sort((a, b) => CompareEntries(a, b, mode, nextDescending));
            for (int i = 0; i < bagAssigned.Count && i < bag.slots.Count; i++)
            {
                bag.slots[i].itemId = bagAssigned[i].itemId;
                bag.slots[i].amount = bagAssigned[i].amount;
            }
        }

        // Pass B: Remaining items go into Main Inventory (slots 1..35) first
        remainingItems.Sort((a, b) => CompareEntries(a, b, mode, nextDescending));

        var mainSlots = UI_Inventory.instance.itemSlots.FindAll(s => s != null && !s.bankSlot && s.slotId >= 1 && s.slotId <= 35);
        mainSlots.Sort((a, b) => a.slotId.CompareTo(b.slotId));

        int mainIndex = 0;
        while (mainIndex < mainSlots.Count && remainingItems.Count > 0)
        {
            var item = remainingItems[0];
            remainingItems.RemoveAt(0);
            mainSlots[mainIndex].AddItem(item.itemId, item.amount);
            mainIndex++;
        }

        // Pass C: Any excess items overflow into general bags
        foreach (int bagId in generalBags)
        {
            if (remainingItems.Count == 0) break;
            var bag = bagObjects[bagId];
            if (bag.slots == null) continue;

            for (int i = 0; i < bag.slots.Count && remainingItems.Count > 0; i++)
            {
                var item = remainingItems[0];
                remainingItems.RemoveAt(0);
                bag.slots[i].itemId = item.itemId;
                bag.slots[i].amount = item.amount;
            }
        }

        // 6. Save bags and refresh UI
        foreach (var kvp in bagObjects)
        {
            kvp.Value.Save();

            if (UI_StorageBagHandler.instance != null)
            {
                StorageBagHandlerRefreshSlotsDelegate?.Invoke(UI_StorageBagHandler.instance, kvp.Key, new int[0]);
            }
            if (StorageBagHandler.instance != null)
            {
                UpdateBagInventorySlotAmountDelegate?.Invoke(StorageBagHandler.instance, kvp.Key);
            }
        }

        InventoryUpdateAmountsDelegate?.Invoke(UI_Inventory.instance);
        RefreshAllSlots();
        AudioHandler.instance?.Play2DSound(12);
    }

    private static void ConsolidateInventorySlots(List<UI_Inventory.ItemSlot> slots)
    {
        if (slots == null || ItemHandler.instance == null) return;
        for (int i = 0; i < slots.Count; i++)
        {
            var source = slots[i];
            if (source == null || source.bankSlot || source.slotId < 1 || source.slotId > 35) continue;
            if (source.itemId <= 0 || source.itemAmount <= 0) continue;
            if (!ItemHandler.instance.IsStackable(source.itemId)) continue;

            var itemDef = ItemHandler.instance.GetItem(source.itemId);
            int maxStack = (itemDef != null && itemDef.maxStack > 0) ? itemDef.maxStack : int.MaxValue;

            for (int j = 0; j < i; j++)
            {
                var target = slots[j];
                if (target == null || target.bankSlot || target.slotId < 1 || target.slotId > 35) continue;
                if (target.itemId == source.itemId && target.itemAmount < maxStack)
                {
                    int canAdd = maxStack - target.itemAmount;
                    int moveAmt = Mathf.Min(source.itemAmount, canAdd);
                    target.itemAmount += moveAmt;
                    source.itemAmount -= moveAmt;
                    if (source.itemAmount <= 0)
                    {
                        source.ClearSlot();
                        break;
                    }
                }
            }
        }
    }

    private static void ConsolidateBagSlots(List<StorageBagHandler.Slot> slots, int bagId)
    {
        if (slots == null || ItemHandler.instance == null) return;
        for (int i = 0; i < slots.Count; i++)
        {
            var source = slots[i];
            if (source == null || source.itemId <= 0 || source.amount <= 0) continue;
            if (!ItemHandler.instance.IsStackable(source.itemId)) continue;

            var itemDef = ItemHandler.instance.GetItem(source.itemId);
            int maxStack = (itemDef != null && itemDef.maxStack > 0) ? itemDef.maxStack : int.MaxValue;

            for (int j = 0; j < i; j++)
            {
                var target = slots[j];
                if (target == null) continue;
                if (target.itemId == source.itemId && target.amount < maxStack)
                {
                    int canAdd = maxStack - target.amount;
                    int moveAmt = Mathf.Min(source.amount, canAdd);
                    target.amount += moveAmt;
                    source.amount -= moveAmt;
                    if (source.amount <= 0)
                    {
                        source.itemId = 0;
                        source.amount = 0;
                        break;
                    }
                }
            }
        }
    }

    private static ItemHandler.Item ResolveSortItem(int itemId)
    {
        if (ItemHandler.instance == null) return null;
        ItemHandler.Item item = ItemHandler.instance.GetItem(itemId);
        if (item != null) return item;
        if (itemId > 1100000) item = ItemHandler.instance.GetItem(itemId - 1100000);
        if (item != null) return item;
        if (itemId > 1000000) item = ItemHandler.instance.GetItem(itemId - 1000000);
        if (item != null) return item;
        if (itemId > 100000) item = ItemHandler.instance.GetItem(itemId - 100000);
        return item;
    }

    private static int CompareEntries(StoredItemEntry a, StoredItemEntry b, ItemSortMode mode, bool descending)
    {
        if (mode == ItemSortMode.Auto)
        {
            int num = UI_ItemSortUtility.GetAutoSortBucket(a.item).CompareTo(UI_ItemSortUtility.GetAutoSortBucket(b.item));
            if (num != 0) return num;
            int num2 = CompareNames(a, b);
            if (num2 != 0) return ApplyDirection(num2, descending);
            int num3 = a.itemId.CompareTo(b.itemId);
            if (num3 != 0) return num3;
            return a.originalIndex.CompareTo(b.originalIndex);
        }

        int comp = 0;
        switch (mode)
        {
            case ItemSortMode.Name:
                comp = CompareNames(a, b);
                break;
            case ItemSortMode.Type:
                comp = GetTypeOrder(a.item).CompareTo(GetTypeOrder(b.item));
                break;
            case ItemSortMode.Amount:
                comp = a.amount.CompareTo(b.amount);
                break;
            case ItemSortMode.UnitValue:
                comp = GetUnitValue(a.item).CompareTo(GetUnitValue(b.item));
                break;
            case ItemSortMode.StackValue:
                comp = GetStackValue(a).CompareTo(GetStackValue(b));
                break;
        }

        if (comp != 0) return ApplyDirection(comp, descending);
        return CompareTieBreakers(a, b);
    }

    private static int CompareTieBreakers(StoredItemEntry a, StoredItemEntry b)
    {
        int num = CompareNames(a, b);
        if (num != 0) return num;
        int num2 = a.itemId.CompareTo(b.itemId);
        if (num2 != 0) return num2;
        return a.originalIndex.CompareTo(b.originalIndex);
    }

    private static int CompareNames(StoredItemEntry a, StoredItemEntry b)
    {
        return string.Compare(a.itemName, b.itemName, StringComparison.OrdinalIgnoreCase);
    }

    private static int ApplyDirection(int comparison, bool descending)
    {
        return descending ? -comparison : comparison;
    }

    private static int GetTypeOrder(ItemHandler.Item item)
    {
        return (int)(item?.itemType ?? ((ItemHandler.ItemType)int.MaxValue));
    }

    private static int GetUnitValue(ItemHandler.Item item)
    {
        return item?.price ?? 0;
    }

    private static long GetStackValue(StoredItemEntry entry)
    {
        return (long)GetUnitValue(entry.item) * (long)entry.amount;
    }

    private void ToggleBagsMenu()
    {
        if (bagsMenuContainer != null)
        {
            CloseBagsMenu();
            return;
        }

        var accessibleBags = StorageBagHandler.instance?.GetAccessibleBagIds(false);
        if (accessibleBags == null || accessibleBags.Count == 0)
        {
            UI_Chatbox.instance?.AddChatMessage("No storage bags found.", UI_Chatbox.MessageType.gameplay);
            return;
        }

        OpenBagsMenu(accessibleBags);
    }

    public void CloseBagsMenu()
    {
        if (bagsMenuContainer != null)
        {
            bagsMenuContainer.Destroy();
            bagsMenuContainer = null;
        }
    }

    private void OpenBagsMenu(List<int> accessibleBags)
    {
        CloseBagsMenu();

        float menuWidth = 230f;
        float rowHeight = 28f;
        float headerHeight = 35f;
        float menuHeight = Mathf.Min(headerHeight + accessibleBags.Count * rowHeight + 10f, currentWindowSize.y - 45f);

        bagsMenuContainer = LUI_Core.instance.AddContainer(
            "BagsFilterMenu",
            Position.bottomLeft,
            Position.bottomLeft,
            new Vector2(6f, 35f),
            new Vector2(menuWidth, menuHeight),
            image_windowbackground.GetParentTransform());

        if (bagsMenuContainer?.spawnedObject != null)
        {
            bagsMenuContainer.spawnedObject.transform.SetAsLastSibling();
        }

        var bg = LUI_Core.instance.AddImage(
            "DefaultUI/DefaultWindowBackground",
            Position.topLeft,
            Position.topLeft,
            Vector2.zero,
            new Vector2(menuWidth, menuHeight),
            bagsMenuContainer);
        bg.RaycastImage(true);

        LUI_Core.AddText(
            Color.white,
            14,
            TextAnchor.MiddleCenter,
            Position.topLeft,
            Position.topLeft,
            new Vector2(10f, -5f),
            new Vector2(menuWidth - 45f, 25f),
            bagsMenuContainer,
            true,
            "Storage Bags Filter");

        LUI_Core.instance.AddButton(
            "Common/button_close",
            "",
            0,
            Position.topRight,
            Position.topRight,
            new Vector2(-5f, -5f),
            new Vector2(25f, 25f),
            _ => CloseBagsMenu(),
            null,
            bagsMenuContainer);

        var hiddenSet = Plugin.GetHiddenBagIdSet();

        for (int i = 0; i < accessibleBags.Count; i++)
        {
            int bagId = accessibleBags[i];
            bool isHidden = hiddenSet.Contains(bagId);
            var itemDef = ItemHandler.instance?.GetItem(bagId);
            string bagName = itemDef != null ? itemDef.itemName : $"Bag {bagId}";
            float posY = -(headerHeight + i * rowHeight);

            string iconName = UI_Inventory.instance != null ? UI_Inventory.instance.GetItemIcon(bagId) : "invisible";
            var icon = LUI_Core.instance.AddImage(
                iconName,
                Position.topLeft,
                Position.topLeft,
                new Vector2(10f, posY),
                new Vector2(22f, 22f),
                bagsMenuContainer);
            icon.RaycastImage(false);

            string statusText = isHidden ? $"[ ] {bagName}" : $"[X] {bagName}";
            var btn = LUI_Core.instance.AddButton(
                statusText,
                Position.topLeft,
                Position.topLeft,
                new Vector2(36f, posY - 2f),
                new Vector2(menuWidth - 46f, 24f),
                _ =>
                {
                    Plugin.ToggleBagHidden(bagId);
                    DrawItems();
                    OpenBagsMenu(accessibleBags);
                },
                null,
                bagsMenuContainer);

            btn?.AddMouseHoverText(isHidden ? "Click to SHOW this bag in Combined Inventory" : "Click to HIDE this bag from Combined Inventory");
        }
    }

    private void Update()
    {
        UpdateDrag();

        if (dragging && dragBar != null)
        {
            Vector2 pos = dragBar.GetPosition();
            Variables.instance?.SetVarString("CombinedInvPos", $"{(int)pos.x},{(int)pos.y}");
        }

        if (suppressClickUntilMouseRelease)
        {
            if (!Input.GetMouseButton(0) && (LUI_DragHandler.instance == null || !LUI_DragHandler.instance.IsDragging()))
            {
                suppressClickUntilMouseRelease = false;
            }
        }

        if (IsOpen())
        {
            EnsureBehindBottomRightTabs();

            // Check if held bags have changed (picked up a bag, equipped/dropped a bag)
            var currentHeld = StorageBagHandler.instance?.GetAccessibleBagIds(false);
            if (currentHeld != null && !AreListsEqual(currentHeld, currentHeldBagIds))
            {
                DrawItems();
            }
        }
    }

    private static bool AreListsEqual(List<int> a, List<int> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    public override void OnDeactivate()
    {
        base.OnDeactivate();
        CloseBagsMenu();
        DestroyActiveDragGhost();
        UI_ItemTooltips.instance?.Deactivate();
    }

    private void DestroyActiveDragGhost()
    {
        if (activeDragGhost != null)
        {
            activeDragGhost.Destroy();
            activeDragGhost = null;
        }
    }

    private static UI_Inventory.ItemSlot ExtractSlot(object obj)
    {
        if (obj is UI_Inventory.ItemSlot directSlot) return directSlot;
        if (obj is CombinedSlotView view) return view.GetItemSlot();
        return null;
    }

    private static void OnSlotMouseEnter(object[] vars)
    {
        if (vars == null || vars.Length == 0) return;
        var slot = ExtractSlot(vars[0]);
        if (slot == null || slot.itemId <= 0) return;
        var icon = vars.Length > 1 ? vars[1] as LUI_Image : null;

        if (UI_ItemTooltips.instance != null)
        {
            UI_ItemTooltips.instance.customMode = slot.IsStorageBagSlot
                ? UI_ItemTooltips.TooltipMode.storageBag
                : UI_ItemTooltips.TooltipMode.inventory;
            UI_ItemTooltips.instance.ItemTooltip(slot.itemId, icon, slot);
        }
    }

    private static void OnSlotMouseExit(object[] vars)
    {
        UI_ItemTooltips.instance?.Deactivate();
    }

    private static void OnSlotRightClick(object[] vars)
    {
        if (vars == null || vars.Length == 0) return;
        var slot = ExtractSlot(vars[0]);
        if (slot == null || slot.itemId <= 0) return;

        UI_RightClickMenu.instance?.Activate(new object[] { slot });
    }

    private static void OnSlotLeftClick(object[] vars)
    {
        if (instance != null && instance.suppressClickUntilMouseRelease)
        {
            instance.suppressClickUntilMouseRelease = false;
            return;
        }

        if (vars == null || vars.Length == 0) return;
        var slot = ExtractSlot(vars[0]);
        if (slot == null) return;

        // If QuickMove mod is not active, but player held Ctrl, handle quick-move directly
        bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (ctrlHeld && !IsQuickMoveModInstalled())
        {
            HandleFallbackQuickMove(slot, shiftHeld);
            return;
        }

        if (UI_Inventory.instance != null)
        {
            UI_Inventory.instance.LeftClickItemSlot(slot);
        }
    }

    private static bool IsQuickMoveModInstalled()
    {
        return AccessTools.TypeByName("QuickMove.InventoryQuickMovePatch") != null;
    }

    private static void HandleFallbackQuickMove(UI_Inventory.ItemSlot slot, bool transferAllMatching)
    {
        if (slot == null || slot.itemId <= 0 || slot.itemAmount <= 0) return;
        if (UI_Inventory.instance == null || StorageBagHandler.instance == null) return;

        if (slot.IsStorageBagSlot)
        {
            int itemId = slot.itemId;
            int amount = slot.itemAmount;
            int fits = UI_Inventory.instance.GetAmountThatCanFit(itemId, amount);
            if (fits > 0)
            {
                UI_Inventory.instance.AddItemToMainInventoryOnly(itemId, fits);
                slot.AddAmount(-fits);
                AudioHandler.instance?.Play2DSound(14);
            }
            else
            {
                UI_Chatbox.instance?.AddChatMessage("Inventory full!", UI_Chatbox.MessageType.gameplay);
            }
        }
        else
        {
            var heldBags = StorageBagHandler.instance.GetAccessibleBagIds(false);
            if (heldBags != null && heldBags.Count > 0)
            {
                var hiddenSet = Plugin.GetHiddenBagIdSet();
                int itemId = slot.itemId;
                int amount = slot.itemAmount;
                foreach (int bagId in heldBags)
                {
                    if (hiddenSet.Contains(bagId)) continue;
                    if (StorageBagHandler.instance.CanBagStoreItem(bagId, itemId))
                    {
                        var bag = new StorageBagHandler.StorageBag(bagId);
                        int space = bag.CountHowManyItemsCanBeAdded(itemId);
                        if (space > 0)
                        {
                            StorageBagHandler.instance.AddItemToStorageBag(bagId, slot, amount);
                            AudioHandler.instance?.Play2DSound(14);
                            break;
                        }
                    }
                }
            }
        }

        UI_ItemTooltips.instance?.Deactivate();
        instance?.RefreshAllSlots();
    }

    private void OnDragStart(object[] vars)
    {
        if (vars == null || vars.Length < 2) return;
        var slot = ExtractSlot(vars[1]);
        if (slot == null || slot.itemId <= 0) return;

        cancelDragEndAfterClose = false;
        DestroyActiveDragGhost();

        if (LUI_Core.instance != null && CBUI.instance?.canvas != null)
        {
            activeDragGhost = LUI_Core.instance.AddImage(
                UI_Inventory.instance.GetItemIcon(slot.itemId),
                Position.topLeft,
                Position.topLeft,
                Vector2.zero,
                new Vector2(42f, 42f),
                CBUI.instance.canvas.transform,
                true);

            if (activeDragGhost.rectTransform != null)
            {
                activeDragGhost.rectTransform.anchorMin = new Vector2(0f, 1f);
                activeDragGhost.rectTransform.anchorMax = new Vector2(0f, 1f);
                activeDragGhost.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            }

            activeDragGhost.SetAbsolutePosition(CBUI.instance.GetMouseAbsolutePosition());
            activeDragGhost.BringToTop();

            suppressClickUntilMouseRelease = true;
            LUI_DragHandler.instance?.StartDrag(activeDragGhost);
        }

        UI_RightClickMenu.instance?.Deactivate();
    }

    private void OnDragEnd(object[] vars)
    {
        suppressClickUntilMouseRelease = false;

        if (cancelDragEndAfterClose)
        {
            cancelDragEndAfterClose = false;
            LUI_DragHandler.instance?.ResetDrag();
            DestroyActiveDragGhost();
            return;
        }

        var sourceSlot = vars != null && vars.Length > 1 ? ExtractSlot(vars[1]) : null;
        GameObject hitObject = LUI_DragHandler.instance != null ? LUI_DragHandler.instance.EndDrag() : null;
        DestroyActiveDragGhost();

        if (sourceSlot == null || sourceSlot.itemId <= 0 || sourceSlot.itemAmount <= 0)
        {
            return;
        }

        if (hitObject != null)
        {
            var invSlot = hitObject.GetComponent<UI_InventorySlot>();
            var bankSlot = hitObject.GetComponent<UI_BankSlot>();
            var actionSlot = hitObject.GetComponent<UI_ActionBarSlot>();
            var storageSlot = hitObject.GetComponent<UI_StorageInventorySlot>();

            // 1. Drop on main inventory slot
            if (invSlot != null)
            {
                UI_Inventory.instance?.CancelUsingItem();
                var targetSlot = UI_Inventory.instance?.GetSlotByIndex(invSlot.slotId);
                if (targetSlot != null)
                {
                    if (sourceSlot.IsStorageBagSlot)
                    {
                        if (UI_StorageBag.MoveStorageItemToInventorySlot((UI_StorageBag.ItemSlot)sourceSlot, targetSlot))
                        {
                            AudioHandler.instance?.Play2DSound(14);
                        }
                    }
                    else
                    {
                        if (UI_Inventory.instance.MoveItemToInventorySlot(sourceSlot, targetSlot))
                        {
                            AudioHandler.instance?.Play2DSound(14);
                        }
                    }
                }
                RefreshAllSlots();
                return;
            }

            // 2. Drop on bank slot
            if (bankSlot != null)
            {
                UI_Inventory.instance?.CancelUsingItem();
                var targetBankSlot = UI_Bank.instance?.GetSlotByIndex(bankSlot.slotId);
                if (targetBankSlot != null)
                {
                    UI_Inventory.instance.DepositItemToBank(sourceSlot, targetBankSlot, 1, true);
                    AudioHandler.instance?.Play2DSound(14);
                }
                RefreshAllSlots();
                return;
            }

            // 3. Drop on action bar slot
            if (actionSlot != null)
            {
                UI_Inventory.instance?.CancelUsingItem();
                UI_ActionBar.instance?.DragItemToSlot(actionSlot.slotId, sourceSlot.itemId);
                AudioHandler.instance?.Play2DSound(14);
                return;
            }

            // 4. Drop on storage bag slot
            if (storageSlot != null)
            {
                UI_Inventory.instance?.CancelUsingItem();
                int targetBagId = storageSlot.bagId;
                int targetSlotIndex = storageSlot.storageSlotIndex;

                if (sourceSlot.IsStorageBagSlot)
                {
                    var sourceBagSlot = (UI_StorageBag.ItemSlot)sourceSlot;
                    if (sourceBagSlot.bagId == targetBagId)
                    {
                        var targetBagSlot = FindBagSlot(targetBagId, targetSlotIndex);
                        if (targetBagSlot != null)
                        {
                            if (UI_StorageBag.MoveStorageItemToStorageSlot(sourceBagSlot, targetBagSlot))
                            {
                                AudioHandler.instance?.Play2DSound(14);
                            }
                        }
                        else
                        {
                            StorageBagHandler.instance?.MoveItemWithinStorageBag(targetBagId, sourceBagSlot.storageSlotIndex, targetSlotIndex);
                            AudioHandler.instance?.Play2DSound(14);
                        }
                    }
                    else
                    {
                        StorageBagHandler.instance?.AddItemToStorageBag(targetBagId, sourceBagSlot, sourceBagSlot.itemAmount, targetSlotIndex);
                        AudioHandler.instance?.Play2DSound(14);
                    }
                }
                else
                {
                    var targetBagSlot = FindBagSlot(targetBagId, targetSlotIndex);
                    if (targetBagSlot != null)
                    {
                        if (UI_StorageBag.MoveInventoryItemToStorageSlot(sourceSlot, targetBagSlot))
                        {
                            AudioHandler.instance?.Play2DSound(14);
                        }
                    }
                    else
                    {
                        StorageBagHandler.instance?.AddItemToStorageBag(targetBagId, sourceSlot, sourceSlot.itemAmount, targetSlotIndex);
                        AudioHandler.instance?.Play2DSound(14);
                    }
                }

                RefreshAllSlots();
                return;
            }
        }

        // 5. Drop outside UI -> drop to ground
        if (CBUI.instance != null && !CBUI.instance.HoveringOverUI())
        {
            UI_Inventory.instance?.DropItem(sourceSlot);
            RefreshAllSlots();
        }
    }
}

// Harmony patches
[HarmonyPatch(typeof(UI_StorageBagHandler), nameof(UI_StorageBagHandler.ShowBag))]
public static class StorageBagHandlerShowBagPatch
{
    private static readonly Func<UI_StorageBagHandler, int, bool> BagOpenDelegate =
        AccessTools.MethodDelegate<Func<UI_StorageBagHandler, int, bool>>(
            AccessTools.Method(typeof(UI_StorageBagHandler), "BagOpen"));

    public static bool Prefix(UI_StorageBagHandler __instance, int id)
    {
        if (Plugin.PreventBagOpenOnPickup != null && Plugin.PreventBagOpenOnPickup.Value)
        {
            if (__instance == null || BagOpenDelegate == null || !BagOpenDelegate(__instance, id))
            {
                return false;
            }
        }
        return true;
    }
}

[HarmonyPatch(typeof(UI_StorageBagHandler), nameof(UI_StorageBagHandler.FindRenderedSlot), new[] { typeof(int), typeof(int) })]
public static class StorageBagHandlerFindRenderedSlotPatch
{
    public static void Postfix(int bagId, int storageSlotIndex, ref UI_Inventory.ItemSlot __result)
    {
        if (__result == null && UI_CombinedInventoryWindow.instance != null && UI_CombinedInventoryWindow.instance.IsOpen())
        {
            __result = UI_CombinedInventoryWindow.instance.FindBagSlot(bagId, storageSlotIndex);
        }
    }
}

[HarmonyPatch(typeof(UI_StorageBagHandler), nameof(UI_StorageBagHandler.FindRenderedSlotByItemId), new[] { typeof(int) })]
public static class StorageBagHandlerFindRenderedSlotByItemIdPatch
{
    public static void Postfix(int itemId, ref UI_Inventory.ItemSlot __result)
    {
        if (__result == null && UI_CombinedInventoryWindow.instance != null && UI_CombinedInventoryWindow.instance.IsOpen())
        {
            __result = UI_CombinedInventoryWindow.instance.FindBagSlotByItemId(itemId);
        }
    }
}

[HarmonyPatch(typeof(UI_StorageBagHandler), "RefreshSlots", new[] { typeof(int), typeof(int[]) })]
public static class StorageBagHandlerRefreshSlotsPatch
{
    public static void Postfix(int typeId, int[] storageSlotIndexes)
    {
        if (UI_CombinedInventoryWindow.instance != null && UI_CombinedInventoryWindow.instance.IsOpen())
        {
            UI_CombinedInventoryWindow.instance.RefreshBagSlots(typeId, storageSlotIndexes);
        }
    }
}

[HarmonyPatch(typeof(UI_Inventory), "UpdateAmounts")]
public static class InventoryUpdateAmountsPatch
{
    public static void Postfix()
    {
        if (UI_CombinedInventoryWindow.instance != null && UI_CombinedInventoryWindow.instance.IsOpen())
        {
            UI_CombinedInventoryWindow.instance.RefreshMainSlots();
        }
    }
}

[HarmonyPatch(typeof(UI_Inventory), nameof(UI_Inventory.SetupUI))]
public static class InventorySetupUIPatch
{
    public static void Postfix(UI_Inventory __instance)
    {
        if (!Plugin.ShowInventoryButton.Value) return;
        if (__instance == null || __instance.container_inventory == null) return;

        try
        {
            var parent = __instance.container_inventory.GetParentTransform();
            var btn = LUI_Core.instance.AddButton(
                "Combined",
                Position.topRight,
                Position.topRight,
                new Vector2(-100f, 5f),
                new Vector2(80f, 25f),
                _ => UI_CombinedInventoryWindow.Toggle(),
                null,
                parent);

            btn?.AddMouseHoverText("Toggle Combined Inventory");
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"Failed to add Combined Inventory button to UI_Inventory: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(UI_BottomRightTabs), nameof(UI_BottomRightTabs.Button_Tab))]
public static class BottomRightTabsButtonTabPatch
{
    public static void Postfix()
    {
        UI_CombinedInventoryWindow.BringBottomRightTabsToFront();
        UI_CombinedInventoryWindow.EnsureBehindBottomRightTabs();
    }
}
