using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace CPRB.Bannerlord
{
    // Land and ground height samples of the map, taken on the main thread (MapOverlay).
    internal sealed class LandGrid
    {
        public int Width, Height;
        public float MinX, MinY, StepX, StepY;
        public float[] Land;     // 1 = land, 0 = sea or lake
        public float[] Ground;   // terrain height

        public float SampleLand(float x, float y)
        {
            return Sample(Land, x, y);
        }

        public float SampleGround(float x, float y)
        {
            return Sample(Ground, x, y);
        }

        private float Sample(float[] a, float x, float y)
        {
            float fx = Clamp((x - MinX) / StepX, 0f, Width - 1);
            float fy = Clamp((y - MinY) / StepY, 0f, Height - 1);
            int ix = Math.Min((int)fx, Width - 2), iy = Math.Min((int)fy, Height - 2);
            float tx = fx - ix, ty = fy - iy;
            int i = iy * Width + ix;
            float a0 = a[i] + (a[i + 1] - a[i]) * tx;
            float a1 = a[i + Width] + (a[i + Width + 1] - a[i + Width]) * tx;
            return a0 + (a1 - a0) * ty;
        }

        private static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }

    // Everything the background build needs, copied from the game on the main thread.
    internal sealed class TerritoryInput
    {
        public float[] SiteX, SiteY;
        public int[] SiteOwner;
        public uint[] OwnerColor;          // 0xRRGGBB, index = owner
        public float MinX, MinY, MaxX, MaxY;
        public int Size;                   // texture width and height
        public LandGrid Land;
        public float Warp;                 // wave amplitude, map units
        public float PatternScale;         // this map's settlement spacing / Warband's (9.47)
        public float GlowWidth;            // map units
        public float GlowPower = 2f;       // glow falls off as (1 - distance / width) ^ GlowPower
        public float ReachStart, ReachEnd; // land this far from every settlement fades out (map units)
        public float MaxSegment;           // longest border piece (map units)
    }

    internal struct BorderSegment
    {
        public float AX, AY, BX, BY;
    }

    internal sealed class TerritoryOutput
    {
        public int Size;
        public byte[] Fill;                // RGBA, row 0 = MinY
        public byte[] Glow;
        public List<BorderSegment> Borders;
        public long Milliseconds;
    }

    // The political map, worked out off the main thread: every point takes the owner of the nearest
    // settlement (after a gentle warp, so borders are wavy instead of straight), like the Warband version.
    //   Fill: owner colour everywhere on land.
    //   Glow: owner colour fading inwards from every country border.
    //   Borders: the country borders as line pieces, drawn as thin decals by MapOverlay.
    internal static class TerritoryBuilder
    {
        private const int Block = 16;

        public static TerritoryOutput Build(TerritoryInput input)
        {
            var watch = Stopwatch.StartNew();
            var job = new Job(input);
            int blocks = (job.N + Block - 1) / Block;
            Parallel.For(0, blocks * blocks, b => job.ShadeBlock(b % blocks, b / blocks));
            var borders = job.TraceBorders();
            return new TerritoryOutput
            {
                Size = job.N,
                Fill = job.Fill,
                Glow = job.Glow,
                Borders = borders,
                Milliseconds = watch.ElapsedMilliseconds,
            };
        }

        private sealed class Job
        {
            private readonly TerritoryInput _in;
            public readonly int N;
            private readonly float _stepX, _stepY, _texel;
            private readonly double _patternScale;  // the Warband wave pattern was made for its map scale
            public readonly byte[] Fill, Glow;
            private readonly int[] _owner;
            private readonly float[] _shown;   // how much of the political map shows: land x reach

            public Job(TerritoryInput input)
            {
                _in = input;
                N = input.Size;
                _stepX = (input.MaxX - input.MinX) / N;
                _stepY = (input.MaxY - input.MinY) / N;
                _texel = Math.Max(_stepX, _stepY);
                _patternScale = 1.0 / Math.Max(input.PatternScale, 1e-3f);
                Fill = new byte[N * N * 4];
                Glow = new byte[N * N * 4];
                _owner = new int[N * N];
                _shown = new float[N * N];
            }

            // same wave pattern as the Warband shader, scaled to this map
            private void Warp(float x, float y, out float wx, out float wy)
            {
                float a = _in.Warp;
                if (a <= 0f)
                {
                    wx = x;
                    wy = y;
                    return;
                }
                double px = x * _patternScale, py = y * _patternScale;
                wx = x + a * (float)(Math.Sin(py * 0.37 + 1.7 * Math.Sin(px * 0.23)) + 0.4 * Math.Sin(py * 1.3 + px * 0.7));
                wy = y + a * (float)(Math.Sin(px * 0.41 + 1.3 * Math.Sin(py * 0.29)) + 0.4 * Math.Sin(px * 1.1 - py * 0.9));
            }

            private float CenterX(int x)
            {
                return _in.MinX + (x + 0.5f) * _stepX;
            }

            private float CenterY(int y)
            {
                return _in.MinY + (y + 0.5f) * _stepY;
            }

            public void ShadeBlock(int bx, int by)
            {
                int x0 = bx * Block, y0 = by * Block;
                int x1 = Math.Min(x0 + Block, N), y1 = Math.Min(y0 + Block, N);
                float[] sx = _in.SiteX, sy = _in.SiteY;
                int[] so = _in.SiteOwner;
                int count = sx.Length;

                // Sites that can matter for this block: for any pixel, the nearest site of another
                // owner is at most (that distance from the block centre) + R away, R covering the block
                // and the warp, so nothing further than that + 2R from the centre can be needed.
                float cx, cy;
                Warp(_in.MinX + (x0 + x1) * 0.5f * _stepX, _in.MinY + (y0 + y1) * 0.5f * _stepY, out cx, out cy);
                int nearest = 0;
                float best = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    float dx = sx[i] - cx, dy = sy[i] - cy, d = dx * dx + dy * dy;
                    if (d < best)
                    {
                        best = d;
                        nearest = i;
                    }
                }
                float bestOther = float.MaxValue;
                for (int i = 0; i < count; i++)
                {
                    if (so[i] == so[nearest])
                        continue;
                    float dx = sx[i] - cx, dy = sy[i] - cy, d = dx * dx + dy * dy;
                    if (d < bestOther)
                        bestOther = d;
                }
                float radius = 0.5f * Block * (float)Math.Sqrt(_stepX * _stepX + _stepY * _stepY) + 4f * _in.Warp;
                float limit = bestOther == float.MaxValue ? float.MaxValue : (float)Math.Sqrt(bestOther) + 2f * radius;
                float limit2 = limit == float.MaxValue ? float.MaxValue : limit * limit;
                var cand = new List<int>(64);
                for (int i = 0; i < count; i++)
                {
                    float dx = sx[i] - cx, dy = sy[i] - cy;
                    if (dx * dx + dy * dy <= limit2)
                        cand.Add(i);
                }

                for (int y = y0; y < y1; y++)
                {
                    float py = CenterY(y);
                    for (int x = x0; x < x1; x++)
                    {
                        float px = CenterX(x);
                        float qx, qy;
                        Warp(px, py, out qx, out qy);

                        // nearest site
                        int i1 = -1;
                        float d1 = float.MaxValue;
                        for (int c = 0; c < cand.Count; c++)
                        {
                            int i = cand[c];
                            float dx = sx[i] - qx, dy = sy[i] - qy, d = dx * dx + dy * dy;
                            if (d < d1)
                            {
                                d1 = d;
                                i1 = i;
                            }
                        }
                        int o1 = so[i1];
                        // nearest site of another owner
                        int iD = -1;
                        float dD = float.MaxValue;
                        for (int c = 0; c < cand.Count; c++)
                        {
                            int i = cand[c];
                            if (so[i] == o1)
                                continue;
                            float dx = sx[i] - qx, dy = sy[i] - qy, d = dx * dx + dy * dy;
                            if (d < dD)
                            {
                                dD = d;
                                iD = i;
                            }
                        }

                        float land = _in.Land.SampleLand(px, py);
                        float reach = 1f - SmoothStep(_in.ReachStart, _in.ReachEnd, (float)Math.Sqrt(d1));
                        float alpha = Saturate(land) * reach;
                        uint col = _in.OwnerColor[o1];
                        float r = (col >> 16) & 0xFF, g = (col >> 8) & 0xFF, b = col & 0xFF;

                        // glow of this side's colour near a country border
                        float glow = 0f;
                        if (iD >= 0)
                        {
                            float edge = EdgeDistance(d1, dD, i1, iD);
                            glow = 1f - Saturate(edge / _in.GlowWidth);
                            glow = (float)Math.Pow(glow, _in.GlowPower);
                        }

                        int p = y * N + x, k = p * 4;
                        _owner[p] = o1;
                        _shown[p] = alpha;
                        Fill[k] = (byte)r;
                        Fill[k + 1] = (byte)g;
                        Fill[k + 2] = (byte)b;
                        Fill[k + 3] = (byte)(alpha * 255f + 0.5f);
                        Glow[k] = (byte)r;
                        Glow[k + 1] = (byte)g;
                        Glow[k + 2] = (byte)b;
                        Glow[k + 3] = (byte)(glow * alpha * 255f + 0.5f);
                    }
                }
            }

            // distance from the point to the boundary between the cells of sites a and b,
            // from the squared distances to them (same formula as the Warband shader)
            private float EdgeDistance(float da, float db, int a, int b)
            {
                float dx = _in.SiteX[a] - _in.SiteX[b], dy = _in.SiteY[a] - _in.SiteY[b];
                return (db - da) / (2f * (float)Math.Sqrt(dx * dx + dy * dy) + 1e-6f);
            }

            private int OwnerAt(float x, float y)
            {
                float qx, qy;
                Warp(x, y, out qx, out qy);
                float[] sx = _in.SiteX, sy = _in.SiteY;
                int nearest = 0;
                float best = float.MaxValue;
                for (int i = 0; i < sx.Length; i++)
                {
                    float dx = sx[i] - qx, dy = sy[i] - qy, d = dx * dx + dy * dy;
                    if (d < best)
                    {
                        best = d;
                        nearest = i;
                    }
                }
                return _in.SiteOwner[nearest];
            }

            // ---- country borders as line pieces ----------------------------------------------
            // Grid = texel centres. Every grid edge whose two ends have different owners gets a
            // crossing point (found exactly by bisection); inside each grid cell the crossings are
            // joined (two: a line; three or four: all to a junction in the middle). The pieces are then
            // chained, simplified and cut to a maximum length.
            public List<BorderSegment> TraceBorders()
            {
                int n = N;
                var edges = new List<int>();   // id: horizontal y * n + x, vertical n * n + y * n + x
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        int p = y * n + x;
                        if (x + 1 < n && _owner[p] != _owner[p + 1] && _shown[p] + _shown[p + 1] >= 1f)
                            edges.Add(p);
                        if (y + 1 < n && _owner[p] != _owner[p + n] && _shown[p] + _shown[p + n] >= 1f)
                            edges.Add(n * n + p);
                    }
                }

                var px = new float[edges.Count];
                var py = new float[edges.Count];
                Parallel.For(0, edges.Count, e =>
                {
                    int id = edges[e];
                    bool vertical = id >= n * n;
                    int p = vertical ? id - n * n : id;
                    int x = p % n, y = p / n;
                    float ax = CenterX(x), ay = CenterY(y);
                    float bx = vertical ? ax : CenterX(x + 1), by = vertical ? CenterY(y + 1) : ay;
                    int oa = _owner[p];
                    float lo = 0f, hi = 1f;
                    for (int it = 0; it < 14; it++)
                    {
                        float mid = 0.5f * (lo + hi);
                        if (OwnerAt(ax + (bx - ax) * mid, ay + (by - ay) * mid) == oa)
                            lo = mid;
                        else
                            hi = mid;
                    }
                    float t = 0.5f * (lo + hi);
                    px[e] = ax + (bx - ax) * t;
                    py[e] = ay + (by - ay) * t;
                });

                var node = new Dictionary<int, int>(edges.Count);
                var nx = new List<float>(edges.Count + 1024);
                var ny = new List<float>(edges.Count + 1024);
                for (int e = 0; e < edges.Count; e++)
                {
                    node[edges[e]] = e;
                    nx.Add(px[e]);
                    ny.Add(py[e]);
                }

                var links = new List<long>();
                var cross = new int[4];
                for (int y = 0; y + 1 < n; y++)
                {
                    for (int x = 0; x + 1 < n; x++)
                    {
                        int p = y * n + x, c = 0, id;
                        if (node.TryGetValue(p, out id)) cross[c++] = id;                 // bottom
                        if (node.TryGetValue(p + n, out id)) cross[c++] = id;             // top
                        if (node.TryGetValue(n * n + p, out id)) cross[c++] = id;         // left
                        if (node.TryGetValue(n * n + p + 1, out id)) cross[c++] = id;     // right
                        if (c == 2)
                        {
                            links.Add(Pack(cross[0], cross[1]));
                        }
                        else if (c >= 3)
                        {
                            float mx = 0f, my = 0f;
                            for (int i = 0; i < c; i++)
                            {
                                mx += nx[cross[i]];
                                my += ny[cross[i]];
                            }
                            int junction = nx.Count;
                            nx.Add(mx / c);
                            ny.Add(my / c);
                            for (int i = 0; i < c; i++)
                                links.Add(Pack(cross[i], junction));
                        }
                    }
                }

                var adj = new List<int>[nx.Count];
                foreach (long l in links)
                {
                    int a = (int)(l >> 32), b = (int)(l & 0xFFFFFFFF);
                    (adj[a] ?? (adj[a] = new List<int>(2))).Add(b);
                    (adj[b] ?? (adj[b] = new List<int>(2))).Add(a);
                }

                var segments = new List<BorderSegment>();
                var used = new HashSet<long>();
                var chain = new List<int>();
                // open chains first (they start at ends and junctions), then closed loops
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int start = 0; start < adj.Length; start++)
                    {
                        if (adj[start] == null || (pass == 0 && adj[start].Count == 2))
                            continue;
                        foreach (int first in adj[start])
                        {
                            if (used.Contains(Pack(start, first)))
                                continue;
                            chain.Clear();
                            chain.Add(start);
                            int prev = start, cur = first;
                            used.Add(Pack(prev, cur));
                            chain.Add(cur);
                            while (adj[cur].Count == 2 && cur != start)
                            {
                                int next = adj[cur][0] == prev ? adj[cur][1] : adj[cur][0];
                                if (used.Contains(Pack(cur, next)))
                                    break;
                                used.Add(Pack(cur, next));
                                prev = cur;
                                cur = next;
                                chain.Add(cur);
                            }
                            EmitChain(chain, nx, ny, segments);
                        }
                    }
                }
                return segments;
            }

            private static long Pack(int a, int b)
            {
                return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            }

            private void EmitChain(List<int> chain, List<float> nx, List<float> ny, List<BorderSegment> output)
            {
                int count = chain.Count;
                if (count < 2)
                    return;
                var xs = new float[count];
                var ys = new float[count];
                for (int i = 0; i < count; i++)
                {
                    xs[i] = nx[chain[i]];
                    ys[i] = ny[chain[i]];
                }
                var keep = new bool[count];
                keep[0] = keep[count - 1] = true;
                Simplify(xs, ys, 0, count - 1, 0.35f * _texel, keep);
                int last = 0;
                for (int i = 1; i < count; i++)
                {
                    if (!keep[i])
                        continue;
                    float ax = xs[last], ay = ys[last], bx = xs[i], by = ys[i];
                    float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                    int pieces = Math.Max(1, (int)Math.Ceiling(len / _in.MaxSegment));
                    for (int k = 0; k < pieces; k++)
                    {
                        float t0 = (float)k / pieces, t1 = (float)(k + 1) / pieces;
                        output.Add(new BorderSegment
                        {
                            AX = ax + (bx - ax) * t0,
                            AY = ay + (by - ay) * t0,
                            BX = ax + (bx - ax) * t1,
                            BY = ay + (by - ay) * t1,
                        });
                    }
                    last = i;
                }
            }

            // Douglas-Peucker
            private static void Simplify(float[] xs, float[] ys, int first, int last, float tolerance, bool[] keep)
            {
                if (last - first < 2)
                    return;
                float ax = xs[first], ay = ys[first], dx = xs[last] - ax, dy = ys[last] - ay;
                float len2 = dx * dx + dy * dy;
                int worst = -1;
                float worstDistance = tolerance;
                for (int i = first + 1; i < last; i++)
                {
                    float ex = xs[i] - ax, ey = ys[i] - ay, d;
                    if (len2 < 1e-12f)
                    {
                        d = (float)Math.Sqrt(ex * ex + ey * ey);
                    }
                    else
                    {
                        float t = Saturate((ex * dx + ey * dy) / len2);
                        float fx = ex - t * dx, fy = ey - t * dy;
                        d = (float)Math.Sqrt(fx * fx + fy * fy);
                    }
                    if (d > worstDistance)
                    {
                        worstDistance = d;
                        worst = i;
                    }
                }
                if (worst < 0)
                    return;
                keep[worst] = true;
                Simplify(xs, ys, first, worst, tolerance, keep);
                Simplify(xs, ys, worst, last, tolerance, keep);
            }
        }

        // Bannerlord's faction colours are dark (made for banners): keep the hue and saturation,
        // lift the brightness to at least minValue (0..1).
        public static uint Brighten(uint rgb, float minValue)
        {
            float r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
            float max = Math.Max(r, Math.Max(g, b)) / 255f;
            if (max <= 0f)
                return 0x8C8C8Cu;
            if (max >= minValue)
                return rgb;
            float k = minValue / max;
            uint R = (uint)Math.Min(255f, r * k + 0.5f), G = (uint)Math.Min(255f, g * k + 0.5f), B = (uint)Math.Min(255f, b * k + 0.5f);
            return (R << 16) | (G << 8) | B;
        }

        public static float Saturate(float v)
        {
            return v < 0f ? 0f : v > 1f ? 1f : v;
        }

        public static float SmoothStep(float a, float b, float v)
        {
            float t = Saturate((v - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
