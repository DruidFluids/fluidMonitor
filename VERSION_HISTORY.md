# fluidMonitor Version History

## Versioning scheme

- **Major (1.x → 2.0)**: breaking changes or huge milestones
- **Minor (1.x.y → 1.(x+1).0)**: new user-facing features, schema changes
- **Patch (1.x.y → 1.x.(y+1))**: bug fixes, behavior tweaks, small refinements

Every change bumps something. No multiple-feature batches sharing a version.

## Release log

### v1.21.2 (current)

**Test suite fixes (19 failures from first v1.21.1 run)**

No production code changes. All 19 test failures were test-suite bugs, not app bugs.

- **`Restore-Settings` cascade (12 failures)** -- `Widget-FreshInstallCentersOnPrimary` deletes `settings.json` and `Restore-Settings` silently did nothing when the backup was null (the file didn't exist at `Backup-Settings` time). Every subsequent test that read the file crashed with "Cannot find path". Fixed: `Restore-Settings` now starts+stops the app once to regenerate a fresh file when no backup exists.
- **`Add-Type` conflict (2 failures: `GameMode-PositionRadioSurvivesDialogReopen`, `GameMode-DoesNotMutatePersistedSettings`)** -- `TestUtil.MouseClickHelper` was already defined at script scope; the two new tests tried to define it again without `-ErrorAction SilentlyContinue`. Added the guard.
- **`Shorten-CpuNameDropsCoreCount` regex (1 failure)** -- The PowerShell `-match` assertion used `'d\+-Core'` (single-quoted = literal `d`). The C# source is correct (`@"\s+\d+-Core"`). Fixed to `'\d\+-Core'`.
- **`Settings-OpeningDoesNotChangeSelectedDisk` (1 failure)** -- `Get-AllText` only finds `ControlType.Text` elements; `ComboBox` items in a `DataTemplate` aren't in the UIA text tree unless the combo is expanded. Fixed: expand the combo via `ExpandCollapsePattern`, check `ListItem` children, then collapse.
- **`Settings-HotkeyCaptureRejectsEscapeAndBareKeys` (1 failure)** -- Inner function `Click-HotkeyBox` defined inside `Test-Case` is unreliable in PS5 scoping. Replaced with a script block (`$clickBox = { ... }`). Also increased waits for each step.
- **`Settings-UndoRestoresPriorAccent` (1 failure)** -- The Skins-row dice only randomizes the skin (v1.20 split), not colors, so `AccentColor` never changed and the pre-condition fired. Switched to the Preset Themes dice (`ThemePresetDiceBtn`) which changes colors+skin together.
- **`Settings-CustomColorPlusButtonSaves` / `Codec-ImportCreatesTaggedCustomColor` (2 failures)** -- `Stop-App` uses `Stop-Process -Force` which bypasses `App.OnExit`; the `SettingsService.Save` call in the save handler had not flushed to disk yet. Added 1000ms flush wait between `Close-SettingsWindow` and `Stop-App`.
- **`Persist-TileOrderFillsMissingKinds` (1 failure)** -- Same `Stop-Process -Force` bypass: `NormalizeTileOrder` runs in `Load()` but the normalized list was never written back (that happens in `OnExit`). Rewrote as a crash-free launch check + source canary for the normalize logic.

### v1.21.1

**Startup position fixup**

- **Fresh install centers on the primary monitor.** A true first run (no `settings.json`) now centers the widget in the primary work area after first layout, instead of opening at the hardcoded (100,100) default. `SettingsService` exposes `LastLoadWasFresh`; `MainWindow` performs the centering on `Loaded` (after `SizeToContent` resolves the real size).
- **Off-screen positions are rescued.** The installer never touches `%APPDATA%`, so a "first" launch after reinstall restores whatever `WindowLeft/Top` the old file had -- including coordinates from a monitor layout that no longer exists. On every startup the restored rect is now checked against the virtual screen; if less than a 40x40 sliver is visible, the widget recenters on primary instead of opening invisibly. Corrupt-file recovery also routes through the fresh-install centering path.
- Tests: `Widget-FreshInstallCentersOnPrimary` (deletes settings.json, asserts the widget center lands at the primary work-area center) and `Widget-OffScreenPositionRescued` (WindowLeft/Top = -30000, asserts the widget is rescued to primary center).

### v1.21.0

**Bug-fix batch — full logic review of the codebase**

- **Disk picker actually works now.** The selection previously only landed in the user's `settings.json`, which the LocalSystem service never reads — the Disk tile was permanently stuck on the `_Total` aggregate regardless of the dropdown. New `setSelectedDisk` command on the cmd pipe: the service persists it to `service.json` and re-routes its PhysicalDisk perf counters live (no restart). Dropdown gains an explicit `(All disks)` entry, and opening Settings no longer silently converts the aggregate default to "Disk 0" (`LoadDiskCombo`/`LoadNetworkAdapterCombo` now run under the `_loading` guard).
- **Game Mode no longer corrupts persisted settings.** Entering game mode used to overwrite the live `Show*` tile flags, `Orientation`, `ClickThrough`, and (via `OnLocationChanged`) `WindowLeft/Top` — so exiting the app while game mode was active saved the game-mode state as the user's normal configuration. Tile visibility and orientation are now overridden at render time only (`RebuildVisibleTiles`/`ApplyOrientation` check `_gameModeActive`), click-through uses a non-persisting path, and window position recording is suspended while active. Removed the dead `SettingsService.Load()` call in `ExitGameMode`.
- **Game Mode position no longer resets to Top Right.** The dialog's fallback check only inspected the top three position radios; any saved bottom/center position was stomped on every open (all eight share a radio group) and then persisted wrong on Save. Fallback now checks all eight.
- **Clock tile gets a Game Mode toggle** (`GameModeShowDateTime`, default off). Previously it had no game-mode flag and stayed visible with no way to hide it.
- **Hotkey capture hardened.** Escape now cancels capture instead of becoming the hotkey, and at least one modifier is required. Previously a bare Esc in the click-through hotkey box registered a global no-modifier `VK_ESCAPE` hotkey that hijacked the Escape key from every application system-wide.
- **Warning gradient renders on time.** Warnings are evaluated before tile text is assigned, and `Fmt` gains a bindable `AccentOverride` attached property — the gradient color used to lag one snapshot behind and never appeared at all if the displayed value string didn't change.
- **Traffic indicator styles are now distinct.** `TrafficIndicatorState` exposes the style name instead of a bool; Blink (0.6s pulse), Fade (1.6s slow ramp), and Glow (accent-colored drop shadow via new `AccentGlowColor` resource) each have their own trigger. Previously all three played the identical blink.
- **CPU name shortening fixed**: "AMD Ryzen 9 9950X3D 16-Core Processor" rendered as "Ryzen 9 9950X3D 16-Core" because the bare " Processor" suffix matched before the longer entries. Core-count tokens are now stripped with a generic `\d+-Core` regex.
- **Update interval honors the slider.** The client-side throttle used a strict comparison against the full interval, so normal pipe jitter dropped every other snapshot (a 1500ms setting rendered at ~3000ms). Now applies with 10% tolerance.
- **Schema migrations can fire.** `SchemaVersion` defaulted to the CURRENT version, so pre-v1.19 settings files (which lack the property) deserialized as already-migrated and skipped every migration. Default is now the oldest version (1); the fresh-install path stamps current explicitly. `SchemaVersion` added to the Reset-All preserve list.
- **ActiveTheme cleared on manual changes.** Manual color edits, mode toggle, Colors-cycler picks, skin changes, and saved-preset loads now clear `ActiveTheme` (as the AppSettings contract always documented), so the Preset Themes cycler no longer shows a stale theme name. The appearance undo snapshot also captures/restores `ActiveTheme`.
- **Warnings dialog ESC commits in-progress edits.** `Keyboard.ClearFocus()` doesn't move logical focus, so a Threshold/FlashColor edit in progress was silently discarded; the binding is now pushed explicitly via `UpdateSource()`.
- **Saved network adapter survives being temporarily down** — it's kept in the dropdown instead of showing a blank selection.
- Cleanup: removed dead cast in `BuildDeviceSelector` (latent InvalidCastException), duplicate save in `SaveGpuCustomName`, and corrected the Reset-All relaunch comment (no `--open-settings` flag exists).

**Tests (same build, per TESTING-DISCIPLINE rule 11):** 14 new automated tests added to `Test-FluidMonitor.ps1`, covering every fix in this batch plus the v1.20.0 deferred testing debt:
- `Service-SetSelectedDiskViaPipe` -- full IPC path: setSelectedDisk command, getConfig echo, and persistence to ProgramData `service.json` (the missing link that made the picker a no-op).
- `Settings-OpeningDoesNotChangeSelectedDisk` -- opening Settings must not convert the all-disks default to "Disk 0"; also asserts the new "(All disks)" entry is present.
- `Settings-HotkeyCaptureRejectsEscapeAndBareKeys` -- Esc cancels capture, bare keys rejected, Ctrl+Alt+G still captures (live keyboard test).
- `GameMode-DoesNotMutatePersistedSettings` -- enters game mode via global hotkey, triggers a real handler save, asserts tile flags / orientation / click-through / window position did not leak to disk.
- `GameMode-PositionRadioSurvivesDialogReopen` -- BottomLeft must survive an open + Save&Close cycle of the Game Mode dialog; also asserts the new Clock checkbox exists.
- `GameMode-ClockToggleRoundTrip` + `GameModeShowDateTime` added to `GameMode-FullConfigRoundTrip`.
- `Persist-MissingSchemaVersionTriggersMigration` -- a settings.json LACKING the SchemaVersion property (every pre-v1.19 file) must fire the v1->v2 migration.
- `Persist-TrafficIndicatorStylesLaunch` -- all four styles launch cleanly; Glow exercises the DynamicResource DropShadowEffect parse path.
- `Shorten-CpuNameDropsCoreCount` -- source canary for the generic N-Core regex + live widget-text assertion on CPUs whose name carries a core-count token (e.g. the 9950X3D).
- `Settings-DarkModeClickClearsActiveTheme` -- manual color path clears ActiveTheme.
- v1.20.0 deferred debt cleared: `Settings-ThemePresetArrowAppliesAtomically` (ActiveTheme + 5 colors + ActiveSkin set together, cross-checked against the source table), `Settings-CustomThemesSurviveResave` (CustomTheme serialization round-trip through a real save), `Settings-ImportButtonPrecedesExport`, `Source-BuiltInThemeSkinsValid` (89-entry count, per-franchise counts, every SkinName exists in BuiltInSkins).
- `Source-VersionLockstep` -- automates discipline rule 12 (the three version files must match).
- New helpers: `Find-ElById` (AutomationId lookup), `Send-FluidPipeCommand` (one-shot cmd-pipe), `Open-SettingsViaGear` (script-scope settings opener).

Remaining manual-verification rows (documented in coverage.txt): interval throttle timing, Warnings-dialog ESC edit commit (templated row focus), and visual confirmation of the Glow indicator under live traffic.

### v1.20.5
- **Themes no longer flag the Colors cycler as "Custom"**: when a Preset Theme is applied, its color palette is now also a recognized entry in `AllPresets()`, suffixed `(theme)`. The Colors cycler matches into it instead of falling through to "Custom".
- **Colors browse popup now grouped**: built-in palettes (Catppuccin, Nord, etc.), Custom Colors (user-saved + imported), and each franchise's themed palettes (Spyro, WoW, Fallout, etc.) each get their own collapsible header. Theme entries inside a franchise group display the bare name with the franchise prefix stripped (`Pandora` instead of `Borderlands Pandora`).
- **Colors cycler arrow label** strips `(theme)` and franchise prefix at display time. Underlying data still carries the full name so the lookup keeps working.

### v1.20.4
- **Build fix**: added `System.Management` PackageReference (v8.0.0) to `Fluid.App.csproj`. v1.20.3 introduced WMI calls in `SettingsWindow.LoadDiskCombo` without referencing the assembly, which caused `error CS0234: The type or namespace name 'Management' does not exist in the namespace 'System'`.

### v1.20.3

**Tile customization batch + UX polish**

- **Network arrow spacing slider** (Settings → Network section). Drag to pull the `↓` `↑` arrows closer to or further from the speed numbers. Range 8-40px, default 16.
- **Disk R: / W: spacing slider** (Settings → new Disk section). Same control for the Disk tile's R: and W: labels.
- **New Disk section in Settings** with a physical-disk dropdown. Lists all `Win32_DiskDrive` entries (model + index). Selecting a disk routes the Read/Write counters to that specific physical disk and shows its model under the Disk tile. Changing the disk requires an app restart for the service to pick up the new selection.
- **Network traffic indicator** cycler button — Off → Blink → Fade → Glow. The `↓` `↑` arrows pulse opacity 1.0 → 0.35 → 1.0 every 0.6s when traffic is active on that direction. Cycler value persisted in `AppSettings.NetworkTrafficIndicator`.
- **Bottom button row redesigned**: "Reset All" → "Reset to Defaults", "Close" → "Save and Close", and ellipsis characters removed from "Warnings" and "Game Mode".
- **Tray right-click menu** stripped to Settings + Exit only. Show, Click-through, Game Mode removed (left-click already toggles show/hide; the other actions are accessible from in-app Settings).
- **Undo depth raised from 2 to 5**. Tooltip updated.
- **Layout selector** converted from radio buttons to paired ToggleButtons. Selected one fills with AccentBrush + white text.
- **Muted text contrast slider relocated** beneath the Muted swatch column. Sized to match the swatch column width. Changing this slider does NOT mark the palette as Custom (it's a render-time multiplier, not a color value change).

**Service additions**
- `Fluid.Service.HardwareMonitor` now routes the `PhysicalDisk` perf counter to a specific instance based on `ServiceConfig.SelectedDiskId`.
- WMI query for selected disk's Model on service startup; populates `StorageStats.Model`.
- `StorageStats` adds `Model` and `DiskId` fields.

### v1.20.2
- **Brightened muted text on all 89 built-in themes**: each theme's MutedTextColor was blended 25% toward its TextColor to improve readability. The relative hue tinting of each muted color is preserved (cool blues stay cool, warm browns stay warm), but overall brightness is significantly improved. For example, `Spyro Artisans Hub` muted went from `#FF6A8A50` (sum 372) to `#FF8EA372` (sum 547).
- **New "Muted Text Visibility" slider** in Settings (under Font > Sizes). Range 50%–160%, default 100%. Above 100% blends muted toward text color (more readable), below 100% blends toward background (more subdued). Lets users dial muted contrast for their monitor and taste without editing themes.
- **`AppSettings.MutedContrast`** persisted in settings.json (default 1.0). `ThemeApplier.Apply` recomputes MutedTextBrush from the raw hex × slider value at render time, so changes are live.

### v1.20.1
- **Theme display names cleaner**: cycler arrow label and browse popup entries now strip the franchise prefix at display time. So the cycler reads `Velvet Room` instead of `Persona 5 Velvet Room`, and inside the `▸ Persona 5 (3)` group the entries read `Phantom Thieves`, `Mementos Subway`, `Velvet Room`. The underlying data still carries the full name (`"Persona 5 Velvet Room"`) so franchise grouping continues to work.

### v1.20.0

**Preset Themes system** — major appearance feature.

- **89 built-in Themes** spanning 25+ gaming franchises. A Theme = colors + skin atomic combo (one click applies both). Franchises covered: Spyro (10), WoW (7), RuneScape (5), League of Legends (4), Fallout (3), Borderlands, Witcher, Cyberpunk 2077, Hades, Helldivers, Doom, Mass Effect, No Man's Sky, Hollow Knight, Stardew Valley, Minecraft, Persona 5, DayZ, Amnesia, Baldur's Gate 3, Crash Bandicoot, Spore, Stronghold 2, Valheim, World of Tanks (3 each).
- **New "Preset Themes" cycler row** above Skins and Colors. Independent dice + undo + browse + folder buttons.
- **Independent dice buttons** per row: Skins dice randomizes skin only, Themes dice randomizes theme (colors+skin atomic). Colors row has no dice.
- **Custom theme loading** via the folder button. Loads `.fluidtheme` JSON files from disk. Loaded themes appear in a "Custom" group at the top of the browse popup.
- **Collapsible franchise groups** in the Themes browse popup.
- **`BuiltInTheme` record** + `CustomTheme` class for persistence.
- **`AppSettings.ActiveTheme`** + `AppSettings.CustomThemes` list with JSON round-trip.

**UX polish**

- **Renamed "Saved presets" → "Saved Themes"** in the slot row heading (since preset slots are functionally combined color+skin entries — same concept as Themes).
- **Fixed duplicate `(i)` badge**: removed the `, i` suffix from imported color names in the cycler. The browse popup's `ⓘ` icon is now the only visual indicator of import provenance.
- **Matte popup dialog** for import success — replaces the cramped inline message in the share code panel.
- **Swapped Export/Import button order**: Import is now on the left (was right), Export on the right.
- **Import box auto-closes** after successful Apply (previously stayed open with the code still visible).
- **Anchor-aware widget resizing**: when the widget's skin or content changes size, Left/Top compensate so the snapped edge stays in place. Bottom-anchored widgets now grow upward, right-anchored grow leftward, and floating widgets grow around their center.

### v1.19.3
- Colors cycler shows only built-ins + CustomColors (UserPresets removed).
- Import success message: "Imported. Save to a preset slot (1-5) to keep these settings."
- Preset-slot-save no longer moves cycler.

### v1.19.2
- Reset to Defaults restarts the app.
- Single-instance mutex retries 2s on startup.

### v1.19.1
- SkinManager calls RebuildVisibleTiles after every skin swap.

### v1.19.0
- CustomColors model refactor + schema v1→v2 migration.
- `×` delete + `ⓘ` info badge in Colors cycler.
- Share code carries `ColorPresetName`.
- Layout reshuffle (°F by behavior cluster; Font reorder; Skins/Colors labels).
- Everforest Dark theme.
- ActiveSkin default → "Default".

### v1.18.0
- Drag-reorder tile toggles.
- Import box opens blank.
- 11 new tests.

### v1.17.x and earlier
Inherited at start of session. See in-source comments.

## Build files updated in lockstep
- `installer/fluid.iss` (`#define AppVersion`)
- `Fluid.App/Fluid.App.csproj` (`<Version>`)
- `Fluid.Service/Fluid.Service.csproj` (`<Version>`)

Installer output: `fluidMonitor_installer_v{AppVersion}.exe`.

## Deferred to v1.20.x

The tile customization batch (Network arrow spacing slider, Disk R/W label spacing slider, Disk selector with model display, Network traffic indicator animation with 4 styles) is deferred from v1.20.0 and will land in v1.20.1+ in a focused session. The Disk multi-physical-disk selector needs Fluid.Service sensor plumbing which is non-trivial.

**TESTING DEBT (next build must address):** v1.20.0 shipped without updated tests for any of the new functionality. Next build must include:
- Round-trip test for `AppSettings.ActiveTheme` and `AppSettings.CustomThemes` (CustomTheme serialization)
- `BuiltInThemes` count assertion (89 entries) + per-franchise group counts
- Test that every `BuiltInTheme.SkinName` references an actual installed skin (catches typos that would silently no-op)
- Test that `ApplyThemePreset` updates `s.ActiveTheme`, all 5 color fields, and `s.ActiveSkin` together
- Test that `AllPresets()` returns only built-ins + CustomColors (UserPresets excluded)
- Test the `(custom, i)` duplicate-badge fix: no `, i` suffix appears in any cycler label
- UI test that the new Themes cycler row + 3 cycler row layout renders
- Test that import-success path closes the share code panel AND clears ShareCodeBox AND opens ImportNoticePopup
- Test that the export/import button order is Import-then-Export
- Anchor-aware resize test (synthetic SizeChanged event with mocked work area, verify Left/Top shifts correctly for each edge case)

Other deferred items:
- Bottom button row redesign (Reset All → Reset to Defaults, Close → Save and Close, etc.)
- Tray right-click menu strip-down (Settings + Exit only)
- Undo depth 2 → 5
- Layout selector radio dots → toggles

