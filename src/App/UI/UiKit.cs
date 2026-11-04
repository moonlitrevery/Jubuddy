using System;
using Godot;

namespace Jubuddy.App.UI;

/// <summary>Peças de interface reutilizáveis e o tema claro e neutro do modo quarto.</summary>
public static class UiKit
{
    public static readonly Color Ink = new(0.18f, 0.18f, 0.2f);
    public static readonly Color Muted = new(0.42f, 0.42f, 0.46f);
    public static readonly Color Paper = new(0.98f, 0.973f, 0.957f, 0.97f);

    public static StyleBoxFlat Flat(Color bg, int radius, float marginX, float marginY, Color? border = null)
    {
        var s = new StyleBoxFlat { BgColor = bg };
        s.SetCornerRadiusAll(radius);
        s.ContentMarginLeft = s.ContentMarginRight = marginX;
        s.ContentMarginTop = s.ContentMarginBottom = marginY;
        if (border is { } b) { s.BorderColor = b; s.SetBorderWidthAll(1); }
        return s;
    }

    public static Theme BuildTheme()
    {
        var t = new Theme { DefaultFontSize = 15 };
        var panel = Flat(Paper, 12, 14, 12);
        panel.ShadowColor = new Color(0, 0, 0, 0.16f);
        panel.ShadowSize = 8;
        panel.ShadowOffset = new Vector2(0, 2);
        t.SetStylebox("panel", "PanelContainer", panel);
        t.SetStylebox("panel", "PopupMenu", Flat(new Color("#FAF8F4"), 8, 8, 6, new Color("#D8D2C7")));
        t.SetStylebox("hover", "PopupMenu", Flat(new Color("#E6E1D8"), 6, 8, 4));
        t.SetStylebox("panel", "PopupPanel", Flat(new Color("#FAF8F4"), 8, 8, 8, new Color("#D8D2C7")));
        foreach (var c in new[] { "font_color", "font_hover_color", "font_accelerator_color" })
            t.SetColor(c, "PopupMenu", Ink);

        foreach (var type in new[] { "Button", "MenuButton", "OptionButton" })
        {
            t.SetStylebox("normal", type, Flat(new Color("#EEEAE3"), 8, 12, 6));
            t.SetStylebox("hover", type, Flat(new Color("#E4DFD6"), 8, 12, 6));
            t.SetStylebox("pressed", type, Flat(new Color("#D8D2C7"), 8, 12, 6));
            t.SetStylebox("disabled", type, Flat(new Color("#F3F1ED"), 8, 12, 6));
            t.SetStylebox("focus", type, new StyleBoxEmpty());
            foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
                t.SetColor(c, type, Ink);
            t.SetColor("font_disabled_color", type, new Color(0.6f, 0.6f, 0.63f));
        }
        foreach (var c in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
            t.SetColor(c, "CheckBox", Ink);
        t.SetColor("font_disabled_color", "CheckBox", new Color(0.6f, 0.6f, 0.63f));
        t.SetStylebox("focus", "CheckBox", new StyleBoxEmpty());

        t.SetColor("font_color", "Label", Ink);
        foreach (var type in new[] { "LineEdit", "TextEdit" })
        {
            t.SetStylebox("normal", type, Flat(Colors.White, 8, 10, 8, new Color("#D8D2C7")));
            t.SetStylebox("focus", type, Flat(Colors.White, 8, 10, 8, new Color("#9AA7B8")));
            t.SetColor("font_color", type, Ink);
            t.SetColor("font_placeholder_color", type, new Color(0.6f, 0.6f, 0.63f));
            t.SetColor("caret_color", type, Ink);
        }
        return t;
    }

    public static Label Text(string text, int size = 15, Color? color = null, bool wrap = false)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) l.AddThemeColorOverride("font_color", c);
        if (wrap) l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    public static Button Btn(string text, Action onPress)
    {
        var b = new Button { Text = text };
        b.Pressed += onPress;
        return b;
    }

    public static Control Spacer() => new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };

    /// <summary>Janela modal simples dentro do próprio quarto (fundo escurecido + painel no centro).</summary>
    public static (Control overlay, VBoxContainer body) Modal(Control host, string title, float width = 460)
    {
        var overlay = new ColorRect { Color = new Color(0, 0, 0, 0.35f), MouseFilter = Control.MouseFilterEnum.Stop };
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.AddChild(center);
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, 0) };
        center.AddChild(panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        panel.AddChild(body);
        body.AddChild(Text(title, 18));
        host.AddChild(overlay);
        return (overlay, body);
    }

    /// <summary>Pede um texto (a frase de um objeto). Salvar só habilita com algo escrito.</summary>
    public static void AskText(Control host, string title, string hint, string initial, Action<string> onOk)
    {
        var (overlay, body) = Modal(host, title);
        if (hint.Length > 0) body.AddChild(Text(hint, 13, Muted, wrap: true));
        var edit = new TextEdit
        {
            Text = initial,
            PlaceholderText = "Escreva com as suas palavras…",
            WrapMode = TextEdit.LineWrappingMode.Boundary,
            CustomMinimumSize = new Vector2(0, 110),
        };
        body.AddChild(edit);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(Spacer());
        row.AddChild(Btn("Cancelar", () => overlay.QueueFree()));
        var ok = Btn("Salvar", () =>
        {
            var text = edit.Text.Trim();
            if (text.Length == 0) return;
            overlay.QueueFree();
            onOk(text);
        });
        ok.Disabled = edit.Text.Trim().Length == 0;
        edit.TextChanged += () => ok.Disabled = edit.Text.Trim().Length == 0;
        row.AddChild(ok);
        body.AddChild(row);
        edit.CallDeferred(Control.MethodName.GrabFocus);
    }

    public static void Confirm(Control host, string title, string message, string okText, Action onOk)
    {
        var (overlay, body) = Modal(host, title, 420);
        body.AddChild(Text(message, 15, null, wrap: true));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(Spacer());
        row.AddChild(Btn("Cancelar", () => overlay.QueueFree()));
        var ok = Btn(okText, () => { overlay.QueueFree(); onOk(); });
        ok.AddThemeColorOverride("font_color", new Color("#9B2C2C"));
        ok.AddThemeColorOverride("font_hover_color", new Color("#9B2C2C"));
        row.AddChild(ok);
        body.AddChild(row);
    }
}
