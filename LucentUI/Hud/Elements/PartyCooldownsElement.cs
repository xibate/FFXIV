using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface.Textures;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Style;
using Lumina.Excel.Sheets;

namespace LucentUI.Hud.Elements;

/// <summary>A list of the party's raid buffs and mitigation: active, cooling down or ready.</summary>
public sealed class PartyCooldownsElement : HudElement
{
    private readonly PartyCooldownTracker tracker;
    private readonly List<Row> rows = new();
    private readonly Dictionary<uint, (string Name, uint Icon)> actions = new();

    public PartyCooldownsElement(PartyCooldownTracker tracker)
    {
        this.tracker = tracker;
    }

    private struct Row
    {
        public CooldownInfo Info;
        public string Member;
        public float Remaining;
        public bool Active;
        public bool Ready;
    }

    public override string Id => "partycooldowns";

    public override string Label => "Party Cooldowns";

    public override Vector2 DefaultPosition => new(-900, 140);

    public override bool IsEnabled(Profile profile) => profile.Modules.PartyCooldowns && profile.PartyCooldowns.Enabled;

    public override VisibilitySettings? GetVisibility(Profile profile) => profile.PartyCooldowns.Visibility;

    public override Vector2 GetSize(Profile profile)
    {
        var s = profile.PartyCooldowns;
        var count = Math.Max(1, s.MaxRows);
        return new Vector2(s.RowSize.X, count * s.RowSize.Y + (count - 1) * s.Spacing);
    }

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        var s = ctx.Profile.PartyCooldowns;
        Collect(s);

        if (rows.Count == 0 && ctx.Preview)
        {
            AddPreview();
        }

        rows.Sort(static (a, b) =>
        {
            // Active first, then cooling down (soonest first), then ready.
            var rank = Rank(a).CompareTo(Rank(b));
            return rank != 0 ? rank : a.Remaining.CompareTo(b.Remaining);
        });

        var size = GetSize(ctx.Profile);
        var count = Math.Min(rows.Count, Math.Max(1, s.MaxRows));
        using var font = s.FontSize > 0 ? ctx.Fonts.Push(s.FontSize) : ctx.Fonts.PushSmall();
        for (var i = 0; i < count; i++)
        {
            var y = s.GrowUp ? size.Y - (i + 1) * s.RowSize.Y - i * s.Spacing : i * (s.RowSize.Y + s.Spacing);
            DrawRow(ctx, rows[i], pos + new Vector2(0, y), s.RowSize);
        }
    }

    private static int Rank(Row row) => row.Active ? 0 : row.Ready ? 2 : 1;

    private void DrawRow(DrawContext ctx, in Row row, Vector2 pos, Vector2 size)
    {
        var (name, icon) = Action(row.Info.ActionId);
        var style = ctx.Style;
        var (min, max) = Gfx.Panel(ctx, pos, size);

        var iconSize = max.Y - min.Y;
        if (icon != 0)
        {
            var texture = Svc.TextureProvider.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty();
            ctx.DrawList.AddImage(texture.Handle, min, min + new Vector2(iconSize), new Vector2(0.08f), new Vector2(0.92f), ctx.Col(row.Ready || row.Active ? Vector4.One : new Vector4(0.55f, 0.55f, 0.55f, 1f)));
            min.X += iconSize + style.BorderSize;
        }

        float fraction;
        Vector4 color;
        if (row.Active)
        {
            fraction = row.Remaining / Math.Max(1f, row.Info.Duration);
            color = style.Accent;
        }
        else if (row.Ready)
        {
            fraction = 1f;
            color = Colors.WithAlpha(Colors.Rgb(0x6EE07A), 0.35f);
        }
        else
        {
            fraction = 1f - row.Remaining / Math.Max(1f, row.Info.Cooldown);
            color = Colors.Rgb(0x55575E);
        }

        Gfx.Bar(ctx, min, max, fraction, color);
        var time = row.Ready ? "Ready" : TextTags.Duration(row.Remaining);
        Gfx.TextRow(ctx, min, max, $"{name}  {row.Member}", string.Empty, time, 4f);
    }

    private void Collect(PartyCooldownSettings s)
    {
        rows.Clear();
        var party = Party.Current;
        if (party.Count <= 1 && !s.ShowWhenSolo)
        {
            return;
        }

        foreach (var member in party)
        {
            var unit = member.Unit;
            if (unit.JobId == 0)
            {
                continue;
            }

            var firstName = unit.Name.Split(' ')[0];
            foreach (var info in PartyCooldownTracker.Table)
            {
                if (!Wanted(s, info.Category) || !info.AppliesTo(unit.JobId, (int)Math.Max(1, unit.Level)))
                {
                    continue;
                }

                var since = tracker.SinceUsed(member.EntityId, info.ActionId);
                var row = new Row { Info = info, Member = firstName };
                if (since is { } t && t < info.Duration)
                {
                    row.Active = true;
                    row.Remaining = (float)(info.Duration - t);
                }
                else if (since is { } c && c < info.Cooldown)
                {
                    row.Remaining = (float)(info.Cooldown - c);
                }
                else
                {
                    row.Ready = true;
                    if (!s.ShowReady)
                    {
                        continue;
                    }
                }

                rows.Add(row);
            }
        }
    }

    private static bool Wanted(PartyCooldownSettings s, CooldownCategory category) => category switch
    {
        CooldownCategory.RaidBuff => s.ShowRaidBuffs,
        CooldownCategory.Mitigation => s.ShowMitigation,
        CooldownCategory.Healing => s.ShowHealing,
        _ => s.ShowTankInvulns,
    };

    private void AddPreview()
    {
        var samples = new (uint Action, string Member, float Remaining, bool Active, bool Ready)[]
        {
            (7436, "Healer", 12f, true, false),
            (3540, "Tank", 0f, false, true),
            (16004, "Dancer", 64f, false, false),
            (7549, "Melee", 0f, false, true),
        };

        foreach (var (action, member, remaining, active, ready) in samples)
        {
            if (PartyCooldownTracker.Lookup.TryGetValue(action, out var info))
            {
                rows.Add(new Row { Info = info, Member = member, Remaining = remaining, Active = active, Ready = ready });
            }
        }
    }

    private (string Name, uint Icon) Action(uint id)
    {
        if (!actions.TryGetValue(id, out var data))
        {
            data = Svc.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRowOrDefault(id) is { } row
                ? (row.Name.ExtractText(), (uint)row.Icon)
                : ($"#{id}", 0u);
            actions[id] = data;
        }

        return data;
    }
}
