using System;
using System.Collections.Generic;
using Godot;
using Jubuddy.Core;

namespace Jubuddy.App.Buddy;

/// <summary>
/// Liga o "cérebro" (máquina de estados, puro C#) ao corpo desenhado: segue o caminho,
/// senta/deita, varre andando, acena quando o modo quarto abre, e "avança no tempo" depois de uma pausa.
/// </summary>
public partial class BuddyDirector : Node
{
    private enum Stage { Walking, PickingBook, Settling, Doing, Waving }

    public BuddyBrain Brain { get; private set; } = null!;
    private Room _room = null!;
    private BuddyBody _body = null!;
    private readonly Random _rng = new();

    private Stage _stage = Stage.Doing;
    private Stage _beforeWave;
    private readonly List<Vec2> _path = new();
    private int _pathIndex;
    private bool _toVia;
    private float _timer;
    private Vector3 _settleFrom;
    private bool _sweepMoving;
    private float _sweepPause;
    private float _validateIn = 1f;
    private int _seenRevision;

    /// <summary>Em movimento (pede mais quadros por segundo).</summary>
    public bool IsMoving => _stage is Stage.Walking or Stage.Settling or Stage.Waving or Stage.PickingBook
                            || (_stage == Stage.Doing && Brain.Plan.Activity == Activity.Sweep);

    public bool IsSleeping => _stage == Stage.Doing && Brain.Plan.Activity == Activity.Sleep;

    public string Describe() => _stage switch
    {
        Stage.Walking => $"andando → {ActivityName(Brain.Plan.Activity)}",
        Stage.Waving => "acenando",
        Stage.PickingBook => "pegando um livro",
        _ => ActivityName(Brain.Plan.Activity),
    };

    public static string ActivityName(Activity a) => a switch
    {
        Activity.Idle => "à toa",
        Activity.Wander => "passeando",
        Activity.Sweep => "varrendo",
        Activity.Read => "lendo",
        Activity.UsePc => "no PC",
        Activity.Sit => "sentado",
        Activity.Sleep => "dormindo",
        _ => a.ToString(),
    };

    public void Init(Room room, BuddyBody body, Activity? forced)
    {
        _room = room;
        _body = body;
        var nav = new NavGrid();
        nav.Build(room);
        var start = nav.NearestFree(new Vec2(0.6f, 0.8f)) ?? Vec2.Zero;
        Brain = new BuddyBrain(new Random(), nav, start, DateTime.Now) { Forced = forced };
        _body.Position = new Vector3(start.X, 0, start.Z);
        _body.TargetYaw = Mathf.Pi / 4;
        _seenRevision = room.Revision;

        // Ao abrir, já está no meio de alguma coisa (como se o tempo estivesse passando).
        Brain.FastForward(room, DateTime.Now);
        TeleportToPlan();
    }

    public override void _Process(double delta)
    {
        var dt = (float)Math.Min(delta, 0.25);
        var now = DateTime.Now;

        if (_room.Revision != _seenRevision)
        {
            // Algo mudou no quarto (modo quarto): repensa o que fazer, a partir de onde está.
            _seenRevision = _room.Revision;
            Brain.Nav.Sync(_room);
            if (_stage != Stage.Waving)
            {
                Brain.Begin(Brain.Decide(_room, now), now);
                StartPlan();
            }
        }

        _validateIn -= dt;
        if (_validateIn <= 0)
        {
            _validateIn = 1f;
            if (_stage != Stage.Waving && Brain.Validate(_room, now)) StartPlan();
        }

        switch (_stage)
        {
            case Stage.Waving:
                _timer -= dt;
                if (_timer <= 0)
                {
                    _stage = _beforeWave;
                    _body.Anim = _stage == Stage.Doing ? AnimFor(Brain.Plan.Activity) : BuddyAnim.Walk;
                    if (_stage == Stage.Doing && Brain.Plan.Facing is { } f) _body.TargetYaw = f;
                }
                break;

            case Stage.Walking:
                if (Follow(dt, BuddyBrain.WalkSpeed))
                {
                    if (_toVia)
                    {
                        _stage = Stage.PickingBook;
                        _timer = 1.4f;
                        _body.Anim = BuddyAnim.PickBook;
                        var shelf = Brain.Plan.ViaItemId != null ? _room.Find(Brain.Plan.ViaItemId) : null;
                        if (shelf != null) _body.TargetYaw = Room.YawOf(shelf) + Mathf.Pi;
                    }
                    else BeginSettle();
                }
                break;

            case Stage.PickingBook:
                _timer -= dt;
                if (_timer <= 0)
                {
                    _toVia = false;
                    SetPath(Brain.Plan.Target);
                    _stage = Stage.Walking;
                    _body.Anim = BuddyAnim.Walk;
                }
                break;

            case Stage.Settling:
            {
                _timer += dt;
                var k = Mathf.Clamp(_timer / 0.45f, 0f, 1f);
                var t = Brain.Plan.Target;
                _body.Position = _settleFrom.Lerp(new Vector3(t.X, 0, t.Z), k);
                if (k >= 1f)
                {
                    Brain.Arrived(now);
                    _stage = Stage.Doing;
                    _body.Anim = AnimFor(Brain.Plan.Activity);
                    _sweepMoving = false;
                    _sweepPause = 0.5f;
                }
                break;
            }

            case Stage.Doing:
                if (Brain.Plan.Activity == Activity.Sweep) SweepAround(dt);
                if (Brain.Tick(_room, now)) StartPlan();
                break;
        }
    }

    private void StartPlan()
    {
        var plan = Brain.Plan;
        _body.Posture = Posture.Stand;
        _body.SeatHeight = 0;
        _body.Anim = BuddyAnim.Walk;
        _body.WalkSpeedFactor = 1f;
        _toVia = plan.Via != null;
        SetPath(_toVia ? plan.Via!.Value : plan.Target);
        _stage = Stage.Walking;
    }

    private void SetPath(Vec2 to)
    {
        _path.Clear();
        _path.AddRange(Brain.Nav.FindPath(Current(), to));
        _pathIndex = 0;
    }

    private Vec2 Current() => new(_body.Position.X, _body.Position.Z);

    /// <summary>Anda pelo caminho. Retorna true ao chegar.</summary>
    private bool Follow(float dt, float speed)
    {
        if (_pathIndex >= _path.Count) return true;
        var pos = Current();
        var target = _path[_pathIndex];
        var to = target - pos;
        var dist = to.Length;
        var step = speed * dt;
        if (dist <= step || dist < 0.01f)
        {
            pos = target;
            _pathIndex++;
        }
        else
        {
            pos += to * (step / dist);
            _body.TargetYaw = MathF.Atan2(to.X, to.Z);
        }
        _body.Position = new Vector3(pos.X, 0, pos.Z);
        Brain.Position = pos;
        return _pathIndex >= _path.Count;
    }

    private void BeginSettle()
    {
        var plan = Brain.Plan;
        _settleFrom = _body.Position;
        _timer = 0;
        _stage = Stage.Settling;
        _body.Posture = plan.Posture;
        _body.SeatHeight = plan.SeatHeight;
        _body.Anim = plan.Posture == Posture.Stand ? AnimFor(plan.Activity) : BuddyAnim.Idle;
        if (plan.Facing is { } f) _body.TargetYaw = f;
    }

    private void SweepAround(float dt)
    {
        if (_sweepMoving)
        {
            if (Follow(dt, 0.35f))
            {
                _sweepMoving = false;
                _sweepPause = 0.4f + (float)_rng.NextDouble();
            }
            return;
        }
        _sweepPause -= dt;
        if (_sweepPause > 0) return;
        var next = Brain.Nav.RandomFree(_rng, Current(), 1.3f);
        if (next == null) { _sweepPause = 2f; return; }
        SetPath(next.Value);
        _body.WalkSpeedFactor = 0.8f;
        _sweepMoving = true;
    }

    private static BuddyAnim AnimFor(Activity a) => a switch
    {
        Activity.Sweep => BuddyAnim.Sweep,
        Activity.Read => BuddyAnim.Read,
        Activity.UsePc => BuddyAnim.Type,
        Activity.Sit => BuddyAnim.Sit,
        Activity.Sleep => BuddyAnim.Sleep,
        _ => BuddyAnim.Idle,
    };

    /// <summary>Põe o corpo direto no estado do plano (sem andar). Usado ao voltar de uma pausa.</summary>
    private void TeleportToPlan()
    {
        var plan = Brain.Plan;
        _path.Clear();
        _body.Position = new Vector3(plan.Target.X, 0, plan.Target.Z);
        _body.Posture = plan.Posture;
        _body.SeatHeight = plan.SeatHeight;
        _body.Anim = AnimFor(plan.Activity);
        _body.TargetYaw = plan.Facing ?? _body.TargetYaw;
        _sweepMoving = false;
        _stage = Stage.Doing;
        _body.SnapPose();
    }

    /// <summary>Depois de um tempo sem desenhar: encadeia as atividades que teriam acontecido.</summary>
    public void CatchUp()
    {
        if (_stage == Stage.Waving) _stage = _beforeWave;
        if (Brain.FastForward(_room, DateTime.Now) > 0) TeleportToPlan();
    }

    /// <summary>Aceninho (quando o modo quarto abre). Dormindo, ele não acorda para acenar.</summary>
    public void Wave(float cameraYaw)
    {
        if (_stage == Stage.Waving || IsSleeping) return;
        _beforeWave = _stage;
        _stage = Stage.Waving;
        _timer = 2.6f;
        _body.Anim = BuddyAnim.Wave;
        _body.TargetYaw = cameraYaw;
    }
}
