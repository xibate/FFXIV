# LucentUI

A clean, modern, modular HUD replacement for FINAL FANTASY XIV, built as a [Dalamud](https://github.com/goatcorp/Dalamud) plugin.
Inspired by [EllesmereUI](https://github.com/EllesmereGaming/EllesmereUI) for World of Warcraft, meant as an alternative to [DelvUI](https://github.com/DelvUI/DelvUI).

## What it does

| Module | Contents |
| --- | --- |
| **Unit Frames** | Player, Target, Target of Target and Focus. Health, shields, mana/GP/CP, job/role/gradient/dark colour modes, text templates, smooth health animation. Click to target, right-click menu, `<mo>` mouseover and **mouseover casting** (actions go to the frame under the mouse). Every text has its own content, size, offset and colour; power bar above or below; own background; optional job icon portrait. |
| **Party Frames** | Same order as the game's party list (or by role), cross-world and trust parties, job icons, range fading, target highlight. Trackers for raises, invulnerabilities, cleansable debuffs and ready checks, plus buff and debuff icons per member (your HoTs and shields, or a list you choose) with the same settings for both: position, inside or outside the frame, growth, offset, size, shape (portrait, square or round), border and timers. |
| **Alliance Frames** | The two other parties in 24-player alliance raids as compact bars, with health even when they are far away, range fading, click to target, and buff/debuff icons. |
| **Job Gauges** | Every combat job (21 jobs): resources (Beast Gauge, Kenki, Soul Voice...), stacks (cartridges, lilies, feathers...), coloured pips (Sen, Beast Chakra, dance steps, cards...), and timers for key buffs, procs and DoTs. Each bar can be turned off, recoloured, or detached to move and size it on its own. |
| **Cooldown Manager** | Your own cooldowns as icon bars with a cooldown swipe, timer and charges: a Cooldowns bar (job abilities on 30s+ recasts), a Utility bar (role actions) and a Buffs bar (buffs you put on yourself, optionally only the ones you track per job). Filled automatically per job from the game's data; pick your own actions per job. Up to 8 custom bars with the actions and items (potions, food, with how many you have) you choose. |
| **Cast Bars** | Player, Target and Focus, with spell icon, name, timer, interruptible colour and the player's slidecast window. |
| **Auras** | Player buffs, player debuffs and target auras as icon grids with timers, tooltips, "only mine" and sort options, borders, timer position and size, and hide/only-show lists. Optional right-click to remove a buff. |
| **Party Cooldowns** | Raid buffs, party mitigation, big heals and tank invulnerabilities used by your party, shown as active / cooling down / ready. |
| **Enemy List** | The game's enemy list as frames, with casts and a marker on enemies attacking you. Click to target. |
| **Nameplates** | Health-bar plates with cast bars for enemies and name-only plates for players and friendly NPCs (each kind: bar, name only or hidden). Plates hold still instead of following head animations. Click a plate to target it. The game's own name is removed from units that get a LucentUI plate. Off by default. |
| **Extras** | GCD bar or ring (can follow the mouse), MP tick timer, limit break bars, pull countdown, experience bar with rested experience. |
| **Hotbar rules** | Show the game's hotbars only in combat, out of combat, in duties and so on. Keybinds keep working. Off by default. |
| **Default HUD** | Hides the game elements LucentUI replaces (HP bars, target info, party list, status, job gauge, enemy list, limit break, experience, countdown, nameplates). Off until you turn it on; everything is restored on unload. |

Core systems, following EllesmereUI's ideas:

- **Unlock Mode** (`/lucent unlock`): drag any element, snap to a grid, nudge with arrow keys (Shift for 10px), right-click to reset, or type exact coordinates in the toolbar. Double-click an element to open its settings.
- **Settings like EllesmereUI's**: pages split into tabs, "Copy from..." another frame or bar, reset a page (Ctrl+click), and a live preview of every element (sample target, casts and party) while the settings window is open.
- **One style page**: accent colour, borders, rounding, bar textures or bar styles (flat, gradient, glossy, inset, striped), drop shadows, fonts, the shape of buff and debuff icons (the game's default 3:4, square like WoW, or round; each element can override it) and every colour live in one place.
- **Media library**: fonts (.ttf, .otf) and status bar textures (.tga, .blp, .png) from LucentUI's media folder and any folder you add. Add your WoW `Interface\AddOns` folder and SharedMedia packs (and EllesmereUI's own media) are available by the same names as in WoW, so imported profiles keep their font and textures. Every bar element can use its own texture.
- **Profiles**: create, copy, rename, reset, and share them as text (export/import via clipboard). Assign a profile to each job and it loads when you switch.
- **EllesmereUI import**: paste an EllesmereUI profile string (`!EUI_...`) and it is converted to a LucentUI profile (see below).
- **Visibility rules** per element: always, in combat, in combat or targeting, out of combat, in duty, or never, with an optional faded opacity instead of hiding.
- **Stay behind game windows**: optional; LucentUI is cut away (or hidden) where a game window such as the inventory is open on top of it, and clicks there reach the window.
- **Searchable options**: type in the search box to find any setting across all pages.
- **Zero cost when off, no change without opt-in**: disabled modules draw and hook nothing, and the game's HUD is untouched until you choose to hide parts of it.

### Importing an EllesmereUI profile

In WoW, export your profile from EllesmereUI (a normal profile export or a full-account export both work) and copy the string; it starts with `!EUI_`. In game, open `/lucent` → **Profiles** and press **Import EllesmereUI profile from clipboard**. The new profile is selected straight away.

Converted:
- **Unit frames**: player, target, target of target and focus sizes, positions, power bars (above/below), text content, text size, offset and class colour, backgrounds, class colours, dark theme, reversed fill.
- **Cast bars**: the Resource Bars cast bar (or the unit frame one if you use that), target and focus cast bars, with sizes, positions, colours, background, icon side and text sizes.
- **Resource Bars**: the power bar becomes each job's main resource bar (mana for healers and casters) and the class resource bar each job's stacks, as detached job gauge bars with EllesmereUI's size and position; the GCD bar too.
- **Cooldown Manager**: the Cooldowns, Utility and Buffs bars' icon size, rows, spacing, position and alignment, including bars attached to the player frame or a resource bar. Your own custom bars become LucentUI custom bars with the same name, size and position (add FFXIV actions or potions to them). WoW spells cannot carry over, so the bars fill with your FFXIV job's own cooldowns.
- **Party frames**: size, spacing, sorting and role order, direction, position, show when solo, hide yourself, name and health text, power bar, debuff icons (size, position, growth, offset, timers).
- **Raid frames → alliance frames**: cell size (the 25-player size if set), spacing, member and group growth, name and health text, texture, colours, debuff icons and position.
- **Links**: elements anchored to other elements and matched widths in EllesmereUI's Unlock Mode.
- **Nameplates**: bar size, text, cast bar, friendly name-only plates and target scale.
- **Fonts and textures**: the global font and each frame's bar texture, by name. They show once the media is in one of your media folders (Style > Media).
- Accent colour and text outline. Anything an export leaves out uses EllesmereUI's own default, and sizes and positions are scaled from your WoW UI scale to your screen.

Skipped, because FFXIV has no equivalent or plugins cannot change it: action bars, bags, chat, minimap, quest tracker, damage meters, Blizzard window skins.

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

Publishing a new version: bump `<Version>` in `LucentUI/LucentUI.csproj` and `AssemblyVersion` in `repo.json` to the same number and push to the default branch. The GitHub Action builds the plugin and publishes a release `v<version>` with `latest.zip`, which is what the installer downloads. The build fails if the two numbers differ.

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
- Nameplates sit where the game puts its own nameplate. They follow the game's nameplate list (line of sight, the game's name settings) when it has units, and otherwise use every unit nearby.
- Party cooldowns are tracked from the moment LucentUI sees them used, so cooldowns used before you loaded or zoned show as ready.
