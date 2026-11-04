using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Jubuddy.Platform;

/// <summary>
/// Plataforma "neutra" (Linux, macOS): sem papel de parede de verdade, sem detecção de tela cheia.
/// O app roda como janela comum sem borda, o que basta para desenvolver.
/// </summary>
public sealed class NullDesktopPlatform : IDesktopPlatform
{
    private Mutex? _mutex;

    public string Name => "genérica";

    public ScreenRect? PrimaryMonitor() => null;

    public IReadOnlyList<ScreenRect> Monitors() => Array.Empty<ScreenRect>();

    public WallpaperAttach AttachAsWallpaper(IntPtr hwnd, ScreenRect monitor) => WallpaperAttach.NotSupported;

    public void PrepareFallback(IntPtr hwnd, ScreenRect monitor) { }

    public void Detach(IntPtr hwnd) { }

    public bool Maintain(IntPtr hwnd) => true;

    public PauseReason QueryPause(IntPtr ownHwnd, bool wallpaperMode, bool pauseWhenCovered, ScreenRect area) => PauseReason.None;

    public bool AutostartSupported => false;
    public bool IsAutostartEnabled() => false;
    public void SetAutostart(bool enabled, string executablePath) { }

    public bool TryBecomeSingleInstance()
    {
        try
        {
            _mutex = new Mutex(true, "Jubuddy.SingleInstance", out var created);
            return created;
        }
        catch { return true; }
    }

    public bool ConsumeOpenRoomRequest() => false;

    public void LowerProcessPriority()
    {
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
    }
}
