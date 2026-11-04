using System;
using System.Collections.Generic;
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

        _harmony.PatchAll();

        // Optional patch for QuickMove mod if loaded
        PatchQuickMoveIfPresent(_harmony);

        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    private void Update()
    {
        if (ToggleKey != null && ToggleKey.Value.IsDown())
        {
            UI_CombinedInventoryWindow.Toggle();
        }
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
                foreach (int bagId in heldBags)
                {
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
    public int mainSlotIndex;
    public int bagId;
    public int bagSlotIndex;
    public UI_StorageBag.ItemSlot bagItemSlot;

    public LUIRect container;
    public LUI_Image bgImage;
    public LUI_Image iconImage;
    public LUI_Text amountText;
    public LUI_Image usingOverlay;
    public LUI_Image frameImage;

    public void Refresh()
    {
        if (!isBagSlot)
        {
            var realSlot = UI_Inventory.instance?.GetSlotByIndex(mainSlotIndex);
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

    private LUI_ScrollView scrollView_Inventory;
    private LUIRect container_inventory;
    private LUI_Image activeDragGhost;
    private bool cancelDragEndAfterClose;

    public readonly List<UI_StorageBag.ItemSlot> renderedBagSlots = new();
    public readonly List<CombinedSlotView> allSlotViews = new();
    private readonly List<int> currentHeldBagIds = new();

    private Vector2 currentWindowSize = Vector2.zero;
    private int currentColumns = 5;
    private bool handlesAttached = false;

    public static void Toggle()
    {
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
        float slotsStartX = 42f;
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

        int newCols = CalculateColumns(newSize.x);
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

        currentColumns = CalculateColumns(currentWindowSize.x);

        container_inventory = LUI_Core.instance.AddEmptyContainer("CombinedInvContainer", image_windowbackground);
        scrollView_Inventory = LUI_Core.instance.AddScrollView(
            null,
            Position.center,
            Position.center,
            Vector2.zero,
            Vector2.zero,
            container_inventory);

        scrollView_Inventory.SetStretch(0f, 1f, 0f, 1f, 0f, -20f, 40f, 25f);

        int columns = currentColumns;
        float slotSize = 45f;
        float spacing = 5f;
        float slotsStartX = 42f; // room for section bag indicator on the left
        float startY = -10f;

        int currentGlobalRow = 0;

        // 1. Add 35 main inventory slots (0..34)
        int mainStartRow = currentGlobalRow;
        for (int i = 0; i < 35; i++)
        {
            int col = i % columns;
            int row = mainStartRow + (i / columns);
            float posX = slotsStartX + col * (slotSize + spacing);
            float posY = startY - row * (slotSize + spacing);

            CreateSlotUI(posX, posY, slotSize, false, i, 0, 0);
        }

        // Indicator on the left side for Main Inventory
        float mainIconY = startY - mainStartRow * (slotSize + spacing) - 8f;
        CreateSectionIndicator(6f, mainIconY, 28f, "BottomRightTabs/Inventory", "Main Inventory (35 slots)", 0);

        int mainRowCount = Mathf.CeilToInt(35f / columns);
        currentGlobalRow += mainRowCount;

        // 2. Add held storage bag slots
        var accessibleBags = StorageBagHandler.instance?.GetAccessibleBagIds(false);
        if (accessibleBags != null)
        {
            foreach (int bagId in accessibleBags)
            {
                currentHeldBagIds.Add(bagId);
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
                string tooltipText = $"{bagName} ({bagSize} slots)";

                CreateSectionIndicator(6f, bagIconY, 28f, bagIconName, tooltipText, bagId);

                int bagRowCount = Mathf.CeilToInt((float)bagSize / columns);
                currentGlobalRow += bagRowCount;
            }
        }

        float contentHeight = currentGlobalRow * (slotSize + spacing) + 20f;
        scrollView_Inventory.SetContentHeight(Mathf.Max(200f, contentHeight));

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

            // Clicking the bag icon opens/focuses that bag's standalone window
            iconImg.AddClickUp(
                _ =>
                {
                    UI_StorageBagHandler.instance?.OpenBag(bagId);
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
        int mainSlotIndex,
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
            mainSlotIndex = mainSlotIndex,
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
            var realSlot = UI_Inventory.instance?.GetSlotByIndex(mainSlotIndex);

            var dropTarget1 = bgImage.spawnedObject.AddComponent<UI_InventorySlot>();
            dropTarget1.slotId = mainSlotIndex;
            var dropTarget2 = iconImage.spawnedObject.AddComponent<UI_InventorySlot>();
            dropTarget2.slotId = mainSlotIndex;

            iconImage.AddMouseHover(
                OnSlotMouseEnter,
                new object[] { realSlot, iconImage, realSlot },
                OnSlotMouseExit,
                null);

            iconImage.AddRightClickDown(OnSlotRightClick, new object[] { realSlot });
            iconImage.AddClickUp(OnSlotLeftClick, new object[] { realSlot });
            iconImage.AddDrag(OnDragStart, OnDragEnd, new object[] { iconImage, realSlot });
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
                new object[] { bagItemSlot, iconImage, bagItemSlot },
                OnSlotMouseExit,
                null);

            iconImage.AddRightClickDown(OnSlotRightClick, new object[] { bagItemSlot });
            iconImage.AddClickUp(OnSlotLeftClick, new object[] { bagItemSlot });
            iconImage.AddDrag(OnDragStart, OnDragEnd, new object[] { iconImage, bagItemSlot });
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

    private void Update()
    {
        UpdateDrag();

        if (dragging && dragBar != null)
        {
            Vector2 pos = dragBar.GetPosition();
            Variables.instance?.SetVarString("CombinedInvPos", $"{(int)pos.x},{(int)pos.y}");
        }

        if (IsOpen())
        {
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

    private static void OnSlotMouseEnter(object[] vars)
    {
        if (vars == null || vars.Length == 0) return;
        var slot = vars[0] as UI_Inventory.ItemSlot;
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
        var slot = vars[0] as UI_Inventory.ItemSlot;
        if (slot == null || slot.itemId <= 0) return;

        UI_RightClickMenu.instance?.Activate(new object[] { slot });
    }

    private static void OnSlotLeftClick(object[] vars)
    {
        if (vars == null || vars.Length == 0) return;
        var slot = vars[0] as UI_Inventory.ItemSlot;
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
                int itemId = slot.itemId;
                int amount = slot.itemAmount;
                foreach (int bagId in heldBags)
                {
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
        var slot = vars[1] as UI_Inventory.ItemSlot;
        if (slot == null || slot.itemId <= 0) return;

        cancelDragEndAfterClose = false;
        DestroyActiveDragGhost();

        if (LUI_Core.instance != null && CBUI.instance?.canvas != null)
        {
            activeDragGhost = LUI_Core.instance.AddImage(
                UI_Inventory.instance.GetItemIcon(slot.itemId),
                Position.center,
                Position.center,
                Vector2.zero,
                new Vector2(42f, 42f),
                CBUI.instance.canvas.transform,
                true);

            activeDragGhost.SetAbsolutePosition(CBUI.instance.GetMouseAbsolutePosition());
            activeDragGhost.BringToTop();

            LUI_DragHandler.instance?.StartDrag(activeDragGhost);
        }

        UI_RightClickMenu.instance?.Deactivate();
    }

    private void OnDragEnd(object[] vars)
    {
        if (cancelDragEndAfterClose)
        {
            cancelDragEndAfterClose = false;
            LUI_DragHandler.instance?.ResetDrag();
            DestroyActiveDragGhost();
            return;
        }

        var sourceSlot = vars != null && vars.Length > 1 ? vars[1] as UI_Inventory.ItemSlot : null;
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
