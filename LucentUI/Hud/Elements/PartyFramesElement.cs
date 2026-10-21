using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using LucentUI.Config;
using LucentUI.Data;
using LucentUI.Style;

namespace LucentUI.Hud.Elements;

public sealed class PartyFramesElement : HudElement
{
    private const int MaxMembers = 8;
    private const float ReadyCheckLinger = 8f;

    // Invulnerabilities: Hallowed Ground, Holmgang, Living Dead, Walking Dead, Superbolide, Undead Rebirth and others.
    private static readonly uint[] InvulnStatuses = { 82, 409, 810, 811, 1302, 1836, 3255 };

    // "Raise" statuses on a dead member (accepting a raise).
    private static readonly uint[] RaisedStatuses = { 148, 1140 };

    // Raise spells: Raise, Resurrection, Ascend, Verraise, Angel Whisper, Egeiro.
    private static readonly HashSet<uint> RaiseActions = new() { 125, 173, 3603, 7523, 18317, 24287 };

    private static readonly (string Name, uint Job)[] PreviewMembers =
    {
        ("Tank One", 19), ("Tank Two", 32), ("Healer One", 24), ("Healer Two", 28),
        ("Melee One", 34), ("Melee Two", 41), ("Ranged One", 31), ("Caster One", 42),
    };

    private readonly List<PartyMemberData> members = new(MaxMembers);
    private readonly List<AuraData> debuffs = new();
    private readonly AuraSettings debuffFilter = new() { ShowBuffs = false, ShowDebuffs = true, ShowPermanent = false };
    private readonly StatusIcons buffIcons = new();
    private readonly StatusIcons debuffIcons = new();
    private double readyCheckSeenAt = -100;

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
            AddPreview();
        }

        if (s.Sort == PartySortMode.Role)
        {
            members.Sort((a, b) => CompareByRole(s.RoleOrder, a, b));
        }

        var readyCheck = s.ShowReadyCheck ? ReadyCheck() : null;
        var target = Svc.TargetManager.Target;
        var canCleanse = !s.CleansableOnlyIfICanCleanse || PlayerCanCleanse();

        for (var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            var unit = member.Unit;
            var offset = i * ((s.Horizontal ? s.Size.X : s.Size.Y) + s.Spacing);
            var memberPos = pos + (s.Horizontal ? new Vector2(offset, 0) : new Vector2(0, offset));

            var hovered = !ctx.Preview && s.Interactive && ctx.Input.HandleUnit(memberPos, memberPos + s.Size, unit.Object, ctx.General);
            var isTarget = s.HighlightTarget && target != null && unit.Object != null && target.Address == unit.Object.Address;

            UnitBar.Render(ctx, memberPos, s.Size, unit, new UnitBarOptions
            {
                Texture = s.Texture,
                ShowPower = s.ShowPower,
                PowerHeight = s.PowerHeight,
                LeftText = s.LeftText,
                CenterText = s.CenterText,
                RightText = s.RightText,
                LeftStyle = s.LeftStyle,
                CenterStyle = s.CenterStyle,
                RightStyle = s.RightStyle,
                ColorMode = s.ColorMode,
                ShowShield = s.ShowShield,
                Shields = s.OwnShieldStyle ? s.Shields : null,
                Highlight = isTarget,
                Hovered = hovered,
                FadeOutOfRange = s.RangeFade,
                Icon = s.ShowJobIcon && unit.JobId > 0 ? 62000 + unit.JobId : 0,
                SmoothKey = unit.Object != null ? DrawContext.Key(Id, unit.Object.GameObjectId) : 0,
            });

            DrawTrackers(ctx, s, member, memberPos, canCleanse, readyCheck);
        }
    }

    private void DrawTrackers(DrawContext ctx, PartySettings s, in PartyMemberData member, Vector2 pos, bool canCleanse, Dictionary<ulong, ReadyCheckStatus>? readyCheck)
    {
        var unit = member.Unit;
        var size = s.Size;
        var obj = unit.Object;
        using var font = ctx.Fonts.Push(s.TrackerFontSize);

        // Raise: dead and either already raised or someone is casting a raise on them.
        if (s.ShowRaise && unit.IsDead && obj != null)
        {
            var raised = Statuses.Find(obj, 0, RaisedStatuses).Remaining > 0f;
            var raising = raised ? 0f : IncomingRaise(obj);
            if (raised || raising > 0f)
            {
                var color = Colors.Rgb(0x6EE07A);
                ctx.DrawList.AddRectFilled(pos, pos + size, ctx.Col(Colors.WithAlpha(color, 0.25f)));
                Gfx.Text(ctx, raised ? "RAISED" : $"RAISING {raising:0.0}", pos + size / 2f, TextAlign.Center, color);
            }
        }

        // Invulnerability: icon and timer at the right edge.
        if (s.ShowInvulnerability && obj != null && !unit.IsDead)
        {
            foreach (var id in InvulnStatuses)
            {
                var (remaining, _) = Statuses.Find(obj, 0, id);
                if (remaining > 0f)
                {
                    DrawStatusIcon(ctx, id, pos + new Vector2(size.X - size.Y - 2, 2), size.Y - 4, remaining);
                    break;
                }
            }
        }

        // Cleansable debuffs: an outline when a member has one you can remove.
        if (obj != null && s.ShowCleansable && canCleanse)
        {
            debuffFilter.MaxIcons = 30;
            Auras.Collect(obj, debuffFilter, debuffs);
            if (debuffs.Exists(d => d.CanDispel))
            {
                ctx.DrawList.AddRect(pos - Vector2.One, pos + size + Vector2.One, ctx.Col(s.CleansableColor), 0f, default, 2f);
            }
        }

        if (obj != null && !unit.IsDead)
        {
            buffIcons.Draw(ctx, s.Buffs, obj, false, pos, size);
        }

        debuffIcons.Draw(ctx, s.Debuffs, obj, true, pos, size);

        // Ready check: a tick or a cross at the right edge.
        if (readyCheck != null && readyCheck.TryGetValue(member.ContentId, out var status))
        {
            DrawReadyMark(ctx, status, pos + new Vector2(size.X - size.Y * 0.5f - 6, size.Y * 0.5f), size.Y * 0.32f);
        }
    }

    private static void DrawStatusIcon(DrawContext ctx, uint statusId, Vector2 pos, float height, float remaining)
    {
        var icon = Auras.IconOf(statusId);
        if (icon == 0)
        {
            return;
        }

        var width = MathF.Round(height * 0.75f);
        var texture = Svc.TextureProvider.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty();
        ctx.DrawList.AddImage(texture.Handle, pos, pos + new Vector2(width, height), Vector2.Zero, Vector2.One, ctx.Col(Vector4.One));
        if (!float.IsPositiveInfinity(remaining))
        {
            Gfx.Text(ctx, TextTags.Duration(remaining), pos + new Vector2(width / 2f, height - 5), TextAlign.Center, Vector4.One);
        }
    }

    private static void DrawReadyMark(DrawContext ctx, ReadyCheckStatus status, Vector2 centre, float radius)
    {
        var dl = ctx.DrawList;
        switch (status)
        {
            case ReadyCheckStatus.Ready:
            {
                var color = ctx.Col(Colors.Rgb(0x6EE07A));
                dl.AddCircleFilled(centre, radius + 2, ctx.Col(new Vector4(0, 0, 0, 0.6f)));
                dl.AddLine(centre + new Vector2(-radius * 0.6f, 0), centre + new Vector2(-radius * 0.1f, radius * 0.5f), color, 2.5f);
                dl.AddLine(centre + new Vector2(-radius * 0.1f, radius * 0.5f), centre + new Vector2(radius * 0.7f, -radius * 0.5f), color, 2.5f);
                break;
            }

            case ReadyCheckStatus.NotReady:
            {
                var color = ctx.Col(Colors.Rgb(0xE05A50));
                dl.AddCircleFilled(centre, radius + 2, ctx.Col(new Vector4(0, 0, 0, 0.6f)));
                dl.AddLine(centre - new Vector2(radius * 0.55f), centre + new Vector2(radius * 0.55f), color, 2.5f);
                dl.AddLine(centre + new Vector2(-radius * 0.55f, radius * 0.55f), centre + new Vector2(radius * 0.55f, -radius * 0.55f), color, 2.5f);
                break;
            }

            case ReadyCheckStatus.AwaitingResponse:
                dl.AddCircle(centre, radius, ctx.Col(new Vector4(1, 1, 1, 0.7f)), 16, 2f);
                break;
        }
    }

    /// <summary>Seconds left on a raise being cast on <paramref name="target"/>, or 0.</summary>
    private static float IncomingRaise(IGameObject target)
    {
        foreach (var member in Party.Current)
        {
            if (member.Unit.Object is IBattleChara caster &&
                caster.IsCasting &&
                RaiseActions.Contains(caster.CastActionId) &&
                caster.CastTargetObjectId == target.GameObjectId)
            {
                return Math.Max(0.1f, caster.TotalCastTime - caster.CurrentCastTime);
            }
        }

        return 0f;
    }

    /// <summary>Ready-check answers by content id while a check is running (and briefly after).</summary>
    private unsafe Dictionary<ulong, ReadyCheckStatus>? ReadyCheck()
    {
        var agent = AgentReadyCheck.Instance();
        if (agent == null)
        {
            return null;
        }

        Dictionary<ulong, ReadyCheckStatus>? result = null;
        var awaiting = false;
        foreach (var entry in agent->ReadyCheckEntries)
        {
            if (entry.ContentId == 0 || entry.Status is ReadyCheckStatus.Unknown or ReadyCheckStatus.MemberNotPresent)
            {
                continue;
            }

            result ??= new Dictionary<ulong, ReadyCheckStatus>();
            result[entry.ContentId] = entry.Status;
            awaiting |= entry.Status == ReadyCheckStatus.AwaitingResponse;
        }

        var now = Dalamud.Bindings.ImGui.ImGui.GetTime();
        if (awaiting)
        {
            readyCheckSeenAt = now;
        }

        // The game keeps the last answers around; only show them during and shortly after a check.
        return now - readyCheckSeenAt <= ReadyCheckLinger ? result : null;
    }

    private static bool PlayerCanCleanse()
    {
        var job = Svc.ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;
        var role = Jobs.Get(job).Role;
        return role == JobRole.Healer || job == 23; // Bards have The Warden's Paean
    }

    private void Collect(PartySettings s)
    {
        members.Clear();
        var party = Party.Current;
        if (party.Count <= 1 && !s.ShowWhenSolo)
        {
            return;
        }

        foreach (var member in party)
        {
            if (members.Count >= MaxMembers || (member.IsPlayer && !s.IncludePlayer))
            {
                continue;
            }

            members.Add(member);
        }
    }

    private void AddPreview()
    {
        for (var i = 0; i < PreviewMembers.Length; i++)
        {
            var (name, job) = PreviewMembers[i];
            var sample = Units.Sample(name, job, 1f - i * 0.09f, UnitReaction.Player, i % 3 == 0 ? 0.15f : 0f);
            sample.InRange = i != 6;
            members.Add(new PartyMemberData { Unit = sample });
        }
    }

    private static int CompareByRole(PartyRoleOrder order, PartyMemberData a, PartyMemberData b)
    {
        var byGroup = GroupRank(order, Jobs.Get(a.Unit.JobId).Role).CompareTo(GroupRank(order, Jobs.Get(b.Unit.JobId).Role));
        if (byGroup != 0)
        {
            return byGroup;
        }

        // Within a group: melee, ranged, casters, then by name.
        var byRole = Jobs.RoleSortOrder(Jobs.Get(a.Unit.JobId).Role).CompareTo(Jobs.RoleSortOrder(Jobs.Get(b.Unit.JobId).Role));
        return byRole != 0 ? byRole : string.Compare(a.Unit.Name, b.Unit.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Where a role's group (tank, healer or DPS) comes in the chosen order.</summary>
    private static int GroupRank(PartyRoleOrder order, JobRole role)
    {
        var group = role switch
        {
            JobRole.Tank => 'T',
            JobRole.Healer => 'H',
            JobRole.Melee or JobRole.Ranged or JobRole.Caster => 'D',
            _ => 'X',
        };

        var sequence = order switch
        {
            PartyRoleOrder.TankDpsHealer => "TDH",
            PartyRoleOrder.HealerTankDps => "HTD",
            PartyRoleOrder.HealerDpsTank => "HDT",
            PartyRoleOrder.DpsTankHealer => "DTH",
            PartyRoleOrder.DpsHealerTank => "DHT",
            _ => "THD",
        };

        var index = sequence.IndexOf(group);
        return index < 0 ? 3 : index;
    }
}
