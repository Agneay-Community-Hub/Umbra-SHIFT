using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UmbraShift
{
    /// <summary>One puzzle, parsed from Resources/levels.txt.</summary>
    public class LevelData
    {
        public string Title = "";
        public string Hint = "";
        public int Par;
        public int W, H;
        public char[,] Grid;
        public Vector2Int Start;
        public readonly List<Vector2Int> Casters = new List<Vector2Int>();
        public readonly List<Vector2Int> Sparks = new List<Vector2Int>();

        public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < W && c.y < H;
        public char At(Vector2Int c) => Grid[c.x, c.y];

        public static List<LevelData> ParseAll(string text)
        {
            var result = new List<LevelData>();
            LevelData cur = null;
            var rows = new List<string>();
            void Flush()
            {
                if (cur != null && rows.Count > 0) { cur.Build(rows); result.Add(cur); }
                rows.Clear();
            }
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.StartsWith("//")) continue;
                if (line.StartsWith("=")) { Flush(); cur = new LevelData { Title = line.Substring(1).Trim() }; }
                else if (line.StartsWith(";") && cur != null) cur.Hint = line.Substring(1).Trim();
                else if (line.StartsWith("!") && cur != null) int.TryParse(line.Substring(1).Trim(), out cur.Par);
                else if (line.Trim().Length > 0 && cur != null) rows.Add(line);
            }
            Flush();
            return result;
        }

        void Build(List<string> rows)
        {
            H = rows.Count;
            W = 0;
            foreach (var r in rows) W = Mathf.Max(W, r.Length);
            Grid = new char[W, H];
            for (int r = 0; r < H; r++)
            {
                int y = H - 1 - r;
                for (int x = 0; x < W; x++)
                {
                    char ch = x < rows[r].Length ? rows[r][x] : '.';
                    var c = new Vector2Int(x, y);
                    if (ch == 'S') { Start = c; ch = '.'; }
                    if (ch == 'C') Casters.Add(c);
                    if (ch == '*') Sparks.Add(c);
                    Grid[x, y] = ch;
                }
            }
        }
    }

    public struct State
    {
        public Vector2Int Pos;
        public int K;      // orientation 0..3 (which world direction is "up" on screen)
        public int Mask;   // collected sparks
        public State(Vector2Int p, int k, int m) { Pos = p; K = k; Mask = m; }
    }

    public enum Status { Ok, Dead, Win }

    public class StepResult
    {
        public State State;
        public Status Status;
        public bool Rotated;
        public bool FellOut;
        public readonly List<Vector2Int> Path = new List<Vector2Int>(); // every cell visited, in order
    }

    /// <summary>
    /// The complete rules of the game. Pure logic, shared by the game and the level validator.
    ///  - Q/E turns the world 90 degrees; gravity always points to the bottom of the screen.
    ///  - The lamp is always at the right of the screen, so obsidian casts shadows toward screen-left.
    ///  - A shadow runs through empty cells until it hits anything (including the player) and is solid.
    /// </summary>
    public static class Sim
    {
        public static readonly Vector2Int[] Up =
            { new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(0, -1), new Vector2Int(1, 0) };

        public static Vector2Int Right(int k) { var u = Up[k]; return new Vector2Int(u.y, -u.x); }
        public static Vector2Int LightDir(int k) => -Right(k);

        public static HashSet<Vector2Int> Shadows(LevelData L, int k, Vector2Int player)
        {
            var s = new HashSet<Vector2Int>();
            var d = LightDir(k);
            foreach (var c in L.Casters)
            {
                var p = c + d;
                while (L.InBounds(p) && p != player)
                {
                    char ch = L.At(p);
                    if (ch != '.' && ch != '*') break;
                    s.Add(p);
                    p += d;
                }
            }
            return s;
        }

        public static bool Solid(LevelData L, Vector2Int c, HashSet<Vector2Int> sh)
        {
            if (!L.InBounds(c)) return false;
            char ch = L.At(c);
            return ch == '#' || ch == 'C' || sh.Contains(c);
        }

        static Status Settle(LevelData L, ref Vector2Int pos, int k, ref int mask, StepResult r)
        {
            var u = Up[k];
            while (true)
            {
                char ch = L.At(pos);
                if (ch == 'X') return Status.Dead;
                if (ch == '*') { int i = L.Sparks.IndexOf(pos); if (i >= 0) mask |= 1 << i; }
                if (ch == 'G') return Status.Win;
                var below = pos - u;
                if (!L.InBounds(below)) { if (r != null) r.FellOut = true; return Status.Dead; }
                if (Solid(L, below, Shadows(L, k, pos))) return Status.Ok;
                pos = below;
                r?.Path.Add(pos);
            }
        }

        public static StepResult Begin(LevelData L)
        {
            var r = new StepResult();
            var p = L.Start; int m = 0;
            r.Path.Add(p);
            r.Status = Settle(L, ref p, 0, ref m, r);
            r.State = new State(p, 0, m);
            return r;
        }

        /// <param name="move">'L','R' walk (screen-relative), 'Q','E' turn the world.</param>
        public static StepResult Step(LevelData L, State s, char move)
        {
            var pos = s.Pos; int k = s.K; int mask = s.Mask;
            var r = new StepResult();
            if (move == 'L' || move == 'R')
            {
                var t = pos + Right(k) * (move == 'R' ? 1 : -1);
                if (!L.InBounds(t) || Solid(L, t, Shadows(L, k, pos))) return null;
                pos = t;
                r.Path.Add(pos);
            }
            else
            {
                k = (k + (move == 'E' ? 1 : 3)) % 4;
                r.Rotated = true;
            }
            r.Status = Settle(L, ref pos, k, ref mask, r);
            r.State = new State(pos, k, mask);
            return r;
        }

        /// <summary>Breadth-first search. Returns the shortest move string, or null.</summary>
        public static string Solve(LevelData L, bool needAllSparks)
        {
            int full = (1 << L.Sparks.Count) - 1;
            var b = Begin(L);
            if (b.Status != Status.Ok) return null;
            var prev = new Dictionary<State, (State, char)>();
            var start = b.State;
            prev[start] = (start, '\0');
            var q = new Queue<State>();
            q.Enqueue(start);
            const string moves = "LRQE";
            while (q.Count > 0)
            {
                var s = q.Dequeue();
                foreach (var mv in moves)
                {
                    var r = Step(L, s, mv);
                    if (r == null || r.Status == Status.Dead) continue;
                    if (r.Status == Status.Win)
                    {
                        if (needAllSparks && r.State.Mask != full) continue;
                        var sb = new StringBuilder();
                        sb.Append(mv);
                        var c = s;
                        while (!c.Equals(start)) { var pr = prev[c]; sb.Append(pr.Item2); c = pr.Item1; }
                        var arr = sb.ToString().ToCharArray();
                        System.Array.Reverse(arr);
                        return new string(arr);
                    }
                    if (!prev.ContainsKey(r.State)) { prev[r.State] = (s, mv); q.Enqueue(r.State); }
                }
            }
            return null;
        }
    }
}
