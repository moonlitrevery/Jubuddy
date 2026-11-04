using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using static Jubuddy.Platform.Windows.Native;

namespace Jubuddy.Platform.Windows;

/// <summary>
/// Papel de parede pela técnica do WorkerW (a mesma do Wallpaper Engine e do Lively),
/// detecção de tela cheia/bloqueio para pausar, e iniciar com o Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDesktopPlatform : IDesktopPlatform
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Jubuddy";

    private long? _savedStyle;
    private long? _savedExStyle;
    private IntPtr _parent;
    private WallpaperAttach? _mode;
    private readonly uint _pid = (uint)Environment.ProcessId;

    private Mutex? _mutex;
    private EventWaitHandle? _openRoom;

    public string Name => "Windows";

    public ScreenRect? PrimaryMonitor()
    {
        var mon = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (mon == IntPtr.Zero || !GetMonitorInfoW(mon, ref info)) return null;
        var r = info.rcMonitor;
        return new ScreenRect(r.Left, r.Top, r.Width, r.Height);
    }

    public IReadOnlyList<ScreenRect> Monitors()
    {
        var found = new List<(ScreenRect Rect, bool Primary)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr mon, IntPtr _, ref RECT __, IntPtr ___) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfoW(mon, ref info))
            {
                var r = info.rcMonitor;
                found.Add((new ScreenRect(r.Left, r.Top, r.Width, r.Height), (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            }
            return true;
        }, IntPtr.Zero);
        var result = new List<ScreenRect>();
        foreach (var m in found) if (m.Primary) result.Add(m.Rect);
        foreach (var m in found) if (!m.Primary) result.Add(m.Rect);
        return result;
    }

    // ---------- Papel de parede ----------

    public WallpaperAttach AttachAsWallpaper(IntPtr hwnd, ScreenRect monitor)
    {
        var progman = FindWindowW("Progman", null);
        if (progman == IntPtr.Zero) return WallpaperAttach.Fallback;

        // Pede ao Explorer para criar o WorkerW atrás dos ícones (mensagem não documentada 0x052C).
        SendMessageTimeoutW(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), SMTO_NORMAL, 1000, out _);
        SendMessageTimeoutW(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out _);

        SaveStyles(hwnd);

        // Clássico (Windows 10 / 11 até 23H2): o WorkerW irmão logo depois do que contém o SHELLDLL_DefView.
        var workerw = FindClassicWorkerW();
        if (workerw != IntPtr.Zero && AttachTo(hwnd, workerw, null, monitor))
        {
            _mode = WallpaperAttach.Embedded;
            return _mode.Value;
        }

        // Windows 11 24H2+: o WorkerW virou filho do Progman. A janela entra como filha do Progman,
        // logo abaixo do SHELLDLL_DefView (ícones por cima, papel de parede original por baixo).
        // Nesse modo o Progman não tem bitmap de redirecionamento (WS_EX_NOREDIRECTIONBITMAP): a janela filha
        // precisa ser "layered" para ganhar superfície própria no compositor, senão fica invisível.
        var defView = FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        var innerWorker = FindWindowExW(progman, IntPtr.Zero, "WorkerW", null);
        if (defView != IntPtr.Zero && innerWorker != IntPtr.Zero && AttachTo(hwnd, progman, defView, monitor, layered: true))
        {
            _mode = WallpaperAttach.EmbeddedRaised;
            return _mode.Value;
        }

        RestoreStyles(hwnd);
        return WallpaperAttach.Fallback;
    }

    private static IntPtr FindClassicWorkerW()
    {
        var result = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowExW(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                var w = FindWindowExW(IntPtr.Zero, top, "WorkerW", null);
                if (w != IntPtr.Zero) { result = w; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private void SaveStyles(IntPtr hwnd)
    {
        if (_savedStyle != null) return;
        _savedStyle = GetLong(hwnd, GWL_STYLE);
        _savedExStyle = GetLong(hwnd, GWL_EXSTYLE);
    }

    private bool AttachTo(IntPtr hwnd, IntPtr parent, IntPtr? insertAfter, ScreenRect monitor, bool layered = false)
    {
        var style = _savedStyle!.Value;
        style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MAXIMIZEBOX | WS_MINIMIZEBOX);
        style |= WS_CHILD | WS_VISIBLE;
        SetLong(hwnd, GWL_STYLE, style);

        var ex = _savedExStyle!.Value;
        ex &= ~(WS_EX_APPWINDOW | WS_EX_TOPMOST);
        ex |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        if (layered) ex |= WS_EX_LAYERED;
        SetLong(hwnd, GWL_EXSTYLE, ex);
        if (layered) SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);

        SetParent(hwnd, parent);
        if (GetParent(hwnd) != parent)
        {
            RestoreStyles(hwnd);
            return false;
        }

        // Coordenadas do monitor convertidas para o espaço do pai (que cobre a tela virtual inteira).
        var r = new RECT { Left = monitor.X, Top = monitor.Y, Right = monitor.X + monitor.W, Bottom = monitor.Y + monitor.H };
        MapWindowPoints(IntPtr.Zero, parent, ref r, 2);
        var flags = SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW;
        if (insertAfter == null) flags |= SWP_NOZORDER;
        SetWindowPos(hwnd, insertAfter ?? IntPtr.Zero, r.Left, r.Top, monitor.W, monitor.H, flags);

        _parent = parent;
        return true;
    }

    public void PrepareFallback(IntPtr hwnd, ScreenRect monitor)
    {
        SaveStyles(hwnd);
        var ex = GetLong(hwnd, GWL_EXSTYLE);
        var wasLayered = (ex & WS_EX_LAYERED) != 0;
        ex &= ~(WS_EX_APPWINDOW | WS_EX_TOPMOST);
        ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED;
        SetLong(hwnd, GWL_EXSTYLE, ex);
        if (!wasLayered) SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
        SetWindowPos(hwnd, HWND_BOTTOM, monitor.X, monitor.Y, monitor.W, monitor.H,
            SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        _parent = IntPtr.Zero;
        _mode = WallpaperAttach.Fallback;
    }

    public void Detach(IntPtr hwnd)
    {
        if (_mode == null) return;
        if (_mode is WallpaperAttach.Embedded or WallpaperAttach.EmbeddedRaised)
            SetParent(hwnd, IntPtr.Zero);
        RestoreStyles(hwnd);
        _mode = null;
        _parent = IntPtr.Zero;
        RefreshDesktopWallpaper();
    }

    private void RestoreStyles(IntPtr hwnd)
    {
        if (_savedStyle is { } s) SetLong(hwnd, GWL_STYLE, s & ~WS_CHILD);
        if (_savedExStyle is { } e) SetLong(hwnd, GWL_EXSTYLE, e);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        _savedStyle = null;
        _savedExStyle = null;
    }

    /// <summary>Repinta o papel de parede original (senão fica o último quadro do buddy "grudado" no fundo).</summary>
    private static void RefreshDesktopWallpaper()
    {
        try
        {
            var sb = new StringBuilder(1024);
            if (SystemParametersInfoGetString(0x0073 /* SPI_GETDESKWALLPAPER */, (uint)sb.Capacity, sb, 0))
                SystemParametersInfoSetString(0x0014 /* SPI_SETDESKWALLPAPER */, 0, sb.ToString(), 0);
        }
        catch { /* só estética */ }
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoGetString(uint uiAction, uint uiParam, StringBuilder pvParam, uint fWinIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoSetString(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

    public bool Maintain(IntPtr hwnd)
    {
        switch (_mode)
        {
            case WallpaperAttach.Embedded:
            case WallpaperAttach.EmbeddedRaised:
                return IsWindow(_parent) && GetParent(hwnd) == _parent;
            case WallpaperAttach.Fallback:
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                return true;
            default:
                return true;
        }
    }

    // ---------- Pausa ----------

    public PauseReason QueryPause(IntPtr ownHwnd, bool wallpaperMode, bool pauseWhenCovered, ScreenRect area)
    {
        if (SystemParametersInfoW(SPI_GETSCREENSAVERRUNNING, 0, out var saver, 0) && saver != 0)
            return PauseReason.ScreenSaver;

        if (!InputDesktopIsDefault())
            return PauseReason.Locked;

        if (SHQueryUserNotificationState(out var state) == 0)
        {
            switch (state)
            {
                case QUNS_NOT_PRESENT: return PauseReason.Locked;
                case QUNS_BUSY:
                case QUNS_RUNNING_D3D_FULL_SCREEN:
                case 7 /* QUNS_APP: app da Store em tela cheia */:
                    return PauseReason.FullscreenApp;
                case QUNS_PRESENTATION_MODE: return PauseReason.Presentation;
            }
        }

        if (!wallpaperMode) return PauseReason.None;

        // O monitor onde o quarto está desenhado.
        var primary = MonitorFromPoint(new POINT { X = area.X + area.W / 2, Y = area.Y + area.H / 2 }, MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(primary, ref info)) return PauseReason.None;
        var mon = info.rcMonitor;

        // Jogo em "tela cheia sem borda" não aciona o SHQueryUserNotificationState: confere a janela em primeiro plano.
        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && IsCandidate(fg, ownHwnd) && Covers(fg, mon) && OnMonitor(fg, primary))
            return PauseReason.FullscreenApp;

        if (!pauseWhenCovered) return PauseReason.None;

        var covered = false;
        EnumWindows((w, _) =>
        {
            if (!IsCandidate(w, ownHwnd) || !OnMonitor(w, primary)) return true;
            if (IsZoomed(w) || (Covers(w, mon) && (GetLong(w, GWL_EXSTYLE) & WS_EX_LAYERED) == 0))
            {
                covered = true;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return covered ? PauseReason.CoveredByWindow : PauseReason.None;
    }

    /// <summary>Janela "de verdade" de outro app: visível, não minimizada, não escondida, não é do shell nem sobreposição.</summary>
    private bool IsCandidate(IntPtr w, IntPtr own)
    {
        if (w == own || !IsWindowVisible(w) || IsIconic(w)) return false;
        GetWindowThreadProcessId(w, out var pid);
        if (pid == _pid) return false;

        var ex = GetLong(w, GWL_EXSTYLE);
        if ((ex & (WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE)) != 0) return false;

        if (DwmGetWindowAttribute(w, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return false;

        var cls = ClassName(w);
        return cls is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
            or "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow" or "TopLevelWindowForOverflowXamlIsland");
    }

    private static bool Covers(IntPtr w, RECT mon)
    {
        if (!GetWindowRect(w, out var r)) return false;
        return r.Left <= mon.Left && r.Top <= mon.Top && r.Right >= mon.Right && r.Bottom >= mon.Bottom;
    }

    private static bool OnMonitor(IntPtr w, IntPtr monitor) => MonitorFromWindow(w, MONITOR_DEFAULTTONEAREST) == monitor;

    private static bool InputDesktopIsDefault()
    {
        const uint DESKTOP_READOBJECTS = 0x0001;
        var desk = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
        if (desk == IntPtr.Zero) return false; // tela de bloqueio / UAC: sem acesso
        try
        {
            var sb = new StringBuilder(64);
            if (!GetUserObjectInformationW(desk, UOI_NAME, sb, sb.Capacity * 2, out _)) return true;
            return string.Equals(sb.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
        }
        finally { CloseDesktop(desk); }
    }

    // ---------- Iniciar com o Windows ----------

    public bool AutostartSupported => true;

    public bool IsAutostartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(RunValue) is string s && s.Length > 0;
    }

    public void SetAutostart(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled) key.SetValue(RunValue, $"\"{executablePath}\"");
        else if (key.GetValue(RunValue) != null) key.DeleteValue(RunValue);
    }

    // ---------- Instância única ----------

    public bool TryBecomeSingleInstance()
    {
        try
        {
            _mutex = new Mutex(true, @"Local\Jubuddy.SingleInstance", out var created);
            _openRoom = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Jubuddy.OpenRoom");
            if (!created)
            {
                _openRoom.Set(); // a outra instância abre o quarto
                return false;
            }
            return true;
        }
        catch { return true; }
    }

    public bool ConsumeOpenRoomRequest()
    {
        try { return _openRoom?.WaitOne(0) == true; } catch { return false; }
    }

    public void LowerProcessPriority()
    {
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
    }
}
