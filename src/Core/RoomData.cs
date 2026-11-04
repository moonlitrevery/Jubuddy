using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jubuddy.Core;

/// <summary>Um objeto do quarto (ou do baú). Tudo o que precisa para redesenhá-lo e lembrar por que ele existe.</summary>
public sealed class PlacedItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ItemKind Kind { get; set; }

    /// <summary>A frase: por que este objeto está no quarto.</summary>
    [JsonPropertyName("frase")]
    public string Why { get; set; } = "";

    // Objetos de chão: centro e giro em quartos de volta (0..3).
    public float X { get; set; }
    public float Z { get; set; }
    public int Rotation { get; set; }

    // Objetos de parede: em qual parede, posição ao longo dela e altura do centro.
    public WallSide Wall { get; set; }
    public float Along { get; set; }
    public float Y { get; set; }

    /// <summary>Pôster: nome do arquivo dentro da pasta posters/ do app.</summary>
    public string? Image { get; set; }

    /// <summary>Pôster: proporção largura/altura da imagem.</summary>
    public float Aspect { get; set; } = 1f;

    /// <summary>Cor principal opcional (#RRGGBB). Vazio = cor neutra padrão.</summary>
    public string? Color { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DateTime? StoredAt { get; set; }

    [JsonIgnore] public ItemSpec Spec => Catalog.Get(Kind);
    [JsonIgnore] public Vec2 Position => new(X, Z);

    public PlacedItem Clone() => (PlacedItem)MemberwiseClone();
}

/// <summary>O arquivo room.json: o que está no quarto e o que está guardado no baú.</summary>
public sealed class RoomData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public float Size { get; set; } = Room.DefaultSize;
    public List<PlacedItem> Items { get; set; } = new();

    [JsonPropertyName("bau")]
    public List<PlacedItem> Chest { get; set; } = new();
}
