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
        var icon = IconSize(s);
        return new Vector2(
            columns * icon.X + (columns - 1) * s.Spacing,
            rows * icon.Y + (rows - 1) * s.Spacing);
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
        var (columns, _) = Grid(s);
        var count = Math.Min(auras.Count, Math.Max(1, s.MaxIcons));

        using var font = ctx.Fonts.PushSmall();
        for (var i = 0; i < count; i++)
        {
            var aura = auras[i];
            var col = i % columns;
            var row = i / columns;

            var x = s.GrowLeft ? size.X - (col + 1) * icon.X - col * s.Spacing : col * (icon.X + s.Spacing);
            var y = s.GrowUp ? size.Y - (row + 1) * icon.Y - row * s.Spacing : row * (icon.Y + s.Spacing);
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
        var style = ctx.Style;
        var border = aura.IsDebuff ? Colors.Rgb(0x8A1F1F) : style.Border;
        ctx.DrawList.AddRectFilled(min - Vector2.One, max + Vector2.One, ctx.Col(border));

        var texture = Svc.TextureProvider.GetFromGameIcon(new GameIconLookup(aura.Icon)).GetWrapOrEmpty();
        ctx.DrawList.AddImage(texture.Handle, min, max, Vector2.Zero, Vector2.One, ctx.Col(Vector4.One));

        if (s.ShowTimer && !aura.IsPermanent && aura.Remaining > 0f)
        {
            var color = aura.Remaining < 5f ? Colors.Rgb(0xFF6B5E) : Vector4.One;
            Gfx.Text(ctx, TextTags.Duration(aura.Remaining), new Vector2((min.X + max.X) / 2f, max.Y - 6f), TextAlign.Center, color);
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
