using System;
using System.Collections.Generic;

namespace Jubuddy.Platform;

/// <summary>Por que o desenho está parado.</summary>
public enum PauseReason
{
    None,
    FullscreenApp,     // jogo ou app em tela cheia
    Presentation,      // modo apresentação / não perturbe do Windows
    CoveredByWindow,   // janela maximizada cobrindo a área de trabalho
    Locked,            // PC bloqueado, troca de usuário
    ScreenSaver,       // proteção de tela
    Minimized,         // janela do modo quarto minimizada
    Forced,            // forçado por linha de comando (para medir)
}

public enum WallpaperAttach
{
    /// <summary>Filho do WorkerW atrás dos ícones (Windows 10 / 11 até 23H2).</summary>
    Embedded,
    /// <summary>Filho do Progman, abaixo do SHELLDLL_DefView (Windows 11 24H2 em diante).</summary>
    EmbeddedRaised,
    /// <summary>Não deu para embutir: janela transparente sem borda, no fundo, clique passa.</summary>
    Fallback,
    /// <summary>Sistema sem suporte (Linux, por enquanto): janela comum sem borda.</summary>
    NotSupported,
}

public readonly record struct ScreenRect(int X, int Y, int W, int H);

/// <summary>
/// Tudo o que depende do sistema operacional fica atrás desta interface.
/// No Windows: <see cref="Windows.WindowsDesktopPlatform"/>. Nos outros: <see cref="NullDesktopPlatform"/>.
/// </summary>
public interface IDesktopPlatform
{
    string Name { get; }

    /// <summary>Retângulo do monitor principal em pixels físicos, ou null se a plataforma não souber.</summary>
    ScreenRect? PrimaryMonitor();

    /// <summary>Monitores: índice 0 é sempre o principal; os outros seguem a ordem do sistema.</summary>
    IReadOnlyList<ScreenRect> Monitors();

    /// <summary>Põe a janela como parte do fundo da área de trabalho.</summary>
    WallpaperAttach AttachAsWallpaper(IntPtr hwnd, ScreenRect monitor);

    /// <summary>Plano B: janela no fundo, fora da barra de tarefas, que deixa o clique passar.</summary>
    void PrepareFallback(IntPtr hwnd, ScreenRect monitor);

    /// <summary>Desfaz o papel de parede (volta a ser janela comum).</summary>
    void Detach(IntPtr hwnd);

    /// <summary>Chamado no máximo 1×/s: mantém a janela no fundo, confere se o pai ainda existe.
    /// Retorna false se o papel de parede se perdeu (ex.: Explorer reiniciou) e precisa ser refeito.</summary>
    bool Maintain(IntPtr hwnd);

    /// <summary>Checagem barata (1×/s) do que impede desenhar agora.</summary>
    /// <param name="area">Monitor onde está o papel de parede (para saber se ele está coberto).</param>
    PauseReason QueryPause(IntPtr ownHwnd, bool wallpaperMode, bool pauseWhenCovered, ScreenRect area);

    bool AutostartSupported { get; }
    bool IsAutostartEnabled();
    void SetAutostart(bool enabled, string executablePath);

    /// <summary>Garante uma instância só. Se já houver outra, avisa ela para abrir o quarto e retorna false.</summary>
    bool TryBecomeSingleInstance();

    /// <summary>Outra instância pediu para abrir o quarto? (checado 1×/s)</summary>
    bool ConsumeOpenRoomRequest();

    /// <summary>Baixa a prioridade do processo para nunca competir com jogos.</summary>
    void LowerProcessPriority();
}

public static class DesktopPlatform
{
    public static IDesktopPlatform Create() =>
        OperatingSystem.IsWindows() ? new Windows.WindowsDesktopPlatform() : new NullDesktopPlatform();
}
