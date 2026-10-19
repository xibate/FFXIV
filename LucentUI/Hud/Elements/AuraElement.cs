using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using FFXIVClientStructs.FFXIV.Client.Game;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Style;

namespace LucentUI.Hud.Elements;

public enum AuraGroup
{
    PlayerBuffs,
    PlayerDebuffs,
    Target,
}

/// <summary>A grid of status icons with timers.</summary>
public sealed class AuraElement : HudElement
{
    // Status icons are 3:4 portrait images.
    private const float IconAspect = 0.75f;

    private readonly AuraGroup group;
    private readonly List<AuraData> buffer = new();

    public AuraElement(AuraGroup group)
    {
        this.group = group;
    }

    public override string Id => group switch
    {
        AuraGroup.PlayerBuffs => "auras.player.buffs",
        AuraGroup.PlayerDebuffs => "auras.player.debuffs",
        _ => "auras.target",
    };

    public override string Label => group switch
    {
        AuraGroup.PlayerBuffs => "Player Buffs",
        AuraGroup.PlayerDebuffs => "Player Debuffs",
        _ => "Target Auras",
    };

    public override Vector2 DefaultPosition => group switch
    {
        AuraGroup.PlayerBuffs => new(470, -520),
        AuraGroup.PlayerDebuffs => new(470, -430),
        _ => new(170, 112),
    };

    public AuraSettings Settings(Profile p) => group switch
    {
        AuraGroup.PlayerBuffs => p.PlayerBuffs,
        AuraGroup.PlayerDebuffs => p.PlayerDebuffs,
        _ => p.TargetAuras,
    };

    public override bool IsEnabled(Profile profile) => profile.Modules.Auras && Settings(profile).Enabled;

    public override Vector2 GetSize(Profile profile)
    {
        var s = Settings(profile);
        var (columns, rows) = Grid(s);
        var cell = CellSize(s, profile.Style);
        return new Vector2(
            columns * cell.X + (columns - 1) * s.Spacing,
            rows * cell.Y + (rows - 1) * s.Spacing);
    }

    /// <summary>One icon's slot, including room for the timer when it sits below the icon.</summary>
    private static Vector2 CellSize(AuraSettings s, StyleSettings style)
    {
        var icon = IconSize(s);
        if (s.ShowTimer && s.TimerPosition == TimerPosition.Below)
        {
            icon.Y += (s.TimerFontSize > 0 ? s.TimerFontSize : style.SmallFontSize) + 2;
        }

        return icon;
    }

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        var s = Settings(ctx.Profile);
        var unit = group == AuraGroup.Target ? Units.Get(UnitKind.Target) : Units.Get(UnitKind.Player);

        Auras.Collect(unit, s, buffer);
        IReadOnlyList<AuraData> auras = buffer;
        if (buffer.Count == 0 && ctx.Preview)
        {
            auras = Auras.Preview();
        }

        if (auras.Count == 0)
        {
            return;
        }

        var size = GetSize(ctx.Profile);
        var icon = IconSize(s);
        var cell = CellSize(s, ctx.Style);
        var (columns, _) = Grid(s);
        var count = Math.Min(auras.Count, Math.Max(1, s.MaxIcons));

        using var font = s.TimerFontSize > 0 ? ctx.Fonts.Push(s.TimerFontSize) : ctx.Fonts.PushSmall();
        for (var i = 0; i < count; i++)
        {
            var aura = auras[i];
            var col = i % columns;
            var row = i / columns;

            var x = s.GrowLeft ? size.X - (col + 1) * cell.X - col * s.Spacing : col * (cell.X + s.Spacing);
            var y = s.GrowUp ? size.Y - (row + 1) * cell.Y - row * s.Spacing : row * (cell.Y + s.Spacing);
            var min = Gfx.Floor(pos + new Vector2(x, y));
            var max = min + icon;

            DrawIcon(ctx, s, aura, min, max);

            if (!ctx.Preview && ctx.Input.IsHovering(min, max))
            {
                HandleHover(ctx, s, aura);
            }
        }
    }

    private static void DrawIcon(DrawContext ctx, AuraSettings s, in AuraData aura, Vector2 min, Vector2 max)
    {
        // The game's status icons have transparent corners, so nothing is filled behind them.
        var texture = Svc.TextureProvider.GetFromGameIcon(new GameIconLookup(aura.Icon)).GetWrapOrEmpty();
        ctx.DrawList.AddImage(texture.Handle, min, max, Vector2.Zero, Vector2.One, ctx.Col(Vector4.One));

        Vector4? outline = s.Border switch
        {
            AuraBorder.Thin => ctx.Style.Border,
            AuraBorder.ByType => aura.CanDispel ? Colors.Rgb(0xB65CE6) : aura.IsDebuff ? Colors.Rgb(0xC8423C) : ctx.Style.Border,
            _ => null,
        };
        if (outline is { } line)
        {
            ctx.DrawList.AddRect(min - Vector2.One, max + Vector2.One, ctx.Col(line), 0f, default, 1f);
        }

        if (s.ShowTimer && s.TimerPosition != TimerPosition.Hidden && !aura.IsPermanent && aura.Remaining > 0f)
        {
            var color = aura.Remaining < 5f ? Colors.Rgb(0xFF6B5E) : Vector4.One;
            var centreX = (min.X + max.X) / 2f;
            var y = s.TimerPosition switch
            {
                TimerPosition.Center => (min.Y + max.Y) / 2f,
                TimerPosition.Below => max.Y + ImGui.GetFontSize() / 2f + 1f,
                _ => max.Y - ImGui.GetFontSize() * 0.4f,
            };
            Gfx.Text(ctx, TextTags.Duration(aura.Remaining), new Vector2(centreX, y), TextAlign.Center, color);
        }
    }

    private unsafe void HandleHover(DrawContext ctx, AuraSettings s, in AuraData aura)
    {
        if (s.Tooltips)
        {
            ImGui.SetTooltip(string.IsNullOrEmpty(aura.Description) ? aura.Name : $"{aura.Name}\n{aura.Description}");
        }

        if (s.RightClickToRemove && group == AuraGroup.PlayerBuffs && !aura.IsDebuff)
        {
            ImGui.SetNextFrameWantCaptureMouse(true);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                // The game ignores this for statuses that cannot be clicked off.
                StatusManager.ExecuteStatusOff(aura.StatusId, aura.SourceId);
            }
        }
    }

    private static Vector2 IconSize(AuraSettings s)
    {
        var height = MathF.Round(Math.Clamp(s.IconHeight, 12f, 96f));
        return new Vector2(MathF.Round(height * IconAspect), height);
    }

    private static (int Columns, int Rows) Grid(AuraSettings s)
    {
        var max = Math.Max(1, s.MaxIcons);
        var columns = Math.Clamp(s.PerRow, 1, max);
        return (columns, (max + columns - 1) / columns);
    }
}
