using System;
using System.Linq;
using Jubuddy.Core;
using Xunit;

namespace Jubuddy.Tests;

public class BrainTests
{
    private static readonly DateTime Afternoon = new(2026, 11, 3, 15, 0, 0);
    private static readonly DateTime Morning = new(2026, 11, 3, 10, 0, 0);
    private static readonly DateTime Madrugada = new(2026, 11, 4, 3, 0, 0);

    private static BuddyBrain NewBrain(Room room, DateTime now, int seed = 1)
    {
        var nav = new NavGrid();
        nav.Build(room);
        return new BuddyBrain(new Random(seed), nav, Vec2.Zero, now);
    }

    private static Room RoomWith(params ItemKind[] kinds)
    {
        var room = new Room();
        foreach (var k in kinds) Assert.NotNull(room.Add(k, "porque sim"));
        return room;
    }

    [Fact]
    public void Quarto_vazio_so_permite_varrer_andar_e_ficar_a_toa()
    {
        var room = new Room();
        var brain = NewBrain(room, Morning);
        var acts = brain.Options(room, Morning).Select(o => o.Activity).ToHashSet();

        Assert.Contains(Activity.Sweep, acts);
        Assert.DoesNotContain(Activity.Read, acts);
        Assert.DoesNotContain(Activity.UsePc, acts);
        Assert.DoesNotContain(Activity.Sit, acts);
        Assert.DoesNotContain(Activity.Sleep, acts);
    }

    [Fact]
    public void Varrer_esta_sempre_disponivel_de_dia()
    {
        var room = RoomWith(ItemKind.Bed, ItemKind.DeskPc, ItemKind.Bookshelf, ItemKind.Chair);
        var brain = NewBrain(room, Morning);
        for (var h = 6; h < 23; h++)
        {
            var t = Morning.Date.AddHours(h);
            Assert.Contains(brain.Options(room, t), o => o.Activity == Activity.Sweep);
        }
    }

    [Fact]
    public void So_le_se_houver_estante()
    {
        var room = new Room();
        var brain = NewBrain(room, Morning);
        Assert.DoesNotContain(brain.Options(room, Morning), o => o.Activity == Activity.Read);

        room.Add(ItemKind.Bookshelf, "livros");
        Assert.Contains(brain.Options(room, Morning), o => o.Activity == Activity.Read);
    }

    [Fact]
    public void So_usa_o_pc_se_houver_mesa_com_pc()
    {
        var room = RoomWith(ItemKind.Chair, ItemKind.Bookshelf);
        var brain = NewBrain(room, Morning);
        Assert.DoesNotContain(brain.Options(room, Morning), o => o.Activity == Activity.UsePc);

        room.Add(ItemKind.DeskPc, "trabalho e jogo aqui");
        Assert.Contains(brain.Options(room, Morning), o => o.Activity == Activity.UsePc);
    }

    [Fact]
    public void De_madrugada_so_dorme()
    {
        var room = RoomWith(ItemKind.Bed, ItemKind.DeskPc, ItemKind.Bookshelf);
        var brain = NewBrain(room, Madrugada);
        var options = brain.Options(room, Madrugada);
        Assert.Single(options);
        Assert.Equal(Activity.Sleep, options[0].Activity);

        for (var i = 0; i < 20; i++)
            Assert.Equal(Activity.Sleep, brain.Decide(room, Madrugada).Activity);
    }

    [Fact]
    public void Dorme_na_cama_se_houver_e_acorda_depois_das_seis()
    {
        var room = RoomWith(ItemKind.Bed);
        var bed = room.Items.Single();
        var brain = NewBrain(room, Madrugada);
        var plan = brain.Decide(room, Madrugada);

        Assert.Equal(Activity.Sleep, plan.Activity);
        Assert.Equal(Posture.Lie, plan.Posture);
        Assert.Equal(bed.Id, plan.ItemId);
        var wake = Madrugada.AddSeconds(plan.DurationSeconds);
        Assert.True(wake >= Madrugada.Date.AddHours(6));
        Assert.True(wake <= Madrugada.Date.AddHours(6).AddMinutes(30));
    }

    [Fact]
    public void Sem_cama_dorme_no_chao()
    {
        var room = new Room();
        var brain = NewBrain(room, Madrugada);
        var plan = brain.Decide(room, Madrugada);
        Assert.Equal(Activity.Sleep, plan.Activity);
        Assert.Equal(Posture.Lie, plan.Posture);
        Assert.Null(plan.ItemId);
    }

    [Fact]
    public void Usar_pc_senta_na_cadeira_em_frente_a_mesa()
    {
        var room = new Room();
        var desk = room.Add(ItemKind.DeskPc, "pc", new Vec2(0, -1.5f))!;
        var front = BuddyBrain.FrontSpot(desk, 0.22f);
        var chair = room.Add(ItemKind.Chair, "cadeira", front)!;
        Assert.True(Vec2.Distance(chair.Position, front) < 0.6f, $"cadeira em {chair.Position}, frente {front}");

        var brain = NewBrain(room, Morning);
        brain.Forced = Activity.UsePc;
        var plan = brain.Decide(room, Morning);
        Assert.Equal(Activity.UsePc, plan.Activity);
        Assert.Equal(Posture.SitChair, plan.Posture);
        Assert.Equal(chair.Position, plan.Target);
    }

    [Fact]
    public void Ler_passa_pela_estante_antes()
    {
        var room = RoomWith(ItemKind.Bookshelf, ItemKind.Chair);
        var shelf = room.OfKind(ItemKind.Bookshelf).Single();
        var brain = NewBrain(room, Morning);
        brain.Forced = Activity.Read;
        var plan = brain.Decide(room, Morning);
        Assert.Equal(Activity.Read, plan.Activity);
        Assert.Equal(shelf.Id, plan.ViaItemId);
        Assert.NotNull(plan.Via);
    }

    [Fact]
    public void Tick_so_troca_de_atividade_quando_ela_termina()
    {
        var room = new Room();
        var brain = NewBrain(room, Morning);
        Assert.True(brain.Tick(room, Morning)); // primeira escolha
        Assert.True(brain.Walking);
        Assert.False(brain.Tick(room, Morning.AddHours(1))); // andando não troca

        brain.Arrived(Morning);
        Assert.False(brain.Tick(room, Morning.AddSeconds(1)));
        Assert.True(brain.Tick(room, brain.DoingEndsAt));
    }

    [Fact]
    public void Se_o_objeto_vai_para_o_bau_ele_muda_de_atividade()
    {
        var room = RoomWith(ItemKind.Bookshelf);
        var shelf = room.Items.Single();
        var brain = NewBrain(room, Morning);
        brain.Forced = Activity.Read;
        brain.Begin(brain.Decide(room, Morning), Morning);
        Assert.Equal(Activity.Read, brain.Plan.Activity);

        room.StoreInChest(shelf.Id);
        Assert.True(brain.Validate(room, Morning));
        Assert.NotEqual(Activity.Read, brain.Plan.Activity);
    }

    [Fact]
    public void Avancar_no_tempo_deixa_o_buddy_em_outra_atividade_ja_no_lugar()
    {
        var room = RoomWith(ItemKind.Bed, ItemKind.DeskPc, ItemKind.Bookshelf, ItemKind.Chair, ItemKind.Rug);
        var brain = NewBrain(room, Morning, seed: 7);
        brain.Tick(room, Morning);
        brain.Arrived(Morning.AddSeconds(5));
        var before = brain.Plan;

        var later = Morning.AddMinutes(40);
        var changes = brain.FastForward(room, later);

        Assert.True(changes > 0);
        Assert.False(brain.Walking);
        Assert.Equal(brain.Plan.Target, brain.Position);
        Assert.True(brain.DoingEndsAt > later);
        Assert.NotSame(before, brain.Plan);
    }

    [Fact]
    public void Avancar_no_tempo_durante_a_madrugada_termina_dormindo()
    {
        var room = RoomWith(ItemKind.Bed);
        var evening = new DateTime(2026, 11, 3, 21, 0, 0);
        var brain = NewBrain(room, evening, seed: 3);
        brain.Tick(room, evening);
        brain.Arrived(evening);

        brain.FastForward(room, Madrugada);
        Assert.Equal(Activity.Sleep, brain.Plan.Activity);
        Assert.Equal(Posture.Lie, brain.Plan.Posture);
    }

    [Fact]
    public void Avancar_enquanto_andava_considera_que_chegou()
    {
        var room = new Room();
        var brain = NewBrain(room, Morning);
        brain.Forced = Activity.Sweep;
        brain.Tick(room, Morning);
        Assert.True(brain.Walking);

        brain.FastForward(room, Morning.AddSeconds(2));
        Assert.False(brain.Walking);
        Assert.Equal(brain.Plan.Target, brain.Position);
    }

    [Fact]
    public void Pausa_de_dias_nao_trava()
    {
        var room = RoomWith(ItemKind.Bed, ItemKind.DeskPc);
        var brain = NewBrain(room, Morning);
        brain.Tick(room, Morning);
        var changes = brain.FastForward(room, Morning.AddDays(9).AddHours(5));
        Assert.InRange(changes, 1, 2000);
        Assert.False(brain.Walking);
    }

    [Fact]
    public void Atividade_forcada_ganha_ate_de_madrugada_para_medir_desempenho()
    {
        var room = new Room();
        var brain = NewBrain(room, Madrugada);
        brain.Forced = Activity.Wander;
        Assert.Equal(Activity.Wander, brain.Decide(room, Madrugada).Activity);
    }

    [Fact]
    public void Destinos_de_andar_ficam_em_lugar_livre()
    {
        var room = RoomWith(ItemKind.Bed, ItemKind.DeskPc, ItemKind.Bookshelf, ItemKind.Plant, ItemKind.Lamp);
        var brain = NewBrain(room, Afternoon, seed: 11);
        brain.Forced = Activity.Wander;
        for (var i = 0; i < 50; i++)
        {
            var plan = brain.Decide(room, Afternoon);
            Assert.True(brain.Nav.IsFree(plan.Target), $"destino {plan.Target} ocupado");
        }
    }
}
