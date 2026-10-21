using System;
using System.Collections.Generic;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using LucentUI.Config;
using LucentUI.Data;

namespace LucentUI.Hud.Elements;

/// <summary>
/// The two other parties of a 24-player alliance, as compact health bars. Health comes from the game's alliance
/// data, so members far away (or not loaded) still show; clicking targets them when they are in range.
/// </summary>
public sealed unsafe class AllianceFramesElement : HudElement
{
    private const int Groups = 2;
    private const int PerGroup = 8;
    private const float MaxRange = 40f;

    private readonly List<UnitData>[] groups = { new(PerGroup), new(PerGroup) };

    public override string Id => "alliance";

    public override string Label => "Alliance";

    public override Vector2 DefaultPosition => new(-960, 60);

    public override bool IsEnabled(Profile profile) => profile.Modules.Alliance && profile.Alliance.Enabled;

    public override VisibilitySettings? GetVisibility(Profile profile) => profile.Alliance.Visibility;

    public override Vector2 GetSize(Profile profile)
    {
        var group = GroupSize(profile.Alliance);
        var s = profile.Alliance;
        return s.SideBySide
            ? new Vector2(group.X * Groups + s.GroupSpacing, group.Y)
            : new Vector2(group.X, group.Y * Groups + s.GroupSpacing);
    }

    private static Vector2 GroupSize(AllianceSettings s) =>
        new(s.Size.X, PerGroup * s.Size.Y + (PerGroup - 1) * s.Spacing);

    public override void Draw(DrawContext ctx, Vector2 pos)
    {
        var s = ctx.Profile.Alliance;
        if (!Collect() && ctx.Preview)
        {
            AddPreview();
        }

        var groupSize = GroupSize(s);
        var target = Svc.TargetManager.Target;
        for (var g = 0; g < Groups; g++)
        {
            var groupPos = pos + (s.SideBySide ? new Vector2(g * (groupSize.X + s.GroupSpacing), 0) : new Vector2(0, g * (groupSize.Y + s.GroupSpacing)));
            var members = groups[g];
            for (var i = 0; i < members.Count; i++)
            {
                var unit = members[i];
                var memberPos = groupPos + new Vector2(0, i * (s.Size.Y + s.Spacing));
                var hovered = !ctx.Preview && s.Interactive && ctx.Input.HandleUnit(memberPos, memberPos + s.Size, unit.Object, ctx.General);
                var isTarget = target != null && unit.Object != null && target.Address == unit.Object.Address;

                UnitBar.Render(ctx, memberPos, s.Size, unit, new UnitBarOptions
                {
                    Texture = s.Texture,
                    ShowPower = false,
                    LeftText = s.LeftText,
                    CenterText = string.Empty,
                    RightText = s.RightText,
                    LeftStyle = s.LeftStyle,
                    RightStyle = s.RightStyle,
                    ColorMode = s.ColorMode,
                    ShowShield = true,
                    Highlight = isTarget,
                    Hovered = hovered,
                    FadeOutOfRange = s.RangeFade,
                    Icon = s.ShowJobIcon && unit.JobId > 0 ? 62000 + unit.JobId : 0,
                    SmoothKey = DrawContext.Key(Id, (ulong)(g * 100 + i) ^ ((ulong)unit.Name.GetHashCode() << 8)),
                });
            }
        }
    }

    /// <summary>Reads the other two alliance parties. Returns false when not in an alliance.</summary>
    private bool Collect()
    {
        groups[0].Clear();
        groups[1].Clear();

        var manager = GroupManager.Instance();
        if (manager == null)
        {
            return false;
        }

        var group = manager->GetGroup(false);
        if (group == null || !group->IsAlliance)
        {
            return false;
        }

        var player = Svc.ObjectTable.LocalPlayer;
        var any = false;
        for (var g = 0; g < Groups; g++)
        {
            for (var i = 0; i < PerGroup; i++)
            {
                var member = group->GetAllianceMemberByGroupAndIndex(g, i);
                if (member == null || member->EntityId == 0 || member->EntityId == 0xE0000000 || member->ClassJob == 0)
                {
                    continue;
                }

                var obj = Svc.ObjectTable.SearchByEntityId(member->EntityId);
                var inRange = player != null && Vector3.Distance(player.Position, member->Position) <= MaxRange;
                groups[g].Add(new UnitData
                {
                    Object = obj,
                    Name = member->NameString,
                    Level = member->Level,
                    Hp = member->CurrentHP,
                    MaxHp = Math.Max(1u, member->MaxHP),
                    Power = member->CurrentMP,
                    MaxPower = member->MaxMP,
                    JobId = member->ClassJob,
                    ShieldFraction = member->DamageShield / 100f,
                    Reaction = UnitReaction.Player,
                    InRange = inRange,
                });
                any = true;
            }
        }

        return any;
    }

    private void AddPreview()
    {
        var jobs = new uint[] { 19, 24, 34, 31, 42, 39, 25, 33 };
        for (var g = 0; g < Groups; g++)
        {
            for (var i = 0; i < PerGroup; i++)
            {
                var hp = 1f - ((i * 37 + g * 53) % 70) / 100f;
                var unit = Units.Sample($"Member {(char)('A' + g + 1)}{i + 1}", jobs[(i + g * 3) % jobs.Length], hp, UnitReaction.Player);
                unit.InRange = (i + g) % 5 != 0;
                groups[g].Add(unit);
            }
        }
    }
}
