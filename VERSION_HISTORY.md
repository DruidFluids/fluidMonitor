# Version History


## v1.0.7 (2026-06-12)

### Downloadable theme packs
- 141 franchise themes (WoW, Fallout, League of Legends, Spyro, etc.) removed from built-in binary
- 18 default themes remain built-in (Default + 17 nature themes)
- 25 theme packs hosted on GitHub as JSON files with manifest
- New Theme Store window (2-column grid, swatch previews, install/remove)
- ThemePackService: fetch manifest, download packs to %AppData%\fluidMonitor\themes\
- ThemeApplier.GetAllThemes() merges built-in + downloaded themes seamlessly

### Discovery points
- Download button (↓) added left of Die button in Preset Themes cycler row
- "More themes on GitHub" banner at bottom of theme browse popup
- Both open the Theme Store window

### From v1.0.6
- Split back-button chrome (← | TITLE) on all Tools sub-windows
- Dashboard tile grid layout for Tools window
- Updates section moved inline to Settings
- Check Now disables when mode is Off


## v1.0.6 (2026-06-12)

### UI: standardized Tools sub-window chrome
- New split back-button title bar: ← | TITLE — entire left region is one clickable back button with hover highlight
- Applied identically to Alerts, Game Mode, and Utilities windows
- Game Mode width bumped from 420 → 460 (matches Alerts)
- Utilities width bumped from 440 → 460 (matches Alerts)
- Utilities: removed duplicate inline "UTILITIES" header, normalized title bar to 11px Bold (was 13px SemiBold)
- Game Mode: content margins normalized to DockPanel layout matching Alerts
- All three windows now use consistent DockPanel content layout with docked bottom bar

### Updates moved to Settings
- Updates section moved from standalone window into Settings right column (below Sensors)
- Compact inline layout: version + mode selector + check button in one card
- "Check now" button disables when update mode is set to Off
- Removed Updates card from Tools window (3 cards remain: Alerts, Game Mode, Utilities)
- UpdatesWindow.xaml/.cs retained but no longer launched

### Tools window redesign
- Dashboard tile grid layout replaces vertical card list
- 3-column grid with large centered icons in colored backgrounds (coral/blue/green)
- Accent border hover effect on each tile
- Window width shrunk from 460 → 380 (compact grid needs less width)


## v1.0.5 (2026-06-12)

### Fixes
- Game Mode back button now functional (Click handler, not broken behavior)
- Sub-windows open in front of Settings (Owner + ShowDialog)
- Alerts back button repositioned below title bar
- Disk/Network value centering improved (HorizontalAlignment)
- Tools window enlarged (460px wide, 380px min height)
- Light mode fix covers all 9 light themes in ThemeApplier.Apply()
- GameModeWindow.xaml.cs included in build
## v1.0.4 (2026-06-12)

### New: Tools window + Updates
- Split [?|gear] button in Settings bottom bar opens Help or Tools
- Tools window: card-based launcher for Alerts, Game Mode, Utilities, Updates
- Dedicated Updates window with Auto / Manual / Off modes (default: Manual)
- Checks GitHub releases for new versions, shows changelog, one-click install
- "Last checked" timestamp (Option A footer style)
- Back arrows on Alerts, Game Mode, Utilities windows to return to Tools

### UI
- Settings bottom bar: split button moved to right, Reset to Defaults to left
- Disk and Network tile values centered (single-digit "0" no longer drifts left)
- All 5 appearance icon buttons uniform 34x34
- Cycler row alignment recalculated
- "TILE ALERTS" and "GAME MODE" promoted to window title bars
- CPU/GPU capitalization via DisplayName property
- 17 nature themes moved to ungrouped default list
- Clock date number highlighted with accent color

## v1.0.2 (2026-06-12)

### Themes
- 17 new themes: Evergreen, Sandstone, Deep Current, Morning Dew, Hearthwood, Terracotta, Tidestone, Forest Gold, Inlet, Canopy, Sage, Clay Coast, Dusk Harbor, Fern, Driftwood, Glacier, Amber Trail
- Updated WoW zone themes: Howling Fjord, Sholazar Basin, Ashenvale, Kun-Lai Summit, Nagrand
- Total built-in themes: 159

### UI
- "Warnings" renamed to "Alerts"
- Themed ScrollBar (6px, rounded, muted)
- Help footer: :)
- VirusTotal scan linked in README

## v1.0.0 (2026-06-12)
Initial public release.
