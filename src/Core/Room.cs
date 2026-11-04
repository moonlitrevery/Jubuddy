using System;
using System.Collections.Generic;
using System.Linq;

namespace Jubuddy.Core;

/// <summary>
/// Regras do quarto: onde cada coisa cabe, o baú, e apagar de vez.
/// Remover um objeto NUNCA apaga: ele vai para o baú. Apagar é só a partir do baú.
/// </summary>
public sealed class Room
{
    public const float DefaultSize = 5f;
    public const float WallHeight = 2.2f;
    public const float WallMarginTop = 0.05f;
    public const float WallMarginBottom = 0.15f;

    public RoomData Data { get; }

    /// <summary>Muda a cada alteração; quem desenha ou navega usa para saber se precisa refazer.</summary>
    public int Revision { get; private set; }

    public Room() : this(new RoomData()) { }

    public Room(RoomData data)
    {
        Data = data;
        Data.Items ??= new();
        Data.Chest ??= new();
        if (Data.Size < 2f || Data.Size > 20f) Data.Size = DefaultSize;
    }

    public float Size => Data.Size;
    public float Half => Data.Size / 2f;
    public IReadOnlyList<PlacedItem> Items => Data.Items;
    public IReadOnlyList<PlacedItem> Chest => Data.Chest;

    public PlacedItem? Find(string id) => Data.Items.FirstOrDefault(i => i.Id == id);
    public PlacedItem? FindInChest(string id) => Data.Chest.FirstOrDefault(i => i.Id == id);
    public bool Has(ItemKind kind) => Data.Items.Any(i => i.Kind == kind);
    public IEnumerable<PlacedItem> OfKind(ItemKind kind) => Data.Items.Where(i => i.Kind == kind);

    public void Touch() => Revision++;

    // ---------- Medidas ----------

    /// <summary>Largura e profundidade ocupadas no chão, considerando o giro.</summary>
    public static (float W, float D) FloorSize(PlacedItem it)
    {
        var s = it.Spec;
        return (it.Rotation & 1) == 0 ? (s.Width, s.Depth) : (s.Depth, s.Width);
    }

    public static Rect2D FloorRect(PlacedItem it)
    {
        var (w, d) = FloorSize(it);
        return Rect2D.FromCenter(it.Position, w, d);
    }

    /// <summary>Largura e altura de um objeto de parede (pôster segue a proporção da imagem).</summary>
    public static (float W, float H) WallSize(PlacedItem it)
    {
        var s = it.Spec;
        if (it.Kind == ItemKind.Poster)
        {
            var aspect = Math.Clamp(it.Aspect <= 0 ? 1f : it.Aspect, 0.4f, 2.5f);
            var h = s.Height;
            var w = h * aspect;
            if (w > 1.4f) { w = 1.4f; h = w / aspect; }
            return (w, h);
        }
        return (s.Width, s.Height);
    }

    /// <summary>Yaw (radianos) do objeto de chão.</summary>
    public static float YawOf(PlacedItem it) => it.Rotation * MathF.PI / 2f;

    /// <summary>Retângulos que o buddy não atravessa.</summary>
    public IEnumerable<Rect2D> Obstacles() =>
        Data.Items.Where(i => !i.Spec.OnWall && i.Spec.Blocks).Select(FloorRect);

    // ---------- Onde cabe ----------

    public bool CanPlace(PlacedItem c)
    {
        if (c.Spec.OnWall) return CanPlaceOnWall(c);

        var r = FloorRect(c);
        if (r.MinX < -Half - 0.001f || r.MaxX > Half + 0.001f || r.MinZ < -Half - 0.001f || r.MaxZ > Half + 0.001f)
            return false;

        // Tapete não bloqueia e pode ficar embaixo de qualquer coisa.
        if (!c.Spec.Blocks) return !Data.Items.Any(o => o.Id != c.Id && o.Kind == ItemKind.Rug && c.Kind == ItemKind.Rug && FloorRect(o).Overlaps(r));

        return !Data.Items.Any(o => o.Id != c.Id && !o.Spec.OnWall && o.Spec.Blocks && FloorRect(o).Overlaps(r));
    }

    private bool CanPlaceOnWall(PlacedItem c)
    {
        if (c.Wall == WallSide.None) return false;
        var (w, h) = WallSize(c);
        if (c.Along - w / 2 < -Half - 0.001f || c.Along + w / 2 > Half + 0.001f) return false;
        if (c.Y - h / 2 < WallMarginBottom - 0.001f || c.Y + h / 2 > WallHeight - WallMarginTop + 0.001f) return false;

        var mine = WallRect(c);
        return !Data.Items.Any(o => o.Id != c.Id && o.Spec.OnWall && o.Wall == c.Wall && WallRect(o).Overlaps(mine));
    }

    /// <summary>Retângulo na parede, usando (ao longo, altura) como se fosse (X, Z).</summary>
    private static Rect2D WallRect(PlacedItem it)
    {
        var (w, h) = WallSize(it);
        return Rect2D.FromCenter(new Vec2(it.Along, it.Y), w, h);
    }

    /// <summary>Procura um lugar livre para o objeto, começando perto do ponto preferido.</summary>
    public bool TryFindFreeSpot(PlacedItem it, Vec2? near = null)
    {
        var probe = it.Clone();
        if (probe.Spec.OnWall)
        {
            var walls = probe.Wall == WallSide.None ? new[] { WallSide.Back, WallSide.Left } : new[] { probe.Wall, probe.Wall == WallSide.Back ? WallSide.Left : WallSide.Back };
            foreach (var wall in walls)
            {
                probe.Wall = wall;
                foreach (var y in new[] { 1.35f, 1.0f, 1.65f, 0.7f })
                {
                    for (var step = 0; step <= (int)(Size / 0.1f); step++)
                    {
                        // 0, +0.1, -0.1, +0.2, ...
                        var off = (step + 1) / 2 * 0.1f * (step % 2 == 0 ? -1 : 1);
                        probe.Along = (near?.X ?? 0f) + off;
                        probe.Y = y;
                        if (CanPlace(probe)) { Copy(probe, it); return true; }
                    }
                }
            }
            return false;
        }

        var start = near ?? Vec2.Zero;
        var candidates = new List<Vec2>();
        const float step2 = 0.1f;
        var n = (int)(Size / step2);
        for (var ix = 0; ix <= n; ix++)
        for (var iz = 0; iz <= n; iz++)
            candidates.Add(new Vec2(-Half + ix * step2, -Half + iz * step2));
        foreach (var p in candidates.OrderBy(p => Vec2.Distance(p, start)))
        {
            probe.X = MathF.Round(p.X, 2);
            probe.Z = MathF.Round(p.Z, 2);
            if (CanPlace(probe)) { Copy(probe, it); return true; }
        }
        return false;
    }

    private static void Copy(PlacedItem from, PlacedItem to)
    {
        to.X = from.X; to.Z = from.Z; to.Rotation = from.Rotation;
        to.Wall = from.Wall; to.Along = from.Along; to.Y = from.Y;
    }

    // ---------- Alterações ----------

    /// <summary>Cria e põe um objeto num lugar livre. Retorna null se não couber em lugar nenhum.</summary>
    public PlacedItem? Add(ItemKind kind, string why, Vec2? near = null, string? image = null, float aspect = 1f)
    {
        var it = new PlacedItem { Kind = kind, Why = why.Trim(), Image = image, Aspect = aspect, AddedAt = DateTime.Now };
        if (it.Spec.OnWall) it.Wall = WallSide.Back;
        if (!TryFindFreeSpot(it, near)) return null;
        Data.Items.Add(it);
        Touch();
        return it;
    }

    public bool TryMoveFloor(string id, Vec2 pos, int rotation)
    {
        var it = Find(id) ?? throw new InvalidOperationException("Objeto não está no quarto.");
        var probe = it.Clone();
        probe.X = pos.X; probe.Z = pos.Z; probe.Rotation = ((rotation % 4) + 4) % 4;
        if (!CanPlace(probe)) return false;
        Copy(probe, it);
        Touch();
        return true;
    }

    public bool TryMoveWall(string id, WallSide wall, float along, float y)
    {
        var it = Find(id) ?? throw new InvalidOperationException("Objeto não está no quarto.");
        var probe = it.Clone();
        probe.Wall = wall; probe.Along = along; probe.Y = y;
        if (!CanPlace(probe)) return false;
        Copy(probe, it);
        Touch();
        return true;
    }

    /// <summary>Gira 90°. Se não couber girado no lugar, tenta um lugar livre perto.</summary>
    public bool TryRotate(string id)
    {
        var it = Find(id) ?? throw new InvalidOperationException("Objeto não está no quarto.");
        if (it.Spec.OnWall) return false;
        var probe = it.Clone();
        probe.Rotation = (it.Rotation + 1) % 4;
        if (!CanPlace(probe) && !TryFindFreeSpot(probe, it.Position)) return false;
        Copy(probe, it);
        Touch();
        return true;
    }

    public void SetWhy(string id, string why)
    {
        var it = Find(id) ?? FindInChest(id) ?? throw new InvalidOperationException("Objeto não encontrado.");
        it.Why = why.Trim();
        Touch();
    }

    public void SetColor(string id, string? hex)
    {
        var it = Find(id) ?? throw new InvalidOperationException("Objeto não está no quarto.");
        it.Color = string.IsNullOrWhiteSpace(hex) ? null : hex;
        Touch();
    }

    // ---------- Baú ----------

    /// <summary>Tira do quarto e guarda no baú. Nada é apagado.</summary>
    public PlacedItem StoreInChest(string id)
    {
        var it = Find(id) ?? throw new InvalidOperationException("Objeto não está no quarto.");
        Data.Items.Remove(it);
        it.StoredAt = DateTime.Now;
        Data.Chest.Add(it);
        Touch();
        return it;
    }

    /// <summary>
    /// Devolve do baú para o quarto. Tenta o lugar antigo; se estiver ocupado, procura o lugar livre mais perto.
    /// Se o quarto estiver cheio, volta para o lugar antigo mesmo assim (nunca perde o objeto).
    /// </summary>
    public PlacedItem RestoreFromChest(string id)
    {
        var it = FindInChest(id) ?? throw new InvalidOperationException("Objeto não está no baú.");
        Data.Chest.Remove(it);
        if (!CanPlace(it)) TryFindFreeSpot(it, it.Spec.OnWall ? new Vec2(it.Along, 0) : it.Position);
        it.StoredAt = null;
        Data.Items.Add(it);
        Touch();
        return it;
    }

    /// <summary>
    /// Apaga de vez. Só funciona para o que já está no baú: do quarto, nada é apagado direto.
    /// A confirmação com a pessoa é responsabilidade da interface.
    /// Retorna o objeto apagado (para quem chamou apagar a imagem do pôster, se houver).
    /// </summary>
    public PlacedItem DeleteForever(string id)
    {
        if (Find(id) != null)
            throw new InvalidOperationException("Objetos no quarto não podem ser apagados direto: guarde no baú primeiro.");
        var it = FindInChest(id) ?? throw new InvalidOperationException("Objeto não está no baú.");
        Data.Chest.Remove(it);
        Touch();
        return it;
    }

    /// <summary>Imagem ainda usada por algum objeto (no quarto ou no baú)?</summary>
    public bool ImageInUse(string fileName) =>
        Data.Items.Concat(Data.Chest).Any(i => string.Equals(i.Image, fileName, StringComparison.OrdinalIgnoreCase));
}
