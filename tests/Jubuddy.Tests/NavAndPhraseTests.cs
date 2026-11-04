using System;
using System.IO;
using System.Linq;
using Jubuddy.Core;
using Xunit;

namespace Jubuddy.Tests;

public class NavGridTests
{
    [Fact]
    public void Caminho_desvia_dos_moveis()
    {
        var room = new Room();
        var shelf = room.Add(ItemKind.Bookshelf, "x", new Vec2(0, 0))!;
        room.TryRotate(shelf.Id); // estante atravessada no meio
        var nav = new NavGrid();
        nav.Build(room);

        var path = nav.FindPath(new Vec2(-1.5f, 0), new Vec2(1.5f, 0));
        Assert.NotEmpty(path);
        var blocked = Room.FloorRect(shelf);
        var prev = new Vec2(-1.5f, 0);
        foreach (var p in path)
        {
            for (var t = 0f; t <= 1f; t += 0.05f)
            {
                var q = new Vec2(prev.X + (p.X - prev.X) * t, prev.Z + (p.Z - prev.Z) * t);
                Assert.False(blocked.Contains(q), $"atravessou a estante em {q}");
            }
            prev = p;
        }
        Assert.True(Vec2.Distance(path[^1], new Vec2(1.5f, 0)) < 0.01f);
    }

    [Fact]
    public void Destino_dentro_de_movel_vai_para_o_livre_mais_perto()
    {
        var room = new Room();
        var bed = room.Add(ItemKind.Bed, "x", new Vec2(0, 0))!;
        var nav = new NavGrid();
        nav.Build(room);
        Assert.False(nav.IsFree(bed.Position));
        var near = nav.NearestFree(bed.Position)!.Value;
        Assert.True(nav.IsFree(near));
        Assert.True(Vec2.Distance(near, bed.Position) < 1.2f);
    }

    [Fact]
    public void Paredes_ficam_fora_do_alcance()
    {
        var room = new Room();
        var nav = new NavGrid();
        nav.Build(room);
        Assert.False(nav.IsFree(new Vec2(-room.Half + 0.05f, 0)));
        Assert.True(nav.IsFree(Vec2.Zero));
    }

    [Fact]
    public void Grade_e_refeita_quando_o_quarto_muda()
    {
        var room = new Room();
        var nav = new NavGrid();
        nav.Sync(room);
        var before = nav.FreeCellCount();
        room.Add(ItemKind.Bed, "x", Vec2.Zero);
        nav.Sync(room);
        Assert.True(nav.FreeCellCount() < before);
    }
}

public class PhraseTests
{
    [Fact]
    public void Arquivo_novo_nao_tem_nenhuma_frase_pronta()
    {
        var dir = Path.Combine(Path.GetTempPath(), "jubuddy-frases-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, "frases.txt");
            var book = PhraseBook.LoadOrCreate(path);
            Assert.True(File.Exists(path));
            Assert.Equal(0, book.Count);
            Assert.Null(book.Next(new Random(1)));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Ignora_comentarios_e_linhas_vazias()
    {
        var book = PhraseBook.Parse("# comentário\n\n  primeira  \r\nsegunda\n#outra\n");
        Assert.Equal(new[] { "primeira", "segunda" }, book.Phrases.ToArray());
    }

    [Fact]
    public void Nao_repete_a_mesma_frase_em_seguida()
    {
        var book = PhraseBook.Parse("a\nb\nc\nd\ne");
        var rng = new Random(5);
        var prev = book.Next(rng);
        for (var i = 0; i < 200; i++)
        {
            var cur = book.Next(rng);
            Assert.NotEqual(prev, cur);
            prev = cur;
        }
    }

    [Fact]
    public void Uma_frase_so_pode_repetir()
    {
        var book = PhraseBook.Parse("única");
        var rng = new Random(1);
        Assert.Equal("única", book.Next(rng));
        Assert.Equal("única", book.Next(rng));
    }

    [Fact]
    public void Intervalo_zero_desliga_as_frases()
    {
        var s = new PhraseScheduler();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        s.Schedule(now, 0, new Random(1));
        Assert.False(s.Due(now.AddYears(1)));
    }

    [Fact]
    public void Intervalo_varia_em_torno_da_media()
    {
        var s = new PhraseScheduler();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var rng = new Random(2);
        for (var i = 0; i < 50; i++)
        {
            s.Schedule(now, 60, rng);
            var mins = (s.NextAt - now).TotalMinutes;
            Assert.InRange(mins, 36, 84);
        }
    }
}
