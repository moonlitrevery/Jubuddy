using System;
using System.Collections.Generic;
using System.Linq;

namespace Jubuddy.Core;

public enum ItemKind { Bed, DeskPc, Bookshelf, Chair, Rug, Plant, Lamp, Frame, Poster }

public enum WallSide { None, Back, Left }

/// <summary>
/// Medidas de um tipo de objeto, na escala chibi do quarto (o buddy tem ~1,1 de altura).
/// Width é o eixo X local, Depth o eixo Z local (a "frente" do objeto aponta para +Z).
/// </summary>
public sealed record ItemSpec(
    ItemKind Kind,
    string Name,
    float Width,
    float Depth,
    float Height,
    bool OnWall,
    bool Blocks,
    float SeatHeight = 0f);

public static class Catalog
{
    public static readonly IReadOnlyList<ItemSpec> All = new[]
    {
        new ItemSpec(ItemKind.Bed,       "Cama",        1.0f, 1.7f,  0.32f, OnWall: false, Blocks: true, SeatHeight: 0.32f),
        new ItemSpec(ItemKind.DeskPc,    "Mesa com PC", 1.0f, 0.5f,  0.48f, OnWall: false, Blocks: true),
        new ItemSpec(ItemKind.Bookshelf, "Estante",     0.8f, 0.3f,  1.3f,  OnWall: false, Blocks: true),
        new ItemSpec(ItemKind.Chair,     "Cadeira",     0.38f, 0.38f, 0.6f, OnWall: false, Blocks: true, SeatHeight: 0.24f),
        new ItemSpec(ItemKind.Rug,       "Tapete",      1.6f, 1.1f,  0.012f, OnWall: false, Blocks: false, SeatHeight: 0.01f),
        new ItemSpec(ItemKind.Plant,     "Planta",      0.4f, 0.4f,  0.8f,  OnWall: false, Blocks: true),
        new ItemSpec(ItemKind.Lamp,      "Luminária",   0.3f, 0.3f,  1.1f,  OnWall: false, Blocks: true),
        new ItemSpec(ItemKind.Frame,     "Quadro",      0.6f, 0.03f, 0.45f, OnWall: true,  Blocks: false),
        new ItemSpec(ItemKind.Poster,    "Pôster",      0.6f, 0.02f, 0.8f,  OnWall: true,  Blocks: false),
    };

    public static ItemSpec Get(ItemKind kind) => All.First(s => s.Kind == kind);

    public static string NameOf(ItemKind kind) => Get(kind).Name;

    /// <summary>Objetos "de enfeite", que o buddy às vezes para e fica olhando.</summary>
    public static bool IsDecor(ItemKind kind) =>
        kind is ItemKind.Plant or ItemKind.Lamp or ItemKind.Frame or ItemKind.Poster;
}
