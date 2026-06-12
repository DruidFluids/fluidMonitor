# Testing Discipline

This project maintains comprehensive automated test coverage. **When adding features, modifying settings, introducing new skins, etc., update the test suite in the same commit.** This rule applies to bug fixes too — every fix gets a regression test that fails on the broken code and passes on the fixed code.

## The four tiers (2026-06-06 refactor)

| Tier | Switch | Time | When to run | What it covers |
|---|---|---|---|---|
| Smoke | `-Smoke` | ~10s | Rapid iteration, pre-commit | Lean service + widget startup sanity |
| Default | (no flag — this is the default) | ~5.5 min | Bare invocation, normal development | All 14 categories, Exhaustive matrix, 16 Settings UI click-flow tests, 16 skin screenshots under Dark theme, live-fire hotkey tests. **THIS IS THE EVERYDAY RUN.** |
| Visual | `-Visual` | ~4 min | After touching skins, themes, or ThemeApplier | 32 visual-{skin}-{orientation}.png frames + 64 coupling-{theme}-{skin}.png frames. Pure screenshot baseline regeneration — humans review. |
| Full Sweep | `-All` | ~9.5 min | Before releases, before sharing builds | Default + Visual combined. Equivalent to old "Exhaustive". |

Bare invocation runs Default. Pass `-Smoke` for fast iteration. Pass `-Visual` to regenerate visual baselines without re-running behavior tests. Pass `-All` for full sweep.

**Why visual matrices are opt-in:** They take 4+ minutes and don't catch bugs by asserting — they catch bugs when you open the resulting PNG and notice it's wrong. Running them every iteration is wasted time unless you actually changed something visual. Default tier still includes one screenshot per skin (under Dark theme) as a "did anything explode" check.

## Where things live

- `tests/Test-FluidMonitor.ps1` — single test runner (smoke + default + exhaustive)
- `tests/TestHelpers.ps1` — managed-only helpers (screenshots, cursor) — kept separate so AV doesn't see PInvoke + screen capture in one file
- `tests/Collect-Diagnostics.ps1` — bundles diagnostic info + screenshots + coverage report into a zip in `installer/Output/`
- `tests/test-results.txt` — last run's structured results
- `tests/coverage.txt` — feature → test mapping with automated % (refreshed every run)
- `installer/Output/fluidMonitor-diagnostics-*.zip` — uploadable bundles

## Test categories

| Category | Tier | What it covers | Add a test here when... |
|---|---|---|---|
| Service | Default | fluidsvc health, pipes, memory | New service feature, IPC change, new pipe |
| Widget | Default | Widget appears, renders, lives | New widget UI element, new live data type |
| Settings | Default | Settings window opens & has sections | New section, new control in Settings |
| SettingsUI | Default | UI click-flow tests (button clicks actually fire) | New button or click flow in Settings — write a UI test, not just a JSON persistence test |
| Persistence | Default | settings.json round-trips correctly | New property added to AppSettings |
| Layout | Default | Orientation, opacity, scale, fonts, dimensions | New layout property |
| Colors | Default | 5 color swatches, dark mode, invalid hex | New color setting or theme system change |
| Tiles | Default | Show/hide combos, custom names, empty state | New tile type or tile customization |
| GameMode | Default | 8 positions, 3 orientations, hotkey, full round-trip | New GameMode property |
| Remote | Default | RemoteMonitoring toggle, device list, adapter | Remote pairing change, new remote property |
| Edge | Default | Missing fields, extra fields, invalid values | New edge case discovered or new validation logic |
| Dialogs | Default | Warnings, Game Mode, Tweaks windows open & screenshot | New dialog window added |
| Warnings | Default | Visual flash + gradient trigger | New warning behavior or metric |
| Skins | Default | Each built-in skin loads + 1 screenshot under Dark | New skin file added to Skins/ |
| Exhaustive | Default | Theme presets, lifecycle drilldowns, all-booleans matrix, live-fire hotkeys | Anything that needs deep coverage but is too slow for the named categories |
| Visual | Visual (opt-in) | Skin × orientation matrix + theme × skin coupling matrix | New theme preset, new visual feature on skins, ThemeApplier changes |

## Rules

These are non-negotiable. The exhaustive suite catches most violations, but they're easier to enforce at PR time.

1. **Every new public property in `AppSettings.cs` or `PopoutSettings.cs`** must have an entry in the appropriate category's round-trip test. Defaults go in `Persist-AllPropertiesRoundTrip` or a category-specific round-trip.
2. **Every new skin file** added to `Fluid.App/Styles/Skins/` is auto-registered in the csproj via a glob (`<Resource Include="Styles\Skins\*.xaml" />`). The file itself doesn't need a csproj edit, but it MUST be added to THREE places: (a) the `BuiltInSkins` array in `Fluid.App/Services/SkinManager.cs`, (b) the `$skins` array in `Visual-SkinOrientationMatrix`, (c) the `$skins` array in `Visual-ThemeSkinCouplingMatrix`. (Run-SkinTests dispatches off `BuiltInSkins` indirectly via loaded skins.) Also update the "Skins: N built-in" row in the coverage report.

   **Critical:** skins must be Resource items, not Page items. The csproj has `<Page Remove="Styles\Skins\*.xaml" />` BEFORE the glob to prevent WPF SDK auto-detection from treating them as Pages (which triggers stricter XAML compile rules that reject `x:Double` tags). Don't remove that line.
3. **Every new theme preset** added to `ThemeApplier.cs` `Presets[]` must be added to the `$themes` array in `Run-ExhaustiveTests`' `Theme-AllPresetsRoundTrip`.
4. **Every new top-level Settings section** must be added to `$required` in `Settings-AllSectionsVisible`.
5. **Every new tile type** must be added to the `$tiles` array in `Settings-TileToggleElements` AND a `Show*` field test in `Run-TilesTests`.
6. **Every new dialog window** (any new `*Window.xaml`) must be added to `Run-DialogsTests` with a screenshot call, AND get an `OnWindowKeyDown` handler for ESC dismissal (see WarningsWindow / GameModeWindow / TweaksWindow for the pattern).
7. **Every new GameMode position or orientation** must be added to `$positions` or `$orientations` in `Run-GameModeTests`.
8. **Every new feature gets a coverage row.** When you add a feature, add a row to `$coverageRows` at the bottom of `Test-FluidMonitor.ps1`. Mark "yes" with the test name if automated, "no" with reason if manual.
9. **If a test starts failing because of an intentional change**, update the test in the SAME commit, not later.
10. **PowerShell test files (`.ps1`) must stay pure ASCII** for all strings, comments, and error messages. PowerShell on Windows reads .ps1 files without a UTF-8 BOM as Windows-1252, and any multi-byte UTF-8 character (em-dash `--`, ellipsis `...`, smart quotes, arrows, emoji) corrupts to mojibake (`a~"`, etc). If the corrupted character lands inside a string literal, the parser can't find the closing quote and the whole file fails to load with "Missing closing '}'" cascades. Use `--` not `--`, `...` not `...`, `->` not `->`, `[SAVE]` not the floppy emoji. C#/XAML/Markdown files are unaffected; this rule is .ps1-specific.
11. **Bug fixes** include a regression test in the matching tier. The test must fail on the unfixed code (verify locally) and pass on the fixed code.
12. **Every version bump touches THREE files in lockstep**: `installer/fluid.iss` (`#define AppVersion`), `Fluid.App/Fluid.App.csproj` (`<Version>`), and `Fluid.Service/Fluid.Service.csproj` (`<Version>`). All three must match or the installer reports a stale version in Add/Remove Programs while the exe metadata says something newer. The `.iss` version drives the `OutputBaseFilename` (`fluidMonitor_installer_v{AppVersion}.exe`), so if you forget to bump it, your "new" installer overwrites the previous filename and you can't tell which build you grabbed. This has been missed multiple times -- when shipping a version, search the repo for the OLD version string before committing to verify nothing was left behind:
    ```
    grep -rn "1.12" --include="*.iss" --include="*.csproj"
    ```

## Visual log

Screenshots are captured for distinct interfaces and visual states:
- Widget at startup (`widget-default.png`)
- Settings window (`settings-window.png`)
- Each skin (`skin-<name>.png`) — Default tier
- Each skin × orientation (`visual-<skin>-<orientation>.png`) — Exhaustive tier
- Each dialog (`dialog-warnings.png`, `dialog-gamemode.png`, `dialog-tweaks.png`)
- Each warning trigger frame (`warnings-trigger-frame[1-3].png`)
- Each theme preset (`theme-<name>.png`) — Exhaustive tier

These end up in `10-screenshots/` inside the diagnostic bundle. Useful for:
- Visual regression detection (compare against prior bundles)
- Sharing with collaborators / Claude for review
- Catching theme/color issues that don't crash but look wrong

To add a screenshot to a new test:
```powershell
if ($someElement) {
    Save-FluidScreenshot -Bounds $someElement.Current.BoundingRectangle -Name "descriptive-name"
}
```

The screenshot helper automatically moves the cursor to (0,0) before capture so tooltips don't appear in shots.

## Running

```powershell
.\tests\Test-FluidMonitor.ps1                  # default tier (~3min) + auto diagnostic bundle
.\tests\Test-FluidMonitor.ps1 -Smoke           # 5 lean checks (~10s)
.\tests\Test-FluidMonitor.ps1 -Exhaustive      # default + exhaustive matrix (~5-7min)
.\tests\Test-FluidMonitor.ps1 -Fast            # skip 10s memory-leak check
.\tests\Test-FluidMonitor.ps1 -Category Skins  # one category
.\tests\Test-FluidMonitor.ps1 -Category Exhaustive  # ONLY exhaustive (skip default)
.\tests\Test-FluidMonitor.ps1 -TestFilter Skin* # by name pattern
.\tests\Test-FluidMonitor.ps1 -NoDiagnostics   # skip the auto-bundle
.\tests\Test-FluidMonitor.ps1 -Verbose         # show line numbers on failures
```

ESC at any time to abort cleanly (still runs cleanup + bundles diagnostics).

## Privacy note

Screenshots can capture sensitive data (e.g., the Settings window shows the handshake key). When sharing diagnostic bundles externally, consider either:
- Reviewing the `10-screenshots/` folder before uploading
- Setting a placeholder handshake key in `settings.json` before running tests for shareable bundles
