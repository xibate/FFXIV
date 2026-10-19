using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Style;

namespace LucentUI.Hud.Elements;

public sealed class PartyFramesElement : HudElement
{
    private const int MaxMembers = 8;
    private const float MaxRange = 30f;

    private static readonly (string Name, uint Job)[] PreviewMembers =
    {
        ("Tank One", 19), ("Tank Two", 32), ("Healer One", 24), ("Healer Two", 28),
        ("Melee One", 34), ("Melee Two", 41), ("Ranged One", 31), ("Caster One", 42),
    };

    private readonly List<UnitData> members = new(MaxMembers);

    public override string Id => "party";

    public override string Label => "Party Frames";

    public override Vector2 DefaultPosition => new(-900, -200);

    public override bool IsEnabled(Profile profile) => profile.Modules.PartyFrames && profile.Party.Enabled;

    public override VisibilitySettings? GetVisibility(Profile profile) => profile.Party.Visibility;

    public override Vector2 GetSize(Profile profile)
    {
        var s = profile.Party;
        var length = MaxMembers * (s.Horizontal ? s.Size.X : s.Size.Y) + (MaxMembers - 1) * s.Spacing;
        return s.Horizontal ? new Vector2(length, s.Size.Y) : new Vector2(s.Size.X, length);
    }

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        var s = ctx.Profile.Party;
        Collect(s);

        if (members.Count == 0 && ctx.Preview)
        {
            for (var i = 0; i < PreviewMembers.Length; i++)
            {
                var (name, job) = PreviewMembers[i];
                var sample = Units.Sample(name, job, 1f - i * 0.09f, UnitReaction.Player, i % 3 == 0 ? 0.15f : 0f);
                sample.InRange = i != 6;
                members.Add(sample);
            }
        }

        if (s.Sort == PartySortMode.Role)
        {
            members.Sort(CompareByRole);
        }

        var target = Svc.TargetManager.Target;
        for (var i = 0; i < members.Count; i++)
        {
            var unit = members[i];
            var offset = i * ((s.Horizontal ? s.Size.X : s.Size.Y) + s.Spacing);
            var memberPos = pos + (s.Horizontal ? new Vector2(offset, 0) : new Vector2(0, offset));

            var hovered = !ctx.Preview && s.Interactive && ctx.Input.HandleUnit(memberPos, memberPos + s.Size, unit.Object, ctx.General);
            var isTarget = s.HighlightTarget && target != null && unit.Object != null && target.Address == unit.Object.Address;

            UnitBar.Render(ctx, memberPos, s.Size, unit, new UnitBarOptions
            {
                ShowPower = s.ShowPower,
                PowerHeight = s.PowerHeight,
                LeftText = s.LeftText,
                CenterText = string.Empty,
                RightText = s.RightText,
                ColorMode = s.ColorMode,
                ShowShield = s.ShowShield,
                Highlight = isTarget,
                Hovered = hovered,
                FadeOutOfRange = s.RangeFade,
            });
        }
    }

    private void Collect(PartySettings s)
    {
        members.Clear();
        var player = Svc.ObjectTable.LocalPlayer;
        if (player == null)
        {
            return;
        }

        if (Svc.PartyList.Length == 0)
        {
            if (s.ShowWhenSolo && s.IncludePlayer && Units.TryRead(player, out var solo))
            {
                members.Add(solo);
            }

            return;
        }

        foreach (var member in Svc.PartyList)
        {
            if (member == null || members.Count >= MaxMembers)
            {
                continue;
            }

            var isPlayer = member.EntityId == player.EntityId;
            if (isPlayer && !s.IncludePlayer)
            {
                continue;
            }

            var obj = member.GameObject;
            if (obj is ICharacter && Units.TryRead(obj, out var unit))
            {
                unit.InRange = isPlayer || Vector3.Distance(player.Position, obj.Position) <= MaxRange;
            }
            else
            {
                // Not loaded nearby (other zone or far away): use the party list's copy of the stats.
                unit = new UnitData
                {
                    Object = obj,
                    Name = member.Name.TextValue,
                    Level = member.Level,
                    Hp = member.CurrentHP,
                    MaxHp = member.MaxHP,
                    Power = member.CurrentMP,
                    MaxPower = member.MaxMP,
                    JobId = member.ClassJob.RowId,
                    Reaction = UnitReaction.Player,
                    InRange = false,
                };
            }

            members.Add(unit);
        }
    }

    private static int CompareByRole(UnitData a, UnitData b)
    {
        var byRole = Jobs.RoleSortOrder(Jobs.Get(a.JobId).Role).CompareTo(Jobs.RoleSortOrder(Jobs.Get(b.JobId).Role));
        return byRole != 0 ? byRole : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    }
}
