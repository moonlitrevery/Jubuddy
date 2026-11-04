using Godot;

namespace Jubuddy.App.UI;

/// <summary>Balãozinho com uma frase, acima da cabeça do buddy.</summary>
public partial class SpeechBubble : CanvasLayer
{
    private PanelContainer _panel = null!;
    private Label _label = null!;
    private double _left;

    public bool Showing => _panel.Visible;

    public override void _Ready()
    {
        Layer = 5;
        _panel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(1, 1, 1, 0.96f),
            ShadowColor = new Color(0, 0, 0, 0.18f),
            ShadowSize = 6,
            ShadowOffset = new Vector2(0, 2),
        };
        sb.SetCornerRadiusAll(14);
        sb.SetContentMarginAll(12);
        _panel.AddThemeStyleboxOverride("panel", sb);
        _label = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeColorOverride("font_color", new Color(0.18f, 0.18f, 0.2f));
        _label.AddThemeFontSizeOverride("font_size", 17);
        _panel.AddChild(_label);
        AddChild(_panel);
    }

    public void Show(string text, double seconds)
    {
        _label.Text = text;
        var width = Mathf.Clamp(text.Length * 9f + 30f, 90f, 340f);
        _label.CustomMinimumSize = new Vector2(width - 24f, 0);
        _panel.Size = Vector2.Zero; // recalcula a altura
        _panel.Visible = true;
        _left = seconds;
    }

    public void HideNow()
    {
        _panel.Visible = false;
        _left = 0;
    }

    /// <summary>Atualiza a posição (cabeça projetada na tela) e o tempo restante.</summary>
    public void Follow(Camera3D cam, Vector3 headWorld, double delta)
    {
        if (!_panel.Visible) return;
        _left -= delta;
        if (_left <= 0) { HideNow(); return; }
        var anchor = headWorld + Vector3.Up * 0.45f;
        if (cam.IsPositionBehind(anchor)) return;
        var p = cam.UnprojectPosition(anchor);
        var size = _panel.Size;
        _panel.Position = new Vector2(p.X - size.X / 2, p.Y - size.Y);
    }
}
