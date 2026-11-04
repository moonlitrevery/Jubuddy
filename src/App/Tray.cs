using System;
using Godot;

namespace Jubuddy.App;

/// <summary>Ícone na bandeja do sistema (StatusIndicator do Godot) com o menu do app.</summary>
public partial class Tray : Node
{
    private const int IdOpen = 1, IdDiag = 2, IdAutostart = 3, IdPhrases = 4, IdFolder = 5, IdQuit = 9;

    private StatusIndicator _indicator = null!;
    private PopupMenu _menu = null!;
    private bool _nativeMenu;

    public event Action? OpenRoom;
    public event Action? Quit;
    public event Action? EditPhrases;
    public event Action? OpenDataFolder;
    public event Action<bool>? DiagnosticsToggled;
    public event Action<bool>? AutostartToggled;

    public void Setup(Texture2D icon, bool autostartAvailable, bool autostartOn, bool diagOn)
    {
        _menu = new PopupMenu { Name = "TrayMenu" };
        _menu.AddItem("Abrir o quarto", IdOpen);
        _menu.AddSeparator();
        _menu.AddItem("Editar minhas frases", IdPhrases);
        _menu.AddItem("Abrir pasta de dados", IdFolder);
        _menu.AddSeparator();
        _menu.AddCheckItem("Modo de diagnóstico", IdDiag);
        _menu.AddCheckItem(autostartAvailable ? "Iniciar com o Windows" : "Iniciar com o Windows (só no executável)", IdAutostart);
        _menu.AddSeparator();
        _menu.AddItem("Sair", IdQuit);
        _menu.SetItemDisabled(_menu.GetItemIndex(IdAutostart), !autostartAvailable);
        AddChild(_menu);
        SetChecks(diagOn, autostartOn);
        _menu.IdPressed += OnId;

        _indicator = new StatusIndicator { Name = "Indicator", Icon = icon, Tooltip = "Jubuddy" };
        AddChild(_indicator);

        // Menu nativo do Windows quando o Godot suporta; senão, clicar no ícone abre o quarto
        // (e lá dentro, em Ajustes, há as mesmas opções).
        _nativeMenu = NativeMenu.HasFeature(NativeMenu.Feature.PopupMenu);
        GD.Print($"Jubuddy: menu da bandeja {(_nativeMenu ? "nativo" : "simples (clique abre o quarto)")}");
        if (_nativeMenu) _indicator.Menu = _menu.GetPath();
        _indicator.Pressed += (button, _) =>
        {
            if (button == (long)MouseButton.Left || !_nativeMenu) OpenRoom?.Invoke();
        };
    }

    public bool HasNativeMenu => _nativeMenu;

    public void SetChecks(bool diag, bool autostart)
    {
        _menu.SetItemChecked(_menu.GetItemIndex(IdDiag), diag);
        _menu.SetItemChecked(_menu.GetItemIndex(IdAutostart), autostart);
    }

    public void SetTooltip(string text)
    {
        if (_indicator != null && _indicator.Tooltip != text) _indicator.Tooltip = text;
    }

    private void OnId(long id)
    {
        switch (id)
        {
            case IdOpen: OpenRoom?.Invoke(); break;
            case IdPhrases: EditPhrases?.Invoke(); break;
            case IdFolder: OpenDataFolder?.Invoke(); break;
            case IdDiag:
            {
                var i = _menu.GetItemIndex(IdDiag);
                var on = !_menu.IsItemChecked(i);
                _menu.SetItemChecked(i, on);
                DiagnosticsToggled?.Invoke(on);
                break;
            }
            case IdAutostart:
            {
                var i = _menu.GetItemIndex(IdAutostart);
                var on = !_menu.IsItemChecked(i);
                _menu.SetItemChecked(i, on);
                AutostartToggled?.Invoke(on);
                break;
            }
            case IdQuit: Quit?.Invoke(); break;
        }
    }
}
