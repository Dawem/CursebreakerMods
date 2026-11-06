# The Black Grimoire: Cursebreaker - Mods

A collection of quality-of-life BepInEx mods for **The Black Grimoire: Cursebreaker**.

---

## Included Mods

- **CombinedInventory**: Provides a unified inventory window to view and manage items across bags and storage, with options for seamless grid display, hiding bags, preventing bag windows from auto-opening on pickup, and configurable toggles to prevent the standard inventory tab from auto-opening at the bank, shops, crafting stations/workbenches, during active crafting, and when using the mount saddlebag shortcut key while the combined inventory is open. Also follows shop sell rules by dimming un-sellable items and disabling the sell button when interacting with merchants.
- **GodMode**: God mode suite featuring invincibility, infinite health/mana/special power, no ability cooldowns, configurable damage multiplier / one-hit kill, movement speed multiplier, debug mode toggle, hotkey toggle (default `F10`), and in-game ModConfigMenu support.
- **HighlightToggle**: Adds a hotkey (default `H`) to toggle persistent highlighting for interactables, loot, resources, and NPCs.
- **ModConfigMenu**: Adds a native "Mods" tab to the in-game Settings/Options window, allowing players to view and configure options for all installed BepInEx mods with live editing, hotkey binding, search filtering, and preset resets.
- **QuickMove**: Allows quick item transfers between inventory and containers using modifier keys (e.g., `Ctrl + Click` to transfer a stack, `Ctrl + Shift + Click` to transfer matching items).
- **SkillRecipeAmounts**: Displays required item amounts on crafting recipes when viewing skills and milestones in the skill menu, matching the native UI style with configurable single-item amount visibility and inventory color-coding.
- **StopStealingMyStuff**: Prevents NPCs (miners, woodcutters, foragers, etc.) from depleting or damaging harvestable resource nodes.
- **ToolSlots**: Adds dedicated equipment slots for tools (axes, pickaxes, fishing rods, shovels, etc.) so they don't take up bag inventory space.
- **WikiSearch**: Adds a right-click context menu option on items (inventory, equipment, bank), NPCs, and mobs to search for them on the wiki in your default browser (customizable wiki URL template, defaults to `https://cursebreaker.wiki/wiki/<item name>`).

---

## Building from Source

- Requires **.NET SDK** (supporting .NET Standard 2.1) or Visual Studio 2022.
- Ensure reference DLLs from the game and BepInEx packages are available. The project files assume dlls will be at `/lib/`
- Build the solution or individual projects:
  ```bash
  dotnet build TheBlackGrimoireCursebreaker.sln -c Release
  ```

---

## License

This project is licensed under the [MIT License](LICENSE).
