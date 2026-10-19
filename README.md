# LucentUI

A clean, modern, modular HUD replacement for FINAL FANTASY XIV, built as a [Dalamud](https://github.com/goatcorp/Dalamud) plugin.
Inspired by [EllesmereUI](https://github.com/EllesmereGaming/EllesmereUI) for World of Warcraft, meant as an alternative to [DelvUI](https://github.com/DelvUI/DelvUI).

## What it does

| Module | Contents |
| --- | --- |
| **Unit Frames** | Player, Target, Target of Target and Focus. Health, shields, mana/GP/CP, job/role/gradient/dark colour modes, text templates. Click to target, right-click menu, `<mo>` mouseover. |
| **Cast Bars** | Player, Target and Focus, with spell icon, name, timer, interruptible colour and the player's slidecast window. |
| **Auras** | Player buffs, player debuffs and target auras as icon grids with timers, tooltips, "only mine" and sort options. Optional right-click to remove a buff. |
| **Party Frames** | Up to 8 members, sorted by role, faded when out of range, with target highlight and click targeting. |
| **Default HUD** | Hides the game elements LucentUI replaces. Off until you turn it on; everything is restored on unload. |

Core systems, following EllesmereUI's ideas:

- **Unlock Mode** (`/lucent unlock`): drag any element, snap to a grid, nudge with arrow keys (Shift for 10px), right-click to reset, or type exact coordinates in the toolbar.
- **One style page**: accent colour, borders, rounding, gradient, fonts and every colour live in one place.
- **Profiles**: create, copy, rename, reset, and share them as text (export/import via clipboard). Assign a profile to each job and it loads when you switch.
- **Visibility rules** per element: always, in combat, in combat or targeting, out of combat, in duty, or never, with an optional faded opacity instead of hiding.
- **Searchable options**: type in the search box to find any setting across all pages.
- **Zero cost when off, no change without opt-in**: disabled modules draw and hook nothing, and the game's HUD is untouched until you choose to hide parts of it.

### Text templates

Frame text uses tags: `{name}` `{name:12}` (truncate) `{name:first}` `{level}` `{job}` `{hp}` `{hp:max}` `{hp:pct}` `{hp:deficit}` `{mp}` `{mp:max}` `{mp:pct}`.
For example `{hp} | {hp:pct}%` shows `64.2k | 50%`.

## Commands

| Command | Action |
| --- | --- |
| `/lucent` | Open or close settings |
| `/lucent unlock` | Toggle Unlock Mode |
| `/lucent toggle` | Show or hide the HUD |
| `/lucent profile <name>` | Switch profile |

## Installing

### From a release (custom repository)

1. Push a version tag (for example `git tag v0.1.0.0 && git push origin v0.1.0.0`). The GitHub Action builds the plugin and attaches `latest.zip` to a release.
2. In game, open `/xlsettings` → **Experimental** → **Custom Plugin Repositories** and add
   `https://raw.githubusercontent.com/xibate/FFXIV/main/repo.json`
   (replace `main` with the branch that holds `repo.json`).
3. Install **LucentUI** from `/xlplugins`.

Bump `<Version>` in `LucentUI/LucentUI.csproj` and `AssemblyVersion` in `repo.json` together for each release.

### As a dev plugin (for development)

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) and run XIVLauncher with Dalamud at least once.
2. Build: `dotnet build -c Release`.
3. In game, `/xlsettings` → **Experimental** → **Dev Plugin Locations**, add the full path to `LucentUI/bin/Release/LucentUI.dll` (or the `x64` folder if your build puts it there), then enable it in `/xlplugins` → **Dev Tools**.

## Project layout

```
LucentUI/
  Plugin.cs              entry point, commands, saving, job-change detection
  Svc.cs                 Dalamud services
  Config/                settings classes, configuration, profile manager (import/export)
  Data/                  reading units, casts and statuses from the game; text tags
  Style/                 colours and job table, fonts, drawing primitives
  Hud/                   renderer, Unlock Mode, input handling, visibility rules
  Hud/Elements/          unit frames, cast bars, auras, party frames
  Game/DefaultHudHider   hides and restores the game's own HUD pieces
  Windows/               settings window and its widget kit
```

Adding a new element: subclass `HudElement` (id, label, default position, size, enabled check, `Draw`), add it to the list in `HudRenderer`, and give it a settings class in `Config/Settings.cs` and a page in `OptionsWindow`. It then works with Unlock Mode, profiles and visibility rules automatically.

## Not yet included

- Job gauges and a GCD/cooldown tracker
- Nameplates, enemy list, alliance frames and party debuffs/cleanse markers
- Hiding LucentUI under game windows (frames currently draw on top of game windows, like DelvUI without its clipping option)
- Cross-world party data beyond what Dalamud's party list provides
