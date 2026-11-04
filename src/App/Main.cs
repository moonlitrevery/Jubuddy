using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Jubuddy.App.Buddy;
using Jubuddy.App.UI;
using Jubuddy.Core;
using Jubuddy.Platform;

namespace Jubuddy.App;

/// <summary>
/// Ponto de entrada. Monta o quarto, alterna entre papel de parede e modo quarto,
/// e governa o desempenho: limite de quadros por atividade e parada total do desenho
/// quando há jogo em tela cheia, janela maximizada, PC bloqueado ou proteção de tela.
/// </summary>
public partial class Main : Node
{
    public enum AppMode { Starting, Wallpaper, Room }

    private const float CameraYaw = Mathf.Pi / 4;
    private const float RoomFitSize = 6.8f;

    // Dados
    public AppPaths Paths { get; private set; } = null!;
    public AppSettings Settings { get; private set; } = null!;
    public BuddyLook Look { get; private set; } = null!;
    public Room Room { get; private set; } = null!;
    public RoomStore Store { get; private set; } = null!;
    public PhraseBook Phrases { get; private set; } = PhraseBook.Empty;
    public List<string> LoadProblems { get; } = new();

    // Cena
    public Camera3D Camera { get; private set; } = null!;
    public RoomView View { get; private set; } = null!;
    public BuddyBody Body { get; private set; } = null!;
    public BuddyDirector Director { get; private set; } = null!;
    private Godot.Environment _env = null!;
    private SpeechBubble _bubble = null!;
    private DiagnosticsOverlay _diag = null!;
    private Tray _tray = null!;
    private RoomEditor _editor = null!;

    // Estado
    public IDesktopPlatform Platform { get; private set; } = null!;
    public AppMode Mode { get; private set; } = AppMode.Starting;
    private LaunchOptions _opts = null!;
    private WallpaperAttach _attach = WallpaperAttach.NotSupported;
    private ScreenRect _wallpaperArea;
    private PauseReason _pause = PauseReason.None;
    private double _checkIn;
    private double _quitIn;
    private float _roomCamSize = RoomFitSize * 1.08f;
    private readonly Random _rng = new();
    private readonly PhraseScheduler _phraseSchedule = new();
    private bool _quitting;

    public bool AutostartAvailable => Platform.AutostartSupported && OS.HasFeature("template");
    public bool Paused => _pause != PauseReason.None;

    public string WallpaperModeName => _attach switch
    {
        WallpaperAttach.Embedded => "embutido no WorkerW (atrás dos ícones)",
        WallpaperAttach.EmbeddedRaised => "embutido no Progman (Windows 11 24H2+)",
        WallpaperAttach.Fallback => "plano B: janela transparente no fundo",
        _ => "janela comum (sistema sem suporte)",
    };

    public override void _Ready()
    {
        _opts = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        Platform = DesktopPlatform.Create();
        if (!Platform.TryBecomeSingleInstance())
        {
            // Já tem um Jubuddy rodando: ele recebeu o pedido para abrir o quarto.
            _quitting = true;
            GetTree().Quit();
            return;
        }
        Platform.LowerProcessPriority();
        GetTree().AutoAcceptQuit = false;

        LoadData();
        BuildWorld();
        BuildOverlays();
        BuildTray();

        _phraseSchedule.Schedule(DateTime.Now, Settings.PhraseIntervalMinutes, _rng);
        _quitIn = _opts.QuitAfterSeconds;
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        // Espera a janela existir de fato antes de mexer nela.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_opts.StartInRoom) EnterRoomMode();
        else EnterWallpaperMode();
        if (_opts.DemoPanel is { } panel && Mode == AppMode.Room) _editor.Demo(panel);

        if (_opts.CapturePath is { } capture)
        {
            await ToSignal(GetTree().CreateTimer(_opts.CaptureDelay), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var err = GetViewport().GetTexture().GetImage().SavePng(capture);
            GD.Print($"Jubuddy: captura em {capture} ({err})");
        }
    }

    // ---------- Dados ----------

    private void LoadData()
    {
        var dir = _opts.DataDir ?? ProjectSettings.GlobalizePath("user://");
        Paths = new AppPaths(dir);
        Paths.EnsureCreated();

        Settings = JsonStore.Load(Paths.SettingsFile, () => new AppSettings(), out var p1);
        Settings.Clamp();
        if (!File.Exists(Paths.SettingsFile)) JsonStore.Save(Paths.SettingsFile, Settings);
        if (_opts.Diagnostics) Settings.Diagnostics = true;

        Look = JsonStore.Load(Paths.LookFile, () => new BuddyLook(), out var p2);
        Look.Clamp();
        if (!File.Exists(Paths.LookFile)) JsonStore.Save(Paths.LookFile, Look);

        Store = new RoomStore(Paths);
        Room = Store.Load(out var p3);

        try { Phrases = PhraseBook.LoadOrCreate(Paths.PhrasesFile); }
        catch (Exception e) { LoadProblems.Add($"Não consegui ler frases.txt: {e.Message}"); }

        foreach (var p in new[] { p1, p2, p3 })
            if (p != null) { LoadProblems.Add(p); GD.PushWarning(p); }
    }

    public void SaveRoom()
    {
        try { Store.Save(Room); }
        catch (Exception e) { GD.PushError($"Falha ao salvar o quarto: {e.Message}"); }
    }

    public void SaveLook()
    {
        try { JsonStore.Save(Paths.LookFile, Look); }
        catch (Exception e) { GD.PushError($"Falha ao salvar buddy.json: {e.Message}"); }
    }

    public void SaveSettings()
    {
        try { JsonStore.Save(Paths.SettingsFile, Settings); }
        catch (Exception e) { GD.PushError($"Falha ao salvar ajustes.json: {e.Message}"); }
    }

    /// <summary>
    /// Copia a imagem do pôster para a pasta do app (reduzida a no máximo 1024 px, para não pesar na memória).
    /// Assim o quarto não quebra se o original for movido ou apagado.
    /// </summary>
    public string? ImportPoster(string sourcePath, out float aspect, out string? error)
    {
        aspect = 1f;
        error = null;
        try
        {
            var img = Image.LoadFromFile(sourcePath);
            if (img == null || img.IsEmpty()) { error = "Não consegui abrir essa imagem."; return null; }
            int w = img.GetWidth(), h = img.GetHeight();
            aspect = w / (float)h;
            const int max = 1024;
            if (w > max || h > max)
            {
                var k = max / (float)Math.Max(w, h);
                img.Resize(Math.Max(1, (int)(w * k)), Math.Max(1, (int)(h * k)), Image.Interpolation.Lanczos);
            }
            var name = Guid.NewGuid().ToString("N") + ".png";
            var err = img.SavePng(Path.Combine(Paths.PostersDir, name));
            if (err != Error.Ok) { error = $"Não consegui copiar a imagem ({err})."; return null; }
            return name;
        }
        catch (Exception e)
        {
            error = $"Não consegui copiar a imagem: {e.Message}";
            return null;
        }
    }

    // ---------- Mundo ----------

    private void BuildWorld()
    {
        _env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Visuals.Hex(Settings.Background, new Color(0.18f, 0.2f, 0.25f)),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(1f, 0.98f, 0.95f),
            AmbientLightEnergy = 0.6f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
        };
        AddChild(new WorldEnvironment { Environment = _env });

        // Uma luz direcional sem sombra: iluminação barata.
        AddChild(new DirectionalLight3D
        {
            ShadowEnabled = false,
            LightEnergy = 0.8f,
            RotationDegrees = new Vector3(-55, 28, 0),
        });

        Camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = RoomFitSize,
            Near = 0.1f,
            Far = 100f,
            Current = true,
        };
        AddChild(Camera);

        View = new RoomView { Name = "Room" };
        AddChild(View);
        View.BuildShell(Room.Size, Settings);
        View.Sync(Room, Paths.PostersDir, force: true);

        Body = new BuddyBody { Name = "Buddy" };
        AddChild(Body);
        Body.Rebuild(Look);

        Director = new BuddyDirector { Name = "Director" };
        AddChild(Director);
        Director.Init(Room, Body, _opts.ForcedActivity);

        ApplyRenderSettings();
    }

    private void BuildOverlays()
    {
        _bubble = new SpeechBubble { Name = "Bubble" };
        AddChild(_bubble);
        _diag = new DiagnosticsOverlay { Name = "Diagnostics" };
        AddChild(_diag);
        _diag.Setup(Paths.DiagnosticsFile, Settings.Diagnostics);
        _editor = new RoomEditor { Name = "Editor" }.Init(this);
        AddChild(_editor);
    }

    private void BuildTray()
    {
        _tray = new Tray { Name = "Tray" };
        AddChild(_tray);
        var icon = GD.Load<Texture2D>("res://icon.svg");
        _tray.Setup(icon, AutostartAvailable, IsAutostartOn(), Settings.Diagnostics);
        _tray.OpenRoom += EnterRoomMode;
        _tray.Quit += RequestQuit;
        _tray.EditPhrases += () => OS.ShellOpen(Paths.PhrasesFile);
        _tray.OpenDataFolder += () => OS.ShellOpen(Paths.DataDir);
        _tray.DiagnosticsToggled += SetDiagnostics;
        _tray.AutostartToggled += SetAutostart;
    }

    private void ApplyRenderSettings()
    {
        GetViewport().Msaa3D = Settings.Antialiasing ? Viewport.Msaa.Msaa2X : Viewport.Msaa.Disabled;
        _env.BackgroundColor = Visuals.Hex(Settings.Background, new Color(0.18f, 0.2f, 0.25f));
        ConfigureCamera();
    }

    private void ConfigureCamera()
    {
        var target = new Vector3(0, 0.75f, 0);
        var pitch = Mathf.DegToRad(30);
        var dir = new Vector3(Mathf.Sin(CameraYaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(CameraYaw) * Mathf.Cos(pitch));
        Camera.LookAtFromPosition(target + dir * 30f, target, Vector3.Up);

        if (Mode == AppMode.Room)
        {
            Camera.Size = _roomCamSize;
            Camera.HOffset = 0;
            Camera.VOffset = 0.35f; // desce um pouco o quarto, abaixo da barra de cima
        }
        else
        {
            var size = RoomFitSize / Settings.WallpaperZoom;
            Camera.Size = size;
            var vp = GetViewport().GetVisibleRect().Size;
            var aspect = vp.Y > 0 ? vp.X / vp.Y : 16f / 9f;
            // Câmera vai para o lado oposto: posição +1 põe o quarto na direita da tela.
            Camera.HOffset = -Settings.WallpaperOffsetX * (size * aspect / 2f) * 0.7f;
            Camera.VOffset = -Settings.WallpaperOffsetY * (size / 2f) * 0.7f;
        }
    }

    public void ZoomRoomCamera(float factor)
    {
        _roomCamSize = Mathf.Clamp(_roomCamSize * factor, 3f, 12f);
        if (Mode == AppMode.Room) Camera.Size = _roomCamSize;
    }

    // ---------- Modos ----------

    private IntPtr Hwnd() => new(DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle));

    public int MonitorCount => Math.Max(1, Platform.Monitors().Count);

    /// <summary>Monitor escolhido para o papel de parede (cai para o principal se ele não existir mais).</summary>
    private ScreenRect WallpaperRect()
    {
        var monitors = Platform.Monitors();
        var i = Settings.WallpaperMonitor - 1;
        if (i >= 0 && i < monitors.Count) return monitors[i];
        return PrimaryRect();
    }

    private ScreenRect PrimaryRect()
    {
        if (Platform.PrimaryMonitor() is { } r) return r;
        var screen = DisplayServer.GetPrimaryScreen();
        var pos = DisplayServer.ScreenGetPosition(screen);
        var size = DisplayServer.ScreenGetSize(screen);
        return new ScreenRect(pos.X, pos.Y, size.X, size.Y);
    }

    /// <summary>Vira papel de parede: atrás dos ícones, sem barra de tarefas, sem interação.</summary>
    public void EnterWallpaperMode()
    {
        if (_quitting) return;
        _editor.Close();
        var hwnd = Hwnd();
        Platform.Detach(hwnd);

        var win = GetWindow();
        var mon = WallpaperRect();
        _wallpaperArea = mon;
        win.Mode = Window.ModeEnum.Windowed;
        win.Unresizable = false;
        win.Borderless = true;
        win.Title = "Jubuddy";
        win.Position = new Vector2I(mon.X, mon.Y);
        win.Size = new Vector2I(mon.W, mon.H);

        _attach = _opts.ForceFallback || Settings.WallpaperFallback
            ? WallpaperAttach.Fallback
            : Platform.AttachAsWallpaper(hwnd, mon);
        var fallback = _attach == WallpaperAttach.Fallback;
        win.Transparent = fallback;
        GetViewport().TransparentBg = fallback;
        _env.BackgroundMode = fallback ? Godot.Environment.BGMode.ClearColor : Godot.Environment.BGMode.Color;
        win.MousePassthrough = fallback;
        if (fallback) Platform.PrepareFallback(hwnd, mon);

        Mode = AppMode.Wallpaper;
        ConfigureCamera();
        GD.Print($"Jubuddy: papel de parede = {WallpaperModeName}");
    }

    /// <summary>Modo quarto: janela normal para mexer no quarto. Fechar volta ao papel de parede.</summary>
    public void EnterRoomMode()
    {
        if (_quitting) return;
        var win = GetWindow();
        if (Mode == AppMode.Room)
        {
            if (win.Mode == Window.ModeEnum.Minimized) win.Mode = Window.ModeEnum.Windowed;
            win.GrabFocus();
            DisplayServer.WindowMoveToForeground();
            return;
        }

        Platform.Detach(Hwnd());
        _attach = WallpaperAttach.NotSupported;
        win.MousePassthrough = false;
        win.Transparent = false;
        GetViewport().TransparentBg = false;
        _env.BackgroundMode = Godot.Environment.BGMode.Color;
        win.Borderless = false;
        win.Unresizable = false;
        win.Title = "Jubuddy — meu quarto";

        var mon = WallpaperRect();
        var size = new Vector2I(Math.Min(1280, (int)(mon.W * 0.85f)), Math.Min(820, (int)(mon.H * 0.85f)));
        win.Size = size;
        win.Position = new Vector2I(mon.X + (mon.W - size.X) / 2, mon.Y + (mon.H - size.Y) / 2);

        Mode = AppMode.Room;
        SetPause(PauseReason.None);
        ConfigureCamera();
        _editor.Open();
        win.GrabFocus();
        DisplayServer.WindowMoveToForeground();
        Director.Wave(CameraYaw);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            if (Mode == AppMode.Room) EnterWallpaperMode();
            else RequestQuit();
        }
        else if (what == NotificationWMSizeChanged && Camera != null)
        {
            ConfigureCamera();
        }
    }

    public void RequestQuit()
    {
        if (_quitting) return;
        _quitting = true;
        SaveRoom();
        try { Platform.Detach(Hwnd()); } catch { /* saindo de qualquer jeito */ }
        GetTree().Quit();
    }

    // ---------- Laço principal: governador de desempenho ----------

    public override void _Process(double delta)
    {
        if (_quitting || Mode == AppMode.Starting) return;

        _checkIn -= delta;
        if (_checkIn <= 0)
        {
            _checkIn = 1.0; // checagens de estado no máximo 1×/s
            CheckState();
        }

        if (Paused) return;

        Engine.MaxFps = TargetFps();
        View.Sync(Room, Paths.PostersDir);
        _bubble.Follow(Camera, Body.HeadGlobalPosition, delta);
    }

    private int TargetFps()
    {
        if (Mode == AppMode.Room && GetWindow().HasFocus()) return Settings.FpsRoomMode;
        if (Director.IsMoving || _bubble.Showing) return Settings.FpsMoving;
        if (Director.IsSleeping) return Settings.FpsSleeping;
        return Settings.FpsStill;
    }

    private void CheckState()
    {
        var hwnd = Hwnd();

        if (Platform.ConsumeOpenRoomRequest()) EnterRoomMode();

        if (Mode == AppMode.Wallpaper && !Platform.Maintain(hwnd))
        {
            GD.PushWarning("Jubuddy: o papel de parede se soltou (Explorer reiniciou?). Refazendo.");
            EnterWallpaperMode();
        }

        var reason = _opts.ForcePause ? PauseReason.Forced
            : _opts.NeverPause ? PauseReason.None
            : Platform.QueryPause(hwnd, Mode == AppMode.Wallpaper, Settings.PauseWhenCovered, _wallpaperArea);
        if (Mode == AppMode.Room && GetWindow().Mode == Window.ModeEnum.Minimized) reason = PauseReason.Minimized;
        SetPause(reason);

        if (!Paused) MaybeShowPhrase();

        var state = Paused ? PauseText(_pause) : "ativo";
        var modeText = Mode == AppMode.Room ? "modo quarto" : WallpaperShortName();
        _diag.TopMargin = Mode == AppMode.Room ? 78 : 16;
        _diag.Sample(!Paused, state, modeText, Director.Describe());
        _tray.SetTooltip(Paused ? $"Jubuddy · pausado ({state})" : $"Jubuddy · {Director.Describe()}");

        if (_quitIn > 0)
        {
            _quitIn -= 1.0;
            if (_quitIn <= 0) RequestQuit();
        }
    }

    private string WallpaperShortName() => _attach switch
    {
        WallpaperAttach.Embedded => "fundo (WorkerW)",
        WallpaperAttach.EmbeddedRaised => "fundo (Progman 24H2)",
        WallpaperAttach.Fallback => "fundo (plano B)",
        _ => "janela",
    };

    public static string PauseText(PauseReason r) => r switch
    {
        PauseReason.FullscreenApp => "tela cheia",
        PauseReason.Presentation => "modo apresentação",
        PauseReason.CoveredByWindow => "janela maximizada",
        PauseReason.Locked => "PC bloqueado",
        PauseReason.ScreenSaver => "proteção de tela",
        PauseReason.Minimized => "minimizado",
        PauseReason.Forced => "pausa forçada",
        _ => "ativo",
    };

    /// <summary>
    /// Para de desenhar por completo (RenderingServer.RenderLoopEnabled = false) e reduz o laço a 2 voltas/s.
    /// Ao voltar, o buddy "avança pelo relógio" até onde estaria agora.
    /// </summary>
    private void SetPause(PauseReason reason)
    {
        if (reason == _pause) return;
        var wasPaused = Paused;
        _pause = reason;

        if (Paused && !wasPaused)
        {
            RenderingServer.RenderLoopEnabled = false;
            Engine.MaxFps = 2;
            Director.ProcessMode = ProcessModeEnum.Disabled;
            Body.ProcessMode = ProcessModeEnum.Disabled;
            _editor.ProcessMode = ProcessModeEnum.Disabled;
            _bubble.HideNow();
        }
        else if (!Paused && wasPaused)
        {
            Director.CatchUp();
            Director.ProcessMode = ProcessModeEnum.Inherit;
            Body.ProcessMode = ProcessModeEnum.Inherit;
            _editor.ProcessMode = ProcessModeEnum.Inherit;
            RenderingServer.RenderLoopEnabled = true;
            Engine.MaxFps = TargetFps();
            if (_phraseSchedule.Due(DateTime.Now))
                _phraseSchedule.Schedule(DateTime.Now, Settings.PhraseIntervalMinutes / 3, _rng); // sem enxurrada ao voltar
        }
    }

    // ---------- Frases ----------

    private void MaybeShowPhrase()
    {
        var now = DateTime.Now;
        if (!_phraseSchedule.Due(now)) return;
        if (Director.IsSleeping)
        {
            _phraseSchedule.Schedule(now, Math.Max(10, Settings.PhraseIntervalMinutes / 2), _rng);
            return;
        }
        ShowPhraseNow();
        _phraseSchedule.Schedule(now, Settings.PhraseIntervalMinutes, _rng);
    }

    public bool ShowPhraseNow()
    {
        if (PhraseBook.ChangedOnDisk(Paths.PhrasesFile, Phrases)) ReloadPhrases();
        var text = Phrases.Next(_rng);
        if (text == null) return false;
        _bubble.Show(text, Settings.PhraseSeconds);
        return true;
    }

    public void ReloadPhrases()
    {
        try { Phrases = PhraseBook.LoadOrCreate(Paths.PhrasesFile); }
        catch (Exception e) { GD.PushWarning($"frases.txt: {e.Message}"); }
    }

    // ---------- Ajustes vindos da interface ----------

    public void ApplyLook() => Body.Rebuild(Look);

    public void SettingsChanged(bool rebuildRoom = false, bool reschedulePhrases = false)
    {
        Settings.Clamp();
        if (Mode == AppMode.Wallpaper) EnterWallpaperMode(); // ex.: trocou de monitor
        if (rebuildRoom)
        {
            View.BuildShell(Room.Size, Settings);
        }
        if (reschedulePhrases) _phraseSchedule.Schedule(DateTime.Now, Settings.PhraseIntervalMinutes, _rng);
        ApplyRenderSettings();
        SaveSettings();
    }

    public void SetDiagnostics(bool on)
    {
        Settings.Diagnostics = on;
        _diag.SetEnabled(on);
        _tray.SetChecks(on, IsAutostartOn());
        SaveSettings();
    }

    public bool IsAutostartOn()
    {
        try { return Platform.IsAutostartEnabled(); } catch { return false; }
    }

    public void SetAutostart(bool on)
    {
        if (!AutostartAvailable) return;
        try { Platform.SetAutostart(on, OS.GetExecutablePath()); }
        catch (Exception e) { GD.PushError($"Iniciar com o Windows: {e.Message}"); }
        _tray.SetChecks(Settings.Diagnostics, IsAutostartOn());
    }
}
