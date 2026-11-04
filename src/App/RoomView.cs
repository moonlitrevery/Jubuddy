using System.Collections.Generic;
using Godot;
using Jubuddy.Core;
using static Jubuddy.App.Visuals;

namespace Jubuddy.App;

/// <summary>Desenha o quarto (chão, duas paredes em corte, estilo diorama) e os objetos.</summary>
public partial class RoomView : Node3D
{
    private Node3D _shell = null!;
    private Node3D _itemsRoot = null!;
    private readonly Dictionary<string, Node3D> _nodes = new();
    private MeshInstance3D _marker = null!;
    private int _builtRevision = -1;
    private Room? _room;
    private string _postersDir = "";

    public override void _Ready()
    {
        _shell = new Node3D { Name = "Shell" };
        _itemsRoot = new Node3D { Name = "Items" };
        AddChild(_shell);
        AddChild(_itemsRoot);
        _marker = new MeshInstance3D
        {
            Name = "Marker",
            Mesh = new BoxMesh { Size = Vector3.One },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_marker);
    }

    public void BuildShell(float size, AppSettings settings)
    {
        foreach (var c in _shell.GetChildren()) c.QueueFree();
        const float t = 0.12f;
        var h = Room.WallHeight;
        var floor = Hex(settings.Floor, Hex("#C8B9A6"));
        var walls = Hex(settings.Walls, Hex("#E6E1D8"));

        // Base do diorama + piso.
        Block(_shell, Mat(floor.Darkened(0.35f)), new Vector3(-t / 2, -0.11f, -t / 2), new Vector3(size + t, 0.2f, size + t));
        Block(_shell, Mat(floor), new Vector3(0, -0.005f, 0), new Vector3(size, 0.01f, size));
        // Duas paredes, o resto fica aberto para a câmera.
        Block(_shell, Mat(walls), new Vector3(-t / 2, h / 2, -size / 2 - t / 2), new Vector3(size + t, h, t));
        Block(_shell, Mat(walls.Darkened(0.06f)), new Vector3(-size / 2 - t / 2, h / 2, 0), new Vector3(t, h, size));
        // Rodapés.
        var trim = Mat(walls.Darkened(0.18f));
        Block(_shell, trim, new Vector3(0, 0.04f, -size / 2 + 0.01f), new Vector3(size, 0.08f, 0.02f));
        Block(_shell, trim, new Vector3(-size / 2 + 0.01f, 0.04f, 0), new Vector3(0.02f, 0.08f, size));
    }

    /// <summary>Redesenha os objetos se o quarto mudou.</summary>
    public void Sync(Room room, string postersDir, bool force = false)
    {
        _postersDir = postersDir;
        if (!force && ReferenceEquals(room, _room) && room.Revision == _builtRevision) return;
        _room = room;
        _builtRevision = room.Revision;

        foreach (var n in _nodes.Values) n.QueueFree();
        _nodes.Clear();
        foreach (var it in room.Items)
        {
            var node = FurnitureBuilder.Build(it, postersDir);
            _itemsRoot.AddChild(node);
            Place(node, it, room.Half);
            _nodes[it.Id] = node;
        }
    }

    public static void Place(Node3D node, PlacedItem it, float half)
    {
        if (!it.Spec.OnWall)
        {
            node.Position = new Vector3(it.X, 0, it.Z);
            node.Rotation = new Vector3(0, Room.YawOf(it), 0);
            return;
        }
        var d = it.Spec.Depth / 2 + 0.002f;
        if (it.Wall == WallSide.Left)
        {
            node.Position = new Vector3(-half + d, it.Y, it.Along);
            node.Rotation = new Vector3(0, Mathf.Pi / 2, 0);
        }
        else
        {
            node.Position = new Vector3(it.Along, it.Y, -half + d);
            node.Rotation = Vector3.Zero;
        }
    }

    public Node3D? NodeOf(string id) => _nodes.TryGetValue(id, out var n) ? n : null;

    /// <summary>Qual objeto está sob o cursor (teste de raio contra a caixa de cada objeto, sem física).</summary>
    public string? Pick(Camera3D cam, Vector2 screen)
    {
        if (_room == null) return null;
        var from = cam.ProjectRayOrigin(screen);
        var dir = cam.ProjectRayNormal(screen);
        string? best = null;
        var bestDist = float.MaxValue;
        foreach (var it in _room.Items)
        {
            if (!_nodes.TryGetValue(it.Id, out var node)) continue;
            var inv = node.GlobalTransform.AffineInverse();
            var lf = inv * from;
            var ld = (inv.Basis * dir).Normalized();
            if (!RayBox(FurnitureBuilder.LocalBounds(it), lf, ld, out var tHit)) continue;
            var dist = (node.GlobalTransform * (lf + ld * tHit) - from).Length();
            // Tapete perde para o que estiver em cima dele.
            if (it.Kind == ItemKind.Rug) dist += 50f;
            if (dist < bestDist) { bestDist = dist; best = it.Id; }
        }
        return best;
    }

    /// <summary>Raio contra caixa alinhada (método das "fatias").</summary>
    private static bool RayBox(Aabb box, Vector3 o, Vector3 d, out float t)
    {
        var tMin = float.NegativeInfinity;
        var tMax = float.PositiveInfinity;
        var min = box.Position;
        var max = box.End;
        for (var i = 0; i < 3; i++)
        {
            if (Mathf.Abs(d[i]) < 1e-6f)
            {
                if (o[i] < min[i] || o[i] > max[i]) { t = 0; return false; }
                continue;
            }
            var t1 = (min[i] - o[i]) / d[i];
            var t2 = (max[i] - o[i]) / d[i];
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Mathf.Max(tMin, t1);
            tMax = Mathf.Min(tMax, t2);
            if (tMin > tMax) { t = 0; return false; }
        }
        t = tMin >= 0 ? tMin : tMax;
        return tMax >= 0;
    }

    /// <summary>Caixa translúcida em volta do objeto: neutra quando selecionado, verde/vermelha ao mover.</summary>
    public void ShowMarker(PlacedItem? it, Color color)
    {
        if (it == null || _room == null) { _marker.Visible = false; return; }
        var b = FurnitureBuilder.LocalBounds(it);
        var probe = new Node3D();
        Place(probe, it, _room.Half);
        var center = b.Position + b.Size / 2;
        _marker.Transform = new Transform3D(probe.Basis, probe.Position + probe.Basis * center)
            .ScaledLocal(b.Size + new Vector3(0.04f, 0.04f, 0.04f));
        probe.Free();
        _marker.MaterialOverride = Mat(color, unshaded: true, transparent: true);
        _marker.Visible = true;
    }
}
