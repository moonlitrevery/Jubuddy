using System;

namespace Jubuddy.Core;

/// <summary>Ponto no chão do quarto (X para a direita, Z para a frente). Sem dependência do Godot.</summary>
public readonly record struct Vec2(float X, float Z)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Z + b.Z);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Z - b.Z);
    public static Vec2 operator *(Vec2 a, float s) => new(a.X * s, a.Z * s);

    public float Length => MathF.Sqrt(X * X + Z * Z);

    public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;

    /// <summary>Direção para um ângulo de giro (yaw). Yaw 0 olha para +Z, como no Godot.</summary>
    public static Vec2 FromYaw(float yaw) => new(MathF.Sin(yaw), MathF.Cos(yaw));

    /// <summary>Yaw para olhar deste ponto até <paramref name="target"/>.</summary>
    public float YawTo(Vec2 target)
    {
        var d = target - this;
        return MathF.Atan2(d.X, d.Z);
    }

    public override string ToString() => $"({X:0.00}, {Z:0.00})";
}

/// <summary>Retângulo alinhado aos eixos, no plano do chão.</summary>
public readonly record struct Rect2D(float MinX, float MinZ, float MaxX, float MaxZ)
{
    public static Rect2D FromCenter(Vec2 c, float width, float depth) =>
        new(c.X - width / 2, c.Z - depth / 2, c.X + width / 2, c.Z + depth / 2);

    public Vec2 Center => new((MinX + MaxX) / 2, (MinZ + MaxZ) / 2);

    /// <summary>Sobreposição com folga: encostar não conta como sobrepor.</summary>
    public bool Overlaps(Rect2D o, float eps = 0.001f) =>
        MinX < o.MaxX - eps && o.MinX < MaxX - eps && MinZ < o.MaxZ - eps && o.MinZ < MaxZ - eps;

    public bool Contains(Vec2 p) => p.X >= MinX && p.X <= MaxX && p.Z >= MinZ && p.Z <= MaxZ;

    public Rect2D Inflate(float r) => new(MinX - r, MinZ - r, MaxX + r, MaxZ + r);
}
