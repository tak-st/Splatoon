using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.DalamudServices.Legacy;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using ImGuiNET;
using Splatoon.SplatoonScripting;

namespace SplatoonScriptsOfficial.Duties.Dawntrail.The_Futures_Rewritten;

public class P2_AutoTargetCrystal : SplatoonScript
{
    public override HashSet<uint>? ValidTerritories => [1238];
    public override Metadata? Metadata => new(2, "Garume + TS");

    private Config C => Controller.GetConfig<Config>();

    private IEnumerable<IBattleNpc> LightCrystals => Svc.Objects.Where(x => x.DataId == 0x45A3).OfType<IBattleNpc>();
    private IBattleNpc? IceCrystal => Svc.Objects.FirstOrDefault(x => x.DataId == 0x45A5) as IBattleNpc;

    public override void OnSettingsDraw()
    {
        ImGui.Checkbox("一定範囲以内にある氷晶だけを対象にする", ref C.LimitDistance);
        if (C.LimitDistance) {
            ImGui.SliderFloat("対象とする範囲(m)", ref C.distance, 0f, 30f);
        }
        ImGui.Text("Light Crystals");
        foreach (var crystal in LightCrystals) ImGui.Text(crystal.Name.ToString());
    }

    public override void OnUpdate()
    {
        if (EzThrottler.Throttle("AutoTargetCrystal", 200)) SetNearTarget();
    }

    private void SetNearTarget()
    {
        if (LightCrystals.Where(x => x.CurrentHp != 0 && (!C.LimitDistance || Player.DistanceTo(x) < C.distance + x.HitboxRadius))
                .MinBy(x => Player.DistanceTo(x)) is { } target)
            Svc.Targets.SetTarget(target);
        else if (IceCrystal is { } ice)
            Svc.Targets.SetTarget(ice);
    }

    private class Config : IEzConfig
    {
        public bool LimitDistance = true;
        public float distance = 25f;
    }
}