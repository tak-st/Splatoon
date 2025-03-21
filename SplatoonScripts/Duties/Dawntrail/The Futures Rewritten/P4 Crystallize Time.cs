using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Components;
using ECommons;
using ECommons.Automation;
using ECommons.Configuration;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameFunctions;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using ECommons.MathHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using ImGuiNET;
using Splatoon;
using Splatoon.SplatoonScripting;
using Splatoon.SplatoonScripting.Priority;
using Splatoon.Utility;
using Action = Lumina.Excel.Sheets.Action;

namespace SplatoonScriptsOfficial.Duties.Dawntrail.The_Futures_Rewritten;

public unsafe class P4_Crystallize_Time : SplatoonScript
{
    public enum Direction
    {
        North = 0,
        NorthEast = 45,
        East = 90,
        SouthEast = 135,
        South = 180,
        SouthWest = 225,
        West = 270,
        NorthWest = 315
    }

    private readonly Vector2 _center = new(100, 100);

    private readonly List<IBattleChara> _earlyHourglassList = [];
    private readonly List<IBattleChara> _lateHourglassList = [];

    private readonly Dictionary<ulong, PlayerData> _players = new();

    private readonly IEnumerable<uint> AllDebuffIds = Enum.GetValues<Debuff>().Cast<uint>();

    private Direction? _baseDirection = Direction.North;
    private string _basePlayerOverride = "";

    private Direction _debugDirection1 = Direction.North;
    private Direction _debugDirection2 = Direction.North;

    private Direction _editSplitElementDirection;
    private float _editSplitElementRadius;

    private Direction? _firstWaveDirection;

    private Direction? _lateHourglassDirection;
    private Direction? _secondWaveDirection;

    private List<float> ExtraRandomness = [];
    private bool Initialized;
    private bool useCommandAgain = false;
    public override Metadata? Metadata => new(21, "Garume, NightmareXIV + TS", "", "https://github.com/tak-st/Splatoon/blob/main/SplatoonScripts/Duties/Dawntrail/The%20Futures%20Rewritten/README.md");

    public override Dictionary<int, string> Changelog => new()
    {
        [10] =
            "A large addition of various functions as well as changes to general mechanic flow. Please validate settings and if possible verify that the script works fine in replay.",
        [11] = "Added dragon explosion anticipation for eruption"
    };

    private IPlayerCharacter BasePlayer
    {
        get
        {
            if (_basePlayerOverride == "")
                return Player.Object;
            return Svc.Objects.OfType<IPlayerCharacter>()
                .FirstOrDefault(x => x.Name.ToString().EqualsIgnoreCase(_basePlayerOverride)) ?? Player.Object;
        }
    }

    private float SpellInWaitingDebuffTime =>
        BasePlayer.StatusList?.FirstOrDefault(x => x.StatusId == (uint)Debuff.DelayReturn)?.RemainingTime ?? -1f;

    private float ReturnDebuffTime =>
        BasePlayer.StatusList?.FirstOrDefault(x => x.StatusId == (uint)Debuff.Return)?.RemainingTime ?? -1f;

    private bool IsActive => Svc.Objects.Any(x => x.DataId == 17837) && !BasePlayer.IsDead;

    public override HashSet<uint>? ValidTerritories => [1238];

    private Config C => Controller.GetConfig<Config>();

    private static IBattleNpc? WestDragon => Svc.Objects.Where(x => x is { DataId: 0x45AC, Position.X: <= 100 })
        .Select(x => x as IBattleNpc).First();

    private static IBattleNpc? EastDragon => Svc.Objects.Where(x => x is { DataId: 0x45AC, Position.X: > 100 })
        .Select(x => x as IBattleNpc).First();

    private List<Vector2>? _cachedCleanses;

    private static IEnumerable<Vector2> CleansesPositions => Svc.Objects
        .Where(x => x is { DataId: 0x1EBD41 })
        .OfType<IEventObj>()
        .OrderBy(x => x.Position.X)
        .Select(x => x.Position.ToVector2());

    private void CheckCleanses()
    {
        if (_cachedCleanses is not null) return;

        var currentPositions = CleansesPositions.ToArray();
        
        if (currentPositions.Length == 4)
        {
            _cachedCleanses = currentPositions.ToList();
        }
    }

    private IEnumerable<Vector2> GetCleanses()
    {
        var currentPositions = CleansesPositions.ToArray();

        if (currentPositions.Length == 4)
        {
            _cachedCleanses = currentPositions.ToList();
            return currentPositions;
        }
        else if (currentPositions.Length <= 3 && _cachedCleanses != null)
        {
            return _cachedCleanses;
        }
        return currentPositions;
    }

    private MechanicStage GetStage()
    {
        if (Svc.Objects.All(x => x.DataId != 17837)) return MechanicStage.Unknown;
        var time = SpellInWaitingDebuffTime;
        if (time > 0)
            return time switch
            {
                < 11.5f => MechanicStage.Step6_ThirdHourglass,
                < 15.6f => MechanicStage.Step5_PerformDodges,
                < 16.5f => MechanicStage.Step4_SecondHourglass,
                < 18.8f => MechanicStage.Step3_IcesAndWinds,
                < 21.9f => MechanicStage.Step2_FirstHourglass,
                _ => MechanicStage.Step1_Spread
            };
        var returnTime = ReturnDebuffTime;
        return returnTime > 0 ? MechanicStage.Step7_SpiritTaker : MechanicStage.Unknown;
    }


    public override void OnStartingCast(uint source, uint castId)
    {
        if (GetStage() == MechanicStage.Unknown) return;
        if (castId == 40251 && source.GetObject() is { } sourceObject)
        {
            var direction = GetDirection(sourceObject.Position);
            if (direction == null) return;
            if (_firstWaveDirection == null)
                _firstWaveDirection = direction;
            else
                _secondWaveDirection = direction;
        }
    }

    public override void OnVFXSpawn(uint target, string vfxPath)
    {
        if (GetStage() == MechanicStage.Unknown) return;
        if (vfxPath == "vfx/common/eff/dk02ht_zan0m.avfx" &&
            target.GetObject() is IBattleNpc piece &&
            _baseDirection == null)
        {
            var newDirection = GetDirection(piece.Position);
            if (newDirection != null) _baseDirection = newDirection;
        }
    }

    public override void OnTetherCreate(uint source, uint target, uint data2, uint data3, uint data5)
    {
        if (GetStage() == MechanicStage.Unknown) return;
        if (source.GetObject() is not IBattleChara sourceObject) return;
        if (data5 == 15)
        {
            switch (data3)
            {
                case 133:
                    _lateHourglassList.Add(sourceObject);
                    break;
                case 134:
                    _earlyHourglassList.Add(sourceObject);
                    break;
            }

            if (_lateHourglassList.Count == 2 && _earlyHourglassList.Count == 2)
            {
                var newDirection = GetDirection(_lateHourglassList[0].Position);
                if (newDirection != null) _lateHourglassDirection = newDirection;
            }
        }
    }

    private static Direction? GetDirection(Vector3? positionNullable)
    {
        if (positionNullable == null) return null;
        var position = positionNullable.Value;
        var isNorth = position.Z < 95f;
        var isEast = position.X > 105f;
        var isSouth = position.Z > 105f;
        var isWest = position.X < 95f;

        if (isNorth && isEast) return Direction.NorthEast;
        if (isEast && isSouth) return Direction.SouthEast;
        if (isSouth && isWest) return Direction.SouthWest;
        if (isWest && isNorth) return Direction.NorthWest;
        if (isNorth) return Direction.North;
        if (isEast) return Direction.East;
        if (isSouth) return Direction.South;
        if (isWest) return Direction.West;
        return null;
    }

    public override void OnGainBuffEffect(uint sourceId, Status Status)
    {
        if (!IsActive || Initialized || sourceId.GetObject() is not IPlayerCharacter player) return;
        var debuffs = player.StatusList.Where(x => AllDebuffIds.Contains(x.StatusId));

        _players.TryAdd(player.GameObjectId, new PlayerData { PlayerName = player.Name.ToString() });

        foreach (var debuff in debuffs)
            switch (debuff.StatusId)
            {
                case (uint)Debuff.Red:
                    _players[player.GameObjectId].Color = Debuff.Red;
                    break;
                case (uint)Debuff.Blue:
                    _players[player.GameObjectId].Color = Debuff.Blue;
                    break;
                case (uint)Debuff.Quietus:
                    _players[player.GameObjectId].HasQuietus = true;
                    break;
                case (uint)Debuff.DelayReturn:
                    break;
                default:
                    _players[player.GameObjectId].Debuff = (Debuff)debuff.StatusId;
                    break;
            }


        if (_players.All(x => x.Value.HasDebuff))
        {
            var redBlizzards = C.PriorityData
                .GetPlayers(x => _players.First(y => y.Value.PlayerName == x.Name).Value is
                    { Color: Debuff.Red, Debuff: Debuff.Blizzard }
                );

            if (redBlizzards != null)
            {
                _players[redBlizzards[0].IGameObject.GameObjectId].MoveType = MoveType.RedBlizzardWest;
                _players[redBlizzards[1].IGameObject.GameObjectId].MoveType = MoveType.RedBlizzardEast;
            }

            var redAeros = C.PriorityData
                .GetPlayers(x => _players.First(y => y.Value.PlayerName == x.Name).Value is
                    { Color: Debuff.Red, Debuff: Debuff.Aero }
                );

            if (redAeros != null)
            {
                _players[redAeros[0].IGameObject.GameObjectId].MoveType = MoveType.RedAeroWest;
                _players[redAeros[1].IGameObject.GameObjectId].MoveType = MoveType.RedAeroEast;
            }

            foreach (var otherPlayer in _players.Where(x => x.Value.MoveType == null))
                _players[otherPlayer.Key].MoveType = otherPlayer.Value.Debuff switch
                {
                    Debuff.Holy => MoveType.BlueHoly,
                    Debuff.Blizzard => MoveType.BlueBlizzard,
                    Debuff.Water => MoveType.BlueWater,
                    Debuff.Eruption => MoveType.BlueEruption,
                    _ => _players[otherPlayer.Key].MoveType
                };


            if (!string.IsNullOrEmpty(C.CommandWhenBlueDebuff) &&
                BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Blue))
            {
                var random = 0;
                if (C.ShouldUseRandomWait)
                    random = RandomNumberGenerator.GetInt32((int)(C.WaitRange.X * 1000), (int)(C.WaitRange.Y * 1000));
                Controller.Schedule(() => { Chat.Instance.ExecuteCommand(C.CommandWhenBlueDebuff); }, random);
            }

            Initialized = true;
            PluginLog.Debug("CT initialized");
        }
    }

    public override void OnReset()
    {
        Initialized = false;
        useCommandAgain = false;
        _baseDirection = null;
        _lateHourglassDirection = null;
        _firstWaveDirection = null;
        _secondWaveDirection = null;
        _cachedCleanses = null;
        _players.Clear();
        _earlyHourglassList.Clear();
        _lateHourglassList.Clear();
        ExtraRandomness =
        [
            (float)Random.Shared.NextDouble() - 0.5f, (float)Random.Shared.NextDouble() - 0.5f,
            (float)Random.Shared.NextDouble() - 0.5f, (float)Random.Shared.NextDouble() - 0.5f
        ];
    }


    private Vector2 SwapXIfNecessary(Vector2 position)
    {
        if (_lateHourglassDirection is Direction.NorthEast or Direction.SouthWest)
            return position;
        var swapX = _center.X * 2 - position.X;
        return new Vector2(swapX, position.Y);
    }

    public override void OnSetup()
    {
        foreach (var move in Enum.GetValues<MoveType>())
            Controller.RegisterElement(move.ToString(), new Element(0)
            {
                radius = 1f,
                thicc = 6f,
                overlayText = ""
            });

        foreach (var stack in Enum.GetValues<WaveStack>())
            Controller.RegisterElement(stack + nameof(WaveStack), new Element(0)
            {
                radius = 0.5f,
                thicc = 6f,
                overlayText = ""
            });

        Controller.RegisterElement("Alert", new Element(1)
        {
            radius = 0f,
            overlayText = "Alert",
            overlayFScale = 1f,
            overlayVOffset = 1f,
            refActorComparisonType = 5,
            refActorPlaceholder = ["<1>"]
        });

        Controller.RegisterElementFromCode("SplitPosition",
            "{\"Name\":\"\",\"Enabled\":false,\"refX\":100.0,\"refY\":100.0,\"radius\":1.0,\"Filled\":false,\"fillIntensity\":0.5,\"overlayBGColor\":4278190080,\"overlayTextColor\":4294967295,\"thicc\":4.0,\"overlayText\":\"Spread!\",\"refActorTetherTimeMin\":0.0,\"refActorTetherTimeMax\":0.0}");

        Controller.RegisterElementFromCode("KBHelper",
            "{\"Name\":\"\",\"type\":2,\"Enabled\":false,\"radius\":0.0,\"color\":3355508503,\"fillIntensity\":0.345,\"thicc\":4.0,\"refActorTetherTimeMin\":0.0,\"refActorTetherTimeMax\":0.0}");

        Controller.RegisterElementFromCode("RedDragonExplosion1",
            "{\"Name\":\"\",\"refX\":87.5,\"refY\":98.0,\"refZ\":1.9073486E-06,\"radius\":13.0,\"color\":3372155112,\"fillIntensity\":0.5,\"refActorTetherTimeMin\":0.0,\"refActorTetherTimeMax\":0.0}");
        Controller.RegisterElementFromCode("RedDragonExplosion2",
            "{\"Name\":\"\",\"refX\":112.5,\"refY\":98.0,\"refZ\":1.9073486E-06,\"radius\":13.0,\"color\":3372155112,\"fillIntensity\":0.5,\"refActorTetherTimeMin\":0.0,\"refActorTetherTimeMax\":0.0}");
    }

    private void Alert(string text)
    {
        var playerOrder = GetPlayerOrder(BasePlayer);
        if (Controller.TryGetElementByName("Alert", out var element))
        {
            element.Enabled = true;
            element.overlayText = text;
            element.refActorPlaceholder = [$"<{playerOrder}>"];
        }
    }

    private static int GetPlayerOrder(IGameObject c)
    {
        for (var i = 1; i <= 8; i++)
            if ((nint)FakePronoun.Resolve($"<{i}>") == c.Address)
                return i;

        return 0;
    }

    private void HideAlert()
    {
        if (Controller.TryGetElementByName("Alert", out var element))
            element.Enabled = false;
    }


    public override void OnUpdate()
    {
        ProcessAutoCast();

        if (GetStage() == MechanicStage.Unknown)
        {
            Controller.GetRegisteredElements().Each(x => x.Value.Enabled = false);
            return;
        }

        var spr = GetStage().EqualsAny(MechanicStage.Step1_Spread, MechanicStage.Step2_FirstHourglass) &&
                  BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Eruption) &&
                  SpellInWaitingDebuffTime < 25f && Svc.Objects.OfType<IPlayerCharacter>().Any(x =>
                      x.StatusList.Count(s => s.StatusId.EqualsAny((uint)Debuff.Red, (uint)Debuff.Blizzard)) == 2);
        Controller.GetElementByName("RedDragonExplosion1")!.Enabled = spr;
        Controller.GetElementByName("RedDragonExplosion2")!.Enabled = spr;


        {
            var e = Controller.GetElementByName("KBHelper")!;
            e.Enabled = false;
            if (GetStage() == MechanicStage.Step2_FirstHourglass &&
                BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Blue))
            {
                var wind = Svc.Objects.OfType<IPlayerCharacter>()
                    .OrderBy(x => Vector3.Distance(x.Position, BasePlayer.Position))
                    .Where(x => x.StatusList.Any(s => s.StatusId == (uint)Debuff.Aero)).FirstOrDefault();
                if (wind != null && Vector3.Distance(BasePlayer.Position, wind.Position) < 5f)
                {
                    e.Enabled = true;
                    e.SetRefPosition(wind.Position);
                    e.SetOffPosition(new Vector3(
                        100 + (_lateHourglassDirection.EqualsAny(Direction.NorthEast, Direction.SouthWest) ? 12 : -12),
                        0, 85));
                }
            }
        }

        var myMove = _players.SafeSelect(BasePlayer.GameObjectId)?.MoveType.ToString();
        var forcedPosition = ResolveRedAeroMove();
        forcedPosition ??= ResolveRedBlizzardMove();
        if (myMove != null)
            foreach (var move in Enum.GetValues<MoveType>())
                if (Controller.TryGetElementByName(move.ToString(), out var element))
                {
                    if (GetStage() == MechanicStage.Step6_ThirdHourglass &&
                        BasePlayer.StatusList.All(x => x.StatusId != (uint)Debuff.Blue))
                    {
                        element.Enabled = false;
                        continue;
                    }

                    element.Enabled = C.ShowOther;
                    element.color = EColor.Red.ToUint();
                    element.overlayText = "";
                    element.tether = false;

                    if (myMove == move.ToString())
                    {
                        element.Enabled = true;
                        element.color = GradientColor.Get(C.BaitColor1, C.BaitColor2).ToUint();
                        element.tether = true;
                        if (forcedPosition == null) continue;
                        element.SetOffPosition(forcedPosition.Value.ToVector3(0));
                        element.radius = 0.4f;
                    }
                }


        if (forcedPosition != null) return;
        switch (GetStage())
        {
            case MechanicStage.Step1_Spread:
                BurnHourglassUniversal();
                break;
            case MechanicStage.Step2_FirstHourglass:
                IceHitDragon();
                break;
            case MechanicStage.Step3_IcesAndWinds:
                BurnHourglassUniversal();
                break;
            case MechanicStage.Step4_SecondHourglass:
                if (C.HitTiming == HitTiming.Early && BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Red))
                    HitDragonAndAero();
                else
                    BurnHourglassUniversal();
                break;
            case MechanicStage.Step5_PerformDodges:
                if (C.HitTiming == HitTiming.Late && BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Red))
                    HitDragonAndAero();
                else
                    BurnHourglassUniversal();
                break;
            case MechanicStage.Step6_ThirdHourglass:
                var marker = _players.FirstOrDefault(x => x.Value.PlayerName == BasePlayer.Name.ToString()).Value?.Marker;
                var isSouthReturnPos = _firstWaveDirection == Direction.South || _secondWaveDirection == Direction.South;
                if (BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Blue) && SpellInWaitingDebuffTime > C.ReturnShowTime && (!C.LateSentence || isSouthReturnPos || !(marker == MarkerType.Attack2 || marker == MarkerType.Attack3)))
                {
                    CorrectCleanse();
                    PlaceReturn(true);
                }
                else
                    if (BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Red))
                        HitDragonAndAero();
                    else
                        PlaceReturn();
                break;
            case MechanicStage.Step7_SpiritTaker:
                if (BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Blue))
                    CorrectCleanse();
                else
                    Split();
                break;
        }
    }

    private void BurnHourglassUniversal()
    {
        if (GetStage() < MechanicStage.Step2_FirstHourglass) BurnYellowHourglass();
        else if (GetStage() < MechanicStage.Step4_SecondHourglass) BurnHourglass();
        else if (GetStage() < MechanicStage.Step6_ThirdHourglass) BurnPurpleHourglass();
    }

    private void AutoCast(uint actionId)
    {
        if (!Svc.Condition[ConditionFlag.DutyRecorderPlayback])
        {
            if (ActionManager.Instance()->GetActionStatus(ActionType.Action, actionId) == 0 &&
                EzThrottler.Throttle(InternalData.FullName + "AutoCast", 100))
                Chat.Instance.ExecuteAction(actionId);
        }
        else
        {
            if (EzThrottler.Throttle(InternalData.FullName + "InformCast", 100))
                DuoLog.Information(
                    $"Would use mitigation action {ExcelActionHelper.GetActionName(actionId)} if possible");
        }
    }

    private void ProcessAutoCast()
    {
        try
        {
            if (Svc.Objects.Any(x => x.DataId == 17837) && !BasePlayer.IsDead)
            {
                var myMove = _players.SafeSelect(BasePlayer.GameObjectId)?.MoveType;
                if (C.UseMitigationDragon && C.MitigationDragonAction != 0 &&
                    myMove is MoveType.RedBlizzardEast or MoveType.RedBlizzardWest &&
                    !BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Red) &&
                    BasePlayer.StatusList.Any(x =>
                        x.StatusId == (uint)Debuff.Return && x.RemainingTime < 20.5f + ExtraRandomness.SafeSelect(2) &&
                        x.RemainingTime > 18f))
                    AutoCast(C.MitigationDragonAction);
                if (C.UseKbiAuto &&
                    BasePlayer.StatusList.Any(x =>
                        x.StatusId == (uint)Debuff.Return && x.RemainingTime < 2f + ExtraRandomness.SafeSelect(0)))
                    //7559 : surecast
                    //7548 : arm's length
                    UseAntiKb();

                if (C.UseMitigation && C.MitigationAction != 0 &&
                    BasePlayer.StatusList.Any(x =>
                        x.StatusId == (uint)Debuff.Return && x.RemainingTime < 6f + ExtraRandomness.SafeSelect(1)))
                    AutoCast(C.MitigationAction);

                if (C.UseTankMitigation && C.TankMitigationAction != 0 &&
                    BasePlayer.StatusList.Any(x =>
                        x.StatusId == (uint)Debuff.Return && x.RemainingTime < 6f + ExtraRandomness.SafeSelect(1)))
                    AutoCast(C.TankMitigationAction);

                if (C is { UseSprintAuto: true, ShouldGoNorthRedBlizzard: true } &&
                    BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Red) &&
                    BasePlayer.StatusList.Any(x =>
                        x.StatusId == (uint)Debuff.Blizzard && x.RemainingTime < 1f + ExtraRandomness.SafeSelect(3)))
                    AutoCast(29057);
            }
        }
        catch (Exception e)
        {
            e.Log();
        }
    }

    private void UseAntiKb()
    {
        foreach (var x in (uint[]) [7559, 7548])
            if (!Svc.Condition[ConditionFlag.DutyRecorderPlayback])
            {
                if (ActionManager.Instance()->GetActionStatus(ActionType.Action, x) == 0 &&
                    EzThrottler.Throttle(InternalData.FullName + "AutoCast", 100)) Chat.Instance.ExecuteAction(x);
            }
            else
            {
                if (EzThrottler.Throttle(InternalData.FullName + "InformCast", 100))
                    DuoLog.Information(
                        $"Would use kb immunity action {ExcelActionHelper.GetActionName(x)} if possible");
            }
    }

    private void BurnYellowHourglass()
    {
        foreach (var player in Enum.GetValues<MoveType>())
        {
            var position = player switch
            {
                MoveType.RedBlizzardWest => new Vector2(87, 100),
                MoveType.RedBlizzardEast => new Vector2(113, 100),
                MoveType.RedAeroWest => new Vector2(88, 115),
                MoveType.RedAeroEast => new Vector2(112, 115),
                MoveType.BlueBlizzard => new Vector2(88, 115),
                MoveType.BlueHoly => new Vector2(88, 115),
                MoveType.BlueWater => new Vector2(88, 115),
                MoveType.BlueEruption => new Vector2(112, 85),
                _ => throw new InvalidOperationException()
            };

            position = SwapXIfNecessary(position);
            if (Controller.TryGetElementByName(SwapIfNecessary(player), out var element))
            {
                element.overlayText = "";
                element.radius = 0.5f;
                element.SetOffPosition(position.ToVector3(0));
            }
        }
    }

    private void IceHitDragon()
    {
        foreach (var player in Enum.GetValues<MoveType>())
        {
            var position = player switch
            {
                MoveType.RedBlizzardWest => WestDragon?.Position.ToVector2() ?? new Vector2(87, 100),
                MoveType.RedBlizzardEast => EastDragon?.Position.ToVector2() ?? new Vector2(113, 100),
                MoveType.RedAeroWest => new Vector2(90, 117),
                MoveType.RedAeroEast => new Vector2(107, 118),
                MoveType.BlueBlizzard => new Vector2(91, 115),
                MoveType.BlueHoly => new Vector2(91, 115),
                MoveType.BlueWater => new Vector2(91, 115),
                MoveType.BlueEruption => new Vector2(112, 85),
                _ => throw new InvalidOperationException()
            };

            position = SwapXIfNecessary(position);
            if (Controller.TryGetElementByName(SwapIfNecessary(player), out var element))
            {
                element.overlayText = "";
                element.radius = 0.5f;
                element.SetOffPosition(position.ToVector3(0));
            }
        }

        var myMove = _players.SafeSelect(BasePlayer.GameObjectId)?.MoveType;
        if (myMove is MoveType.RedBlizzardEast or MoveType.RedBlizzardWest)
        {
            var remainingTime = BasePlayer.StatusList.FirstOrDefault(x => x.StatusId == (uint)Debuff.Blizzard)
                ?.RemainingTime;
            Alert(C.HitDragonText.Get() + (remainingTime != null ? $" ({remainingTime.Value:0.0}s)" : ""));
        }
    }

    private void BurnHourglass()
    {
        foreach (var player in Enum.GetValues<MoveType>())
        {
            var position = player switch
            {
                MoveType.RedBlizzardWest => new Vector2(112, 86),
                MoveType.RedBlizzardEast => new Vector2(112, 86),
                MoveType.RedAeroWest => new Vector2(100, 115),
                MoveType.RedAeroEast => new Vector2(107, 118),
                MoveType.BlueBlizzard => new Vector2(112, 86),
                MoveType.BlueHoly => new Vector2(112, 86),
                MoveType.BlueWater => new Vector2(112, 86),
                MoveType.BlueEruption => new Vector2(112, 86),
                _ => throw new InvalidOperationException()
            };

            position = SwapXIfNecessary(position);
            if (Controller.TryGetElementByName(SwapIfNecessary(player), out var element))
            {
                element.overlayText = "";
                element.radius = 1f;
                element.SetOffPosition(position.ToVector3(0));
            }
        }
    }

    private void BurnPurpleHourglass()
    {
        var directionTxt = "";
        foreach (var player in Enum.GetValues<MoveType>())
        {
            var position = player switch
            {
                MoveType.RedBlizzardWest => new Vector2(100, 85),
                MoveType.RedBlizzardEast => new Vector2(100, 85),
                MoveType.RedAeroWest => new Vector2(100, 118),
                MoveType.RedAeroEast => new Vector2(110, 110),
                MoveType.BlueBlizzard => new Vector2(100, 85),
                MoveType.BlueHoly => new Vector2(100, 85),
                MoveType.BlueWater => new Vector2(100, 85),
                MoveType.BlueEruption => new Vector2(100, 85),
                _ => throw new InvalidOperationException()
            };

            position = SwapXIfNecessary(position);

            var isSouthReturnPos = _firstWaveDirection == Direction.South || _secondWaveDirection == Direction.South;
            var marker = _players.FirstOrDefault(x => x.Value.PlayerName == BasePlayer.Name.ToString()).Value?.Marker;
            if (BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Blue) && (!C.LateSentence || isSouthReturnPos || _secondWaveDirection == null || !(marker == MarkerType.Attack2 || marker == MarkerType.Attack3)) && C.PrioritizeMarker)
            {
                CheckCleanses();

                if (marker != null)
                {
                    directionTxt = marker switch
                    {
                        MarkerType.Attack1 => "(この後、B)",
                        MarkerType.Attack2 => "(この後、2)",
                        MarkerType.Attack3 => "(この後、3)",
                        MarkerType.Attack4 => "(この後、D)",
                        _ => "(マーカーなし)"
                    };
                    if (_firstWaveDirection == Direction.West && (marker == MarkerType.Attack1 || marker == MarkerType.Attack2))
                    {
                        position = new Vector2(110, 85);
                    }
                }
                else
                {
                    directionTxt = "(マーカーなし)";
                }
            }
            if (Controller.TryGetElementByName(SwapIfNecessary(player), out var element))
            {
                element.overlayText = "";
                element.radius = 1f;
                element.SetOffPosition(position.ToVector3(0));
            }

            var myMove = _players.SafeSelect(BasePlayer.GameObjectId)?.MoveType;
            if (_firstWaveDirection != null && player is (MoveType.RedAeroWest or MoveType.RedAeroEast) && BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Red)) {
                if (player == myMove)
                {
                    directionTxt = GetRedAeroInfoText(player);
                }
            }
        }

        if (!useCommandAgain && directionTxt == "(マーカーなし)") {
            useCommandAgain = true;
            var random = RandomNumberGenerator.GetInt32((int)0, (int)1000);
            Controller.Schedule(() => { Chat.Instance.ExecuteCommand(C.CommandWhenBlueDebuff); }, random);
        }

        var remainingTime = SpellInWaitingDebuffTime;
        Alert(C.AvoidWaveText.Get() + directionTxt + $" ({remainingTime:0.0}s)");
    }

    private string GetRedAeroInfoText(MoveType player) {
        if (_firstWaveDirection == Direction.West)
        {
            if (_lateHourglassDirection is Direction.NorthEast) // ／
            {
                return player switch
                {
                    MoveType.RedAeroWest => "(波南避け -> ダッシュ)",
                    MoveType.RedAeroEast => "(先竜回収 -> 波東避け)",
                    _ => ""
                };
            }
            else // ＼
            {
                return player switch
                {
                    MoveType.RedAeroWest => "(早爆発 -> 波西避け)",
                    MoveType.RedAeroEast => "(遅爆発 -> 先竜回収 -> 波東避け)",
                    _ => ""
                };
            }
        }

        if (_firstWaveDirection == Direction.East)
        {
            if (_lateHourglassDirection is Direction.NorthEast) // ／
            {
                return player switch
                {
                    MoveType.RedAeroWest => "(遅爆発 -> 先竜回収 -> 波西避け)",
                    MoveType.RedAeroEast => "(早爆発 -> 波東避け)",
                    _ => ""
                };
            }
            else // ＼
            {
                return player switch
                {
                    MoveType.RedAeroWest => "(先竜回収 -> 波西避け)",
                    MoveType.RedAeroEast => "(波南避け -> ダッシュ)",
                    _ => ""
                };
            }
        }

        return "";
    }

    private void HitDragonAndAero()
    {
        var infoTxt = "";
        foreach (var player in Enum.GetValues<MoveType>())
        {
            Direction? returnDirection = (_firstWaveDirection, _secondWaveDirection) switch
            {
                (Direction.North, Direction.East) => Direction.NorthEast,
                (Direction.East, Direction.South) => Direction.SouthEast,
                (Direction.South, Direction.West) => Direction.SouthWest,
                (Direction.West, Direction.North) => Direction.NorthWest,
                (Direction.North, Direction.West) => Direction.NorthWest,
                (Direction.West, Direction.South) => Direction.SouthWest,
                (Direction.South, Direction.East) => Direction.SouthEast,
                (Direction.East, Direction.North) => Direction.NorthEast,
                _ => null
            };

            var returnPosition = returnDirection switch
            {
                Direction.NorthEast => new Vector2(115, 85),
                Direction.SouthEast => new Vector2(115, 115),
                Direction.SouthWest => new Vector2(85, 115),
                Direction.NorthWest => new Vector2(85, 85),
                _ => new Vector2(100f, 85f)
            };

            Vector2? position = player switch
            {
                MoveType.RedBlizzardWest => returnPosition,
                MoveType.RedBlizzardEast => returnPosition,
                MoveType.RedAeroWest => (C.ReverseHitDragon ? EastDragon : WestDragon)?.Position.ToVector2() ?? new Vector2(87, 108),
                MoveType.RedAeroEast => (C.ReverseHitDragon ? WestDragon : EastDragon)?.Position.ToVector2() ?? new Vector2(113, 108),
                //MoveType.BlueBlizzard => new Vector2(100, 100),
                //MoveType.BlueHoly => new Vector2(100, 100),
                //MoveType.BlueWater => new Vector2(100, 100),
                //MoveType.BlueEruption => new Vector2(100, 100),
                _ => null
            };

            if (position != null)
            {
                position = SwapXIfNecessary(position.Value);
                if (Controller.TryGetElementByName(SwapIfNecessary(player), out var element))
                {
                    element.overlayText = "";
                    element.radius = 2f;
                    element.SetOffPosition(position.Value.ToVector3(0));
                }
            }
            var myMove = _players.SafeSelect(BasePlayer.GameObjectId)?.MoveType;

            if (player == myMove)
            {
                infoTxt = GetRedAeroInfoText(player);
            }
        }

        if (infoTxt == "") {
            var _player = (BasePlayer.Position.X < 100 ? MoveType.RedAeroWest : MoveType.RedAeroEast);
            infoTxt = GetRedAeroInfoText(_player);
        }

        //if (myMove is MoveType.RedAeroEast or MoveType.RedAeroWest)
        var remainingTime = SpellInWaitingDebuffTime - 8;
        Alert(C.HitDragonText.Get() + infoTxt + $" ({remainingTime:0.0}s)");
    }

    private string SwapIfNecessary(MoveType move)
    {
        if (_lateHourglassDirection is Direction.NorthEast or Direction.SouthWest)
            return move.ToString();
        return move switch
        {
            MoveType.RedBlizzardWest => MoveType.RedBlizzardEast.ToString(),
            MoveType.RedBlizzardEast => MoveType.RedBlizzardWest.ToString(),
            MoveType.RedAeroWest => MoveType.RedAeroEast.ToString(),
            MoveType.RedAeroEast => MoveType.RedAeroWest.ToString(),
            _ => move.ToString()
        };
    }

    private void CorrectCleanse()
    {
        var directionTxt = "";
        foreach (var player in Enum.GetValues<MoveType>())
        {
            var direction = Direction.West;
            if (C.PrioritizeMarker &&
                _players.FirstOrDefault(x => x.Value.PlayerName == BasePlayer.Name.ToString()).Value?.Marker is { } marker)
            {
                direction = marker switch
                {
                    MarkerType.Attack1 => C.WhenAttack1,
                    MarkerType.Attack2 => C.WhenAttack2,
                    MarkerType.Attack3 => C.WhenAttack3,
                    MarkerType.Attack4 => C.WhenAttack4,
                    _ => C.WhenAttack3
                };
                directionTxt = marker switch
                {
                    MarkerType.Attack1 => "(B)",
                    MarkerType.Attack2 => "(2)",
                    MarkerType.Attack3 => "(3)",
                    MarkerType.Attack4 => "(D)",
                    _ => "(マーカーなし)"
                };
            }
            else
            {
                if (player.Equals(C.WestSentence))
                    direction = Direction.West;
                else if (player.Equals(C.SouthWestSentence))
                    direction = Direction.SouthWest;
                else if (player.Equals(C.SouthEastSentence))
                    direction = Direction.SouthEast;
                else if (player.Equals(C.EastSentence))
                    direction = Direction.East;
            }

            if (C.PrioritizeMarker && string.IsNullOrEmpty(directionTxt))
                directionTxt = "(マーカーなし)";

            var cleanses = GetCleanses().ToArray();
            if (cleanses.Length >= 4)
            {
                var position = direction switch
                {
                    Direction.West      => cleanses[0],
                    Direction.SouthWest => cleanses[1],
                    Direction.SouthEast => cleanses[2],
                    Direction.East      => cleanses[3],
                    _                   => new Vector2(100, 100)
                };

                if (Controller.TryGetElementByName(SwapIfNecessary(player), out var element))
                {
                    element.radius = 0.2f;
                    element.SetOffPosition(position.ToVector3(0));
                    element.overlayText = C.CleansePosText.Get();
                    element.overlayFScale = 2f;
                    element.overlayVOffset = 1f;
                    element.tether = true;
                }

                if (C.ShowOtherCleanse && C.PrioritizeMarker)
                {
                    List<string> items = Enum.GetValues<MoveType>()
                                            .Select(e => e.ToString())
                                            .Where(name => name.StartsWith("Blue") && name != player.ToString())
                                            .ToList();
                    if (items.Count > 0 && direction != C.WhenAttack1)
                    {
                        var CleansePos = C.WhenAttack1 switch
                        {
                            Direction.West      => cleanses[0],
                            Direction.SouthWest => cleanses[1],
                            Direction.SouthEast => cleanses[2],
                            Direction.East      => cleanses[3],
                            _                   => new Vector2(100, 100)
                        };
                        if (Controller.TryGetElementByName(items[0], out var element1))
                        {
                            element1.radius = 2f;
                            element1.SetOffPosition(CleansePos.ToVector3(0));
                            element1.overlayText = "<< 1 >>";
                            element1.overlayFScale = 2f;
                            element1.overlayVOffset = 2f;
                            element1.tether = false;
                            element1.color = EColor.Red.ToUint();
                        }
                        items.RemoveAt(0);
                    }

                    if (items.Count > 0 && direction != C.WhenAttack2)
                    {
                        var CleansePos = C.WhenAttack2 switch
                        {
                            Direction.West      => cleanses[0],
                            Direction.SouthWest => cleanses[1],
                            Direction.SouthEast => cleanses[2],
                            Direction.East      => cleanses[3],
                            _                   => new Vector2(100, 100)
                        };
                        if (Controller.TryGetElementByName(items[0], out var element2))
                        {
                            element2.radius = 2f;
                            element2.SetOffPosition(CleansePos.ToVector3(0));
                            element2.overlayText = "<< 2 >>";
                            element2.overlayFScale = 2f;
                            element2.overlayVOffset = 2f;
                            element2.tether = false;
                            element2.color = EColor.Red.ToUint();
                        }
                        items.RemoveAt(0);
                    }

                    if (items.Count > 0 && direction != C.WhenAttack3)
                    {
                        var CleansePos = C.WhenAttack3 switch
                        {
                            Direction.West      => cleanses[0],
                            Direction.SouthWest => cleanses[1],
                            Direction.SouthEast => cleanses[2],
                            Direction.East      => cleanses[3],
                            _                   => new Vector2(100, 100)
                        };
                        if (Controller.TryGetElementByName(items[0], out var element3))
                        {
                            element3.radius = 2f;
                            element3.SetOffPosition(CleansePos.ToVector3(0));
                            element3.overlayText = "<< 3 >>";
                            element3.overlayFScale = 2f;
                            element3.overlayVOffset = 2f;
                            element3.tether = false;
                            element3.color = EColor.Red.ToUint();
                        }
                        items.RemoveAt(0);
                    }

                    if (items.Count > 0 && direction != C.WhenAttack4)
                    {
                        var CleansePos = C.WhenAttack4 switch
                        {
                            Direction.West      => cleanses[0],
                            Direction.SouthWest => cleanses[1],
                            Direction.SouthEast => cleanses[2],
                            Direction.East      => cleanses[3],
                            _                   => new Vector2(100, 100)
                        };
                        if (Controller.TryGetElementByName(items[0], out var element4))
                        {
                            element4.radius = 2f;
                            element4.SetOffPosition(CleansePos.ToVector3(0));
                            element4.overlayText = "<< 4 >>";
                            element4.overlayFScale = 2f;
                            element4.overlayVOffset = 2f;
                            element4.tether = false;
                            element4.color = EColor.Red.ToUint();
                        }
                        items.RemoveAt(0);
                    }
                }
            }
            else
            {
                var position = direction switch
                {
                    Direction.West      => new Vector2(92, 100),
                    Direction.SouthWest => new Vector2(91, 109),
                    Direction.SouthEast => new Vector2(109, 109),
                    Direction.East      => new Vector2(108, 100),
                    _                   => new Vector2(100, 100)
                };

                if (Controller.TryGetElementByName(SwapIfNecessary(player), out var element))
                {
                    element.radius = 0.1f;
                    element.SetOffPosition(position.ToVector3(0));
                    element.overlayText = C.CleansePosText.Get() + "?";
                    element.overlayFScale = 2f;
                    element.overlayVOffset = 1f;
                    element.tether = true;
                }
            }
        }

        if (BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Blue))
        {
            var remainingTime = SpellInWaitingDebuffTime > 0 ? SpellInWaitingDebuffTime : ReturnDebuffTime;
            Alert(C.CleanseText.Get() + directionTxt + $" ({remainingTime:0.0}s)");
        }
        else
        {
            HideAlert();
        }
    }

    private void PlaceReturn(bool discreet = false)
    {
        CheckCleanses();

        if (C.NukemaruRewind)
            NukemaruPlaceReturn(discreet);
        else if (C.KBIRewind)
            KBIPlaceReturn(discreet);
        else
            DefaultPlaceReturn(discreet);

        if (!discreet) {
            var directionTxt = "";
            if (BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Blue) && C.PrioritizeMarker){
                var marker = _players.FirstOrDefault(x => x.Value.PlayerName == BasePlayer.Name.ToString()).Value?.Marker;
                if (marker != null)
                {
                    directionTxt = marker switch
                    {
                        MarkerType.Attack1 => "(この後、B)",
                        MarkerType.Attack2 => "(この後、2)",
                        MarkerType.Attack3 => "(この後、3)",
                        MarkerType.Attack4 => "(この後、D)",
                        _ => "(マーカーなし)"
                    };
                } else {
                    directionTxt = "(マーカーなし)";
                }
            }

            var remainingTime = SpellInWaitingDebuffTime;
            Alert(C.PlaceReturnText.Get() + directionTxt + $" ({remainingTime:0.0}s)");
        }
    }

    private void KBIPlaceReturn(bool discreet = false)
    {
        var returnDirection = (_firstWaveDirection, _secondWaveDirection) switch
        {
            (Direction.North, Direction.East) => Direction.North,
            (Direction.East, Direction.South) => Direction.South,
            (Direction.South, Direction.West) => Direction.South,
            (Direction.West, Direction.North) => Direction.North,
            (Direction.North, Direction.West) => Direction.North,
            (Direction.West, Direction.South) => Direction.South,
            (Direction.South, Direction.East) => Direction.South,
            (Direction.East, Direction.North) => Direction.North,
            _ => throw new InvalidOperationException()
        };
        if (Controller.TryGetElementByName(WaveStack.West + nameof(WaveStack), out var myElement))
        {
            myElement.Enabled = true;
            myElement.tether = !discreet;
            myElement.color = GradientColor.Get(C.BaitColor1, C.BaitColor2).ToUint();
            myElement.SetOffPosition(Vector3.Zero);
            myElement.SetRefPosition(new Vector3(100, 0, 100 + (returnDirection == Direction.North ? -2 : 2)));
            myElement.overlayText = C.PlaceReturnPosText.Get();
            myElement.overlayFScale = 2f;
            myElement.overlayVOffset = 2f;
        }
    }

    private void NukemaruPlaceReturn(bool discreet = false)
    {
        var returnDirection = (_firstWaveDirection, _secondWaveDirection) switch
        {
            (Direction.North, Direction.East) => Direction.NorthEast,
            (Direction.East, Direction.South) => Direction.SouthEast,
            (Direction.South, Direction.West) => Direction.SouthWest,
            (Direction.West, Direction.North) => Direction.NorthWest,
            (Direction.North, Direction.West) => Direction.NorthWest,
            (Direction.West, Direction.South) => Direction.SouthWest,
            (Direction.South, Direction.East) => Direction.SouthEast,
            (Direction.East, Direction.North) => Direction.NorthEast,
            _ => throw new InvalidOperationException()
        };

        var basePosition = returnDirection switch
        {
            Direction.NorthEast => new Vector3(100, 0, 95),
            Direction.SouthEast => new Vector3(100, 0, 105),
            Direction.SouthWest => new Vector3(100, 0, 105),
            Direction.NorthWest => new Vector3(100, 0, 95),
            _ => throw new InvalidOperationException()
        };

        var direction = returnDirection switch
        {
            Direction.NorthEast => C.NukemaruRewindPositionWhenNorthEastWave,
            Direction.SouthEast => C.NukemaruRewindPositionWhenSouthEastWave,
            Direction.SouthWest => C.NukemaruRewindPositionWhenSouthWestWave,
            Direction.NorthWest => C.NukemaruRewindPositionWhenNorthWestWave,
            _ => throw new InvalidOperationException()
        };

        var position = basePosition +
                       MathHelper.RotateWorldPoint(Vector3.Zero, ((int)direction).DegreesToRadians(),
                           -Vector3.UnitZ * 3f);

        if (Controller.TryGetElementByName(WaveStack.West + nameof(WaveStack), out var myElement))
        {
            myElement.Enabled = true;
            myElement.tether = !discreet;
            myElement.color = GradientColor.Get(C.BaitColor1, C.BaitColor2).ToUint();
            myElement.SetOffPosition(Vector3.Zero);
            myElement.SetRefPosition(position);
            myElement.overlayText = C.PlaceReturnPosText.Get();
            myElement.overlayFScale = 2f;
            myElement.overlayVOffset = 2f;
        }
    }

    private void DefaultPlaceReturn(bool discreet = false)
    {
        var returnDirection = (_firstWaveDirection, _secondWaveDirection) switch
        {
            (Direction.North, Direction.East) => Direction.NorthEast,
            (Direction.East, Direction.South) => Direction.SouthEast,
            (Direction.South, Direction.West) => Direction.SouthWest,
            (Direction.West, Direction.North) => Direction.NorthWest,
            (Direction.North, Direction.West) => Direction.NorthWest,
            (Direction.West, Direction.South) => Direction.SouthWest,
            (Direction.South, Direction.East) => Direction.SouthEast,
            (Direction.East, Direction.North) => Direction.NorthEast,
            _ => throw new InvalidOperationException()
        };

        var basePosition = returnDirection switch
        {
            Direction.NorthEast => new Vector2(113, 87),
            Direction.SouthEast => new Vector2(113, 113),
            Direction.SouthWest => new Vector2(87, 113),
            Direction.NorthWest => new Vector2(87, 87),
            _ => throw new InvalidOperationException()
        };

        var isWest = returnDirection switch
        {
            Direction.NorthEast => C.IsWestWhenNorthEastWave,
            Direction.SouthEast => C.IsWestWhenSouthEastWave,
            Direction.SouthWest => C.IsWestWhenSouthWestWave,
            Direction.NorthWest => C.IsWestWhenNorthWestWave,
            _ => throw new InvalidOperationException()
        };

        var myStack = (isWest, C.IsTank) switch
        {
            (true, true) => WaveStack.WestTank,
            (false, true) => WaveStack.EastTank,
            (true, false) => WaveStack.West,
            (false, false) => WaveStack.East
        };

        var westTankPosition = basePosition;
        var eastTankPosition = basePosition;
        var westPosition = basePosition;
        var eastPosition = basePosition;

        switch (returnDirection)
        {
            case Direction.NorthEast:
                if (C.hamukatuRewind) {
                    westTankPosition += new Vector2(-0.5f, 0.5f);
                    eastTankPosition += new Vector2(-1f, 1f);
                } else {
                    westTankPosition += new Vector2(-3f, -0.5f);
                    eastTankPosition += new Vector2(0.5f, 3f);
                }
                westPosition += new Vector2(-8f, 4f);
                eastPosition += new Vector2(-4f, 8f);
                break;
            case Direction.SouthEast:
                if (C.hamukatuRewind) {
                    westTankPosition += new Vector2(-0.5f, -0.5f);
                    eastTankPosition += new Vector2(-1f, -1f);
                } else {
                    westTankPosition += new Vector2(-3f, 0.5f);
                    eastTankPosition += new Vector2(0.5f, -3f);
                }
                westPosition += new Vector2(-8f, -4f);
                eastPosition += new Vector2(-4f, -8f);
                break;
            case Direction.SouthWest:
                if (C.hamukatuRewind) {
                    westTankPosition += new Vector2(0.5f, -0.5f);
                    eastTankPosition += new Vector2(1f, -1f);
                } else {
                    westTankPosition += new Vector2(-0.5f, -3f);
                    eastTankPosition += new Vector2(3f, 0.5f);
                }
                westPosition += new Vector2(4f, -8f);
                eastPosition += new Vector2(8f, -4f);
                break;
            default:
                if (C.hamukatuRewind) {
                    westTankPosition += new Vector2(0.5f, 0.5f);
                    eastTankPosition += new Vector2(1f, 1f);
                } else {
                    westTankPosition += new Vector2(-0.5f, 3f);
                    eastTankPosition += new Vector2(3f, -0.5f);
                }
                westPosition += new Vector2(4f, 8f);
                eastPosition += new Vector2(8f, 4f);
                break;
        }

        foreach (var stack in Enum.GetValues<WaveStack>())
            if (Controller.TryGetElementByName(stack + nameof(WaveStack), out var element))
            {
                element.Enabled = C.ShowOtherReturn;
                element.radius = stack is WaveStack.WestTank or WaveStack.EastTank ? 0.5f : 1.2f;
                element.overlayText = "";
                element.SetOffPosition(stack switch
                {
                    WaveStack.WestTank => westTankPosition.ToVector3(0),
                    WaveStack.EastTank => eastTankPosition.ToVector3(0),
                    WaveStack.West => westPosition.ToVector3(0),
                    WaveStack.East => eastPosition.ToVector3(0),
                    _ => throw new InvalidOperationException()
                });
            }

        if (Controller.TryGetElementByName(myStack + nameof(WaveStack), out var myElement))
        {
            myElement.Enabled = true;
            myElement.tether = !discreet;
            myElement.color = GradientColor.Get(C.BaitColor1, C.BaitColor2).ToUint();
            myElement.overlayText = C.PlaceReturnPosText.Get();
            myElement.overlayFScale = 2f;
            myElement.overlayVOffset = 2f;
        }
    }

    private void Split()
    {
        Controller.GetRegisteredElements().Each(x => x.Value.Enabled = false);
        if (C.HighlightSplitPosition && Controller.TryGetElementByName("SplitPosition", out var myElement))
        {
            myElement.Enabled = true;
            myElement.tether = true;
            myElement.color = GradientColor.Get(C.BaitColor1, C.BaitColor2).ToUint();
            myElement.overlayText = "";
        }

        var remainingTime = ReturnDebuffTime;
        if (remainingTime >= 3.35) {
            Alert(C.SplitText.Get() + ($" ({(remainingTime - 3.35):0.0}s)"));
        } else {
            Alert(C.WaitText.Get() + ($" {remainingTime:0.0}s"));
        }
        
    }

    public override void OnSettingsDraw()
    {
        ImGuiEx.Text("TS Fork Version " + Metadata?.Version);
        if(Metadata?.Website != null && ImGui.Selectable("Read Me"))
        {
            GenericHelpers.ShellStart(Metadata.Website);
        }
        if (ImGuiEx.CollapsingHeader("General"))
        {
            ImGuiEx.Text("優先順位");
            ImGui.Indent();
            ImGui.Text("↑ 西");
            C.PriorityData.Draw();
            ImGui.Text("↓ 東");
            ImGui.Unindent();
            ImGui.Separator();

            ImGuiEx.EnumCombo("竜当たりタイミング", ref C.HitTiming);
            ImGui.Checkbox("自身から出現位置が遠い竜に当たる", ref C.ReverseHitDragon);
            ImGui.Checkbox("赤ブリで竜に当たった際に北に行くべきか", ref C.ShouldGoNorthRedBlizzard);
            ImGuiEx.HelpMarker(
                "赤ブリの際に、北に誰もいない場合、行先は南ではなく北に表示されます。");
            if (C.ShouldGoNorthRedBlizzard)
            {
                ImGui.Indent();
                ImGui.Checkbox("自動でスプリントを使用 ~1秒", ref C.UseSprintAuto);
                ImGui.Unindent();
            }

            ImGui.Checkbox("赤ブリ竜当たり後にアクションを自動使用", ref C.UseMitigationDragon);
            if (C.UseMitigationDragon)
            {
                ImGui.Indent();
                var actions = Ref<Dictionary<uint, string>>.Get(InternalData.FullName + "dragonMitigations",
                    () => Svc.Data.GetExcelSheet<Action>()
                        .Where(x => x.IsPlayerAction && x.ClassJobCategory.RowId != 0 && x.ActionCategory.RowId == 4)
                        .ToDictionary(x => x.RowId, x => x.Name.ExtractText()));
                ImGuiEx.Combo("アクションを選択", ref C.MitigationDragonAction, actions.Keys, names: actions);
                ImGui.Unindent();
            }

            ImGui.Separator();
            ImGuiEx.Text("白床の移動");
            ImGui.Indent();
            ImGui.Checkbox("マーカーによる優先度", ref C.PrioritizeMarker);
            if (C.PrioritizeMarker)
            {
                ImGui.Indent();
                ImGui.InputText("青デバフ時に実行するコマンド", ref C.CommandWhenBlueDebuff, 30);
                ImGui.Checkbox("ランダム待機", ref C.ShouldUseRandomWait);
                if (C.ShouldUseRandomWait)
                {
                    var minWait = C.WaitRange.X;
                    var maxWait = C.WaitRange.Y;
                    ImGui.SliderFloat2("待機範囲 (秒)", ref C.WaitRange, 0f, 14f, "%.1f");
                    if (Math.Abs(minWait - C.WaitRange.X) > 0.01f)
                    {
                        if (C.WaitRange.X > C.WaitRange.Y)
                            C.WaitRange.Y = C.WaitRange.X;
                    }
                    else if (Math.Abs(maxWait - C.WaitRange.Y) > 0.01f)
                    {
                        if (C.WaitRange.Y < C.WaitRange.X)
                            C.WaitRange.X = C.WaitRange.Y;
                    }
                }
                ImGui.Checkbox("頭割り処理後に自身にマーカーが無い場合、再度コマンドを実行", ref C.CommandWhenBlueDebuffAgain);
                ImGuiEx.HelpMarker("ランダム待機の時間が長すぎる場合、正常に動作しない可能性があります。");

                ImGui.Separator();
                ImGuiEx.EnumCombo("攻撃1の時", ref C.WhenAttack1);
                ImGuiEx.EnumCombo("攻撃2の時", ref C.WhenAttack2);
                ImGuiEx.EnumCombo("攻撃3の時", ref C.WhenAttack3);
                ImGuiEx.EnumCombo("攻撃4の時", ref C.WhenAttack4);
                ImGui.Unindent();
            }
            else
            {
                ImGuiEx.EnumCombo("D(4)の白床", ref C.WestSentence);
                ImGuiEx.EnumCombo("3の白床", ref C.SouthWestSentence);
                ImGuiEx.EnumCombo("2の白床", ref C.SouthEastSentence);
                ImGuiEx.EnumCombo("B(1)の白床", ref C.EastSentence);
                ImGui.Unindent();
            }

            ImGui.Separator();

            ImGui.Checkbox("リターン位置が北かつ担当位置が2または3の場合、テイカー散会時に白床を回収", ref C.LateSentence);

            ImGui.Checkbox("静的なスピリットテイカーの位置を強調表示", ref C.HighlightSplitPosition);

            if (C.HighlightSplitPosition)
                if (Controller.TryGetElementByName("SplitPosition", out var element))
                {
                    ImGuiEx.TextWrapped(EColor.RedBright, "登録済み要素セクションに移動し、「SplitPosition」要素を配置したい場所に配置してください。必要に応じてE12S(制限解除)に行ってプレビューを確認してください。");

                    ImGui.Indent();
                    ImGui.Text($"位置:{element.refX}, {element.refY}");
                    ImGuiEx.EnumCombo("方向を編集", ref _editSplitElementDirection);
                    ImGui.InputFloat("半径を編集", ref _editSplitElementRadius, 0.1f);
                    if (ImGui.Button("設定"))
                    {
                        var position = new Vector3(100, 0, 100) + MathHelper.RotateWorldPoint(Vector3.Zero,
                            ((int)_editSplitElementDirection).DegreesToRadians(),
                            -Vector3.UnitZ * _editSplitElementRadius);
                        element.SetRefPosition(position);
                    }

                    ImGui.Unindent();
                }

            ImGui.Separator();

            ImGuiEx.Text("リターンを配置");
            ImGui.Indent();

            ImGui.SliderFloat("位置を強制表示する残り時間", ref C.ReturnShowTime, 0, 12);

            var kbiRewind = C.KBIRewind;
            var nukemaruRewind = C.NukemaruRewind;
            ImGui.Checkbox("ノックバック無効のリターン位置 (ベータ)", ref kbiRewind);
            ImGui.Checkbox("ぬけまるのリターン位置", ref nukemaruRewind);
            ImGui.Checkbox("ハムカツのタンクリターン位置", ref C.hamukatuRewind);

            if (!C.KBIRewind && kbiRewind)
                nukemaruRewind = false;
            else if (!C.NukemaruRewind && nukemaruRewind) kbiRewind = false;

            C.KBIRewind = kbiRewind;
            C.NukemaruRewind = nukemaruRewind;

            if (C.NukemaruRewind)
            {
                ImGui.Indent();
                ImGuiEx.EnumCombo("北東(1)の波の時", ref C.NukemaruRewindPositionWhenNorthEastWave);
                ImGuiEx.EnumCombo("南東(2)の波の時", ref C.NukemaruRewindPositionWhenSouthEastWave);
                ImGuiEx.EnumCombo("南西(3)の波の時", ref C.NukemaruRewindPositionWhenSouthWestWave);
                ImGuiEx.EnumCombo("北西(4)の波の時", ref C.NukemaruRewindPositionWhenNorthWestWave);
                ImGui.Unindent();
            }

            if (C is { KBIRewind: false, NukemaruRewind: false })
            {
                ImGui.Checkbox("タンク?", ref C.IsTank);

                if (C.IsTank && C.hamukatuRewind)
                {
                    ImGui.Text("北東(1)の波の時:");
                    ImGui.SameLine();
                    ImGuiEx.RadioButtonBool($"前##{nameof(C.IsWestWhenNorthEastWave)}",
                        $"後##{nameof(C.IsWestWhenNorthEastWave)}", ref C.IsWestWhenNorthEastWave, true);
                    ImGui.Text("南東(2)の波の時:");
                    ImGui.SameLine();
                    ImGuiEx.RadioButtonBool($"前##{nameof(C.IsWestWhenSouthEastWave)}",
                        $"後##{nameof(C.IsWestWhenSouthEastWave)}", ref C.IsWestWhenSouthEastWave, true);
                    ImGui.Text("南西(3)の波の時:");
                    ImGui.SameLine();
                    ImGuiEx.RadioButtonBool($"前##{nameof(C.IsWestWhenSouthWestWave)}",
                        $"後##{nameof(C.IsWestWhenSouthWestWave)}", ref C.IsWestWhenSouthWestWave, true);
                    ImGui.Text("北西(4)の波の時:");
                    ImGui.SameLine();
                    ImGuiEx.RadioButtonBool($"前##{nameof(C.IsWestWhenNorthWestWave)}",
                        $"後##{nameof(C.IsWestWhenNorthWestWave)}", ref C.IsWestWhenNorthWestWave, true);
                }
                else
                {
                    ImGuiEx.Text("()内は外周を前とした際の方向");
                    ImGui.Text("北東(1)の波の時:");
                    ImGui.SameLine();
                    ImGuiEx.RadioButtonBool($"西(左)##{nameof(C.IsWestWhenNorthEastWave)}",
                        $"東(右)##{nameof(C.IsWestWhenNorthEastWave)}", ref C.IsWestWhenNorthEastWave, true);
                    ImGui.Text("南東(2)の波の時:");
                    ImGui.SameLine();
                    ImGuiEx.RadioButtonBool($"西(右)##{nameof(C.IsWestWhenSouthEastWave)}",
                        $"東(左)##{nameof(C.IsWestWhenSouthEastWave)}", ref C.IsWestWhenSouthEastWave, true);
                    ImGui.Text("南西(3)の波の時:");
                    ImGui.SameLine();
                    ImGuiEx.RadioButtonBool($"西(右)##{nameof(C.IsWestWhenSouthWestWave)}",
                        $"東(左)##{nameof(C.IsWestWhenSouthWestWave)}", ref C.IsWestWhenSouthWestWave, true);
                    ImGui.Text("北西(4)の波の時:");
                    ImGui.SameLine();
                    ImGuiEx.RadioButtonBool($"西(左)##{nameof(C.IsWestWhenNorthWestWave)}",
                        $"東(右)##{nameof(C.IsWestWhenNorthWestWave)}", ref C.IsWestWhenNorthWestWave, true);
                }
            }

            ImGui.Unindent();

            ImGui.Separator();

            ImGui.Text("ダイアログテキスト:");
            ImGui.Indent();
            var splitText = C.SplitText.Get();
            ImGui.Text("散開テキスト:");
            ImGui.SameLine();
            C.SplitText.ImGuiEdit(ref splitText);

            var waitText = C.WaitText.Get();
            ImGui.Text("発動待ちテキスト:");
            ImGui.SameLine();
            C.WaitText.ImGuiEdit(ref waitText);

            var hitDragonText = C.HitDragonText.Get();
            ImGui.Text("竜当たりテキスト:");
            ImGui.SameLine();
            C.HitDragonText.ImGuiEdit(ref hitDragonText);

            var avoidWaveText = C.AvoidWaveText.Get();
            ImGui.Text("波回避テキスト:");
            ImGui.SameLine();
            C.AvoidWaveText.ImGuiEdit(ref avoidWaveText);

            var cleanseText = C.CleanseText.Get();
            ImGui.Text("白床テキスト:");
            ImGui.SameLine();
            C.CleanseText.ImGuiEdit(ref cleanseText);

            var placeReturnText = C.PlaceReturnText.Get();
            ImGui.Text("リターン配置テキスト:");
            ImGui.SameLine();
            C.PlaceReturnText.ImGuiEdit(ref placeReturnText);

            var cleansePosText = C.CleansePosText.Get();
            ImGui.Text("白床表示テキスト:");
            ImGui.SameLine();
            C.CleansePosText.ImGuiEdit(ref cleansePosText);

            var placeReturnPosText = C.PlaceReturnPosText.Get();
            ImGui.Text("リターン位置表示テキスト:");
            ImGui.SameLine();
            C.PlaceReturnPosText.ImGuiEdit(ref placeReturnPosText);

            ImGui.Unindent();

            ImGui.Separator();
            ImGui.Text("誘導色:");
            ImGuiComponents.HelpMarker(
                "誘導と表示されるテキストの色を変更します。\n異なる値を設定すると虹色になります。");
            ImGui.Indent();
            ImGui.ColorEdit4("色1", ref C.BaitColor1, ImGuiColorEditFlags.NoInputs);
            ImGui.SameLine();
            ImGui.ColorEdit4("色2", ref C.BaitColor2, ImGuiColorEditFlags.NoInputs);
            ImGui.Unindent();

            ImGui.Separator();
            ImGui.Checkbox("リターンの2秒前にノックバック無効を自動使用", ref C.UseKbiAuto);
            ImGui.Checkbox("リターンの4秒前に軽減を自動使用", ref C.UseMitigation);
            if (C.UseMitigation)
            {
                ImGui.Indent();
                var actions = Ref<Dictionary<uint, string>>.Get(InternalData.FullName + "mitigations",
                    () => Svc.Data.GetExcelSheet<Action>()
                        .Where(x => x.IsPlayerAction && x.ClassJobCategory.RowId != 0 && x.ActionCategory.RowId == 4)
                        .ToDictionary(x => x.RowId, x => x.Name.ExtractText()));
                ImGuiEx.Combo("アクションを選択", ref C.MitigationAction, actions.Keys, names: actions);
                ImGui.Unindent();
            }

            ImGui.Checkbox("リターンの4秒前にタンク軽減を自動使用",
                ref C.UseTankMitigation);
            if (C.UseTankMitigation)
            {
                ImGui.Indent();
                var actions = Ref<Dictionary<uint, string>>.Get(InternalData.FullName + "tankMitigations",
                    () => Svc.Data.GetExcelSheet<Action>()
                        .Where(x => x.IsPlayerAction &&
                                    (x.ClassJobCategory.Value.DRK || x.ClassJobCategory.Value.WAR ||
                                    x.ClassJobCategory.Value.PLD || x.ClassJobCategory.Value.GNB) &&
                                    x.ActionCategory.RowId == 4)
                        .ToDictionary(x => x.RowId, x => x.Name.ExtractText()));
                ImGuiEx.Combo("タンクアクションを選択", ref C.TankMitigationAction, actions.Keys, names: actions);
                ImGui.Unindent();
            }

            ImGui.Separator();

            ImGui.Checkbox("他人を表示（動作）", ref C.ShowOther);
            ImGui.Checkbox("他人を表示（リターン）", ref C.ShowOtherReturn);
            ImGui.Checkbox("他人を表示（白床）", ref C.ShowOtherCleanse);

            if (ImGui.CollapsingHeader("優先リスト"))
            {
                if (C.PriorityData != null)
                {
                    var players = C.PriorityData.GetPlayers(x => true);
                    if (players != null) {
                        ImGuiEx.Text(players.Select(x => x.NameWithWorld).Print("\n"));
                        var redPlayers = C.PriorityData.GetPlayers(x => _players.First(y => y.Value.PlayerName == x.Name).Value is
                        { Color: Debuff.Red, Debuff: Debuff.Blizzard });
                        if (redPlayers != null)
                        {
                            ImGui.Separator();
                            ImGuiEx.Text("赤ブリザード:");
                            ImGuiEx.Text(redPlayers.Select(x => x.NameWithWorld).Print("\n"));
                        } else {
                            ImGui.Separator();
                            ImGuiEx.Text("赤ブリザードの優先リスト取得に失敗");
                        }
                        var aeroPlayers = C.PriorityData.GetPlayers(x => _players.First(y => y.Value.PlayerName == x.Name).Value is
                        { Color: Debuff.Red, Debuff: Debuff.Aero });
                        if (aeroPlayers != null)
                        {
                            ImGui.Separator();
                            ImGuiEx.Text("赤エアロ:");
                            ImGuiEx.Text(aeroPlayers.Select(x => x.NameWithWorld).Print("\n"));
                        } else {
                            ImGui.Separator();
                            ImGuiEx.Text("赤エアロの優先リスト取得に失敗");
                        }
                    } else {
                        ImGuiEx.Text("優先リストのプレイヤー取得に失敗");
                    }
                } else {
                    ImGuiEx.Text("優先リストの読込に失敗");
                }
            }
        }
        if (ImGuiEx.CollapsingHeader("Debug"))
        {
            ImGuiEx.Text($"Stage: {GetStage()}, remaining time = {SpellInWaitingDebuffTime}");
            ImGui.SetNextItemWidth(200);
            ImGui.InputText("Player override", ref _basePlayerOverride, 50);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            if (ImGui.BeginCombo("Select..", "Select..."))
            {
                foreach (var x in Svc.Objects.OfType<IPlayerCharacter>())
                    if (ImGui.Selectable(x.GetNameWithWorld()))
                        _basePlayerOverride = x.Name.ToString();
                ImGui.EndCombo();
            }

            ImGui.Text($"Base Direction: {_baseDirection.ToString()}");
            ImGui.Text($"Late Hourglass Direction: {_lateHourglassDirection.ToString()}");
            ImGui.Text($"First Wave Direction: {_firstWaveDirection.ToString()}");
            ImGui.Text($"Second Wave Direction: {_secondWaveDirection.ToString()}");
            
            if (_firstWaveDirection != null) {
                ImGui.Text($"RedAero West Info: {GetRedAeroInfoText(MoveType.RedAeroWest)}");
                ImGui.Text($"RedAero East Info: {GetRedAeroInfoText(MoveType.RedAeroEast)}");
            } else {
                ImGui.Text($"RedAero West Info: ");
                ImGui.Text($"RedAero East Info: ");
            }
            
            if (_cachedCleanses != null) {
                ImGui.Text($"Cleanses: {string.Join(", ", _cachedCleanses.Select(pos => $"[{pos.X:F2}, {pos.Y:F2}]"))}");
            } else {
                ImGui.Text($"Cleanses: ");
            }

            ImGuiEx.EzTable("Player Data", _players.SelectMany(x => new ImGuiEx.EzTableEntry[]
            {
                new("Player Name", () => ImGuiEx.Text(x.Value.PlayerName)),
                new("Color", () => ImGuiEx.Text(x.Value.Color.ToString())),
                new("Debuff", () => ImGuiEx.Text(x.Value.Debuff.ToString())),
                new("Has Quietus", () => ImGuiEx.Text(x.Value.HasQuietus.ToString())),
                new("Marker", () => ImGuiEx.Text(x.Value.Marker.ToString())),
                new("Move Type", () => ImGuiEx.Text(x.Value.MoveType.ToString()))
            }));

            ImGuiEx.EnumCombo("First Wave Direction", ref _debugDirection1);
            ImGuiEx.EnumCombo("Second Wave Direction", ref _debugDirection2);
            if (ImGui.Button("Show Return Placement"))
            {
                _firstWaveDirection = _debugDirection1;
                _secondWaveDirection = _debugDirection2;
            }
        }
    }

    public override void OnActorControl(uint sourceId, uint command, uint p1, uint p2, uint p3, uint p4, uint p5,
        uint p6, ulong targetId,
        byte replaying)
    {
        if (GetStage() == MechanicStage.Unknown) return;
        if (command == 502)
            try
            {
                _players[p2].Marker = (MarkerType)p1;
            }
            catch
            {
                PluginLog.Warning($"GameObjectId:{p2} was not found");
            }
    }

    private Vector2? ResolveRedAeroMove()
    {
        if (_players.SafeSelect(BasePlayer.GameObjectId)?.MoveType?
                .EqualsAny(MoveType.RedAeroEast, MoveType.RedAeroWest) != true) return null;
        var isPlayerWest = _players.SafeSelect(BasePlayer.GameObjectId)?.MoveType == MoveType.RedAeroWest;
        var isLateHourglassSameSide =
            _lateHourglassDirection is Direction.NorthEast or Direction.SouthWest == isPlayerWest;
        var stage = GetStage();
        switch (stage)
        {
            case MechanicStage.Step1_Spread:
                return MirrorX(RedAeroEastMovements.Step1_InitialDodge, isPlayerWest);
            case MechanicStage.Step2_FirstHourglass when isLateHourglassSameSide:
            {
                if (BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Aero))
                    return MirrorX(RedAeroEastMovements.Step2_KnockPlayers, isPlayerWest);

                Alert(C.HitDragonText.Get());
                return (isPlayerWest ^ C.ReverseHitDragon ? WestDragon : EastDragon)?.Position.ToVector2();
            }
            case MechanicStage.Step2_FirstHourglass:
                return MirrorX(RedAeroEastMovements.Step3_DodgeSecondHourglass, isPlayerWest);
            case MechanicStage.Step3_IcesAndWinds when isLateHourglassSameSide:
            {
                if (BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Red))
                {
                    Alert(C.HitDragonText.Get());
                    return (isPlayerWest ^ C.ReverseHitDragon ? WestDragon : EastDragon)?.Position.ToVector2();
                }

                return MirrorX(RedAeroEastMovements.Step3_DodgeSecondHourglass, isPlayerWest);
            }
            case MechanicStage.Step3_IcesAndWinds:
                return MirrorX(RedAeroEastMovements.Step3_DodgeSecondHourglass, isPlayerWest);
            case MechanicStage.Step4_SecondHourglass when isLateHourglassSameSide:
            {
                if (BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Red))
                {
                    Alert(C.HitDragonText.Get());
                    return (isPlayerWest ^ C.ReverseHitDragon ? WestDragon : EastDragon)?.Position.ToVector2();
                }

                return MirrorX(RedAeroEastMovements.Step3_DodgeSecondHourglass, isPlayerWest);
            }
            case MechanicStage.Step4_SecondHourglass:
                return MirrorX(RedAeroEastMovements.Step3_DodgeSecondHourglass, isPlayerWest);
            case MechanicStage.Step5_PerformDodges when BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Red):
                Alert(C.HitDragonText.Get());
                return (isPlayerWest ^ C.ReverseHitDragon ? WestDragon : EastDragon)?.Position.ToVector2();
            case MechanicStage.Step5_PerformDodges:
                return MirrorX(RedAeroEastMovements.Step3_DodgeSecondHourglass, isPlayerWest);
            default:
                return null;
        }
    }

    private Vector2? ResolveRedBlizzardMove()
    {
        if (_players.SafeSelect(BasePlayer.GameObjectId)?.MoveType?.EqualsAny(MoveType.RedBlizzardWest,
                MoveType.RedBlizzardEast) != true) return null;
        var isPlayerWest = _players.SafeSelect(BasePlayer.GameObjectId)?.MoveType == MoveType.RedBlizzardWest;
        var isLateHourglassSameSide =
            (_lateHourglassDirection == Direction.NorthEast || _lateHourglassDirection == Direction.SouthWest) ==
            isPlayerWest;
        var stage = GetStage();
        if (stage <= MechanicStage.Step5_PerformDodges)
        {
            if (BasePlayer.StatusList.Any(x => x.StatusId == (uint)Debuff.Red)) return null;
            if (isLateHourglassSameSide)
            {
                if (stage <= MechanicStage.Step4_SecondHourglass && !C.ShouldGoNorthRedBlizzard)
                    return MirrorX(new Vector2(119, 103), isPlayerWest);
                return MirrorX(new Vector2(105, 82), isPlayerWest);
            }

            return MirrorX(new Vector2(105, 82), isPlayerWest);
        }

        return null;
    }

    private static Vector2 MirrorX(Vector2 x, bool mirror)
    {
        if (mirror)
            return x with { X = 100f - Math.Abs(x.X - 100f) };
        return x;
    }

    private enum Debuff : uint
    {
        Red = 0xCBF,
        Blue = 0xCC0,
        Holy = 0x996,
        Eruption = 0x99C,
        Water = 0x99D,
        Blizzard = 0x99E,
        Aero = 0x99F,
        Quietus = 0x104E,
        DelayReturn = 0x1070,
        Return = 0x994
    }


    private enum HitTiming
    {
        Early,
        Late
    }

    private enum MarkerType : uint
    {
        Attack1 = 0,
        Attack2 = 1,
        Attack3 = 2,
        Attack4 = 3
    }

    private enum MoveType
    {
        RedBlizzardWest,
        RedBlizzardEast,
        RedAeroWest,
        RedAeroEast,
        BlueBlizzard,
        BlueHoly,
        BlueWater,
        BlueEruption
    }

    private enum WaveStack
    {
        WestTank,
        EastTank,
        West,
        East
    }

    private enum MechanicStage
    {
        Unknown,

        /// <summary>
        ///     Tethers appear, red winds and red ices go to their designated positions, eruption goes front, other blues go back
        /// </summary>
        Step1_Spread,

        /// <summary>
        ///     First set of hourglass goes off, winds go to their positions, ice prepares to pop dragon heads, and blue people in
        ///     back go to winds to be knocked
        /// </summary>
        Step2_FirstHourglass,

        /// <summary>
        ///     Winds and ices now went off. Party in back gets knocked to front; ices must now dodge hourglasses and rejoin the
        ///     group in front, while winds must prepare to pop their dragon heads.
        /// </summary>
        Step3_IcesAndWinds,

        /// <summary>
        ///     Second set of hourglass goes off. Winds must immediately intercept dragon heads if early pop is selected, otherwise
        ///     they wait for third set of hourglass at south.
        /// </summary>
        Step4_SecondHourglass,

        /// <summary>
        ///     Stack in front now resolved, and blue people can perform their dodges.
        /// </summary>
        Step5_PerformDodges,

        /// <summary>
        ///     Third set of hourglass goes off. Blue people must cleanse now. Red already prepares to drop their rewinds, and once
        ///     blues cleanse, they too prepare to drop their rewinds.
        /// </summary>
        Step6_ThirdHourglass,

        /// <summary>
        ///     Players must now spread for spirit taker bait, press mitigations and kb immunity appropriately if needed
        /// </summary>
        Step7_SpiritTaker
    }


    private record PlayerData
    {
        public Debuff? Color;
        public Debuff? Debuff;
        public bool HasQuietus;
        public MarkerType? Marker;
        public MoveType? MoveType;
        public string PlayerName;

        public bool HasDebuff => Debuff != null && Color != null;
    }

    private static class RedAeroEastMovements
    {
        public static Vector2 Step1_InitialDodge = new(112, 115);
        public static Vector2 Step2_KnockPlayers = new(109.9f, 117); //only when purple hourglass on our side
        public static Vector2 Step3_DodgeSecondHourglass = new(107.8f, 117.9f);
        public static Vector2 Step4_DodgeExa = new(100, 117);
    }

    private class Config : IEzConfig
    {
        public InternationalString AvoidWaveText = new() { En = "Avoid Wave", Jp = "波をよけろ！" };
        public Vector4 BaitColor1 = 0xFFFF00FF.ToVector4();
        public Vector4 BaitColor2 = 0xFFFFFF00.ToVector4();
        public InternationalString CleanseText = new() { En = "Get Cleanse", Jp = "白を取れ！" };
        public InternationalString CleansePosText = new() { En = "<< Go Here >>", Jp = "<< 白床位置 >>" };
        public string CommandWhenBlueDebuff = "";
        public bool CommandWhenBlueDebuffAgain = false;
        public MoveType EastSentence = MoveType.BlueBlizzard;

        public bool HighlightSplitPosition;

        public InternationalString HitDragonText = new() { En = "Hit Dragon", Jp = "竜に当たれ！" };

        public HitTiming HitTiming = HitTiming.Late;

        public bool ReverseHitDragon = false;

        public bool IsTank;
        public bool IsWestWhenNorthEastWave;
        public bool IsWestWhenNorthWestWave;
        public bool IsWestWhenSouthEastWave;
        public bool IsWestWhenSouthWestWave;

        public bool KBIRewind;
        public uint MitigationAction;
        public uint MitigationDragonAction;

        public bool NoWindWait = false;
        public bool NukemaruRewind;
        public bool hamukatuRewind = false;

        public Direction NukemaruRewindPositionWhenNorthEastWave = Direction.North;
        public Direction NukemaruRewindPositionWhenNorthWestWave = Direction.North;
        public Direction NukemaruRewindPositionWhenSouthEastWave = Direction.South;
        public Direction NukemaruRewindPositionWhenSouthWestWave = Direction.South;
        public InternationalString PlaceReturnText = new() { En = "Place Return", Jp = "リターンを置け！" };
        public InternationalString PlaceReturnPosText = new() { En = "<< Go Here >>", Jp = "<< 設置位置 >>" };

        public bool PrioritizeMarker;

        public PriorityData PriorityData = new();

        public bool ShouldGoNorthRedBlizzard;

        public bool ShouldUseRandomWait = true;

        public bool LateSentence = false;

        public bool ShowOther;
        public bool ShowOtherReturn;
        public bool ShowOtherCleanse;
        public MoveType SouthEastSentence = MoveType.BlueHoly;
        public MoveType SouthWestSentence = MoveType.BlueWater;
        public InternationalString SplitText = new() { En = "Split", Jp = "散開！" };
        public InternationalString WaitText = new() { En = "Wait", Jp = "リターン発動まで" };
        public uint TankMitigationAction;
        public bool UseKbiAuto;
        public bool UseMitigation;
        public bool UseMitigationDragon;
        public bool UseSprintAuto;
        public bool UseTankMitigation;
        public Vector2 WaitRange = new(0.5f, 1.5f);
        public MoveType WestSentence = MoveType.BlueEruption;
        public Direction WhenAttack1 = Direction.East;
        public Direction WhenAttack2 = Direction.SouthEast;
        public Direction WhenAttack3 = Direction.SouthWest;
        public Direction WhenAttack4 = Direction.West;
        public float ReturnShowTime = 4.0f;
    }
}