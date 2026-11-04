using System;
using Godot;
using Jubuddy.Core;
using static Jubuddy.App.Visuals;

namespace Jubuddy.App.Buddy;

public enum BuddyAnim { Idle, Walk, Sweep, Read, Type, Sit, Sleep, Wave, PickBook }

/// <summary>
/// O bonequinho, todo em formas simples no estilo chibi (cabeça grande, corpo pequeno),
/// com animações procedurais calculadas em código a cada quadro.
/// Origem nos pés; olha para +Z local. O lado direito dele é -X.
/// </summary>
public partial class BuddyBody : Node3D
{
    private const float HipY = 0.24f;

    public BuddyAnim Anim { get; set; } = BuddyAnim.Idle;
    public Posture Posture { get; set; } = Posture.Stand;
    public float SeatHeight { get; set; }
    public float TargetYaw { get; set; }
    public float WalkSpeedFactor { get; set; } = 1f;

    private Node3D _pose = null!, _hips = null!, _torso = null!, _neck = null!, _head = null!;
    private Node3D _armL = null!, _armR = null!, _legL = null!, _legR = null!;
    private Node3D _broom = null!, _book = null!;
    private Node3D _eyeL = null!, _eyeR = null!;
    private MeshInstance3D _shadow = null!;
    private MeshInstance3D _zzz = null!;
    private StandardMaterial3D _zzzMat = null!;

    private float _t, _walkPhase, _blinkIn = 3f, _blinkLeft;
    private float _yaw;
    private float _headScale = 1f;
    private readonly Random _rng = new();

    // Rotações atuais (suavizadas) de cada junta.
    private Vector3 _rArmL, _rArmR, _rLegL, _rLegR, _rTorso, _rNeck, _rPose;
    private Vector3 _pPose;

    public override void _Ready()
    {
        // "z" do sono: um quadradinho com textura desenhada em código (8 KB), sem carregar fonte nenhuma.
        _zzzMat = new StandardMaterial3D
        {
            AlbedoTexture = MakeZTexture(),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
        };
        _zzz = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(0.16f, 0.16f) },
            MaterialOverride = _zzzMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_zzz);
    }

    /// <summary>Desenha um "Z" com contorno numa imagem 32×32.</summary>
    private static Texture2D MakeZTexture()
    {
        const int n = 32;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        var ink = new Color(1, 1, 1, 1);
        var edge = new Color(0.2f, 0.2f, 0.25f, 0.7f);
        bool IsZ(int x, int y)
        {
            if (x < 6 || x > 25 || y < 6 || y > 25) return false;
            if (y <= 9 || y >= 22) return true;                // barras de cima e de baixo
            var diag = 25 - (y - 6) * 19 / 16;                 // diagonal
            return Mathf.Abs(x - diag) <= 2;
        }
        for (var y = 0; y < n; y++)
        for (var x = 0; x < n; x++)
        {
            if (IsZ(x, y)) { img.SetPixel(x, y, ink); continue; }
            var near = false;
            for (var dy = -2; dy <= 2 && !near; dy++)
            for (var dx = -2; dx <= 2 && !near; dx++)
                near = IsZ(x + dx, y + dy);
            if (near) img.SetPixel(x, y, edge);
        }
        return ImageTexture.CreateFromImage(img);
    }

    public void Rebuild(BuddyLook look)
    {
        look.Clamp();
        foreach (var c in GetChildren())
            if (c != _zzz) { RemoveChild(c); c.QueueFree(); }

        Scale = Vector3.One * look.Height;
        _headScale = look.HeadSize;

        var skin = Mat(Hex(look.Skin, Hex("#E8B796")));
        var hair = Mat(Hex(look.Hair, Hex("#3B2A20")));
        var top = Mat(Hex(look.Top, Hex("#7A93B8")));
        var bottom = Mat(Hex(look.Bottom, Hex("#4A4F5C")));
        var shoes = Mat(Hex(look.Shoes, Hex("#33302E")));
        var eyes = Mat(Hex(look.Eyes, Hex("#2B1D14")));

        // Sombra de bolha (sem sombra real).
        _shadow = Part(this, Cylinder(1f, 1f, 0.002f, 20), Mat(new Color(0, 0, 0, 0.22f), unshaded: true, transparent: true),
            new Vector3(0, 0.004f, 0), new Vector3(0.2f, 1f, 0.2f));

        _pose = new Node3D { Name = "Pose" };
        AddChild(_pose);
        _hips = new Node3D { Name = "Hips", Position = new Vector3(0, HipY, 0) };
        _pose.AddChild(_hips);

        // Pernas: pivô no quadril.
        var legMat = look.Outfit == Outfit.Vestido ? skin : bottom;
        _legL = Joint(_hips, new Vector3(0.065f, 0, 0));
        _legR = Joint(_hips, new Vector3(-0.065f, 0, 0));
        foreach (var leg in new[] { _legL, _legR })
        {
            Part(leg, Capsule(0.058f, 0.24f), legMat, new Vector3(0, -0.1f, 0));
            Ball(leg, shoes, new Vector3(0, -0.215f, 0.025f), new Vector3(0.065f, 0.045f, 0.09f), 12);
        }

        // Tronco.
        _torso = Joint(_hips, Vector3.Zero);
        Part(_torso, Capsule(0.125f, 0.34f), top, new Vector3(0, 0.15f, 0), new Vector3(1f, 1f, 0.85f));
        if (look.Outfit == Outfit.Vestido)
            Part(_torso, Cylinder(0.12f, 0.2f, 0.2f), bottom, new Vector3(0, 0.02f, 0));
        else
            Ball(_torso, bottom, new Vector3(0, 0.03f, 0), new Vector3(0.128f, 0.07f, 0.11f), 14);
        if (look.Outfit == Outfit.Moletom)
            Ball(_torso, Mat(Hex(look.Top).Darkened(0.12f)), new Vector3(0, 0.28f, -0.09f), new Vector3(0.13f, 0.07f, 0.07f), 12);

        // Braços: pivô no ombro.
        var sleeve = look.Outfit == Outfit.Moletom ? top : skin;
        _armL = Joint(_torso, new Vector3(0.155f, 0.27f, 0));
        _armR = Joint(_torso, new Vector3(-0.155f, 0.27f, 0));
        foreach (var arm in new[] { _armL, _armR })
        {
            Part(arm, Capsule(0.042f, 0.22f), sleeve, new Vector3(0, -0.09f, 0));
            if (look.Outfit != Outfit.Moletom)
                Ball(arm, top, new Vector3(0, -0.015f, 0), new Vector3(0.055f, 0.06f, 0.055f), 12);
            Ball(arm, skin, new Vector3(0, -0.2f, 0), new Vector3(0.05f, 0.05f, 0.05f), 12);
        }

        // Cabeça.
        _neck = Joint(_torso, new Vector3(0, 0.33f, 0));
        _head = Joint(_neck, new Vector3(0, 0.25f * look.HeadSize, 0));
        _head.Scale = Vector3.One * look.HeadSize;
        const float R = 0.27f;
        Ball(_head, skin, Vector3.Zero, new Vector3(R, R * 0.96f, R), 20);
        _eyeL = Joint(_head, new Vector3(0.095f, -0.01f, 0.245f));
        _eyeR = Joint(_head, new Vector3(-0.095f, -0.01f, 0.245f));
        Ball(_eyeL, eyes, Vector3.Zero, new Vector3(0.032f, 0.045f, 0.02f), 10);
        Ball(_eyeR, eyes, Vector3.Zero, new Vector3(0.032f, 0.045f, 0.02f), 10);
        var shine = Mat(Colors.White, unshaded: true);
        Ball(_eyeL, shine, new Vector3(0.01f, 0.015f, 0.012f), new Vector3(0.009f, 0.009f, 0.006f), 6);
        Ball(_eyeR, shine, new Vector3(0.01f, 0.015f, 0.012f), new Vector3(0.009f, 0.009f, 0.006f), 6);
        Ball(_head, Mat(Hex(look.Skin).Darkened(0.35f)), new Vector3(0, -0.105f, 0.243f), new Vector3(0.028f, 0.01f, 0.012f), 8);
        if (look.Blush)
        {
            var blush = Mat(Hex(look.Skin).Lerp(Hex("#F08A8A"), 0.45f));
            Ball(_head, blush, new Vector3(0.155f, -0.07f, 0.205f), new Vector3(0.04f, 0.022f, 0.012f), 8);
            Ball(_head, blush, new Vector3(-0.155f, -0.07f, 0.205f), new Vector3(0.04f, 0.022f, 0.012f), 8);
        }
        BuildHair(look, hair);
        if (look.Glasses) BuildGlasses(Mat(Hex(look.GlassesColor, Hex("#2A2A2A"))));

        BuildProps();
        SnapPose();
    }

    private static Node3D Joint(Node3D parent, Vector3 pos)
    {
        var n = new Node3D { Position = pos };
        parent.AddChild(n);
        return n;
    }

    private void BuildHair(BuddyLook look, Material hair)
    {
        const float R = 0.27f;
        switch (look.HairStyle)
        {
            case HairStyle.Raspado:
                Ball(_head, hair, new Vector3(0, 0.06f, -0.035f), new Vector3(R * 1.015f, R * 0.93f, R * 1.0f), 18);
                return;
            case HairStyle.Cacheado:
                Ball(_head, hair, new Vector3(0, 0.04f, -0.035f), new Vector3(R * 1.1f, R * 0.98f, R * 1.06f), 18);
                for (var i = 0; i < 14; i++)
                {
                    var a = i / 14f * Mathf.Tau;
                    var y = 0.05f + 0.13f * Mathf.Sin(i * 1.7f);
                    var p = new Vector3(Mathf.Cos(a) * 0.27f, y, Mathf.Sin(a) * 0.27f - 0.04f);
                    if (p.Z > 0.12f && p.Y < 0.12f) continue; // não cobre o rosto
                    Ball(_head, hair, p, Vector3.One * 0.075f, 10);
                }
                Ball(_head, hair, new Vector3(0, 0.27f, -0.02f), Vector3.One * 0.09f, 10);
                return;
        }

        // Base comum: "boné" de cabelo levemente atrás, que deixa o rosto aparecendo.
        Ball(_head, hair, new Vector3(0, 0.035f, -0.03f), new Vector3(R * 1.06f, R * 0.98f, R * 1.06f), 18);
        // Franja de lado.
        Ball(_head, hair, new Vector3(0.05f, 0.15f, 0.18f), new Vector3(0.19f, 0.075f, 0.11f), 12).RotationDegrees = new Vector3(15, 0, -14);

        switch (look.HairStyle)
        {
            case HairStyle.Comprido:
                Ball(_head, hair, new Vector3(0, -0.12f, -0.12f), new Vector3(0.27f, 0.34f, 0.15f), 14);
                Ball(_head, hair, new Vector3(0.22f, -0.1f, 0.0f), new Vector3(0.07f, 0.22f, 0.09f), 10);
                Ball(_head, hair, new Vector3(-0.22f, -0.1f, 0.0f), new Vector3(0.07f, 0.22f, 0.09f), 10);
                break;
            case HairStyle.Coque:
                Ball(_head, hair, new Vector3(0, 0.25f, -0.13f), Vector3.One * 0.11f, 12);
                break;
            case HairStyle.RaboDeCavalo:
                Ball(_head, hair, new Vector3(0, 0.1f, -0.28f), Vector3.One * 0.05f, 8);
                Part(_head, Capsule(0.055f, 0.3f), hair, new Vector3(0, -0.04f, -0.31f), null, new Vector3(-18, 0, 0));
                break;
        }
    }

    private void BuildGlasses(Material m)
    {
        foreach (var x in new[] { 0.095f, -0.095f })
            Part(_head, Torus(0.042f, 0.052f), m, new Vector3(x, -0.01f, 0.262f), null, new Vector3(90, 0, 0));
        Block(_head, m, new Vector3(0, 0.0f, 0.268f), new Vector3(0.05f, 0.01f, 0.01f));
    }

    private void BuildProps()
    {
        // Vassoura: presa ao tronco, na frente, pendendo para baixo.
        _broom = Joint(_torso, new Vector3(-0.02f, 0.12f, 0.2f));
        Part(_broom, Cylinder(0.014f, 0.014f, 0.72f, 6), Mat(Hex("#A4865F")), new Vector3(0, -0.22f, 0));
        Block(_broom, Mat(Hex("#D9C27A")), new Vector3(0, -0.6f, 0), new Vector3(0.2f, 0.09f, 0.05f));
        Block(_broom, Mat(Hex("#B4342F").Lerp(Colors.Gray, 0.5f)), new Vector3(0, -0.545f, 0), new Vector3(0.21f, 0.025f, 0.055f));
        _broom.RotationDegrees = new Vector3(35, 0, 0);
        _broom.Visible = false;

        // Livro aberto na frente do peito.
        _book = Joint(_torso, new Vector3(0, 0.13f, 0.21f));
        Block(_book, Mat(Hex("#7D5A50")), new Vector3(0, 0, -0.004f), new Vector3(0.26f, 0.18f, 0.012f));
        Block(_book, Mat(Hex("#F5F1E6")), new Vector3(0.0f, 0, 0.004f), new Vector3(0.24f, 0.165f, 0.01f));
        Block(_book, Mat(Hex("#D8D2C4")), new Vector3(0, 0, 0.01f), new Vector3(0.006f, 0.165f, 0.004f));
        _book.RotationDegrees = new Vector3(-35, 0, 0);
        _book.Visible = false;
    }

    /// <summary>Pula as transições suaves (ao teleportar depois de uma pausa).</summary>
    public void SnapPose()
    {
        _yaw = TargetYaw;
        Rotation = new Vector3(0, _yaw, 0);
        Animate(0f, snap: true);
    }

    public Vector3 HeadGlobalPosition => _head?.GlobalPosition ?? GlobalPosition + Vector3.Up;

    public override void _Process(double delta)
    {
        var dt = (float)Math.Min(delta, 0.25);
        _t += dt;
        _yaw = Mathf.LerpAngle(_yaw, TargetYaw, 1f - Mathf.Exp(-10f * dt));
        Rotation = new Vector3(0, _yaw, 0);
        Animate(dt, snap: false);
    }

    private void Animate(float dt, bool snap)
    {
        if (_pose == null) return;

        var armL = Vector3.Zero; var armR = Vector3.Zero;
        var legL = Vector3.Zero; var legR = Vector3.Zero;
        var torso = Vector3.Zero; var neck = Vector3.Zero;
        var poseRot = Vector3.Zero; var posePos = Vector3.Zero;
        var broom = false; var book = false; var eyesClosed = false;
        var s = Scale.Y <= 0 ? 1f : Scale.Y;
        var breathe = Mathf.Sin(_t * 1.6f) * 0.012f;

        // Postura base.
        switch (Posture)
        {
            case Posture.SitChair:
                posePos.Y = SeatHeight / s - HipY + 0.03f;
                legL.X = legR.X = -1.35f;
                break;
            case Posture.SitFloor:
                posePos.Y = SeatHeight / s - HipY + 0.05f;
                legL.X = -1.45f; legR.X = -1.45f;
                legL.Y = 0.25f; legR.Y = -0.25f;
                break;
            case Posture.Lie:
                poseRot.X = -Mathf.Pi / 2;
                posePos.Y = SeatHeight / s + 0.22f;
                break;
        }

        switch (Anim)
        {
            case BuddyAnim.Walk:
            case BuddyAnim.Sweep:
            {
                var sweeping = Anim == BuddyAnim.Sweep;
                _walkPhase += dt * (sweeping ? 6f : 9f) * WalkSpeedFactor;
                var sw = Mathf.Sin(_walkPhase);
                if (Posture == Posture.Stand)
                {
                    legL.X = sw * 0.5f; legR.X = -sw * 0.5f;
                    posePos.Y += Mathf.Abs(Mathf.Cos(_walkPhase)) * 0.03f;
                    poseRot.Z = sw * 0.07f; // o balanço de chibi
                }
                if (sweeping)
                {
                    broom = true;
                    armL.X = -0.75f; armR.X = -0.55f;
                    armL.Z = -0.25f; armR.Z = 0.25f;
                    torso.X = 0.15f;
                    torso.Y = Mathf.Sin(_t * 5f) * 0.18f;
                    neck.X = 0.2f;
                }
                else
                {
                    armL.X = -sw * 0.45f; armR.X = sw * 0.45f;
                    armL.Z = 0.12f; armR.Z = -0.12f;
                }
                break;
            }
            case BuddyAnim.Read:
                book = true;
                armL.X = armR.X = -1.05f;
                armL.Z = -0.35f; armR.Z = 0.35f;
                neck.X = 0.3f + Mathf.Sin(_t * 0.4f) * 0.04f;
                // Vira a página de vez em quando.
                _book.Rotation = new Vector3(Mathf.DegToRad(-35), Mathf.Sin(_t * 0.25f) > 0.97f ? 0.25f : 0f, 0);
                break;
            case BuddyAnim.Type:
            {
                armL.X = -1.2f + Mathf.Max(0, Mathf.Sin(_t * 14f)) * 0.08f;
                armR.X = -1.2f + Mathf.Max(0, Mathf.Sin(_t * 14f + 1.7f)) * 0.08f;
                armL.Z = -0.25f; armR.Z = 0.25f;
                neck.X = 0.08f;
                neck.Y = Mathf.Sin(_t * 0.5f) * 0.06f;
                break;
            }
            case BuddyAnim.Sit:
                armL.X = armR.X = -0.35f;
                armL.Z = 0.1f; armR.Z = -0.1f;
                neck.Y = Mathf.Sin(_t * 0.35f) * 0.35f;
                neck.X = Mathf.Sin(_t * 0.23f) * 0.08f;
                break;
            case BuddyAnim.Sleep:
                eyesClosed = true;
                armL.Z = 0.15f; armR.Z = -0.15f;
                breathe *= 1.8f;
                break;
            case BuddyAnim.Wave:
                armR.Z = -2.5f + Mathf.Sin(_t * 9f) * 0.35f;
                armL.Z = 0.1f;
                neck.Z = Mathf.Sin(_t * 3f) * 0.08f;
                break;
            case BuddyAnim.PickBook:
                armR.X = -2.0f;
                neck.X = -0.25f;
                break;
            default: // Idle
                neck.Y = Mathf.Sin(_t * 0.5f) * 0.45f;
                neck.X = Mathf.Sin(_t * 0.31f) * 0.06f;
                armL.Z = 0.08f + breathe; armR.Z = -0.08f - breathe;
                break;
        }

        var k = snap ? 1f : 1f - Mathf.Exp(-12f * dt);
        _rArmL = _rArmL.Lerp(armL, k); _rArmR = _rArmR.Lerp(armR, k);
        _rLegL = _rLegL.Lerp(legL, k); _rLegR = _rLegR.Lerp(legR, k);
        _rTorso = _rTorso.Lerp(torso, k); _rNeck = _rNeck.Lerp(neck, k);
        var kp = snap ? 1f : 1f - Mathf.Exp(-8f * dt);
        _rPose = _rPose.Lerp(poseRot, kp); _pPose = _pPose.Lerp(posePos, kp);

        _armL.Rotation = _rArmL; _armR.Rotation = _rArmR;
        _legL.Rotation = _rLegL; _legR.Rotation = _rLegR;
        _torso.Rotation = _rTorso; _neck.Rotation = _rNeck;
        _pose.Rotation = _rPose; _pose.Position = _pPose;
        _torso.Scale = new Vector3(1, 1 + breathe, 1);

        _broom.Visible = broom;
        if (broom) _broom.Rotation = new Vector3(Mathf.DegToRad(35), Mathf.Sin(_t * 5f) * 0.35f, 0);
        _book.Visible = book;
        _shadow.Visible = Posture == Posture.Stand;

        // Piscar.
        _blinkIn -= dt;
        if (_blinkIn <= 0) { _blinkLeft = 0.12f; _blinkIn = 2.5f + (float)_rng.NextDouble() * 3f; }
        _blinkLeft -= dt;
        var eyeY = eyesClosed || _blinkLeft > 0 ? 0.15f : 1f;
        _eyeL.Scale = _eyeR.Scale = new Vector3(1, eyeY, 1);

        // "z" subindo quando dorme.
        _zzz.Visible = Anim == BuddyAnim.Sleep;
        if (_zzz.Visible)
        {
            var cycle = (_t * 0.35f) % 1f;
            var head = ToLocal(_head.GlobalPosition);
            _zzz.Position = head + new Vector3(0.15f + cycle * 0.1f, 0.3f + cycle * 0.35f, 0);
            _zzzMat.AlbedoColor = new Color(1, 1, 1, 0.9f * (1f - cycle));
        }
    }
}
