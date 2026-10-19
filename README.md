# LucentUI

A clean, modern, modular HUD replacement for FINAL FANTASY XIV, built as a [Dalamud](https://github.com/goatcorp/Dalamud) plugin.
Inspired by [EllesmereUI](https://github.com/EllesmereGaming/EllesmereUI) for World of Warcraft, meant as an alternative to [DelvUI](https://github.com/DelvUI/DelvUI).

## What it does

| Module | Contents |
| --- | --- |
| **Unit Frames** | Player, Target, Target of Target and Focus. Health, shields, mana/GP/CP, job/role/gradient/dark colour modes, text templates, smooth health animation. Click to target, right-click menu, `<mo>` mouseover. |
| **Party Frames** | Same order as the game's party list (or by role), cross-world and trust parties, job icons, range fading, target highlight. Trackers for raises, invulnerabilities, cleansable debuffs and ready checks, plus debuff icons per member. |
| **Job Gauges** | Every combat job (21 jobs): resources (Beast Gauge, Kenki, Soul Voice...), stacks (cartridges, lilies, feathers...), coloured pips (Sen, Beast Chakra, dance steps, cards...), and timers for key buffs, procs and DoTs. Each bar can be turned off or recoloured. |
| **Cast Bars** | Player, Target and Focus, with spell icon, name, timer, interruptible colour and the player's slidecast window. |
| **Auras** | Player buffs, player debuffs and target auras as icon grids with timers, tooltips, "only mine" and sort options. Optional right-click to remove a buff. |
| **Party Cooldowns** | Raid buffs, party mitigation, big heals and tank invulnerabilities used by your party, shown as active / cooling down / ready. |
| **Enemy List** | The game's enemy list as frames, with casts and a marker on enemies attacking you. Click to target. |
| **Nameplates** | Health bars, names, levels and cast bars above enemies (optionally players and friendly NPCs). Off by default. |
| **Extras** | GCD bar or ring (can follow the mouse), MP tick timer, limit break bars, pull countdown, experience bar with rested experience. |
| **Hotbar rules** | Show the game's hotbars only in combat, out of combat, in duties and so on. Keybinds keep working. Off by default. |
| **Default HUD** | Hides the game elements LucentUI replaces (HP bars, target info, party list, status, job gauge, enemy list, limit break, experience, countdown, nameplates). Off until you turn it on; everything is restored on unload. |

Core systems, following EllesmereUI's ideas:

- **Unlock Mode** (`/lucent unlock`): drag any element, snap to a grid, nudge with arrow keys (Shift for 10px), right-click to reset, or type exact coordinates in the toolbar.
- **One style page**: accent colour, borders, rounding, bar styles (flat, gradient, glossy, inset, striped), drop shadows, game fonts and every colour live in one place.
- **Profiles**: create, copy, rename, reset, and share them as text (export/import via clipboard). Assign a profile to each job and it loads when you switch.
- **EllesmereUI import**: paste an EllesmereUI profile string (`!EUI_...`) and it is converted to a LucentUI profile (see below).
- **Visibility rules** per element: always, in combat, in combat or targeting, out of combat, in duty, or never, with an optional faded opacity instead of hiding.
- **Stay behind game windows**: optional; LucentUI is cut away (or hidden) where a game window such as the inventory is open on top of it, and clicks there reach the window.
- **Searchable options**: type in the search box to find any setting across all pages.
- **Zero cost when off, no change without opt-in**: disabled modules draw and hook nothing, and the game's HUD is untouched until you choose to hide parts of it.

### Importing an EllesmereUI profile

In WoW, export your profile from EllesmereUI and copy the string (it starts with `!EUI_`). In game, open `/lucent` → **Profiles**, type a name and press **Import EllesmereUI profile from clipboard** (the normal Import button also recognises these strings).

Converted: player, target, target-of-target and focus frame sizes, positions, power bars, text, class-colour and dark-theme choices, reversed fill and enabled state; player and target cast bar sizes and colour; player aura icon size, rows and limits; raid/party frame size, spacing, sorting and direction; accent colour and text outline. Sizes and positions are scaled from the WoW UI scale to your screen.

Skipped, because FFXIV has no equivalent or plugins cannot change it: action bars, bags, chat, minimap, quest tracker, damage meters, Blizzard window skins, WoW fonts and textures.

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

### From the plugin installer (recommended)

1. In game, open `/xlsettings` → **Experimental** → **Custom Plugin Repositories** and add
   `https://raw.githubusercontent.com/xibate/FFXIV/claude/quirky-volta-q8y8hy/repo.json`
   then tick **Enabled** and press **Save and Close**.
2. Open `/xlplugins`, search for **LucentUI** and install it. Updates arrive like any other plugin.

Publishing a new version: bump `<Version>` in `LucentUI/LucentUI.csproj` and `AssemblyVersion` in `repo.json` together, then push a matching tag (for example `git tag v0.2.0.0 && git push origin v0.2.0.0`). The GitHub Action builds the plugin and attaches `latest.zip` to a release, which is what the installer downloads.

### As a dev plugin (for development)

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download) and run XIVLauncher with Dalamud at least once.
2. Build: `dotnet build -c Release`.
3. In game, `/xlsettings` → **Experimental** → **Dev Plugin Locations**, add the full path to `LucentUI/bin/Release/LucentUI.dll` (or the `x64` folder if your build puts it there), then enable it in `/xlplugins` → **Dev Tools**.

## Project layout

```
LucentUI/
  Plugin.cs              entry point, commands, saving, job-change detection
  Svc.cs                 Dalamud services
  Config/                settings, configuration, profiles (import/export), EllesmereUI import
  Data/                  units, casts, statuses, party (game order), party cooldown tracking, text tags
  Gauges/                job gauge model and the per-job bar definitions
  Style/                 colours and job table, fonts, drawing primitives and bar styles
  Hud/                   renderer, Unlock Mode, input, visibility rules, smoothing, window clipping
  Hud/Elements/          unit/party frames, job gauge, cast bars, auras, cooldowns, enemy list, nameplates, extras
  Game/DefaultHudHider   hides and restores the game's own HUD pieces and applies hotbar rules
  Windows/               settings window, its pages and widget kit
```

Adding a job gauge bar: add a `GaugePart` to the job's list in `Gauges/JobGaugeDefinitions.cs`; it gets a settings toggle and colour automatically.

Adding a new element: subclass `HudElement` (id, label, default position, size, enabled check, `Draw`), add it to the list in `HudRenderer`, and give it a settings class in `Config/Settings.cs` and a page in `OptionsWindow`. It then works with Unlock Mode, profiles and visibility rules automatically.

## Known limits

- Not tested in game yet: please report anything that looks wrong.
- Nameplates draw LucentUI health bars at the game's nameplate positions; the game's own name text stays unless you hide the game's nameplates.
- Party cooldowns are tracked from the moment LucentUI sees them used, so cooldowns used before you loaded or zoned show as ready.
