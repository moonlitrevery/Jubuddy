using System.Text.Json.Serialization;

namespace Jubuddy.Core;

public enum HairStyle { Curto, Comprido, Coque, Cacheado, Raspado, RaboDeCavalo }

public enum Outfit { Camiseta, Moletom, Vestido }

/// <summary>Aparência do buddy (buddy.json). Tudo ajustável no modo quarto ou editando o arquivo.</summary>
public sealed class BuddyLook
{
    [JsonPropertyName("pele")] public string Skin { get; set; } = "#E8B796";
    [JsonPropertyName("cabelo")] public string Hair { get; set; } = "#3B2A20";
    [JsonPropertyName("penteado")] public HairStyle HairStyle { get; set; } = HairStyle.Curto;
    [JsonPropertyName("olhos")] public string Eyes { get; set; } = "#2B1D14";
    [JsonPropertyName("roupa")] public Outfit Outfit { get; set; } = Outfit.Camiseta;
    [JsonPropertyName("corDeCima")] public string Top { get; set; } = "#7A93B8";
    [JsonPropertyName("corDeBaixo")] public string Bottom { get; set; } = "#4A4F5C";
    [JsonPropertyName("sapatos")] public string Shoes { get; set; } = "#33302E";
    [JsonPropertyName("oculos")] public bool Glasses { get; set; }
    [JsonPropertyName("corDosOculos")] public string GlassesColor { get; set; } = "#2A2A2A";
    [JsonPropertyName("bochechas")] public bool Blush { get; set; } = true;
    [JsonPropertyName("tamanhoDaCabeca")] public float HeadSize { get; set; } = 1f;
    [JsonPropertyName("altura")] public float Height { get; set; } = 1f;

    public void Clamp()
    {
        HeadSize = System.Math.Clamp(HeadSize, 0.8f, 1.25f);
        Height = System.Math.Clamp(Height, 0.85f, 1.15f);
    }
}

/// <summary>Ajustes do app (ajustes.json).</summary>
public sealed class AppSettings
{
    /// <summary>Intervalo médio entre frases, em minutos. 0 = nunca.</summary>
    [JsonPropertyName("frasesIntervaloMinutos")] public double PhraseIntervalMinutes { get; set; } = 45;
    [JsonPropertyName("frasesDuracaoSegundos")] public double PhraseSeconds { get; set; } = 7;

    [JsonPropertyName("diagnostico")] public bool Diagnostics { get; set; }

    [JsonPropertyName("fpsAndando")] public int FpsMoving { get; set; } = 30;
    [JsonPropertyName("fpsParado")] public int FpsStill { get; set; } = 8;
    [JsonPropertyName("fpsDormindo")] public int FpsSleeping { get; set; } = 3;
    [JsonPropertyName("fpsModoQuarto")] public int FpsRoomMode { get; set; } = 60;

    [JsonPropertyName("antiSerrilhado")] public bool Antialiasing { get; set; } = true;

    /// <summary>Pausa quando uma janela maximizada cobre a área de trabalho.</summary>
    [JsonPropertyName("pausarComJanelaMaximizada")] public bool PauseWhenCovered { get; set; } = true;

    /// <summary>Posição horizontal do quarto no papel de parede: -1 (esquerda) a 1 (direita).</summary>
    [JsonPropertyName("papelDeParedePosicaoX")] public float WallpaperOffsetX { get; set; } = 0.35f;
    [JsonPropertyName("papelDeParedePosicaoY")] public float WallpaperOffsetY { get; set; } = 0f;
    [JsonPropertyName("papelDeParedeZoom")] public float WallpaperZoom { get; set; } = 0.75f;

    /// <summary>Em qual monitor: 1 = principal, 2 = o segundo, e assim por diante.</summary>
    [JsonPropertyName("papelDeParedeMonitor")] public int WallpaperMonitor { get; set; } = 1;

    /// <summary>Pula o WorkerW e usa direto a janela transparente no fundo (se o modo embutido não aparecer).</summary>
    [JsonPropertyName("papelDeParedeAlternativo")] public bool WallpaperFallback { get; set; }

    [JsonPropertyName("corDeFundo")] public string Background { get; set; } = "#2E3440";
    [JsonPropertyName("corDoChao")] public string Floor { get; set; } = "#C8B9A6";
    [JsonPropertyName("corDasParedes")] public string Walls { get; set; } = "#E6E1D8";

    public void Clamp()
    {
        FpsMoving = System.Math.Clamp(FpsMoving, 5, 60);
        FpsStill = System.Math.Clamp(FpsStill, 1, 60);
        FpsSleeping = System.Math.Clamp(FpsSleeping, 1, 30);
        FpsRoomMode = System.Math.Clamp(FpsRoomMode, 15, 144);
        PhraseIntervalMinutes = System.Math.Clamp(PhraseIntervalMinutes, 0, 24 * 60);
        PhraseSeconds = System.Math.Clamp(PhraseSeconds, 2, 60);
        WallpaperOffsetX = System.Math.Clamp(WallpaperOffsetX, -1f, 1f);
        WallpaperOffsetY = System.Math.Clamp(WallpaperOffsetY, -1f, 1f);
        WallpaperZoom = System.Math.Clamp(WallpaperZoom, 0.3f, 2f);
        WallpaperMonitor = System.Math.Clamp(WallpaperMonitor, 1, 16);
    }
}
