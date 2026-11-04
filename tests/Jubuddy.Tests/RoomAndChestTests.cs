using System;
using System.Linq;
using Jubuddy.Core;
using Xunit;

namespace Jubuddy.Tests;

public class RoomAndChestTests
{
    [Fact]
    public void Quarto_comeca_vazio()
    {
        var room = new Room();
        Assert.Empty(room.Items);
        Assert.Empty(room.Chest);
    }

    [Fact]
    public void Objeto_guarda_a_frase()
    {
        var room = new Room();
        var it = room.Add(ItemKind.Plant, "  porque eu cuido das coisas devagar  ")!;
        Assert.Equal("porque eu cuido das coisas devagar", it.Why);
    }

    [Fact]
    public void Guardar_no_bau_nao_apaga()
    {
        var room = new Room();
        var it = room.Add(ItemKind.Bookshelf, "eu leio")!;
        room.StoreInChest(it.Id);

        Assert.Empty(room.Items);
        var inChest = Assert.Single(room.Chest);
        Assert.Equal(it.Id, inChest.Id);
        Assert.Equal("eu leio", inChest.Why);
        Assert.NotNull(inChest.StoredAt);
    }

    [Fact]
    public void Devolver_do_bau_volta_ao_lugar_antigo()
    {
        var room = new Room();
        var it = room.Add(ItemKind.Chair, "sento", new Vec2(1, 1))!;
        var pos = it.Position;
        room.StoreInChest(it.Id);
        room.RestoreFromChest(it.Id);

        var back = Assert.Single(room.Items);
        Assert.Equal(pos, back.Position);
        Assert.Null(back.StoredAt);
        Assert.Empty(room.Chest);
    }

    [Fact]
    public void Devolver_do_bau_procura_outro_lugar_se_o_antigo_estiver_ocupado()
    {
        var room = new Room();
        var chair = room.Add(ItemKind.Chair, "a", new Vec2(0, 0))!;
        var old = chair.Position;
        room.StoreInChest(chair.Id);
        var plant = room.Add(ItemKind.Plant, "b", old)!;
        Assert.Equal(old, plant.Position);

        var back = room.RestoreFromChest(chair.Id);
        Assert.True(room.CanPlace(back));
        Assert.NotEqual(old, back.Position);
    }

    [Fact]
    public void Apagar_de_vez_so_funciona_dentro_do_bau()
    {
        var room = new Room();
        var it = room.Add(ItemKind.Lamp, "luz")!;

        Assert.Throws<InvalidOperationException>(() => room.DeleteForever(it.Id));
        Assert.Single(room.Items);

        room.StoreInChest(it.Id);
        var deleted = room.DeleteForever(it.Id);
        Assert.Equal(it.Id, deleted.Id);
        Assert.Empty(room.Chest);
        Assert.Empty(room.Items);
    }

    [Fact]
    public void Apagar_id_inexistente_nao_mexe_em_nada()
    {
        var room = new Room();
        room.Add(ItemKind.Lamp, "luz");
        Assert.Throws<InvalidOperationException>(() => room.DeleteForever("nao-existe"));
        Assert.Single(room.Items);
    }

    [Fact]
    public void Moveis_nao_se_sobrepoem_mas_tapete_fica_embaixo()
    {
        var room = new Room();
        var rug = room.Add(ItemKind.Rug, "tapete", new Vec2(0, 0))!;
        var bed = room.Add(ItemKind.Bed, "cama", new Vec2(0, 0))!;
        Assert.Equal(new Vec2(0, 0), rug.Position);
        Assert.Equal(new Vec2(0, 0), bed.Position); // cama em cima do tapete: ok

        var desk = room.Add(ItemKind.DeskPc, "mesa", new Vec2(0, 0))!;
        Assert.False(Room.FloorRect(desk).Overlaps(Room.FloorRect(bed)));

        Assert.False(room.TryMoveFloor(desk.Id, bed.Position, 0));
    }

    [Fact]
    public void Nada_sai_do_quarto()
    {
        var room = new Room();
        var bed = room.Add(ItemKind.Bed, "cama")!;
        Assert.False(room.TryMoveFloor(bed.Id, new Vec2(room.Half, 0), 0));
        Assert.True(room.TryMoveFloor(bed.Id, new Vec2(room.Half - 0.5f, room.Half - 0.85f), 0));
    }

    [Fact]
    public void Girar_troca_largura_e_profundidade()
    {
        var room = new Room();
        var bed = room.Add(ItemKind.Bed, "cama", new Vec2(0, 0))!;
        var (w0, d0) = Room.FloorSize(bed);
        Assert.True(room.TryRotate(bed.Id));
        var (w1, d1) = Room.FloorSize(bed);
        Assert.Equal(w0, d1);
        Assert.Equal(d0, w1);
    }

    [Fact]
    public void Quadros_na_parede_nao_se_sobrepoem()
    {
        var room = new Room();
        var a = room.Add(ItemKind.Frame, "a")!;
        var b = room.Add(ItemKind.Frame, "b")!;
        Assert.Equal(WallSide.Back, a.Wall);
        Assert.True(room.CanPlace(a));
        Assert.True(room.CanPlace(b));
        Assert.False(room.TryMoveWall(b.Id, a.Wall, a.Along, a.Y));
        Assert.False(room.TryMoveWall(b.Id, WallSide.Back, 0, Room.WallHeight)); // passa do teto
    }

    [Fact]
    public void Poster_segue_a_proporcao_da_imagem()
    {
        var wide = new PlacedItem { Kind = ItemKind.Poster, Aspect = 2f };
        var tall = new PlacedItem { Kind = ItemKind.Poster, Aspect = 0.5f };
        var (ww, wh) = Room.WallSize(wide);
        var (tw, th) = Room.WallSize(tall);
        Assert.True(ww > wh);
        Assert.True(th > tw);
    }

    [Fact]
    public void Quarto_cheio_retorna_null_em_vez_de_sobrepor()
    {
        var room = new Room();
        PlacedItem? last;
        var count = 0;
        do { last = room.Add(ItemKind.Bed, "cama"); count++; } while (last != null && count < 100);
        Assert.Null(last);
        var rects = room.Items.Select(Room.FloorRect).ToList();
        for (var i = 0; i < rects.Count; i++)
        for (var j = i + 1; j < rects.Count; j++)
            Assert.False(rects[i].Overlaps(rects[j]));
    }

    [Fact]
    public void Revisao_muda_a_cada_alteracao()
    {
        var room = new Room();
        var r0 = room.Revision;
        var it = room.Add(ItemKind.Plant, "x")!;
        var r1 = room.Revision;
        room.StoreInChest(it.Id);
        var r2 = room.Revision;
        Assert.True(r1 > r0);
        Assert.True(r2 > r1);
    }
}
