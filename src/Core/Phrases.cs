using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jubuddy.Core;

/// <summary>
/// As frases da pessoa, lidas de um arquivo de texto (uma por linha).
/// Nenhuma frase vem embutida no app: o arquivo nasce só com instruções em comentário.
/// </summary>
public sealed class PhraseBook
{
    public const string FileHeader =
        "# Suas frases para o buddy. Uma por linha.\n" +
        "# Linhas que começam com # são ignoradas, e linhas em branco também.\n" +
        "# Ele mostra uma delas num balãozinho de vez em quando (o intervalo fica em Ajustes).\n" +
        "\n";

    private readonly List<string> _phrases;
    private readonly Queue<int> _recent = new();

    public IReadOnlyList<string> Phrases => _phrases;
    public int Count => _phrases.Count;

    /// <summary>Data de modificação do arquivo de onde veio (para recarregar quando a pessoa editar).</summary>
    public DateTime SourceStamp { get; private init; }

    private PhraseBook(List<string> phrases) => _phrases = phrases;

    public static PhraseBook Empty => new(new List<string>());

    public static PhraseBook Parse(string text)
    {
        var list = text.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();
        return new PhraseBook(list);
    }

    /// <summary>Lê o arquivo; se ele não existir, cria só com o cabeçalho de instruções.</summary>
    public static PhraseBook LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, FileHeader);
        }
        var text = File.ReadAllText(path);
        return new PhraseBook(Parse(text)._phrases) { SourceStamp = File.GetLastWriteTimeUtc(path) };
    }

    public static bool ChangedOnDisk(string path, PhraseBook current) =>
        File.Exists(path) && File.GetLastWriteTimeUtc(path) != current.SourceStamp;

    /// <summary>Próxima frase, evitando repetir as últimas. Null se não houver nenhuma.</summary>
    public string? Next(Random rng)
    {
        if (_phrases.Count == 0) return null;
        var memory = Math.Min(3, _phrases.Count - 1);
        int pick;
        var tries = 0;
        do { pick = rng.Next(_phrases.Count); }
        while (_recent.Contains(pick) && ++tries < 20);
        _recent.Enqueue(pick);
        while (_recent.Count > memory) _recent.Dequeue();
        return _phrases[pick];
    }
}

/// <summary>Quando mostrar a próxima frase. Intervalo médio com variação, sem cobrança nenhuma.</summary>
public sealed class PhraseScheduler
{
    public DateTime NextAt { get; private set; } = DateTime.MaxValue;

    public void Schedule(DateTime now, double intervalMinutes, Random rng)
    {
        if (intervalMinutes <= 0) { NextAt = DateTime.MaxValue; return; }
        var factor = 0.6 + rng.NextDouble() * 0.8; // 60% a 140% do intervalo
        NextAt = now.AddMinutes(intervalMinutes * factor);
    }

    public bool Due(DateTime now) => now >= NextAt;
}
