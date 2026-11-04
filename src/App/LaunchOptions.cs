using Jubuddy.Core;

namespace Jubuddy.App;

/// <summary>
/// Opções de linha de comando (depois de "--"). Servem para testar e medir desempenho de forma repetível:
///   --quarto                     abre direto no modo quarto
///   --diagnostico                liga o modo de diagnóstico nesta execução
///   --forcar-atividade=X         varrer | andar | ler | pc | sentar | dormir | atoa
///   --forcar-pausa               fica pausado o tempo todo (mede o custo "parado sem desenhar")
///   --nunca-pausar               ignora tela cheia/maximizada (para medir o custo de desenhar)
///   --forcar-alternativo         pula o WorkerW e usa a janela transparente de plano B
///   --dados=PASTA                usa outra pasta de dados (não mexe no seu quarto)
///   --sair-apos=SEGUNDOS         fecha sozinho (para medições automáticas)
///   --demo-painel=X              (com --quarto) abre bau | aparencia | ajustes | objeto, para conferir a interface
///   --captura=ARQUIVO.png        salva uma imagem do que foi desenhado (depois de --captura-apos=SEG, padrão 5)
/// </summary>
public sealed class LaunchOptions
{
    public bool StartInRoom { get; private set; }
    public bool Diagnostics { get; private set; }
    public Activity? ForcedActivity { get; private set; }
    public bool ForcePause { get; private set; }
    public bool NeverPause { get; private set; }
    public bool ForceFallback { get; private set; }
    public string? DataDir { get; private set; }
    public double QuitAfterSeconds { get; private set; }
    public string? CapturePath { get; private set; }
    public double CaptureDelay { get; private set; } = 5;
    public string? DemoPanel { get; private set; }

    public static LaunchOptions Parse(string[] args)
    {
        var o = new LaunchOptions();
        foreach (var raw in args)
        {
            var a = raw.Trim();
            var eq = a.IndexOf('=');
            var key = eq >= 0 ? a[..eq] : a;
            var val = eq >= 0 ? a[(eq + 1)..] : "";
            switch (key)
            {
                case "--quarto": o.StartInRoom = true; break;
                case "--diagnostico": o.Diagnostics = true; break;
                case "--forcar-pausa": o.ForcePause = true; break;
                case "--nunca-pausar": o.NeverPause = true; break;
                case "--forcar-alternativo": o.ForceFallback = true; break;
                case "--dados": if (val.Length > 0) o.DataDir = val; break;
                case "--captura": if (val.Length > 0) o.CapturePath = val; break;
                case "--demo-painel": if (val.Length > 0) o.DemoPanel = val; break;
                case "--captura-apos":
                    if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var c))
                        o.CaptureDelay = c;
                    break;
                case "--sair-apos":
                    if (double.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s))
                        o.QuitAfterSeconds = s;
                    break;
                case "--forcar-atividade":
                    o.ForcedActivity = val switch
                    {
                        "varrer" => Activity.Sweep,
                        "andar" => Activity.Wander,
                        "ler" => Activity.Read,
                        "pc" => Activity.UsePc,
                        "sentar" => Activity.Sit,
                        "dormir" => Activity.Sleep,
                        "atoa" => Activity.Idle,
                        _ => null,
                    };
                    break;
            }
        }
        return o;
    }
}
