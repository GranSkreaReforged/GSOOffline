using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GSOOffline
{
    /// <summary>
    /// Dev tool for drawing ferry routes (DevCommands routeplan / watermap): a grid of water and land around some
    /// points, probed with downward rays across a ferry's hull, and the shortest water route through it. Only run
    /// from the bridge; the routes it prints are baked into Data/ferries.json.
    /// </summary>
    internal static class FerryRoutePlanner
    {
        private const float SolidAbove = 3.4f;   // anything solid higher than this sticks out of the water (y 3.15)
        private const float HullRadius = 6f;     // clearance around the ferry's centre line

        private static readonly Vector3[] Probes =
        {
            Vector3.zero,
            new Vector3(HullRadius, 0f, 0f), new Vector3(-HullRadius, 0f, 0f),
            new Vector3(0f, 0f, HullRadius), new Vector3(0f, 0f, -HullRadius),
        };

        /// <summary>Open water at this point, a hull's width around.</summary>
        internal static bool IsWater(float x, float z)
        {
            foreach (var o in Probes)
                if (SolidTop(new Vector3(x + o.x, 0f, z + o.z)) > SolidAbove) return false;
            return true;
        }

        internal static float SolidTop(Vector3 p)
        {
            float top = float.MinValue;
            foreach (var hit in Physics.RaycastAll(new Vector3(p.x, 80f, p.z), Vector3.down, 90f, ~0, QueryTriggerInteraction.Ignore))
            {
                var c = hit.collider;
                if (c.GetComponentInParent<Scr_Transport>() != null || c.GetComponentInParent<Scr_PlayerShip>() != null ||
                    c.GetComponentInParent<Scr_Player>() != null || c.GetComponentInParent<Scr_OtherPlayer>() != null) continue;
                if (hit.point.y > top) top = hit.point.y;
            }
            return top;
        }

        /// <summary>Straight run between two points stays in open water (checked every metre).</summary>
        internal static bool ClearLine(Vector3 a, Vector3 b)
        {
            float len = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
            for (float d = 0f; d <= len; d += 1f)
            {
                var c = Vector3.Lerp(a, b, len < 0.01f ? 0f : d / len);
                if (!IsWater(c.x, c.z)) return false;
            }
            return IsWater(b.x, b.z);
        }

        private class Grid
        {
            public float x0, z0, cell;
            public int w, h;
            public sbyte[] state;   // 0 unknown, 1 water, -1 land

            public bool Water(int i, int j)
            {
                int k = j * w + i;
                if (state[k] == 0) state[k] = (sbyte)(IsWater(x0 + (i + 0.5f) * cell, z0 + (j + 0.5f) * cell) ? 1 : -1);
                return state[k] > 0;
            }

            public Vector3 Centre(int i, int j) => new Vector3(x0 + (i + 0.5f) * cell, 3.15f, z0 + (j + 0.5f) * cell);
            public int I(float x) => Mathf.Clamp((int)((x - x0) / cell), 0, w - 1);
            public int J(float z) => Mathf.Clamp((int)((z - z0) / cell), 0, h - 1);
        }

        private static Grid MakeGrid(IList<Vector3> pts, float margin, float cell)
        {
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var p in pts)
            {
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
            }
            var g = new Grid { x0 = minX - margin, z0 = minZ - margin, cell = cell };
            g.w = Mathf.CeilToInt((maxX - minX + 2 * margin) / cell);
            g.h = Mathf.CeilToInt((maxZ - minZ + 2 * margin) / cell);
            g.state = new sbyte[g.w * g.h];
            return g;
        }

        /// <summary>
        /// Water route through every point in order (A* on the grid, then shortened to the fewest straight legs
        /// that stay clear). Points that aren't open water themselves (docks) are joined from the nearest water cell.
        /// Returns null if some leg has no water route inside the margin.
        /// </summary>
        internal static List<Vector3> Plan(IList<Vector3> pts, float margin, float cell, string pngPath)
        {
            var g = MakeGrid(pts, margin, cell);
            var route = new List<Vector3> { pts[0] };
            var cells = new List<int>();
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                var path = AStar(g, pts[k], pts[k + 1]);
                if (path == null)
                {
                    Plugin.Log.LogWarning($"[dev] routeplan: no water route from {SceneWorld.Vec(pts[k])} to {SceneWorld.Vec(pts[k + 1])}");
                    WritePng(g, cells, route, pngPath);
                    return null;
                }
                cells.AddRange(path);
                var leg = new List<Vector3> { pts[k] };
                foreach (int c in path) leg.Add(g.Centre(c % g.w, c / g.w));
                leg.Add(pts[k + 1]);
                var shortened = Shorten(leg);
                route.AddRange(shortened.GetRange(1, shortened.Count - 1));
            }
            WritePng(g, cells, route, pngPath);
            return route;
        }

        // Greedy string-pulling: from each kept point, jump to the farthest later point still reachable in a clear line.
        private static List<Vector3> Shorten(List<Vector3> leg)
        {
            var kept = new List<Vector3> { leg[0] };
            int at = 0;
            while (at < leg.Count - 1)
            {
                int next = at + 1;
                for (int k = leg.Count - 1; k > at + 1; k--)
                    if (ClearLine(leg[at], leg[k])) { next = k; break; }
                kept.Add(leg[next]);
                at = next;
            }
            return kept;
        }

        private static List<int> AStar(Grid g, Vector3 from, Vector3 to)
        {
            int start = NearestWater(g, from), goal = NearestWater(g, to);
            if (start < 0 || goal < 0) return null;
            int n = g.w * g.h;
            var cost = new float[n];
            var came = new int[n];
            for (int k = 0; k < n; k++) { cost[k] = float.MaxValue; came[k] = -1; }
            var open = new SortedDictionary<float, List<int>>();
            Action<int, float> push = (c, f) =>
            {
                if (!open.TryGetValue(f, out var l)) open[f] = l = new List<int>();
                l.Add(c);
            };
            Func<int, float> heur = c => Vector2.Distance(new Vector2(c % g.w, c / g.w), new Vector2(goal % g.w, goal / g.w));
            cost[start] = 0f;
            push(start, heur(start));
            int[] di = { 1, -1, 0, 0, 1, 1, -1, -1 }, dj = { 0, 0, 1, -1, 1, -1, 1, -1 };
            int expanded = 0;
            while (open.Count > 0 && expanded < 200000)
            {
                var first = default(KeyValuePair<float, List<int>>);
                foreach (var kv in open) { first = kv; break; }
                int cur = first.Value[first.Value.Count - 1];
                first.Value.RemoveAt(first.Value.Count - 1);
                if (first.Value.Count == 0) open.Remove(first.Key);
                if (cur == goal) break;
                expanded++;
                int ci = cur % g.w, cj = cur / g.w;
                for (int d = 0; d < 8; d++)
                {
                    int ni = ci + di[d], nj = cj + dj[d];
                    if (ni < 0 || nj < 0 || ni >= g.w || nj >= g.h || !g.Water(ni, nj)) continue;
                    if (d >= 4 && (!g.Water(ci + di[d], cj) || !g.Water(ci, cj + dj[d]))) continue;   // no corner cutting
                    int nk = nj * g.w + ni;
                    float nc = cost[cur] + (d < 4 ? 1f : 1.4142f);
                    if (nc >= cost[nk]) continue;
                    cost[nk] = nc;
                    came[nk] = cur;
                    push(nk, nc + heur(nk));
                }
            }
            if (came[goal] < 0 && goal != start) return null;
            var path = new List<int>();
            for (int c = goal; c >= 0; c = came[c]) path.Add(c);
            path.Reverse();
            return path;
        }

        private static int NearestWater(Grid g, Vector3 p)
        {
            int pi = g.I(p.x), pj = g.J(p.z);
            for (int r = 0; r < 12; r++)
                for (int j = pj - r; j <= pj + r; j++)
                    for (int i = pi - r; i <= pi + r; i++)
                    {
                        if (Mathf.Max(Mathf.Abs(i - pi), Mathf.Abs(j - pj)) != r) continue;
                        if (i < 0 || j < 0 || i >= g.w || j >= g.h) continue;
                        if (g.Water(i, j)) return j * g.w + i;
                    }
            return -1;
        }

        /// <summary>Water map of the whole grid: blue water, green land, grey unprobed, red route cells, white waypoints.</summary>
        internal static void WaterMap(Vector3 a, Vector3 b, float cell, string pngPath)
        {
            var g = MakeGrid(new[] { a, b }, 0f, cell);
            for (int j = 0; j < g.h; j++)
                for (int i = 0; i < g.w; i++) g.Water(i, j);
            WritePng(g, new List<int>(), new List<Vector3>(), pngPath);
        }

        private static void WritePng(Grid g, List<int> cells, List<Vector3> waypoints, string path)
        {
            if (path == null) return;
            var px = new byte[g.w * g.h * 3];
            for (int k = 0; k < g.state.Length; k++)
            {
                byte r = 128, gg = 128, b = 128;
                if (g.state[k] > 0) { r = 40; gg = 90; b = 200; }
                else if (g.state[k] < 0) { r = 60; gg = 150; b = 60; }
                Set(px, g, k, r, gg, b);
            }
            foreach (int c in cells) Set(px, g, c, 230, 40, 40);
            foreach (var w in waypoints)
            {
                int ci = g.I(w.x), cj = g.J(w.z);
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        int i = ci + di, j = cj + dj;
                        if (i >= 0 && j >= 0 && i < g.w && j < g.h) Set(px, g, j * g.w + i, 255, 255, 255);
                    }
            }
            // Each cell as a 3x3 block so small maps stay readable.
            const int S = 3;
            var big = new byte[g.w * S * g.h * S * 3];
            for (int y = 0; y < g.h * S; y++)
                for (int x = 0; x < g.w * S; x++)
                    Buffer.BlockCopy(px, ((y / S) * g.w + x / S) * 3, big, (y * g.w * S + x) * 3, 3);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, Png(g.w * S, g.h * S, big));
            Plugin.Log.LogInfo($"[dev] map {g.w}x{g.h} cells of {g.cell} m from ({g.x0:F0}, {g.z0:F0}) -> {path}");
        }

        // North (+z) at the top of the image.
        private static void Set(byte[] px, Grid g, int k, byte r, byte gg, byte b)
        {
            int i = k % g.w, j = g.h - 1 - k / g.w;
            int o = (j * g.w + i) * 3;
            px[o] = r; px[o + 1] = gg; px[o + 2] = b;
        }

        // Minimal PNG: 8-bit RGB, one zlib stream of stored (uncompressed) blocks.
        private static byte[] Png(int w, int h, byte[] rgb)
        {
            var raw = new MemoryStream();
            for (int y = 0; y < h; y++)
            {
                raw.WriteByte(0);
                raw.Write(rgb, y * w * 3, w * 3);
            }
            byte[] data = raw.ToArray();
            var z = new MemoryStream();
            z.WriteByte(0x78); z.WriteByte(0x01);
            for (int off = 0; off < data.Length || off == 0; off += 65535)
            {
                int len = Math.Min(65535, data.Length - off);
                z.WriteByte((byte)(off + len >= data.Length ? 1 : 0));
                z.WriteByte((byte)len); z.WriteByte((byte)(len >> 8));
                z.WriteByte((byte)~len); z.WriteByte((byte)(~len >> 8));
                z.Write(data, off, len);
                if (len == 0) break;
            }
            uint a1 = 1, a2 = 0;
            foreach (byte v in data) { a1 = (a1 + v) % 65521; a2 = (a2 + a1) % 65521; }
            uint adler = (a2 << 16) | a1;
            z.WriteByte((byte)(adler >> 24)); z.WriteByte((byte)(adler >> 16)); z.WriteByte((byte)(adler >> 8)); z.WriteByte((byte)adler);

            var png = new MemoryStream();
            png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            var ihdr = new byte[13];
            Be(ihdr, 0, (uint)w); Be(ihdr, 4, (uint)h);
            ihdr[8] = 8; ihdr[9] = 2;
            Chunk(png, "IHDR", ihdr);
            Chunk(png, "IDAT", z.ToArray());
            Chunk(png, "IEND", new byte[0]);
            return png.ToArray();
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4];
            Be(len, 0, (uint)data.Length);
            s.Write(len, 0, 4);
            var td = new byte[4 + data.Length];
            for (int k = 0; k < 4; k++) td[k] = (byte)type[k];
            Buffer.BlockCopy(data, 0, td, 4, data.Length);
            s.Write(td, 0, td.Length);
            var crc = new byte[4];
            Be(crc, 0, Crc(td));
            s.Write(crc, 0, 4);
        }

        private static void Be(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
        }

        private static uint Crc(byte[] d)
        {
            uint c = 0xFFFFFFFF;
            foreach (byte v in d)
            {
                c ^= v;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            return ~c;
        }
    }
}
