using System;
using System.IO;
using System.Linq;
using Jubuddy.Core;
using Xunit;

namespace Jubuddy.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir;
    private readonly AppPaths _paths;

    public StorageTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "jubuddy-tests-" + Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_dir);
        _paths.EnsureCreated();
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Sem_arquivo_carrega_quarto_vazio()
    {
        var room = new RoomStore(_paths).Load(out var problem);
        Assert.Null(problem);
        Assert.Empty(room.Items);
        Assert.Empty(room.Chest);
    }

    [Fact]
    public void Salvar_e_carregar_mantem_tudo_inclusive_o_bau()
    {
        var store = new RoomStore(_paths);
        var room = new Room();
        var bed = room.Add(ItemKind.Bed, "descanso é coisa séria", new Vec2(1, 1))!;
        room.TryRotate(bed.Id);
        var poster = room.Add(ItemKind.Poster, "meu moodboard", image: "abc.png", aspect: 1.5f)!;
        var lamp = room.Add(ItemKind.Lamp, "luz quente à noite")!;
        room.SetColor(lamp.Id, "#AA8844");
        var shelf = room.Add(ItemKind.Bookshelf, "ação, coração, não")!;
        room.StoreInChest(shelf.Id);
        store.Save(room);

        var loaded = new RoomStore(_paths).Load(out var problem);
        Assert.Null(problem);
        Assert.Equal(3, loaded.Items.Count);
        Assert.Single(loaded.Chest);

        var b = loaded.Find(bed.Id)!;
        Assert.Equal(bed.Position, b.Position);
        Assert.Equal(bed.Rotation, b.Rotation);
        Assert.Equal("descanso é coisa séria", b.Why);

        var p = loaded.Find(poster.Id)!;
        Assert.Equal("abc.png", p.Image);
        Assert.Equal(1.5f, p.Aspect);
        Assert.Equal(WallSide.Back, p.Wall);
        Assert.Equal(poster.Along, p.Along);
        Assert.Equal(poster.Y, p.Y);

        Assert.Equal("#AA8844", loaded.Find(lamp.Id)!.Color);
        var s = loaded.FindInChest(shelf.Id)!;
        Assert.Equal("ação, coração, não", s.Why);
        Assert.NotNull(s.StoredAt);
    }

    [Fact]
    public void Arquivo_salvo_e_legivel_com_acentos()
    {
        var store = new RoomStore(_paths);
        var room = new Room();
        room.Add(ItemKind.Plant, "coração");
        store.Save(room);
        var json = File.ReadAllText(_paths.RoomFile);
        Assert.Contains("coração", json);
        Assert.Contains("\"frase\"", json);
        Assert.Contains("\"bau\"", json);
    }

    [Fact]
    public void Salvar_duas_vezes_guarda_a_versao_anterior()
    {
        var store = new RoomStore(_paths);
        var room = new Room();
        room.Add(ItemKind.Plant, "primeira");
        store.Save(room);
        room.Add(ItemKind.Lamp, "segunda");
        store.Save(room);

        Assert.True(File.Exists(_paths.RoomFile + ".bak"));
        Assert.False(File.Exists(_paths.RoomFile + ".tmp"));
        var bak = File.ReadAllText(_paths.RoomFile + ".bak");
        Assert.Contains("primeira", bak);
        Assert.DoesNotContain("segunda", bak);
    }

    [Fact]
    public void Arquivo_corrompido_nao_e_apagado_e_usa_o_bak()
    {
        var store = new RoomStore(_paths);
        var room = new Room();
        room.Add(ItemKind.Plant, "boa");
        store.Save(room);
        room.Add(ItemKind.Lamp, "também boa");
        store.Save(room); // .bak tem só a planta

        File.WriteAllText(_paths.RoomFile, "{ isto não é json");
        var loaded = new RoomStore(_paths).Load(out var problem);

        Assert.NotNull(problem);
        Assert.Single(loaded.Items);
        Assert.Equal("boa", loaded.Items[0].Why);
        var kept = Directory.GetFiles(_dir, "quarto.json.corrompido-*");
        Assert.Single(kept);
        Assert.Equal("{ isto não é json", File.ReadAllText(kept[0]));
    }

    [Fact]
    public void Apagar_poster_de_vez_apaga_a_imagem_copiada()
    {
        var store = new RoomStore(_paths);
        var img = Path.Combine(_paths.PostersDir, "p1.png");
        File.WriteAllBytes(img, new byte[] { 1, 2, 3 });

        var room = new Room();
        var poster = room.Add(ItemKind.Poster, "moodboard", image: "p1.png")!;
        room.StoreInChest(poster.Id);
        Assert.True(File.Exists(img)); // no baú a imagem continua

        var deleted = room.DeleteForever(poster.Id);
        store.DeletePosterImageIfUnused(room, deleted);
        Assert.False(File.Exists(img));
    }

    [Fact]
    public void Ajustes_e_aparencia_vao_e_voltam()
    {
        var look = new BuddyLook { Skin = "#123456", HairStyle = HairStyle.Coque, Outfit = Outfit.Vestido, Glasses = true };
        JsonStore.Save(_paths.LookFile, look);
        var back = JsonStore.Load(_paths.LookFile, () => new BuddyLook(), out var problem);
        Assert.Null(problem);
        Assert.Equal("#123456", back.Skin);
        Assert.Equal(HairStyle.Coque, back.HairStyle);
        Assert.Equal(Outfit.Vestido, back.Outfit);
        Assert.True(back.Glasses);
        Assert.Contains("\"penteado\": \"Coque\"", File.ReadAllText(_paths.LookFile));

        var settings = new AppSettings { PhraseIntervalMinutes = 90, Diagnostics = true };
        JsonStore.Save(_paths.SettingsFile, settings);
        var s2 = JsonStore.Load(_paths.SettingsFile, () => new AppSettings(), out _);
        Assert.Equal(90, s2.PhraseIntervalMinutes);
        Assert.True(s2.Diagnostics);
    }
}
