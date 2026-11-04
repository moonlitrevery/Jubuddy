using System;
using System.Collections.Generic;

namespace Jubuddy.Core;

/// <summary>
/// Grade de navegação do chão (A* em 8 direções). Barata: um quarto 5×5 com células de 0,2 tem 625 células,
/// e só é refeita quando o quarto muda (Room.Revision).
/// </summary>
public sealed class NavGrid
{
    public const float AgentRadius = 0.17f;

    private readonly float _cell;
    private float _half;
    private int _n;
    private bool[] _blocked = Array.Empty<bool>();
    private int _builtRevision = -1;
    private Room? _builtFor;

    public NavGrid(float cell = 0.2f) => _cell = cell;

    public int Columns => _n;
    public float Cell => _cell;

    /// <summary>Refaz a grade se o quarto mudou desde a última vez.</summary>
    public void Sync(Room room)
    {
        if (ReferenceEquals(room, _builtFor) && room.Revision == _builtRevision) return;
        Build(room);
    }

    public void Build(Room room)
    {
        _builtFor = room;
        _builtRevision = room.Revision;
        _half = room.Half;
        _n = Math.Max(2, (int)MathF.Round(room.Size / _cell));
        _blocked = new bool[_n * _n];

        var obstacles = new List<Rect2D>();
        foreach (var r in room.Obstacles()) obstacles.Add(r.Inflate(AgentRadius));

        for (var iz = 0; iz < _n; iz++)
        for (var ix = 0; ix < _n; ix++)
        {
            var c = CellCenter(ix, iz);
            var blocked = c.X < -_half + AgentRadius || c.X > _half - AgentRadius ||
                          c.Z < -_half + AgentRadius || c.Z > _half - AgentRadius;
            if (!blocked)
            {
                foreach (var o in obstacles)
                    if (o.Contains(c)) { blocked = true; break; }
            }
            _blocked[iz * _n + ix] = blocked;
        }
    }

    public Vec2 CellCenter(int ix, int iz) => new(-_half + (ix + 0.5f) * _cell, -_half + (iz + 0.5f) * _cell);

    private (int ix, int iz) CellOf(Vec2 p)
    {
        var ix = (int)MathF.Floor((p.X + _half) / _cell);
        var iz = (int)MathF.Floor((p.Z + _half) / _cell);
        return (Math.Clamp(ix, 0, _n - 1), Math.Clamp(iz, 0, _n - 1));
    }

    private bool Blocked(int ix, int iz) => ix < 0 || iz < 0 || ix >= _n || iz >= _n || _blocked[iz * _n + ix];

    public bool IsFree(Vec2 p)
    {
        if (p.X < -_half || p.X > _half || p.Z < -_half || p.Z > _half) return false;
        var (ix, iz) = CellOf(p);
        return !Blocked(ix, iz);
    }

    public int FreeCellCount()
    {
        var count = 0;
        foreach (var b in _blocked) if (!b) count++;
        return count;
    }

    /// <summary>Centro da célula livre mais perto do ponto (busca em anéis). Null se não houver nenhuma.</summary>
    public Vec2? NearestFree(Vec2 p)
    {
        if (IsFree(p)) return p;
        var (cx, cz) = CellOf(p);
        for (var ring = 1; ring < _n; ring++)
        {
            Vec2? best = null;
            var bestD = float.MaxValue;
            for (var dz = -ring; dz <= ring; dz++)
            for (var dx = -ring; dx <= ring; dx++)
            {
                if (Math.Abs(dx) != ring && Math.Abs(dz) != ring) continue;
                var ix = cx + dx; var iz = cz + dz;
                if (Blocked(ix, iz)) continue;
                var c = CellCenter(ix, iz);
                var d = Vec2.Distance(c, p);
                if (d < bestD) { bestD = d; best = c; }
            }
            if (best != null) return best;
        }
        return null;
    }

    /// <summary>Um ponto livre aleatório (opcionalmente dentro de um raio).</summary>
    public Vec2? RandomFree(Random rng, Vec2? around = null, float radius = 0f)
    {
        var free = new List<int>();
        for (var i = 0; i < _blocked.Length; i++)
        {
            if (_blocked[i]) continue;
            if (around is { } a && radius > 0)
            {
                var c = CellCenter(i % _n, i / _n);
                if (Vec2.Distance(c, a) > radius) continue;
            }
            free.Add(i);
        }
        if (free.Count == 0) return null;
        var pick = free[rng.Next(free.Count)];
        return CellCenter(pick % _n, pick / _n);
    }

    /// <summary>
    /// Caminho de <paramref name="from"/> até o ponto livre mais perto de <paramref name="to"/>.
    /// Retorna os pontos a seguir (sem o ponto de partida). Lista vazia = já está lá ou não há caminho.
    /// </summary>
    public List<Vec2> FindPath(Vec2 from, Vec2 to)
    {
        var result = new List<Vec2>();
        var startP = NearestFree(from);
        var goalP = NearestFree(to);
        if (startP is null || goalP is null) return result;

        var (sx, sz) = CellOf(startP.Value);
        var (gx, gz) = CellOf(goalP.Value);
        var finalPoint = IsFree(to) ? to : goalP.Value;

        if (sx == gx && sz == gz)
        {
            if (Vec2.Distance(from, finalPoint) > 0.02f) result.Add(finalPoint);
            return result;
        }

        var total = _n * _n;
        var g = new float[total];
        var came = new int[total];
        var closed = new bool[total];
        Array.Fill(g, float.MaxValue);
        Array.Fill(came, -1);
        var open = new PriorityQueue<int, float>();
        var start = sz * _n + sx;
        var goal = gz * _n + gx;
        g[start] = 0;
        open.Enqueue(start, Heuristic(sx, sz, gx, gz));

        var found = false;
        while (open.Count > 0)
        {
            var cur = open.Dequeue();
            if (closed[cur]) continue;
            if (cur == goal) { found = true; break; }
            closed[cur] = true;
            int cx = cur % _n, cz = cur / _n;
            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = cx + dx, nz = cz + dz;
                if (Blocked(nx, nz)) continue;
                // Não corta quina de obstáculo na diagonal.
                if (dx != 0 && dz != 0 && (Blocked(cx + dx, cz) || Blocked(cx, cz + dz))) continue;
                var ni = nz * _n + nx;
                if (closed[ni]) continue;
                var step = (dx != 0 && dz != 0) ? 1.41421f : 1f;
                var ng = g[cur] + step;
                if (ng < g[ni])
                {
                    g[ni] = ng;
                    came[ni] = cur;
                    open.Enqueue(ni, ng + Heuristic(nx, nz, gx, gz));
                }
            }
        }

        if (!found) return result;

        var cells = new List<int>();
        for (var c = goal; c != -1 && c != start; c = came[c]) cells.Add(c);
        cells.Reverse();

        var raw = new List<Vec2>(cells.Count);
        foreach (var c in cells) raw.Add(CellCenter(c % _n, c / _n));
        raw[^1] = finalPoint;

        // Suaviza: pula pontos intermediários quando há linha de visão.
        var anchor = from;
        var i = 0;
        while (i < raw.Count)
        {
            var far = i;
            for (var j = raw.Count - 1; j > i; j--)
            {
                if (LineFree(anchor, raw[j])) { far = j; break; }
            }
            result.Add(raw[far]);
            anchor = raw[far];
            i = far + 1;
        }
        return result;
    }

    private static float Heuristic(int ax, int az, int bx, int bz)
    {
        float dx = Math.Abs(ax - bx), dz = Math.Abs(az - bz);
        return Math.Max(dx, dz) + 0.41421f * Math.Min(dx, dz);
    }

    /// <summary>O segmento passa só por células livres?</summary>
    public bool LineFree(Vec2 a, Vec2 b)
    {
        var dist = Vec2.Distance(a, b);
        var steps = Math.Max(1, (int)MathF.Ceiling(dist / (_cell * 0.35f)));
        for (var s = 1; s < steps; s++)
        {
            var t = s / (float)steps;
            var p = new Vec2(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);
            if (!IsFree(p)) return false;
        }
        return true;
    }
}
