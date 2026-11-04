using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jubuddy.Core;

/// <summary>Onde fica cada arquivo, dentro da pasta de dados do app. Tudo local.</summary>
public sealed class AppPaths
{
    public string DataDir { get; }
    public string RoomFile => Path.Combine(DataDir, "quarto.json");
    public string LookFile => Path.Combine(DataDir, "buddy.json");
    public string SettingsFile => Path.Combine(DataDir, "ajustes.json");
    public string PhrasesFile => Path.Combine(DataDir, "frases.txt");
    public string PostersDir => Path.Combine(DataDir, "posters");
    public string DiagnosticsFile => Path.Combine(DataDir, "diagnostico.csv");

    public AppPaths(string dataDir)
    {
        DataDir = dataDir;
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(PostersDir);
    }
}

/// <summary>JSON com escrita atômica: nunca deixa um arquivo pela metade, e guarda a versão anterior (.bak).</summary>
public static class JsonStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Lê o arquivo. Se não existir, devolve o padrão. Se estiver corrompido, NÃO apaga:
    /// renomeia para *.corrompido-DATA e tenta o .bak; o problema volta em <paramref name="problem"/>.
    /// </summary>
    public static T Load<T>(string path, Func<T> fallback, out string? problem) where T : class
    {
        problem = null;
        if (!File.Exists(path))
        {
            var bak = path + ".bak";
            if (File.Exists(bak) && TryRead<T>(bak, out var fromBak)) return fromBak!;
            return fallback();
        }

        if (TryRead<T>(path, out var value)) return value!;

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var keep = $"{path}.corrompido-{stamp}";
        try { File.Move(path, keep); } catch { /* se não der para mover, ao menos não sobrescrevemos agora */ }
        problem = $"{Path.GetFileName(path)} estava ilegível; guardei uma cópia em {Path.GetFileName(keep)}.";

        var backup = path + ".bak";
        if (File.Exists(backup) && TryRead<T>(backup, out var restored))
        {
            problem += " Usei a versão anterior (.bak).";
            return restored!;
        }
        return fallback();
    }

    private static bool TryRead<T>(string path, out T? value) where T : class
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
            return value != null;
        }
        catch (Exception)
        {
            value = null;
            return false;
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(value, Options);
        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var w = new StreamWriter(fs))
        {
            w.Write(json);
            w.Flush();
            fs.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
            File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(tmp, path);
    }
}

/// <summary>Salvar e carregar o quarto (com o baú).</summary>
public sealed class RoomStore
{
    private readonly AppPaths _paths;

    public RoomStore(AppPaths paths) => _paths = paths;

    public Room Load(out string? problem)
    {
        var data = JsonStore.Load(_paths.RoomFile, () => new RoomData(), out problem);
        // Itens com tipo desconhecido ou sem id não derrubam o quarto.
        data.Items.RemoveAll(i => string.IsNullOrEmpty(i.Id) || !Enum.IsDefined(i.Kind));
        data.Chest.RemoveAll(i => string.IsNullOrEmpty(i.Id) || !Enum.IsDefined(i.Kind));
        return new Room(data);
    }

    public void Save(Room room) => JsonStore.Save(_paths.RoomFile, room.Data);

    /// <summary>Apaga a imagem de um pôster apagado de vez, se nenhum outro objeto usa a mesma imagem.</summary>
    public void DeletePosterImageIfUnused(Room room, PlacedItem deleted)
    {
        if (string.IsNullOrEmpty(deleted.Image) || room.ImageInUse(deleted.Image)) return;
        var file = Path.Combine(_paths.PostersDir, Path.GetFileName(deleted.Image));
        if (File.Exists(file)) File.Delete(file);
    }
}
