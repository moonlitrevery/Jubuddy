using System.Collections.Generic;
using Godot;

namespace Jubuddy.App;

/// <summary>
/// Materiais e malhas compartilhados. Tudo é forma simples (caixa, esfera, cápsula, cilindro),
/// e cada cor/tamanho existe uma vez só na memória.
/// </summary>
public static class Visuals
{
    private static readonly Dictionary<(Color, bool, float, bool), StandardMaterial3D> Materials = new();
    private static readonly Dictionary<string, Mesh> Meshes = new();

    public static Color Hex(string? hex, Color fallback) =>
        !string.IsNullOrWhiteSpace(hex) && Color.HtmlIsValid(hex) ? Color.FromHtml(hex) : fallback;

    public static Color Hex(string hex) => Hex(hex, Colors.Magenta);

    /// <summary>Material fosco com iluminação por vértice (barato) e sem brilho especular.</summary>
    public static StandardMaterial3D Mat(Color c, bool unshaded = false, float emission = 0f, bool transparent = false)
    {
        var key = (c, unshaded, emission, transparent);
        if (Materials.TryGetValue(key, out var m)) return m;
        // Arrastar o seletor de cor gera muitas cores de passagem; os nós ativos mantêm as suas.
        if (Materials.Count > 600) Materials.Clear();
        m = new StandardMaterial3D
        {
            AlbedoColor = c,
            Roughness = 1f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            ShadingMode = unshaded ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerVertex,
        };
        if (emission > 0)
        {
            m.EmissionEnabled = true;
            m.Emission = c;
            m.EmissionEnergyMultiplier = emission;
        }
        if (transparent || c.A < 0.999f)
        {
            m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            m.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        }
        Materials[key] = m;
        return m;
    }

    public static Mesh Box(Vector3 size)
    {
        var key = $"box{size}";
        if (!Meshes.TryGetValue(key, out var mesh)) Meshes[key] = mesh = new BoxMesh { Size = size };
        return mesh;
    }

    /// <summary>Esfera de raio 1; o tamanho vem da escala do nó (assim todas compartilham a mesma malha).</summary>
    public static Mesh Sphere(int segments = 16)
    {
        var key = $"sph{segments}";
        if (!Meshes.TryGetValue(key, out var mesh))
            Meshes[key] = mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = segments, Rings = segments / 2 };
        return mesh;
    }

    public static Mesh Capsule(float radius, float height)
    {
        var key = $"cap{radius}:{height}";
        if (!Meshes.TryGetValue(key, out var mesh))
            Meshes[key] = mesh = new CapsuleMesh { Radius = radius, Height = height, RadialSegments = 14, Rings = 4 };
        return mesh;
    }

    public static Mesh Cylinder(float top, float bottom, float height, int segments = 16)
    {
        var key = $"cyl{top}:{bottom}:{height}:{segments}";
        if (!Meshes.TryGetValue(key, out var mesh))
            Meshes[key] = mesh = new CylinderMesh { TopRadius = top, BottomRadius = bottom, Height = height, RadialSegments = segments, Rings = 1 };
        return mesh;
    }

    public static Mesh Torus(float inner, float outer)
    {
        var key = $"tor{inner}:{outer}";
        if (!Meshes.TryGetValue(key, out var mesh))
            Meshes[key] = mesh = new TorusMesh { InnerRadius = inner, OuterRadius = outer, Rings = 16, RingSegments = 6 };
        return mesh;
    }

    /// <summary>Cria uma peça (malha + material) presa a <paramref name="parent"/>. Nunca projeta sombra.</summary>
    public static MeshInstance3D Part(Node3D parent, Mesh mesh, Material mat, Vector3 pos, Vector3? scale = null, Vector3? rotationDeg = null)
    {
        var mi = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = pos,
        };
        if (scale is { } s) mi.Scale = s;
        if (rotationDeg is { } r) mi.RotationDegrees = r;
        parent.AddChild(mi);
        return mi;
    }

    public static MeshInstance3D Ball(Node3D parent, Material mat, Vector3 pos, Vector3 radii, int segments = 16) =>
        Part(parent, Sphere(segments), mat, pos, radii);

    public static MeshInstance3D Block(Node3D parent, Material mat, Vector3 pos, Vector3 size, Vector3? rotationDeg = null) =>
        Part(parent, Box(size), mat, pos, null, rotationDeg);
}
