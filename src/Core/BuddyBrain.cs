using System;
using System.Collections.Generic;
using System.Linq;

namespace Jubuddy.Core;

public enum Activity { Idle, Wander, Sweep, Read, UsePc, Sit, Sleep }

public enum Posture { Stand, SitChair, SitFloor, Lie }

/// <summary>
/// O que o buddy vai fazer: ir até <see cref="Target"/> (passando por <see cref="Via"/>, se houver),
/// assumir a postura e ficar fazendo a atividade por <see cref="DurationSeconds"/>.
/// </summary>
public sealed record ActivityPlan(
    Activity Activity,
    Vec2 Target,
    float? Facing,
    Posture Posture,
    float SeatHeight,
    double DurationSeconds,
    string? ItemId = null,
    Vec2? Via = null,
    string? ViaItemId = null,
    string? LookAtItemId = null);

/// <summary>
/// Máquina de estados do buddy. Escolhe a atividade pelo que existe no quarto e pela hora:
/// só lê com estante, só usa o PC com mesa com PC, varre sempre, dorme de madrugada.
/// Não depende do Godot e recebe o relógio de fora, para ser testável e para "avançar no tempo".
/// </summary>
public sealed class BuddyBrain
{
    public const float WalkSpeed = 0.7f;

    private readonly Random _rng;
    private readonly NavGrid _nav;

    public ActivityPlan Plan { get; private set; }
    public bool Walking { get; private set; }
    public DateTime WalkStartedAt { get; private set; }
    public DateTime DoingEndsAt { get; private set; }

    /// <summary>Última posição conhecida (o Godot atualiza enquanto anda).</summary>
    public Vec2 Position { get; set; }

    /// <summary>Para medir desempenho e testar: força sempre a mesma atividade, se ela estiver disponível.</summary>
    public Activity? Forced { get; set; }

    public NavGrid Nav => _nav;

    public BuddyBrain(Random rng, NavGrid nav, Vec2 start, DateTime now)
    {
        _rng = rng;
        _nav = nav;
        Position = start;
        Plan = new ActivityPlan(Activity.Idle, start, null, Posture.Stand, 0, 3);
        Walking = false;
        DoingEndsAt = now; // decide algo de verdade no primeiro Tick
    }

    // ---------- Hora do dia ----------

    /// <summary>Madrugada: das 0h às 6h ele está dormindo.</summary>
    public static bool IsNight(DateTime t) => t.Hour < 6;

    /// <summary>Fim da noite: 23h já é hora de deitar.</summary>
    public static bool IsBedtime(DateTime t) => t.Hour >= 23;

    private static bool IsNapTime(DateTime t) => t.Hour is >= 13 and < 16;

    private DateTime NextWake(DateTime t)
    {
        var wake = t.Date.AddHours(6);
        if (t >= wake) wake = wake.AddDays(1);
        return wake.AddMinutes(_rng.Next(0, 25));
    }

    // ---------- Escolha ----------

    /// <summary>Atividades possíveis agora, com peso. Usada pela escolha e pelos testes.</summary>
    public IReadOnlyList<(Activity Activity, double Weight)> Options(Room room, DateTime now, Activity? previous = null)
    {
        var list = new List<(Activity, double)>();

        if (IsNight(now))
        {
            list.Add((Activity.Sleep, 1));
            return list;
        }

        var hasSeat = room.Has(ItemKind.Chair) || room.Has(ItemKind.Bed) || room.Has(ItemKind.Rug);
        list.Add((Activity.Sweep, 1.0));                // varre sempre
        list.Add((Activity.Wander, 1.2));
        list.Add((Activity.Idle, 0.9));
        if (room.Has(ItemKind.Bookshelf)) list.Add((Activity.Read, 2.0));
        if (room.Has(ItemKind.DeskPc)) list.Add((Activity.UsePc, 2.5));
        if (hasSeat) list.Add((Activity.Sit, 1.0));
        if (IsBedtime(now)) list.Add((Activity.Sleep, 6.0));
        else if (IsNapTime(now) && room.Has(ItemKind.Bed)) list.Add((Activity.Sleep, 0.15));

        if (previous is { } p)
        {
            for (var i = 0; i < list.Count; i++)
                if (list[i].Item1 == p && p != Activity.Sleep)
                    list[i] = (list[i].Item1, list[i].Item2 * 0.35);
        }
        return list;
    }

    public ActivityPlan Decide(Room room, DateTime now)
    {
        _nav.Sync(room);
        var options = Options(room, now, Plan.Activity);
        Activity chosen;

        if (Forced is { } f)
        {
            // Forçado (medição/teste). Se faltar o objeto necessário, Build cai para um passeio.
            chosen = f;
        }
        else
        {
            var total = options.Sum(o => o.Weight);
            var roll = _rng.NextDouble() * total;
            chosen = options[^1].Activity;
            foreach (var (a, w) in options)
            {
                if (roll < w) { chosen = a; break; }
                roll -= w;
            }
        }

        return Build(chosen, room, now);
    }

    private double Between(double a, double b) => a + _rng.NextDouble() * (b - a);

    private Vec2 RandomSpot(Vec2? around = null, float radius = 0)
        => _nav.RandomFree(_rng, around, radius) ?? _nav.RandomFree(_rng) ?? Position;

    private ActivityPlan Build(Activity activity, Room room, DateTime now)
    {
        switch (activity)
        {
            case Activity.Sleep:
                return BuildSleep(room, now);

            case Activity.UsePc:
            {
                var desk = Pick(room.OfKind(ItemKind.DeskPc));
                if (desk == null) break;
                var (seat, facing, posture, height, chairId) = DeskSeat(room, desk);
                return new ActivityPlan(Activity.UsePc, seat, facing, posture, height, Between(90, 300), ItemId: chairId ?? desk.Id, LookAtItemId: desk.Id);
            }

            case Activity.Read:
            {
                var shelf = Pick(room.OfKind(ItemKind.Bookshelf));
                if (shelf == null) break;
                var via = FrontSpot(shelf, 0.3f);
                var seat = ReadingSeat(room, shelf);
                return seat with { Activity = Activity.Read, DurationSeconds = Between(60, 180), Via = via, ViaItemId = shelf.Id };
            }

            case Activity.Sit:
            {
                var seat = AnySeat(room);
                if (seat == null) break;
                return seat with { Activity = Activity.Sit, DurationSeconds = Between(30, 90) };
            }

            case Activity.Sweep:
                return new ActivityPlan(Activity.Sweep, RandomSpot(), null, Posture.Stand, 0, Between(40, 90));

            case Activity.Wander:
                return new ActivityPlan(Activity.Wander, RandomSpot(), null, Posture.Stand, 0, Between(2, 6));

            case Activity.Idle:
            {
                var decor = room.Items.Where(i => Catalog.IsDecor(i.Kind)).ToList();
                if (decor.Count > 0 && _rng.NextDouble() < 0.5)
                {
                    var d = decor[_rng.Next(decor.Count)];
                    var look = LookSpot(room, d);
                    return new ActivityPlan(Activity.Idle, look.spot, look.facing, Posture.Stand, 0, Between(6, 15), LookAtItemId: d.Id);
                }
                return new ActivityPlan(Activity.Idle, Position, null, Posture.Stand, 0, Between(8, 25));
            }
        }

        // O objeto necessário sumiu no meio do caminho: dá uma voltinha.
        return new ActivityPlan(Activity.Wander, RandomSpot(), null, Posture.Stand, 0, Between(2, 6));
    }

    private PlacedItem? Pick(IEnumerable<PlacedItem> items)
    {
        var list = items.ToList();
        return list.Count == 0 ? null : list[_rng.Next(list.Count)];
    }

    private ActivityPlan BuildSleep(Room room, DateTime now)
    {
        var until = IsNight(now) || IsBedtime(now) ? NextWake(now) : now.AddMinutes(Between(15, 30));
        var duration = Math.Max(30, (until - now).TotalSeconds);

        var bed = Pick(room.OfKind(ItemKind.Bed));
        if (bed != null)
        {
            var yaw = Room.YawOf(bed);
            // Pés para o lado do pé da cama (+Z local); cabeça no travesseiro.
            var feet = bed.Position + Vec2.FromYaw(yaw) * 0.5f;
            return new ActivityPlan(Activity.Sleep, feet, yaw, Posture.Lie, bed.Spec.SeatHeight, duration, ItemId: bed.Id);
        }

        var rug = Pick(room.OfKind(ItemKind.Rug));
        if (rug != null)
        {
            var yaw = Room.YawOf(rug) + MathF.PI / 2;
            return new ActivityPlan(Activity.Sleep, rug.Position, yaw, Posture.Lie, rug.Spec.SeatHeight, duration, ItemId: rug.Id);
        }

        // Sem cama nem tapete: dorme num canto livre do chão.
        var spot = _nav.NearestFree(new Vec2(-room.Half + 0.6f, -room.Half + 0.9f)) ?? RandomSpot();
        return new ActivityPlan(Activity.Sleep, spot, MathF.PI / 2, Posture.Lie, 0, duration);
    }

    /// <summary>Lugar de usar o PC: na cadeira que estiver em frente à mesa, ou em pé.</summary>
    private (Vec2 seat, float facing, Posture posture, float height, string? chairId) DeskSeat(Room room, PlacedItem desk)
    {
        var yaw = Room.YawOf(desk);
        var front = FrontSpot(desk, 0.22f);
        var facing = yaw + MathF.PI; // olhando para a mesa
        var chair = room.OfKind(ItemKind.Chair)
            .OrderBy(c => Vec2.Distance(c.Position, front))
            .FirstOrDefault(c => Vec2.Distance(c.Position, front) < 0.6f);
        if (chair != null)
            return (chair.Position, facing, Posture.SitChair, chair.Spec.SeatHeight, chair.Id);
        return (front, facing, Posture.Stand, 0, null);
    }

    /// <summary>Onde ler: cadeira, beira da cama, tapete, ou em pé na frente da estante.</summary>
    private ActivityPlan ReadingSeat(Room room, PlacedItem shelf)
    {
        var seat = AnySeat(room);
        if (seat != null) return seat;
        var spot = FrontSpot(shelf, 0.45f);
        return new ActivityPlan(Activity.Read, spot, Room.YawOf(shelf), Posture.Stand, 0, 60);
    }

    private ActivityPlan? AnySeat(Room room)
    {
        var seats = new List<ActivityPlan>();
        foreach (var c in room.OfKind(ItemKind.Chair))
            seats.Add(new ActivityPlan(Activity.Sit, c.Position, Room.YawOf(c), Posture.SitChair, c.Spec.SeatHeight, 60, ItemId: c.Id));
        foreach (var b in room.OfKind(ItemKind.Bed))
        {
            var yaw = Room.YawOf(b);
            var right = Vec2.FromYaw(yaw + MathF.PI / 2);
            var edge = b.Position + right * (b.Spec.Width / 2 - 0.12f);
            seats.Add(new ActivityPlan(Activity.Sit, edge, yaw + MathF.PI / 2, Posture.SitChair, b.Spec.SeatHeight, 60, ItemId: b.Id));
        }
        foreach (var r in room.OfKind(ItemKind.Rug))
        {
            var off = new Vec2((float)Between(-0.3, 0.3), (float)Between(-0.2, 0.2));
            seats.Add(new ActivityPlan(Activity.Sit, r.Position + off, (float)Between(-MathF.PI, MathF.PI), Posture.SitFloor, r.Spec.SeatHeight, 60, ItemId: r.Id));
        }
        return seats.Count == 0 ? null : seats[_rng.Next(seats.Count)];
    }

    /// <summary>Ponto em frente a um objeto de chão (a frente é +Z local).</summary>
    public static Vec2 FrontSpot(PlacedItem it, float gap)
    {
        var yaw = Room.YawOf(it);
        return it.Position + Vec2.FromYaw(yaw) * (it.Spec.Depth / 2 + gap);
    }

    private (Vec2 spot, float facing) LookSpot(Room room, PlacedItem it)
    {
        Vec2 target;
        Vec2 spot;
        if (it.Spec.OnWall)
        {
            target = it.Wall == WallSide.Back ? new Vec2(it.Along, -room.Half) : new Vec2(-room.Half, it.Along);
            spot = it.Wall == WallSide.Back ? new Vec2(it.Along, -room.Half + 0.9f) : new Vec2(-room.Half + 0.9f, it.Along);
        }
        else
        {
            target = it.Position;
            spot = it.Position + Vec2.FromYaw((float)Between(-MathF.PI, MathF.PI)) * 0.7f;
        }
        spot = _nav.NearestFree(spot) ?? Position;
        return (spot, spot.YawTo(target));
    }

    // ---------- Andamento ----------

    public void Begin(ActivityPlan plan, DateTime now)
    {
        Plan = plan;
        Walking = true;
        WalkStartedAt = now;
    }

    /// <summary>Chegou ao destino: começa a atividade de fato.</summary>
    public void Arrived(DateTime now)
    {
        Walking = false;
        Position = Plan.Target;
        DoingEndsAt = now.AddSeconds(Plan.DurationSeconds);
    }

    /// <summary>Chamado a cada quadro (ou a cada segundo). Retorna true quando escolheu uma nova atividade.</summary>
    public bool Tick(Room room, DateTime now)
    {
        if (Walking || now < DoingEndsAt) return false;
        Begin(Decide(room, now), now);
        return true;
    }

    /// <summary>Se a atividade usa um objeto que saiu do quarto, ou se virou madrugada, escolhe outra.</summary>
    public bool Validate(Room room, DateTime now)
    {
        var missing =
            (Plan.ItemId != null && room.Find(Plan.ItemId) == null) ||
            (Plan.ViaItemId != null && Walking && room.Find(Plan.ViaItemId) == null) ||
            (Plan.LookAtItemId != null && room.Find(Plan.LookAtItemId) == null);
        var shouldSleep = Forced == null && IsNight(now) && Plan.Activity != Activity.Sleep;
        if (!missing && !shouldSleep) return false;
        Begin(Decide(room, now), now);
        return true;
    }

    /// <summary>Tempo estimado para andar até o destino do plano atual.</summary>
    public double EstimatedWalkSeconds(Vec2 from)
    {
        var d = Vec2.Distance(from, Plan.Target);
        if (Plan.Via is { } v) d = Vec2.Distance(from, v) + Vec2.Distance(v, Plan.Target) + 1.5f;
        return d / WalkSpeed;
    }

    /// <summary>
    /// "Avança pelo relógio" enquanto nada foi desenhado (jogo em tela cheia, PC bloqueado...).
    /// Encadeia as atividades que teriam acontecido entre o último estado e agora, sem animar nada.
    /// Ao final o buddy está parado, já fazendo a atividade em que estaria agora.
    /// Retorna quantas atividades mudaram (0 = continua no mesmo lugar).
    /// </summary>
    public int FastForward(Room room, DateTime now)
    {
        var changes = 0;

        if (Walking)
        {
            var arrive = WalkStartedAt.AddSeconds(EstimatedWalkSeconds(Position));
            Arrived(arrive < now ? arrive : now);
            changes++;
        }

        // Pausas muito longas: só a última hora importa para onde ele está agora.
        if ((now - DoingEndsAt).TotalHours > 1)
            DoingEndsAt = now.AddMinutes(-10);

        var guard = 0;
        while (now >= DoingEndsAt && guard++ < 2000)
        {
            var at = DoingEndsAt;
            var from = Position;
            Begin(Decide(room, at), at);
            var arrive = at.AddSeconds(EstimatedWalkSeconds(from));
            Arrived(arrive < now ? arrive : now);
            changes++;
        }
        return changes;
    }
}
