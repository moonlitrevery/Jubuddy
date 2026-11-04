using System;
using System.Linq;
using Godot;
using Jubuddy.Core;
using static Jubuddy.App.UI.UiKit;

namespace Jubuddy.App.UI;

/// <summary>
/// Modo quarto: adicionar, mover, girar e guardar objetos; clicar neles para ver a frase;
/// o baú (de onde tudo pode voltar); a aparência do buddy; e os ajustes.
/// </summary>
public partial class RoomEditor : CanvasLayer
{
    private Main _m = null!;

    private Control _root = null!;
    private PanelContainer _itemPanel = null!;
    private Label _itemTitle = null!, _itemWhy = null!;
    private Button _rotateBtn = null!;
    private ColorPickerButton _itemColor = null!;
    private PanelContainer _side = null!;
    private VBoxContainer _sideBody = null!;
    private Button _chestBtn = null!;
    private Label _hint = null!, _toast = null!;
    private double _toastLeft;
    private double _saveLookIn = -1;

    private string? _selected;
    private string? _movingId;
    private PlacedItem? _probe, _original;
    private bool _moveValid;

    private enum Side { None, Chest, Look, Settings }
    private Side _sideMode = Side.None;

    public RoomEditor Init(Main main)
    {
        _m = main;
        return this;
    }

    public bool IsOpen => Visible;
    public bool IsMoving => _movingId != null;

    public override void _Ready()
    {
        Layer = 10;
        Visible = false;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = BuildTheme() };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        BuildTopBar();
        BuildItemPanel();
        BuildSide();

        _hint = Text("", 13, new Color(1, 1, 1, 0.85f));
        _hint.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
        _hint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft);
        _hint.Position = new Vector2(16, -30);
        _hint.GrowVertical = Control.GrowDirection.Begin;
        _root.AddChild(_hint);

        _toast = Text("", 15);
        var toastPanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        toastPanel.AddChild(_toast);
        toastPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        toastPanel.Position = new Vector2(0, 70);
        toastPanel.GrowHorizontal = Control.GrowDirection.Both;
        _root.AddChild(toastPanel);
    }

    // ---------- Abrir/fechar ----------

    public void Open()
    {
        Visible = true;
        RefreshChestButton();
        SetHint("Clique num objeto para ver por que ele está aqui. Roda do mouse aproxima.");
        if (_m.LoadProblems.Count > 0) Toast(string.Join("\n", _m.LoadProblems), 8);
    }

    public void Close()
    {
        CancelMove();
        Deselect();
        ShowSide(Side.None);
        foreach (var c in _root.GetChildren().OfType<ColorRect>()) c.QueueFree(); // modais abertos
        Visible = false;
    }

    /// <summary>Abre um painel sem clicar (usado com --demo-painel para conferir a interface).</summary>
    public void Demo(string panel)
    {
        switch (panel)
        {
            case "bau":
                if (_m.Room.Chest.Count == 0 && _m.Room.Items.Count > 0) { _m.Room.StoreInChest(_m.Room.Items[^1].Id); RefreshChestButton(); }
                ShowSide(Side.Chest);
                break;
            case "aparencia": ShowSide(Side.Look); break;
            case "ajustes": ShowSide(Side.Settings); break;
            case "objeto":
                var it = _m.Room.Items.FirstOrDefault(i => i.Kind == ItemKind.DeskPc) ?? _m.Room.Items.FirstOrDefault();
                if (it != null) Select(it.Id);
                break;
            case "frase":
                AskText(_root, "Por que a planta está no seu quarto?", "Essa frase fica guardada junto do objeto. Clicar nele mostra ela.", "", _ => { });
                break;
        }
    }

    // ---------- Barra de cima ----------

    private void BuildTopBar()
    {
        var bar = new PanelContainer();
        bar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);
        bar.OffsetLeft = 12; bar.OffsetRight = -12; bar.OffsetTop = 12;
        _root.AddChild(bar);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        bar.AddChild(row);

        var add = new MenuButton { Text = "＋ Adicionar", Flat = false };
        var popup = add.GetPopup();
        for (var i = 0; i < Catalog.All.Count; i++) popup.AddItem(Catalog.All[i].Name, i);
        popup.IdPressed += id => BeginAdd(Catalog.All[(int)id].Kind);
        row.AddChild(add);

        _chestBtn = Btn("Baú", () => ToggleSide(Side.Chest));
        row.AddChild(_chestBtn);
        row.AddChild(Btn("Aparência", () => ToggleSide(Side.Look)));
        row.AddChild(Btn("Ajustes", () => ToggleSide(Side.Settings)));
        row.AddChild(Spacer());
        row.AddChild(Btn("Voltar ao papel de parede", () => _m.EnterWallpaperMode()));
    }

    private void RefreshChestButton() => _chestBtn.Text = _m.Room.Chest.Count > 0 ? $"Baú ({_m.Room.Chest.Count})" : "Baú";

    // ---------- Objeto selecionado ----------

    private void BuildItemPanel()
    {
        _itemPanel = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(560, 0) };
        _itemPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
        _itemPanel.GrowHorizontal = Control.GrowDirection.Both;
        _itemPanel.GrowVertical = Control.GrowDirection.Begin;
        _itemPanel.OffsetBottom = -16;
        _root.AddChild(_itemPanel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 8);
        _itemPanel.AddChild(v);
        _itemTitle = Text("", 13, Muted);
        v.AddChild(_itemTitle);
        _itemWhy = Text("", 19, null, wrap: true);
        _itemWhy.CustomMinimumSize = new Vector2(530, 0);
        v.AddChild(_itemWhy);

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        v.AddChild(row);
        row.AddChild(Btn("Mover", () => { if (_selected != null) BeginMove(_selected); }));
        _rotateBtn = Btn("Girar", RotateSelected);
        row.AddChild(_rotateBtn);
        row.AddChild(Btn("Editar frase", EditSelectedWhy));
        row.AddChild(Text("Cor", 14, Muted));
        _itemColor = new ColorPickerButton { CustomMinimumSize = new Vector2(48, 0), EditAlpha = false, TooltipText = "Cor principal do objeto" };
        _itemColor.PopupClosed += () =>
        {
            if (_selected == null) return;
            _m.Room.SetColor(_selected, "#" + _itemColor.Color.ToHtml(false));
            _m.SaveRoom();
            Select(_selected);
        };
        row.AddChild(_itemColor);
        row.AddChild(Btn("Cor padrão", () =>
        {
            if (_selected == null) return;
            _m.Room.SetColor(_selected, null);
            _m.SaveRoom();
            Select(_selected);
        }));
        row.AddChild(Spacer());
        row.AddChild(Btn("Guardar no baú", StoreSelected));
        row.AddChild(Btn("✕", Deselect));
    }

    private void Select(string id)
    {
        var it = _m.Room.Find(id);
        if (it == null) { Deselect(); return; }
        _selected = id;
        _itemTitle.Text = Catalog.NameOf(it.Kind).ToUpperInvariant() + $"  ·  desde {it.AddedAt:dd/MM/yyyy}";
        _itemWhy.Text = string.IsNullOrWhiteSpace(it.Why) ? "(ainda sem frase)" : $"“{it.Why}”";
        _rotateBtn.Visible = !it.Spec.OnWall;
        _itemColor.Color = Visuals.Hex(it.Color, FurnitureBuilder.DefaultColor(it.Kind));
        _itemPanel.Visible = true;
        _itemPanel.ResetSize();
        _hint.Visible = false;
        _m.View.ShowMarker(it, new Color(1, 1, 1, 0.16f));
    }

    private void Deselect()
    {
        _selected = null;
        _itemPanel.Visible = false;
        _hint.Visible = true;
        _m.View.ShowMarker(null, default);
    }

    private void RotateSelected()
    {
        if (_selected == null) return;
        if (_m.Room.TryRotate(_selected)) { _m.SaveRoom(); Select(_selected); }
        else Toast("Não cabe girado aqui.");
    }

    private void EditSelectedWhy()
    {
        if (_selected == null) return;
        var it = _m.Room.Find(_selected);
        if (it == null) return;
        var id = _selected;
        AskText(_root, $"Por que {Article(it.Kind)} está aqui?", "", it.Why, text =>
        {
            _m.Room.SetWhy(id, text);
            _m.SaveRoom();
            Select(id);
        });
    }

    private void StoreSelected()
    {
        if (_selected == null) return;
        var it = _m.Room.StoreInChest(_selected);
        _m.SaveRoom();
        Deselect();
        RefreshChestButton();
        if (_sideMode == Side.Chest) ShowSide(Side.Chest);
        Toast($"{Catalog.NameOf(it.Kind)} foi para o baú. Dá para devolver quando quiser.");
    }

    private static string Article(ItemKind k) => k switch
    {
        ItemKind.Bed => "a cama",
        ItemKind.DeskPc => "a mesa com PC",
        ItemKind.Bookshelf => "a estante",
        ItemKind.Chair => "a cadeira",
        ItemKind.Rug => "o tapete",
        ItemKind.Plant => "a planta",
        ItemKind.Lamp => "a luminária",
        ItemKind.Frame => "o quadro",
        ItemKind.Poster => "este pôster",
        _ => "isto",
    };

    // ---------- Adicionar ----------

    private void BeginAdd(ItemKind kind)
    {
        CancelMove();
        if (kind == ItemKind.Poster) { PickPosterImage(); return; }
        AskText(_root, $"Por que {Article(kind)} está no seu quarto?",
            "Essa frase fica guardada junto do objeto. Clicar nele mostra ela.", "", why =>
            {
                var it = _m.Room.Add(kind, why, NearBuddy());
                if (it == null) { Toast("Não tem espaço livre para isso agora."); return; }
                _m.SaveRoom();
                Select(it.Id);
                BeginMove(it.Id);
            });
    }

    private Vec2 NearBuddy() => new(_m.Body.Position.X, _m.Body.Position.Z);

    private void PickPosterImage()
    {
        var dlg = new FileDialog
        {
            Title = "Escolha uma imagem para o pôster",
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Filters = new[] { "*.png, *.jpg, *.jpeg, *.webp, *.bmp ; Imagens" },
        };
        dlg.FileSelected += path =>
        {
            dlg.QueueFree();
            var file = _m.ImportPoster(path, out var aspect, out var error);
            if (file == null) { Toast(error ?? "Não consegui abrir essa imagem."); return; }
            AskText(_root, "Por que este pôster está aqui?", "A imagem foi copiada para a pasta do app, então pode mover o original.", "", why =>
            {
                var it = _m.Room.Add(ItemKind.Poster, why, null, file, aspect);
                if (it == null) { Toast("Não tem espaço livre nas paredes."); return; }
                _m.SaveRoom();
                Select(it.Id);
                BeginMove(it.Id);
            });
        };
        dlg.Canceled += () => dlg.QueueFree();
        _root.AddChild(dlg);
        dlg.PopupCentered(new Vector2I(820, 520));
    }

    // ---------- Mover ----------

    private void BeginMove(string id)
    {
        var it = _m.Room.Find(id);
        if (it == null) return;
        _movingId = id;
        _original = it.Clone();
        _probe = it.Clone();
        _moveValid = true;
        _itemPanel.Visible = false;
        _hint.Visible = true;
        SetHint(it.Spec.OnWall
            ? "Passe o mouse nas paredes e clique para pendurar. Botão direito ou Esc cancela."
            : "Mova o mouse e clique para soltar. R gira, botão direito ou Esc cancela.");
        _m.View.ShowMarker(_probe, new Color(0.4f, 0.9f, 0.5f, 0.28f));
    }

    private void UpdateMove(Vector2 screen)
    {
        if (_probe == null || _movingId == null) return;
        var cam = _m.Camera;
        var from = cam.ProjectRayOrigin(screen);
        var dir = cam.ProjectRayNormal(screen);
        var half = _m.Room.Half;

        if (_probe.Spec.OnWall)
        {
            Vector3? back = new Plane(new Vector3(0, 0, 1), -half).IntersectsRay(from, dir);
            Vector3? left = new Plane(new Vector3(1, 0, 0), -half).IntersectsRay(from, dir);
            var backOk = back is { } b && b.X >= -half && b.X <= half && b.Y >= 0 && b.Y <= Room.WallHeight;
            var leftOk = left is { } l && l.Z >= -half && l.Z <= half && l.Y >= 0 && l.Y <= Room.WallHeight;
            if (backOk && leftOk)
            {
                if ((back!.Value - from).Length() <= (left!.Value - from).Length()) leftOk = false; else backOk = false;
            }
            if (backOk) { _probe.Wall = WallSide.Back; _probe.Along = Snap(back!.Value.X); _probe.Y = Snap(back.Value.Y); }
            else if (leftOk) { _probe.Wall = WallSide.Left; _probe.Along = Snap(left!.Value.Z); _probe.Y = Snap(left.Value.Y); }
            else return;
        }
        else
        {
            if (new Plane(Vector3.Up, 0).IntersectsRay(from, dir) is not { } hit) return;
            _probe.X = Snap(hit.X);
            _probe.Z = Snap(hit.Z);
        }

        _moveValid = _m.Room.CanPlace(_probe);
        var node = _m.View.NodeOf(_movingId);
        if (node != null) RoomView.Place(node, _probe, half);
        _m.View.ShowMarker(_probe, _moveValid ? new Color(0.4f, 0.9f, 0.5f, 0.28f) : new Color(1f, 0.35f, 0.3f, 0.35f));
    }

    private static float Snap(float v) => Mathf.Snapped(v, 0.05f);

    private void RotateProbe()
    {
        if (_probe == null || _probe.Spec.OnWall || _movingId == null) return;
        _probe.Rotation = (_probe.Rotation + 1) % 4;
        _moveValid = _m.Room.CanPlace(_probe);
        var node = _m.View.NodeOf(_movingId);
        if (node != null) RoomView.Place(node, _probe, _m.Room.Half);
        _m.View.ShowMarker(_probe, _moveValid ? new Color(0.4f, 0.9f, 0.5f, 0.28f) : new Color(1f, 0.35f, 0.3f, 0.35f));
    }

    private void CommitMove()
    {
        if (_probe == null || _movingId == null) return;
        if (!_moveValid) { Toast("Não cabe aí."); return; }
        var id = _movingId;
        var ok = _probe.Spec.OnWall
            ? _m.Room.TryMoveWall(id, _probe.Wall, _probe.Along, _probe.Y)
            : _m.Room.TryMoveFloor(id, _probe.Position, _probe.Rotation);
        EndMove();
        if (ok) _m.SaveRoom();
        Select(id);
    }

    private void CancelMove()
    {
        if (_movingId == null) return;
        var node = _m.View.NodeOf(_movingId);
        if (node != null && _original != null) RoomView.Place(node, _original, _m.Room.Half);
        var id = _movingId;
        EndMove();
        if (_m.Room.Find(id) != null) Select(id);
    }

    private void EndMove()
    {
        _movingId = null;
        _probe = null;
        _original = null;
        _m.View.ShowMarker(null, default);
        SetHint("Clique num objeto para ver por que ele está aqui. Roda do mouse aproxima.");
    }

    // ---------- Entrada (cliques no 3D) ----------

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;

        if (_movingId != null)
        {
            switch (e)
            {
                case InputEventMouseMotion mm: UpdateMove(mm.Position); break;
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb: UpdateMove(mb.Position); CommitMove(); break;
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }: CancelMove(); break;
                case InputEventKey { Pressed: true, Keycode: Key.Escape }: CancelMove(); break;
                case InputEventKey { Pressed: true, Keycode: Key.R }: RotateProbe(); break;
                default: return;
            }
            GetViewport().SetInputAsHandled();
            return;
        }

        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb:
            {
                var id = _m.View.Pick(_m.Camera, mb.Position);
                if (id != null) Select(id); else Deselect();
                GetViewport().SetInputAsHandled();
                break;
            }
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _m.ZoomRoomCamera(0.92f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _m.ZoomRoomCamera(1.08f);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                if (_sideMode != Side.None) ShowSide(Side.None); else Deselect();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_toastLeft > 0)
        {
            _toastLeft -= delta;
            if (_toastLeft <= 0) _toast.GetParent<Control>().Visible = false;
        }
        if (_saveLookIn > 0)
        {
            _saveLookIn -= delta;
            if (_saveLookIn <= 0) _m.SaveLook();
        }
    }

    private void Toast(string text, double seconds = 3.5)
    {
        _toast.Text = text;
        var p = _toast.GetParent<Control>();
        p.Visible = true;
        p.ResetSize();
        p.Position = new Vector2((_root.Size.X - p.Size.X) / 2, 72);
        _toastLeft = seconds;
    }

    private void SetHint(string text) => _hint.Text = text;

    // ---------- Painel lateral ----------

    private void BuildSide()
    {
        _side = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(410, 0) };
        _side.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.RightWide);
        _side.OffsetTop = 72; _side.OffsetBottom = -16; _side.OffsetRight = -12; _side.OffsetLeft = -422;
        _root.AddChild(_side);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _side.AddChild(scroll);
        _sideBody = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _sideBody.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_sideBody);
    }

    private void ToggleSide(Side s) => ShowSide(_sideMode == s ? Side.None : s);

    private void ShowSide(Side s)
    {
        _sideMode = s;
        foreach (var c in _sideBody.GetChildren()) c.QueueFree();
        _side.Visible = s != Side.None;
        switch (s)
        {
            case Side.Chest: FillChest(); break;
            case Side.Look: FillLook(); break;
            case Side.Settings: FillSettings(); break;
        }
    }

    private void Header(string title, string? sub = null)
    {
        var row = new HBoxContainer();
        row.AddChild(Text(title, 19));
        row.AddChild(Spacer());
        row.AddChild(Btn("✕", () => ShowSide(Side.None)));
        _sideBody.AddChild(row);
        if (sub != null) _sideBody.AddChild(Text(sub, 13, Muted, wrap: true));
    }

    private void FillChest()
    {
        Header("Baú", "O que sai do quarto fica guardado aqui, com a frase. Nada some sem você confirmar.");
        var chest = _m.Room.Chest.OrderByDescending(i => i.StoredAt).ToList();
        if (chest.Count == 0) { _sideBody.AddChild(Text("O baú está vazio.", 15, Muted)); return; }
        foreach (var it in chest)
        {
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", Flat(new Color("#F1EEE8"), 10, 12, 10));
            var v = new VBoxContainer();
            v.AddThemeConstantOverride("separation", 6);
            card.AddChild(v);
            v.AddChild(Text(Catalog.NameOf(it.Kind), 15));
            v.AddChild(Text(string.IsNullOrWhiteSpace(it.Why) ? "(sem frase)" : $"“{it.Why}”", 14, Muted, wrap: true));
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);
            var id = it.Id;
            row.AddChild(Btn("Devolver ao quarto", () =>
            {
                var back = _m.Room.RestoreFromChest(id);
                _m.SaveRoom();
                RefreshChestButton();
                ShowSide(Side.Chest);
                Toast($"{Catalog.NameOf(back.Kind)} voltou para o quarto.");
            }));
            row.AddChild(Spacer());
            var del = Btn("Apagar de vez…", () => ConfirmDelete(id));
            del.AddThemeColorOverride("font_color", new Color("#9B2C2C"));
            row.AddChild(del);
            v.AddChild(row);
            _sideBody.AddChild(card);
        }
    }

    private void ConfirmDelete(string id)
    {
        var it = _m.Room.FindInChest(id);
        if (it == null) return;
        var name = Catalog.NameOf(it.Kind);
        Confirm(_root, "Apagar de vez?",
            $"{name} e a frase dele vão sumir para sempre" +
            (it.Kind == ItemKind.Poster ? ", junto com a cópia da imagem" : "") +
            ". Isso não tem volta.",
            "Apagar de vez", () =>
            {
                var deleted = _m.Room.DeleteForever(id);
                _m.Store.DeletePosterImageIfUnused(_m.Room, deleted);
                FurnitureBuilder.ForgetPoster(deleted.Image);
                _m.SaveRoom();
                RefreshChestButton();
                ShowSide(Side.Chest);
                Toast($"{name} foi apagado.");
            });
    }

    private void FillLook()
    {
        Header("Aparência", "Ajuste até parecer com você. Também dá para editar buddy.json na pasta de dados.");
        var look = _m.Look;
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 8);
        _sideBody.AddChild(grid);

        void ColorRow(string label, Func<string> get, Action<string> set)
        {
            grid.AddChild(Text(label));
            var pick = new ColorPickerButton { Color = Visuals.Hex(get()), EditAlpha = false, CustomMinimumSize = new Vector2(150, 30) };
            pick.ColorChanged += c => { set("#" + c.ToHtml(false)); LookChanged(); };
            grid.AddChild(pick);
        }

        void EnumRow<T>(string label, T current, (T value, string name)[] options, Action<T> set) where T : struct, Enum
        {
            grid.AddChild(Text(label));
            var opt = new OptionButton { CustomMinimumSize = new Vector2(150, 0) };
            for (var i = 0; i < options.Length; i++)
            {
                opt.AddItem(options[i].name, i);
                if (options[i].value.Equals(current)) opt.Selected = i;
            }
            opt.ItemSelected += idx => { set(options[(int)idx].value); LookChanged(); };
            grid.AddChild(opt);
        }

        void SliderRow(string label, float min, float max, float value, Action<float> set)
        {
            grid.AddChild(Text(label));
            var s = new HSlider { MinValue = min, MaxValue = max, Step = 0.01, Value = value, CustomMinimumSize = new Vector2(150, 24) };
            s.ValueChanged += v => { set((float)v); LookChanged(); };
            grid.AddChild(s);
        }

        void CheckRow(string label, bool value, Action<bool> set)
        {
            grid.AddChild(Text(label));
            var c = new CheckBox { ButtonPressed = value };
            c.Toggled += on => { set(on); LookChanged(); };
            grid.AddChild(c);
        }

        ColorRow("Pele", () => look.Skin, v => look.Skin = v);
        ColorRow("Cabelo", () => look.Hair, v => look.Hair = v);
        EnumRow("Penteado", look.HairStyle, new[]
        {
            (HairStyle.Curto, "Curto"), (HairStyle.Comprido, "Comprido"), (HairStyle.Coque, "Coque"),
            (HairStyle.Cacheado, "Cacheado"), (HairStyle.Raspado, "Raspado"), (HairStyle.RaboDeCavalo, "Rabo de cavalo"),
        }, v => look.HairStyle = v);
        ColorRow("Olhos", () => look.Eyes, v => look.Eyes = v);
        EnumRow("Roupa", look.Outfit, new[]
        {
            (Outfit.Camiseta, "Camiseta"), (Outfit.Moletom, "Moletom"), (Outfit.Vestido, "Vestido"),
        }, v => look.Outfit = v);
        ColorRow("Roupa (cima)", () => look.Top, v => look.Top = v);
        ColorRow("Roupa (baixo)", () => look.Bottom, v => look.Bottom = v);
        ColorRow("Sapatos", () => look.Shoes, v => look.Shoes = v);
        CheckRow("Óculos", look.Glasses, v => look.Glasses = v);
        ColorRow("Cor dos óculos", () => look.GlassesColor, v => look.GlassesColor = v);
        CheckRow("Bochechas", look.Blush, v => look.Blush = v);
        SliderRow("Cabeça", 0.8f, 1.25f, look.HeadSize, v => look.HeadSize = v);
        SliderRow("Altura", 0.85f, 1.15f, look.Height, v => look.Height = v);
    }

    private void LookChanged()
    {
        _m.ApplyLook();
        _saveLookIn = 0.6; // salva quando parar de mexer
    }

    private void FillSettings()
    {
        Header("Ajustes");
        var s = _m.Settings;

        _sideBody.AddChild(Text("Frases", 16));
        _sideBody.AddChild(Text($"{_m.Phrases.Count} frase(s) em frases.txt. Só as suas: o app não vem com nenhuma.", 13, Muted, wrap: true));
        var pr = new HBoxContainer();
        pr.AddThemeConstantOverride("separation", 6);
        pr.AddChild(Btn("Editar", () => OS.ShellOpen(_m.Paths.PhrasesFile)));
        pr.AddChild(Btn("Recarregar", () => { _m.ReloadPhrases(); ShowSide(Side.Settings); }));
        pr.AddChild(Btn("Mostrar uma", () => { if (!_m.ShowPhraseNow()) Toast("O arquivo de frases ainda está vazio."); }));
        _sideBody.AddChild(pr);
        _sideBody.AddChild(SpinRow("Intervalo médio (min, 0 = nunca)", 0, 600, 5, s.PhraseIntervalMinutes, v => { s.PhraseIntervalMinutes = v; _m.SettingsChanged(reschedulePhrases: true); }));

        _sideBody.AddChild(new HSeparator());
        _sideBody.AddChild(Text("Papel de parede", 16));
        if (_m.MonitorCount > 1)
            _sideBody.AddChild(SpinRow($"Monitor (1 = principal, de {_m.MonitorCount})", 1, _m.MonitorCount, 1, s.WallpaperMonitor,
                v => { s.WallpaperMonitor = (int)v; _m.SettingsChanged(); }));
        _sideBody.AddChild(SliderRow2("Posição (esq. ↔ dir.)", -1, 1, s.WallpaperOffsetX, v => { s.WallpaperOffsetX = v; _m.SettingsChanged(); }));
        _sideBody.AddChild(SliderRow2("Posição (baixo ↔ cima)", -1, 1, s.WallpaperOffsetY, v => { s.WallpaperOffsetY = v; _m.SettingsChanged(); }));
        _sideBody.AddChild(SliderRow2("Tamanho", 0.3f, 2f, s.WallpaperZoom, v => { s.WallpaperZoom = v; _m.SettingsChanged(); }));
        _sideBody.AddChild(ColorRow2("Fundo", s.Background, v => { s.Background = v; _m.SettingsChanged(rebuildRoom: true); }));
        _sideBody.AddChild(ColorRow2("Chão", s.Floor, v => { s.Floor = v; _m.SettingsChanged(rebuildRoom: true); }));
        _sideBody.AddChild(ColorRow2("Paredes", s.Walls, v => { s.Walls = v; _m.SettingsChanged(rebuildRoom: true); }));

        _sideBody.AddChild(new HSeparator());
        _sideBody.AddChild(Text("Desempenho", 16));
        _sideBody.AddChild(Check("Papel de parede alternativo (use se o quarto não aparecer no fundo)", s.WallpaperFallback, v => { s.WallpaperFallback = v; _m.SettingsChanged(); }));
        _sideBody.AddChild(Check("Pausar quando uma janela maximizada cobre a área de trabalho", s.PauseWhenCovered, v => { s.PauseWhenCovered = v; _m.SettingsChanged(); }));
        _sideBody.AddChild(Check("Anti-serrilhado (MSAA 2×)", s.Antialiasing, v => { s.Antialiasing = v; _m.SettingsChanged(); }));
        _sideBody.AddChild(Check("Modo de diagnóstico (CPU, qps e pausa no canto)", s.Diagnostics, v => _m.SetDiagnostics(v)));
        _sideBody.AddChild(SpinRow("Quadros/s andando", 5, 60, 1, s.FpsMoving, v => { s.FpsMoving = (int)v; _m.SettingsChanged(); }));
        _sideBody.AddChild(SpinRow("Quadros/s parado", 1, 30, 1, s.FpsStill, v => { s.FpsStill = (int)v; _m.SettingsChanged(); }));

        _sideBody.AddChild(new HSeparator());
        _sideBody.AddChild(Text("Sistema", 16));
        var auto = Check(_m.AutostartAvailable ? "Iniciar com o Windows" : "Iniciar com o Windows (só no executável exportado)",
            _m.IsAutostartOn(), v => _m.SetAutostart(v));
        auto.Disabled = !_m.AutostartAvailable;
        _sideBody.AddChild(auto);
        var sys = new HBoxContainer();
        sys.AddThemeConstantOverride("separation", 6);
        sys.AddChild(Btn("Abrir pasta de dados", () => OS.ShellOpen(_m.Paths.DataDir)));
        sys.AddChild(Spacer());
        sys.AddChild(Btn("Sair do Jubuddy", () => _m.RequestQuit()));
        _sideBody.AddChild(sys);
        _sideBody.AddChild(Text($"Dados em: {_m.Paths.DataDir}", 12, Muted, wrap: true));
        _sideBody.AddChild(Text($"Papel de parede: {_m.WallpaperModeName}", 12, Muted, wrap: true));
    }

    private static Control SpinRow(string label, double min, double max, double step, double value, Action<double> set)
    {
        var row = new HBoxContainer();
        var l = Text(label, 14);
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(l);
        var spin = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(96, 0) };
        spin.ValueChanged += v => set(v);
        row.AddChild(spin);
        return row;
    }

    private static Control SliderRow2(string label, float min, float max, float value, Action<float> set)
    {
        var v = new VBoxContainer();
        v.AddChild(Text(label, 14));
        var s = new HSlider { MinValue = min, MaxValue = max, Step = 0.01, Value = value };
        s.ValueChanged += x => set((float)x);
        v.AddChild(s);
        return v;
    }

    private static Control ColorRow2(string label, string hex, Action<string> set)
    {
        var row = new HBoxContainer();
        var l = Text(label, 14);
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(l);
        var pick = new ColorPickerButton { Color = Visuals.Hex(hex), EditAlpha = false, CustomMinimumSize = new Vector2(110, 28) };
        // Só aplica ao fechar o seletor: refazer o quarto a cada arrastada seria desperdício.
        pick.PopupClosed += () => set("#" + pick.Color.ToHtml(false));
        row.AddChild(pick);
        return row;
    }

    private static CheckBox Check(string label, bool value, Action<bool> set)
    {
        var c = new CheckBox { Text = label, ButtonPressed = value, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 0) };
        c.Toggled += on => set(on);
        return c;
    }
}
