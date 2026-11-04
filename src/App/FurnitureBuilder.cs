using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Jubuddy.Core;
using static Jubuddy.App.Visuals;

namespace Jubuddy.App;

/// <summary>
/// Catálogo de móveis feitos em código com formas simples. Visual neutro de propósito:
/// a cor principal de cada objeto pode ser trocada (PlacedItem.Color).
/// Origem: objetos de chão no centro da base; objetos de parede no centro da peça. A frente é +Z.
/// </summary>
public static class FurnitureBuilder
{
    private static readonly Color Wood = Hex("#B9A48C");
    private static readonly Color WoodDark = Hex("#8E7B68");
    private static readonly Color Linen = Hex("#EFEDE8");
    private static readonly Color Charcoal = Hex("#34373C");

    private static readonly Dictionary<string, Texture2D?> PosterCache = new();

    public static Color DefaultColor(ItemKind kind) => kind switch
    {
        ItemKind.Bed => Hex("#A9B4BE"),
        ItemKind.DeskPc => Hex("#C9BBA8"),
        ItemKind.Bookshelf => Wood,
        ItemKind.Chair => Hex("#A38F7B"),
        ItemKind.Rug => Hex("#B8ABA0"),
        ItemKind.Plant => Hex("#6F8F6A"),
        ItemKind.Lamp => Hex("#EDE6D6"),
        ItemKind.Frame => Hex("#D9CFC0"),
        ItemKind.Poster => Colors.White,
        _ => Colors.Gray,
    };

    public static Node3D Build(PlacedItem it, string postersDir)
    {
        var root = new Node3D { Name = $"{it.Kind}_{it.Id[..6]}" };
        var main = Hex(it.Color, DefaultColor(it.Kind));
        var s = it.Spec;
        switch (it.Kind)
        {
            case ItemKind.Bed: BuildBed(root, s, main); break;
            case ItemKind.DeskPc: BuildDesk(root, s, main); break;
            case ItemKind.Bookshelf: BuildShelf(root, s, main, it.Id); break;
            case ItemKind.Chair: BuildChair(root, main); break;
            case ItemKind.Rug: BuildRug(root, s, main); break;
            case ItemKind.Plant: BuildPlant(root, main); break;
            case ItemKind.Lamp: BuildLamp(root, main); break;
            case ItemKind.Frame: BuildFrame(root, s, main); break;
            case ItemKind.Poster: BuildPoster(root, it, postersDir); break;
        }
        return root;
    }

    /// <summary>Caixa de seleção/clique em coordenadas locais do nó.</summary>
    public static Aabb LocalBounds(PlacedItem it)
    {
        if (it.Spec.OnWall)
        {
            var (w, h) = Room.WallSize(it);
            return new Aabb(new Vector3(-w / 2, -h / 2, -0.06f), new Vector3(w, h, 0.12f));
        }
        var s = it.Spec;
        var height = it.Kind switch { ItemKind.DeskPc => 0.85f, ItemKind.Rug => 0.03f, _ => s.Height };
        return new Aabb(new Vector3(-s.Width / 2, 0, -s.Depth / 2), new Vector3(s.Width, height, s.Depth));
    }

    private static void BuildBed(Node3D r, ItemSpec s, Color blanket)
    {
        float w = s.Width, d = s.Depth;
        Block(r, Mat(Wood), new Vector3(0, 0.09f, 0), new Vector3(w, 0.18f, d));
        Block(r, Mat(Linen), new Vector3(0, 0.24f, 0.01f), new Vector3(w - 0.06f, 0.12f, d - 0.06f));
        Block(r, Mat(Hex("#F7F5F1")), new Vector3(0, 0.33f, -d / 2 + 0.22f), new Vector3(w * 0.62f, 0.08f, 0.26f));
        Block(r, Mat(blanket), new Vector3(0, 0.305f, d * 0.14f), new Vector3(w - 0.02f, 0.04f, d * 0.62f));
        Block(r, Mat(blanket.Darkened(0.12f)), new Vector3(0, 0.2f, d / 2 - 0.02f), new Vector3(w - 0.02f, 0.18f, 0.03f));
        Block(r, Mat(WoodDark), new Vector3(0, 0.3f, -d / 2 + 0.03f), new Vector3(w, 0.6f, 0.06f));
    }

    private static void BuildDesk(Node3D r, ItemSpec s, Color top)
    {
        float w = s.Width, d = s.Depth, h = s.Height;
        Block(r, Mat(top), new Vector3(0, h - 0.02f, 0), new Vector3(w, 0.04f, d));
        var leg = Mat(WoodDark);
        foreach (var x in new[] { -w / 2 + 0.04f, w / 2 - 0.04f })
        foreach (var z in new[] { -d / 2 + 0.04f, d / 2 - 0.04f })
            Block(r, leg, new Vector3(x, (h - 0.04f) / 2, z), new Vector3(0.04f, h - 0.04f, 0.04f));

        // Monitor, teclado, mouse e gabinete.
        var dark = Mat(Charcoal);
        Block(r, dark, new Vector3(-0.05f, h + 0.01f, -d / 2 + 0.12f), new Vector3(0.14f, 0.02f, 0.1f));
        Block(r, dark, new Vector3(-0.05f, h + 0.08f, -d / 2 + 0.12f), new Vector3(0.03f, 0.12f, 0.03f));
        Block(r, dark, new Vector3(-0.05f, h + 0.25f, -d / 2 + 0.13f), new Vector3(0.46f, 0.28f, 0.03f));
        Block(r, Mat(Hex("#7E9CC0"), unshaded: true), new Vector3(-0.05f, h + 0.25f, -d / 2 + 0.146f), new Vector3(0.42f, 0.24f, 0.004f));
        Block(r, Mat(Hex("#4A4D52")), new Vector3(-0.05f, h + 0.008f, 0.06f), new Vector3(0.32f, 0.016f, 0.1f));
        Block(r, Mat(Hex("#4A4D52")), new Vector3(0.2f, h + 0.008f, 0.07f), new Vector3(0.04f, 0.016f, 0.06f));
        Block(r, dark, new Vector3(w / 2 - 0.12f, h + 0.15f, -0.04f), new Vector3(0.13f, 0.3f, 0.28f));
        Block(r, Mat(Hex("#9FD3A8"), unshaded: true), new Vector3(w / 2 - 0.12f, h + 0.26f, 0.101f), new Vector3(0.02f, 0.02f, 0.004f));
    }

    private static readonly Color[] BookColors =
    {
        Hex("#9C6B5A"), Hex("#6E8A7A"), Hex("#C2A35A"), Hex("#5F6F8F"), Hex("#A88C9E"), Hex("#7D7D7D"), Hex("#B9856A"),
    };

    private static void BuildShelf(Node3D r, ItemSpec s, Color wood, string seedId)
    {
        float w = s.Width, d = s.Depth, h = s.Height, t = 0.03f;
        var m = Mat(wood);
        Block(r, m, new Vector3(-w / 2 + t / 2, h / 2, 0), new Vector3(t, h, d));
        Block(r, m, new Vector3(w / 2 - t / 2, h / 2, 0), new Vector3(t, h, d));
        Block(r, Mat(wood.Darkened(0.15f)), new Vector3(0, h / 2, -d / 2 + 0.01f), new Vector3(w, h, 0.02f));
        const int shelves = 4;
        var rng = new Random(StableHash(seedId));
        for (var i = 0; i <= shelves; i++)
        {
            var y = i * (h - t) / shelves + t / 2;
            Block(r, m, new Vector3(0, y, 0), new Vector3(w, t, d));
            if (i == shelves) break;
            // Livros (cores com semente fixa por objeto: a estante não muda a cada abertura).
            var x = -w / 2 + t + 0.02f;
            var limit = w / 2 - t - 0.02f - (rng.NextDouble() < 0.4 ? 0.2f : 0f);
            while (x < limit - 0.04f)
            {
                var bw = 0.03f + (float)rng.NextDouble() * 0.03f;
                var bh = 0.16f + (float)rng.NextDouble() * 0.1f;
                var c = BookColors[rng.Next(BookColors.Length)];
                Block(r, Mat(c), new Vector3(x + bw / 2, y + t / 2 + bh / 2, 0.01f), new Vector3(bw, bh, d - 0.08f));
                x += bw + 0.004f;
            }
        }
    }

    /// <summary>Hash estável entre execuções (string.GetHashCode muda a cada processo no .NET).</summary>
    private static int StableHash(string s)
    {
        unchecked
        {
            var h = (int)2166136261;
            foreach (var ch in s) h = (h ^ ch) * 16777619;
            return h;
        }
    }

    private static void BuildChair(Node3D r, Color c)
    {
        var m = Mat(c);
        var leg = Mat(c.Darkened(0.25f));
        Block(r, m, new Vector3(0, 0.22f, 0), new Vector3(0.38f, 0.04f, 0.38f));
        foreach (var x in new[] { -0.16f, 0.16f })
        foreach (var z in new[] { -0.16f, 0.16f })
            Block(r, leg, new Vector3(x, 0.1f, z), new Vector3(0.035f, 0.2f, 0.035f));
        Block(r, m, new Vector3(0, 0.42f, -0.17f), new Vector3(0.38f, 0.32f, 0.04f));
    }

    private static void BuildRug(Node3D r, ItemSpec s, Color c)
    {
        Block(r, Mat(c.Darkened(0.12f)), new Vector3(0, 0.004f, 0), new Vector3(s.Width, 0.008f, s.Depth));
        Block(r, Mat(c), new Vector3(0, 0.009f, 0), new Vector3(s.Width - 0.14f, 0.006f, s.Depth - 0.14f));
    }

    private static void BuildPlant(Node3D r, Color leaves)
    {
        Part(r, Cylinder(0.16f, 0.12f, 0.26f), Mat(Hex("#C9A994")), new Vector3(0, 0.13f, 0));
        Part(r, Cylinder(0.14f, 0.14f, 0.01f), Mat(Hex("#5B4A3E")), new Vector3(0, 0.255f, 0));
        var m = Mat(leaves);
        var m2 = Mat(leaves.Darkened(0.15f));
        Ball(r, m, new Vector3(0, 0.45f, 0), new Vector3(0.17f, 0.2f, 0.17f), 12);
        Ball(r, m2, new Vector3(0.08f, 0.6f, 0.03f), new Vector3(0.13f, 0.16f, 0.13f), 12);
        Ball(r, m, new Vector3(-0.07f, 0.66f, -0.04f), new Vector3(0.11f, 0.13f, 0.11f), 12);
        Ball(r, m2, new Vector3(0.01f, 0.76f, 0.02f), new Vector3(0.07f, 0.07f, 0.07f), 10);
    }

    private static void BuildLamp(Node3D r, Color shade)
    {
        var metal = Mat(Hex("#5C5C5C"));
        Part(r, Cylinder(0.12f, 0.13f, 0.03f), metal, new Vector3(0, 0.015f, 0));
        Part(r, Cylinder(0.015f, 0.015f, 0.85f, 8), metal, new Vector3(0, 0.45f, 0));
        // Cúpula "acesa" sem luz de verdade: emissiva, custo zero de iluminação.
        Part(r, Cylinder(0.08f, 0.15f, 0.2f), Mat(shade, emission: 0.35f), new Vector3(0, 0.95f, 0));
    }

    private static void BuildFrame(Node3D r, ItemSpec s, Color canvas)
    {
        float w = s.Width, h = s.Height;
        Block(r, Mat(Hex("#6B5B4F")), Vector3.Zero, new Vector3(w, h, 0.03f));
        Block(r, Mat(canvas), new Vector3(0, 0, 0.016f), new Vector3(w - 0.08f, h - 0.08f, 0.004f));
        Ball(r, Mat(canvas.Darkened(0.25f)), new Vector3(-0.08f, 0.02f, 0.019f), new Vector3(0.08f, 0.08f, 0.002f), 12);
        Block(r, Mat(canvas.Darkened(0.12f)), new Vector3(0.06f, -0.08f, 0.019f), new Vector3(0.28f, 0.06f, 0.002f));
    }

    private static void BuildPoster(Node3D r, PlacedItem it, string postersDir)
    {
        var (w, h) = Room.WallSize(it);
        Block(r, Mat(Hex("#F4F2EE")), Vector3.Zero, new Vector3(w + 0.04f, h + 0.04f, 0.012f));
        var tex = LoadPoster(it.Image, postersDir);
        var mat = tex == null
            ? Mat(Hex("#CFC8BD"), unshaded: true)
            : new StandardMaterial3D
            {
                AlbedoTexture = tex,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            };
        Part(r, new QuadMesh { Size = new Vector2(w, h) }, mat, new Vector3(0, 0, 0.0075f));
    }

    /// <summary>Carrega a cópia do pôster da pasta do app (com cache).</summary>
    public static Texture2D? LoadPoster(string? file, string postersDir)
    {
        if (string.IsNullOrEmpty(file)) return null;
        if (PosterCache.TryGetValue(file, out var cached)) return cached;
        Texture2D? tex = null;
        var path = Path.Combine(postersDir, Path.GetFileName(file));
        if (File.Exists(path))
        {
            var img = Image.LoadFromFile(path);
            if (img != null && !img.IsEmpty())
            {
                img.GenerateMipmaps();
                tex = ImageTexture.CreateFromImage(img);
            }
        }
        PosterCache[file] = tex;
        return tex;
    }

    public static void ForgetPoster(string? file)
    {
        if (file != null) PosterCache.Remove(file);
    }
}
