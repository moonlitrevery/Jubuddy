using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Godot;

namespace Jubuddy.App.UI;

/// <summary>
/// Modo de diagnóstico: no canto mostra CPU do processo, quadros por segundo, memória e se está pausado.
/// Também grava uma linha a cada 5 s em diagnostico.csv, para conferir depois de jogar.
/// </summary>
public partial class DiagnosticsOverlay : CanvasLayer
{
    private Label _label = null!;
    private PanelContainer _panel = null!;
    private readonly Process _proc = Process.GetCurrentProcess();
    private TimeSpan _lastCpu;
    private DateTime _lastWall;
    private double _logIn;
    private string _csvPath = "";

    public double CpuPercent { get; private set; }
    public double MemoryMb { get; private set; }
    public bool Enabled { get; private set; }
    public float TopMargin { get; set; } = 16;

    public override void _Ready()
    {
        Layer = 100;
        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        var sb = new StyleBoxFlat { BgColor = new Color(0.08f, 0.09f, 0.11f, 0.78f) };
        sb.SetCornerRadiusAll(8);
        sb.SetContentMarginAll(8);
        _panel.AddThemeStyleboxOverride("panel", sb);
        _label = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _label.AddThemeFontSizeOverride("font_size", 14);
        _label.AddThemeColorOverride("font_color", new Color(0.92f, 0.95f, 0.9f));
        _panel.AddChild(_label);
        AddChild(_panel);
        _proc.Refresh();
        _lastCpu = _proc.TotalProcessorTime;
        _lastWall = DateTime.UtcNow;
    }

    public void Setup(string csvPath, bool enabled)
    {
        _csvPath = csvPath;
        SetEnabled(enabled);
    }

    public void SetEnabled(bool on)
    {
        Enabled = on;
        _panel.Visible = on;
    }

    /// <summary>Chamado 1×/s pelo governador. Medir é barato (uma leitura de contador do processo).</summary>
    public void Sample(bool rendering, string state, string mode, string activity)
    {
        _proc.Refresh();
        var cpu = _proc.TotalProcessorTime;
        var wall = DateTime.UtcNow;
        var dWall = (wall - _lastWall).TotalSeconds;
        if (dWall > 0.2)
        {
            CpuPercent = (cpu - _lastCpu).TotalSeconds / dWall / System.Environment.ProcessorCount * 100.0;
            _lastCpu = cpu;
            _lastWall = wall;
        }
        MemoryMb = _proc.WorkingSet64 / (1024.0 * 1024.0);

        if (!Enabled) return;

        var fps = rendering ? Engine.GetFramesPerSecond() : 0;
        _label.Text =
            $"CPU {CpuPercent:0.0}%   {fps:0} qps   {MemoryMb:0} MB\n" +
            $"{(rendering ? "desenhando" : "PAUSADO")} · {state}\n" +
            $"{mode} · {activity}";
        var vp = GetViewport().GetVisibleRect().Size;
        _panel.Size = Vector2.Zero;
        _panel.Position = new Vector2(vp.X - _panel.GetCombinedMinimumSize().X - 16, TopMargin);

        _logIn -= dWall;
        if (_logIn <= 0 && _csvPath.Length > 0)
        {
            _logIn = 5;
            try
            {
                var newFile = !File.Exists(_csvPath);
                using var w = File.AppendText(_csvPath);
                if (newFile) w.WriteLine("hora;cpu_pct;qps;memoria_mb;desenhando;estado;modo;atividade");
                var ci = CultureInfo.InvariantCulture;
                w.WriteLine(string.Join(";",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", ci),
                    CpuPercent.ToString("0.00", ci),
                    fps.ToString("0", ci),
                    MemoryMb.ToString("0", ci),
                    rendering ? "1" : "0",
                    state, mode, activity));
            }
            catch { /* diagnóstico nunca derruba o app */ }
        }
    }
}
