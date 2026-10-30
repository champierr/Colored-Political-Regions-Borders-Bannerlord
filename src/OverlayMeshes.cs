using System;
using System.Collections.Generic;
using System.Diagnostics;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace CPRB.Bannerlord
{
    // The political map as meshes laid just above the terrain, drawn with a copy of the game's
    // "editor_map_border" material: the same material and the same kind of code-built mesh the game uses
    // for battle boundary walls, so the shader is known to exist. That shader multiplies what is behind
    // it (no G-buffer, no depth writes), which tints the terrain like coloured glass and keeps its relief
    // and the day/night light. Multiplying does not depend on drawing order, so the layers just stack.
    //   fill    - map-wide grid, lifted clear of the terrain; fades in when zoomed out. Multiplying alone
    //             can only darken, which turns blue or purple kingdoms grey on yellow land, so the fill is
    //             two meshes on the same grid: one darkens the terrain by (1 - a), the other (the game's
    //             additive "vertex_color_add_mat") adds the kingdom colour * a: together a real blend.
    //   glow    - fine grid only near country borders, hugging the terrain; two meshes like the fill
    //   borders - thin ribbons along the borders, built in a few widths; the closest width is shown
    //
    // Why not decals or a normal see-through material (found by testing in game, see the log history):
    //   - map decals only show textures from the game's prebuilt decal atlas and fade beyond 120 units;
    //   - materials switched to alpha blending in code are not drawn (their forward shader is missing).
    //
    // The map_border pixel shader (Shaders/Sources/map_border.rs), with a white texture and the camera
    // above the mesh, outputs the multiplier  M = saturate(2 * C.grb + 1 - A)  where C is the vertex colour
    // and A = 0.9 * smoothstep(0, 0.8, (200 - |vector_argument.y - z|) / 200). With vector_argument.y at
    // the mesh's average height A is 0.9, so a vertex colour of (M - 0.1) / 2 (red and green swapped)
    // gives the multiplier M exactly. The zoom fade moves vector_argument.y away, which lowers A and so
    // lifts every multiplier towards 1.
    //
    // Meshes are made with MeshBuilder (one engine call per mesh; editing a mesh corner by corner costs a
    // call per corner and per face). The work is still queued and done a few milliseconds per frame, and
    // when ownership changes only the parts whose colours changed are rebuilt.
    internal sealed class OverlayMeshes
    {
        private const int FillChunks = 16, GlowChunks = 16, BorderChunks = 8;
        private const float FillCell = 3f;
        private const float GlowCell = 0.75f;
        private const float FineStep = 0.5f;          // terrain height samples for the meshes near the ground
        private const float PieceLength = 1.5f;       // border ribbons are cut into pieces this long
        private const float FullA = 0.9f;
        private const float VisibleDistance = 1e6f;
        private static readonly float[] BorderTint = { 0.006f, 0.005f, 0.004f };   // linear, near black

        public static readonly float[] BorderLevels = BuildLevels();

        private readonly Scene _scene;
        private readonly LandGrid _land;
        private readonly Settings _settings;
        private readonly float _minX, _minY, _maxX, _maxY;
        private readonly Dictionary<long, float> _fine = new Dictionary<long, float>();
        private readonly Queue<Action> _jobs = new Queue<Action>();

        private Material _material;       // multiplies
        private Material _add;            // adds (fill colour), null when fill_light is 0
        private Texture _white;

        private sealed class Part
        {
            public GameEntity Entity;
            public Mesh Mesh;
            public float AvgZ;
            public ulong Hash;
            public int Faces;
            public int Covered;        // coloured corners (for the log)
            public double Strength;    // sum of their strengths
        }

        private readonly Part[] _fill = new Part[FillChunks * FillChunks];
        private readonly Part[] _fillAdd = new Part[FillChunks * FillChunks];
        private readonly Part[] _glow = new Part[GlowChunks * GlowChunks];
        private readonly Part[] _glowAdd = new Part[GlowChunks * GlowChunks];
        private readonly Part[][] _borders = new Part[BorderLevels.Length][];
        private readonly bool[] _borderQueued = new bool[BorderLevels.Length];
        private readonly int[] _borderBuilt = new int[BorderLevels.Length];   // chunks built since queued
        private List<int>[] _piecesByChunk;
        private float[] _pax, _pay, _pbx, _pby;

        private byte[] _fillData, _glowData;
        private int _n;
        private float _opacity = -1f, _glowStrength, _borderAlpha;
        private float _zoom = 1f, _daylight = 1f;
        private uint _fillAddFactor, _glowAddFactor;
        private bool _fillVisible = true;
        private int _wantedLevel = -1, _shownLevel = -1;

        public int FineSamples;
        private readonly float[] _linear = new float[256];   // sRGB byte -> linear multiplier

        public OverlayMeshes(Scene scene, LandGrid land, float minX, float minY, float maxX, float maxY, Settings settings)
        {
            _scene = scene;
            _land = land;
            _settings = settings;
            _minX = minX;
            _minY = minY;
            _maxX = maxX;
            _maxY = maxY;
            for (int l = 0; l < BorderLevels.Length; l++)
                _borders[l] = new Part[BorderChunks * BorderChunks];
            for (int i = 0; i < 256; i++)
                _linear[i] = (float)Math.Pow(i / 255.0, settings.TintGamma);
            CreateLayers();
        }

        // border half-widths in map units, each 1.4 x the one before
        private static float[] BuildLevels()
        {
            var levels = new float[7];
            float w = 0.06f;
            for (int i = 0; i < levels.Length; i++, w *= 1.4f)
                levels[i] = w;
            return levels;
        }

        public int Pending => _jobs.Count;

        public bool CreateMaterial()
        {
            Material source = Material.GetFromResource(_settings.MeshMaterial);
            if (source == null)
            {
                Log.Info("material " + _settings.MeshMaterial + ": not found");
                return false;
            }
            Log.Info(MapOverlay.Describe(source, _settings.MeshMaterial) + "; flags " + source.Flags);
            _white = Texture.CreateFromByteArray(Solid(4, 4, 255), 4, 4);
            _white.Name = "cprb_white";
            _material = source.CreateCopy();
            _material.SetTexture(Material.MBTextureType.DiffuseMap, _white);
            if (_settings.FillLight > 0f)
            {
                Material add = Material.GetFromResource("vertex_color_add_mat") ?? Material.GetFromResource("vertex_color_mat");
                if (add == null)
                    Log.Info("no additive material: the fill only tints");
                else
                {
                    Log.Info(MapOverlay.Describe(add, "additive") + "; flags " + add.Flags);
                    _add = add.CreateCopy();
                }
            }
            return true;
        }

        public Material Material => _material;

        private static byte[] Solid(int w, int h, byte v)
        {
            var data = new byte[w * h * 4];
            for (int i = 0; i < data.Length; i++)
                data[i] = v;
            return data;
        }

        // ---- colours ------------------------------------------------------------------------------

        // multiplier for one channel: 1 - k (1 - tint), as the vertex colour value that gives it
        private static byte Channel(float tint, float k)
        {
            float m = Math.Max(0.1f, 1f - k * (1f - tint));
            return (byte)Math.Round(Math.Min(1f, (m - 0.1f) * 0.5f) * 255f);
        }

        // vertex colour (ARGB) that tints by (tr, tg, tb) at strength k; the shader swaps red and green
        public static uint Tint(float tr, float tg, float tb, float k)
        {
            return 0xFF000000u | ((uint)Channel(tg, k) << 16) | ((uint)Channel(tr, k) << 8) | Channel(tb, k);
        }

        private float Linear(byte srgb)
        {
            return _linear[srgb];
        }

        // ---- data from the territory build ----------------------------------------------------------

        // new territory data: rebuild what changed
        public void Update(TerritoryOutput result, float opacity, float glowStrength, float borderAlpha)
        {
            _fillData = result.Fill;
            _glowData = result.Glow;
            _n = result.Size;
            _opacity = opacity;
            _glowStrength = glowStrength;
            _borderAlpha = borderAlpha;
            QueueFillAndGlow();
            SetSegments(result.Borders);
        }

        // opacity changed (Ctrl + 1): the strengths are baked into the vertex colours
        public void Restyle(float opacity, float glowStrength)
        {
            if (_fillData == null || (opacity == _opacity && glowStrength == _glowStrength))
                return;
            _opacity = opacity;
            _glowStrength = glowStrength;
            QueueFillAndGlow();
        }

        private void QueueFillAndGlow()
        {
            for (int c = 0; c < _fill.Length; c++)
            {
                int chunk = c;
                _jobs.Enqueue(() => BuildFill(chunk));
            }
            for (int c = 0; c < _glow.Length; c++)
            {
                int chunk = c;
                _jobs.Enqueue(() => BuildGlow(chunk));
            }
        }

        private void SetSegments(List<BorderSegment> segments)
        {
            var ax = new List<float>();
            var ay = new List<float>();
            var bx = new List<float>();
            var by = new List<float>();
            foreach (BorderSegment s in segments)
            {
                float dx = s.BX - s.AX, dy = s.BY - s.AY, len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-4f)
                    continue;
                int pieces = Math.Max(1, (int)Math.Ceiling(len / PieceLength));
                for (int k = 0; k < pieces; k++)
                {
                    float t0 = (float)k / pieces, t1 = (float)(k + 1) / pieces;
                    ax.Add(s.AX + dx * t0);
                    ay.Add(s.AY + dy * t0);
                    bx.Add(s.AX + dx * t1);
                    by.Add(s.AY + dy * t1);
                }
            }
            _pax = ax.ToArray();
            _pay = ay.ToArray();
            _pbx = bx.ToArray();
            _pby = by.ToArray();
            _piecesByChunk = new List<int>[BorderChunks * BorderChunks];
            for (int c = 0; c < _piecesByChunk.Length; c++)
                _piecesByChunk[c] = new List<int>();
            for (int p = 0; p < _pax.Length; p++)
                _piecesByChunk[ChunkOf((_pax[p] + _pbx[p]) * 0.5f, (_pay[p] + _pby[p]) * 0.5f, BorderChunks)].Add(p);

            RequeueBorders();
        }

        // the shown width is rebuilt now (part by part, so the old lines stay until replaced);
        // the other widths are dropped and built again when the zoom needs them
        private void RequeueBorders()
        {
            for (int l = 0; l < BorderLevels.Length; l++)
            {
                _borderQueued[l] = false;
                if (l != _shownLevel)
                    Clear(_borders[l]);
            }
            if (_shownLevel >= 0)
                QueueBorders(_shownLevel);
            if (_wantedLevel >= 0 && _wantedLevel != _shownLevel)
                QueueBorders(_wantedLevel);
        }

        // Before a battle the game frees the map's GPU memory (MapScreen.ClearGPUMemory calls
        // Texture.ReleaseGpuMemories and SceneLayer.ClearRuntimeGPUMemory). Textures from the game files are
        // loaded again from disk, but our white texture is made from bytes: it stays empty, and every
        // multiplying mesh turns black (seen as black squares along the borders after a battle). So when the
        // map is back: a new texture and new materials, and every mesh built again.
        public void Recover()
        {
            Material oldMaterial = _material;
            bool wasLoaded = _white != null && _white.IsLoaded();
            if (!CreateMaterial())
                return;
            // until its part is rebuilt, a mesh keeps the old material: give that one the new texture too
            if (oldMaterial != null)
                oldMaterial.SetTexture(Material.MBTextureType.DiffuseMap, _white);
            Log.Info("back on the map after a mission (old texture loaded: " + wasLoaded + "): building the meshes again");
            if (_fillData == null)
                return;
            foreach (Part[] parts in new[] { _fill, _fillAdd, _glow, _glowAdd })
                foreach (Part part in parts)
                    if (part != null)
                        part.Hash = 0;
            QueueFillAndGlow();
            if (_shownLevel >= 0)
                foreach (Part part in _borders[_shownLevel])
                    if (part != null)
                        part.Hash = 0;
            if (_piecesByChunk != null)
                RequeueBorders();
        }

        private void QueueBorders(int level)
        {
            _borderQueued[level] = true;
            _borderBuilt[level] = 0;
            for (int c = 0; c < BorderChunks * BorderChunks; c++)
            {
                int chunk = c;
                _jobs.Enqueue(() => BuildBorders(level, chunk));
            }
        }

        // runs queued building work for up to budgetMs
        public void Pump(double budgetMs)
        {
            if (_jobs.Count == 0)
                return;
            var watch = Stopwatch.StartNew();
            while (_jobs.Count > 0 && watch.Elapsed.TotalMilliseconds < budgetMs)
                _jobs.Dequeue()();
        }

        // ---- geometry helpers --------------------------------------------------------------------

        private int ChunkOf(float x, float y, int chunks)
        {
            int cx = Math.Max(0, Math.Min(chunks - 1, (int)((x - _minX) / (_maxX - _minX) * chunks)));
            int cy = Math.Max(0, Math.Min(chunks - 1, (int)((y - _minY) / (_maxY - _minY) * chunks)));
            return cy * chunks + cx;
        }

        // RGBA texel of territory data at a map position (nearest)
        private int Texel(float x, float y)
        {
            int tx = Math.Max(0, Math.Min(_n - 1, (int)((x - _minX) / (_maxX - _minX) * _n)));
            int ty = Math.Max(0, Math.Min(_n - 1, (int)((y - _minY) / (_maxY - _minY) * _n)));
            return (ty * _n + tx) * 4;
        }

        // highest coarse ground sample within radius (for the fill, seen from far away)
        private float CoarseMax(float x, float y, float radius)
        {
            float best = float.MinValue;
            int x0 = (int)Math.Floor((x - radius - _land.MinX) / _land.StepX), x1 = (int)Math.Ceiling((x + radius - _land.MinX) / _land.StepX);
            int y0 = (int)Math.Floor((y - radius - _land.MinY) / _land.StepY), y1 = (int)Math.Ceiling((y + radius - _land.MinY) / _land.StepY);
            for (int j = Math.Max(0, y0); j <= Math.Min(_land.Height - 1, y1); j++)
                for (int i = Math.Max(0, x0); i <= Math.Min(_land.Width - 1, x1); i++)
                    best = Math.Max(best, _land.Ground[j * _land.Width + i]);
            return best == float.MinValue ? _land.SampleGround(x, y) : best;
        }

        private float Fine(int ix, int iy)
        {
            long key = ((long)ix << 32) | (uint)iy;
            float h;
            if (!_fine.TryGetValue(key, out h))
            {
                h = _scene.GetTerrainHeight(new Vec2(ix * FineStep, iy * FineStep), true);
                _fine[key] = h;
                FineSamples++;
            }
            return h;
        }

        // highest exact terrain height within radius (for the meshes seen up close)
        private float FineMax(float x, float y, float radius)
        {
            int x0 = (int)Math.Floor((x - radius) / FineStep), x1 = (int)Math.Ceiling((x + radius) / FineStep);
            int y0 = (int)Math.Floor((y - radius) / FineStep), y1 = (int)Math.Ceiling((y + radius) / FineStep);
            float best = float.MinValue;
            for (int j = y0; j <= y1; j++)
                for (int i = x0; i <= x1; i++)
                    best = Math.Max(best, Fine(i, j));
            return best;
        }

        private static ulong Mix(ulong hash, ulong value)
        {
            return (hash ^ value) * 1099511628211UL;
        }

        // one mesh: corners are shared between faces, like MeshBuilder.CreateUnitMesh
        private sealed class Builder
        {
            private readonly List<Vec3> _pos = new List<Vec3>();
            private readonly List<uint> _color = new List<uint>();
            private readonly List<int> _faces = new List<int>();
            public float SumZ;

            public int Corner(Vec3 p, uint color)
            {
                _pos.Add(p);
                _color.Add(color);
                SumZ += p.z;
                return _pos.Count - 1;
            }

            public void Face(int a, int b, int c)
            {
                _faces.Add(a);
                _faces.Add(b);
                _faces.Add(c);
            }

            public int Faces => _faces.Count / 3;
            public int Corners => _pos.Count;

            public Mesh Build(Material material, bool fast)
            {
                Mesh mesh;
                if (fast)
                {
                    var builder = new MeshBuilder();
                    var ids = new int[_pos.Count];
                    for (int i = 0; i < _pos.Count; i++)
                        ids[i] = builder.AddFaceCorner(_pos[i], Vec3.Up, new Vec2(0f, 0f), _color[i]);
                    for (int f = 0; f < _faces.Count; f += 3)
                        builder.AddFace(ids[_faces[f]], ids[_faces[f + 1]], ids[_faces[f + 2]]);
                    mesh = builder.Finalize();
                    mesh.SetMaterial(material);
                    mesh.CullingMode = MBMeshCullingMode.None;
                    mesh.RecomputeBoundingBox();
                    return mesh;
                }
                mesh = Mesh.CreateMesh(true);
                UIntPtr handle = mesh.LockEditDataWrite();
                var corners = new int[_pos.Count];
                for (int i = 0; i < _pos.Count; i++)
                    corners[i] = mesh.AddFaceCorner(_pos[i], Vec3.Up, new Vec2(0f, 0f), _color[i], handle);
                for (int f = 0; f < _faces.Count; f += 3)
                    mesh.AddFace(corners[_faces[f]], corners[_faces[f + 1]], corners[_faces[f + 2]], handle);
                mesh.SetMaterial(material);
                mesh.CullingMode = MBMeshCullingMode.None;
                mesh.ComputeNormals();
                mesh.ComputeTangents();
                mesh.RecomputeBoundingBox();
                mesh.UnlockEditDataWrite(handle);
                return mesh;
            }
        }

        private Part Place(Part old, Builder b, string name, ulong hash, Material material = null)
        {
            Remove(old);
            if (b.Faces == 0)
                return new Part { Hash = hash };
            var part = new Part { Mesh = b.Build(material ?? _material, _settings.FastMeshes != 0), AvgZ = b.SumZ / Math.Max(1, b.Corners), Hash = hash, Faces = b.Faces };
            part.Mesh.SetVectorArgument(VisibleDistance, part.AvgZ, 0f, 0f);
            part.Entity = GameEntity.CreateEmpty(_scene);
            part.Entity.Name = name;
            part.Entity.AddMesh(part.Mesh);
            part.Entity.SetVisibilityExcludeParents(true);
            return part;
        }

        private void Remove(Part part)
        {
            if (part == null || part.Entity == null)
                return;
            part.Entity.SetVisibilityExcludeParents(false);
            _scene.RemoveEntity(part.Entity, 100);
            part.Entity = null;
        }

        private void Clear(Part[] parts)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                Remove(parts[i]);
                parts[i] = null;
            }
        }

        // ---- fill and glow: two-pass grids --------------------------------------------------------------
        // Both layers are a grid of quads where the territory data has coverage. Each grid is two meshes on
        // the same corners: one darkens the terrain by (1 - a) (multiply), the other adds colour * a * light
        // (additive), which together blend the colour in like the Warband version. Without the additive
        // material (fill_light 0) the multiply mesh tints with the colour instead.

        private sealed class GridLayer
        {
            public string Name;
            public float Cell;
            public int Chunks;
            public bool Lifted;                 // fill: high above the highest ground (seen from far away)
            public Part[] Mul, Add;
        }

        private GridLayer _fillLayer, _glowLayer;

        private void CreateLayers()
        {
            _fillLayer = new GridLayer { Name = "fill", Cell = FillCell, Chunks = FillChunks, Lifted = true, Mul = _fill, Add = _fillAdd };
            _glowLayer = new GridLayer { Name = "glow", Cell = GlowCell, Chunks = GlowChunks, Lifted = false, Mul = _glow, Add = _glowAdd };
        }

        private void BuildFill(int chunk)
        {
            BuildGrid(_fillLayer, _fillData, _opacity, chunk);
        }

        private void BuildGlow(int chunk)
        {
            BuildGrid(_glowLayer, _glowData, _glowStrength, chunk);
        }

        private void BuildGrid(GridLayer layer, byte[] data, float strength, int chunk)
        {
            float cell = layer.Cell;
            int chunks = layer.Chunks;
            int cx = chunk % chunks, cy = chunk / chunks;
            int nx = (int)Math.Ceiling((_maxX - _minX) / cell), ny = (int)Math.Ceiling((_maxY - _minY) / cell);
            int i0 = cx * nx / chunks, i1 = (cx + 1) * nx / chunks, j0 = cy * ny / chunks, j1 = (cy + 1) * ny / chunks;
            int w = i1 - i0 + 1, h = j1 - j0 + 1;
            var mul = new uint[w * h];
            var add = new uint[w * h];
            var cover = new bool[w * h];
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++)
                {
                    float x = Math.Min(_maxX, _minX + (i0 + i) * cell), y = Math.Min(_maxY, _minY + (j0 + j) * cell);
                    int t = Texel(x, y), v = j * w + i;
                    float a = data[t + 3] / 255f * strength;
                    float r = Linear(data[t]), g = Linear(data[t + 1]), b = Linear(data[t + 2]);
                    if (_add != null)
                    {
                        float light = a * _settings.FillLight;
                        mul[v] = Tint(0f, 0f, 0f, a);
                        add[v] = Additive(r * light, g * light, b * light);
                    }
                    else
                    {
                        mul[v] = Tint(r, g, b, a);
                    }
                    cover[v] = data[t + 3] > 0;
                }
            }
            ulong hash = 1469598103934665603UL;
            for (int v = 0; v < mul.Length; v++)
                hash = Mix(Mix(hash, cover[v] ? mul[v] : 0u), cover[v] ? add[v] : 0u);
            Part old = layer.Mul[chunk];
            if (old != null && old.Hash == hash)
                return;

            var bm = new Builder();
            var ba = new Builder();
            var ids = new int[w * h];
            for (int v = 0; v < ids.Length; v++)
                ids[v] = -1;
            for (int j = 0; j + 1 < h; j++)
            {
                for (int i = 0; i + 1 < w; i++)
                {
                    int v00 = j * w + i, v10 = v00 + 1, v01 = v00 + w, v11 = v01 + 1;
                    if (!cover[v00] && !cover[v10] && !cover[v01] && !cover[v11])
                        continue;
                    int a = GridCorner(layer, bm, ba, ids, mul, add, v00, i0 + i, j0 + j);
                    int c10 = GridCorner(layer, bm, ba, ids, mul, add, v10, i0 + i + 1, j0 + j);
                    int c11 = GridCorner(layer, bm, ba, ids, mul, add, v11, i0 + i + 1, j0 + j + 1);
                    int c01 = GridCorner(layer, bm, ba, ids, mul, add, v01, i0 + i, j0 + j + 1);
                    bm.Face(a, c10, c11);
                    bm.Face(a, c11, c01);
                    ba.Face(a, c10, c11);
                    ba.Face(a, c11, c01);
                }
            }
            // for the log: how many corners are coloured and how strongly
            int covered = 0;
            double sum = 0;
            for (int v = 0; v < cover.Length; v++)
            {
                if (cover[v] && ids[v] >= 0)
                {
                    covered++;
                    sum += data[Texel(Math.Min(_maxX, _minX + (i0 + v % w) * cell), Math.Min(_maxY, _minY + (j0 + v / w) * cell)) + 3] / 255.0 * strength;
                }
            }

            bool fill = layer == _fillLayer;
            Part part = Place(old, bm, "cprb_" + layer.Name + "_" + chunk, hash);
            layer.Mul[chunk] = part;
            part.Covered = covered;
            part.Strength = sum;
            if (fill)
            {
                ApplyZoom(part);
                if (part.Entity != null && !_fillVisible)
                    part.Entity.SetVisibilityExcludeParents(false);
            }
            if (_add != null)
            {
                Part extra = Place(layer.Add[chunk], ba, "cprb_" + layer.Name + "_add_" + chunk, hash, _add);
                layer.Add[chunk] = extra;
                if (extra.Mesh != null)
                    extra.Mesh.Color = fill ? _fillAddFactor : _glowAddFactor;
                if (fill && extra.Entity != null && !_fillVisible)
                    extra.Entity.SetVisibilityExcludeParents(false);
            }
        }

        // the corner of both meshes at grid vertex v (the same index in both builders)
        private int GridCorner(GridLayer layer, Builder bm, Builder ba, int[] ids, uint[] mul, uint[] add, int v, int gi, int gj)
        {
            if (ids[v] >= 0)
                return ids[v];
            float x = Math.Min(_maxX, _minX + gi * layer.Cell), y = Math.Min(_maxY, _minY + gj * layer.Cell);
            float z = layer.Lifted
                ? CoarseMax(x, y, layer.Cell) + _settings.FillLift
                : FineMax(x, y, layer.Cell * 0.75f) + _settings.NearLift;
            var p = new Vec3(x, y, z);
            ids[v] = bm.Corner(p, mul[v]);
            ba.Corner(p, add[v]);
            return ids[v];
        }

        // vertex colour for the additive material (notexture shader: colour * mesh colour, added)
        private static uint Additive(float r, float g, float b)
        {
            uint R = (uint)Math.Round(Math.Min(1f, r) * 255f), G = (uint)Math.Round(Math.Min(1f, g) * 255f), B = (uint)Math.Round(Math.Min(1f, b) * 255f);
            return 0xFF000000u | (R << 16) | (G << 8) | B;
        }

        // ---- borders --------------------------------------------------------------------------------

        // a ribbon per piece, four corners across: soft edge, solid core, solid core, soft edge
        private void BuildBorders(int level, int chunk)
        {
            float half = BorderLevels[level];
            float inner = half * 0.6f, outer = half * 1.4f;
            uint solid = Tint(BorderTint[0], BorderTint[1], BorderTint[2], _borderAlpha);
            uint clear = Tint(1f, 1f, 1f, 0f);
            ulong hash = Mix(1469598103934665603UL, solid);
            foreach (int p in _piecesByChunk[chunk])
            {
                hash = Mix(hash, (ulong)BitConverter.ToInt32(BitConverter.GetBytes(_pax[p]), 0) << 32 | (uint)BitConverter.ToInt32(BitConverter.GetBytes(_pay[p]), 0));
                hash = Mix(hash, (ulong)BitConverter.ToInt32(BitConverter.GetBytes(_pbx[p]), 0) << 32 | (uint)BitConverter.ToInt32(BitConverter.GetBytes(_pby[p]), 0));
            }
            Part old = _borders[level][chunk];
            if (old != null && old.Hash == hash)
            {
                _borderBuilt[level]++;
                ShowWantedWidth();
                return;
            }
            var b = new Builder();
            foreach (int p in _piecesByChunk[chunk])
            {
                float ax = _pax[p], ay = _pay[p], bx = _pbx[p], by = _pby[p];
                float dx = bx - ax, dy = by - ay, len = (float)Math.Sqrt(dx * dx + dy * dy);
                float nx = -dy / len, ny = dx / len;
                float za = FineMax(ax, ay, 0.6f) + _settings.NearLift + 0.03f;
                float zb = FineMax(bx, by, 0.6f) + _settings.NearLift + 0.03f;
                int a0 = b.Corner(new Vec3(ax - nx * outer, ay - ny * outer, za), clear);
                int a1 = b.Corner(new Vec3(ax - nx * inner, ay - ny * inner, za), solid);
                int a2 = b.Corner(new Vec3(ax + nx * inner, ay + ny * inner, za), solid);
                int a3 = b.Corner(new Vec3(ax + nx * outer, ay + ny * outer, za), clear);
                int b0 = b.Corner(new Vec3(bx - nx * outer, by - ny * outer, zb), clear);
                int b1 = b.Corner(new Vec3(bx - nx * inner, by - ny * inner, zb), solid);
                int b2 = b.Corner(new Vec3(bx + nx * inner, by + ny * inner, zb), solid);
                int b3 = b.Corner(new Vec3(bx + nx * outer, by + ny * outer, zb), clear);
                b.Face(a0, b0, b1);
                b.Face(a0, b1, a1);
                b.Face(a1, b1, b2);
                b.Face(a1, b2, a2);
                b.Face(a2, b2, b3);
                b.Face(a2, b3, a3);
            }
            Part part = Place(old, b, "cprb_border_" + level + "_" + chunk, hash);
            _borders[level][chunk] = part;
            if (part.Entity != null && level != _shownLevel)
                part.Entity.SetVisibilityExcludeParents(false);
            _borderBuilt[level]++;
            ShowWantedWidth();
        }

        // switches to the wanted width once all of its parts are built, so the lines never disappear
        private void ShowWantedWidth()
        {
            if (_wantedLevel < 0 || _wantedLevel == _shownLevel || _borderBuilt[_wantedLevel] < BorderChunks * BorderChunks)
                return;
            if (_shownLevel >= 0)
                foreach (Part part in _borders[_shownLevel])
                    if (part != null && part.Entity != null)
                        part.Entity.SetVisibilityExcludeParents(false);
            foreach (Part part in _borders[_wantedLevel])
                if (part != null && part.Entity != null)
                    part.Entity.SetVisibilityExcludeParents(true);
            _shownLevel = _wantedLevel;
        }

        // ---- every frame ------------------------------------------------------------------------------

        // zoom fade of the fill, 0 (zoomed in) .. 1 (zoomed out)
        public void SetZoom(float zoom)
        {
            if (Math.Abs(zoom - _zoom) < 0.005f && !(zoom == 0f && _zoom != 0f) && !(zoom == 1f && _zoom != 1f))
                return;
            _zoom = zoom;
            bool visible = zoom > 0.01f;
            foreach (Part part in _fill)
            {
                if (part == null || part.Entity == null)
                    continue;
                ApplyZoom(part);
                if (visible != _fillVisible)
                    part.Entity.SetVisibilityExcludeParents(visible);
            }
            foreach (Part part in _fillAdd)
            {
                if (part != null && part.Entity != null && visible != _fillVisible)
                    part.Entity.SetVisibilityExcludeParents(visible);
            }
            _fillVisible = visible;
            UpdateAddFactor();
        }

        // light of the day at the campaign hour (1 by day, night_light at night): the added colour is not
        // lit by the scene, so it is dimmed by hand at night
        public void SetDaylight(float daylight)
        {
            if (Math.Abs(daylight - _daylight) < 0.01f)
                return;
            _daylight = daylight;
            UpdateAddFactor();
        }

        // mesh colour of the additive meshes: zoom fade (fill only) and daylight
        private void UpdateAddFactor()
        {
            uint fill = Grey(_zoom * _daylight), glow = Grey(_daylight);
            if (fill != _fillAddFactor)
            {
                _fillAddFactor = fill;
                foreach (Part part in _fillAdd)
                    if (part != null && part.Mesh != null)
                        part.Mesh.Color = fill;
            }
            if (glow != _glowAddFactor)
            {
                _glowAddFactor = glow;
                foreach (Part part in _glowAdd)
                    if (part != null && part.Mesh != null)
                        part.Mesh.Color = glow;
            }
        }

        private static uint Grey(float v)
        {
            uint f = (uint)Math.Round(TerritoryBuilder.Saturate(v) * 255f);
            return 0xFF000000u | (f << 16) | (f << 8) | f;
        }

        // A = 0.9 - (1 - zoom) * opacity lifts every multiplier by (1 - zoom) * opacity, which is at least
        // the fill's own darkening, so zoom 0 is neutral; vector_argument.y is placed to give that A
        private void ApplyZoom(Part part)
        {
            if (part == null || part.Mesh == null)
                return;
            float a = FullA - (1f - _zoom) * Math.Max(0f, _opacity);
            float offset = 0f;
            if (a < FullA - 1e-3f)
            {
                double s = Math.Max(0.0, Math.Min(1.0, a / FullA));
                double t = 0.5 - Math.Sin(Math.Asin(1.0 - 2.0 * s) / 3.0);   // inverse of smoothstep
                offset = (float)(200.0 * (1.0 - 0.8 * t));
            }
            part.Mesh.SetVectorArgument(VisibleDistance, part.AvgZ + offset, 0f, 0f);
        }

        // shows the prebuilt ribbon width closest to the wanted half-width, building it first if needed
        public void SetBorderWidth(float halfWidth)
        {
            int best = 0;
            for (int l = 1; l < BorderLevels.Length; l++)
            {
                if (Math.Abs(Math.Log(BorderLevels[l] / halfWidth)) < Math.Abs(Math.Log(BorderLevels[best] / halfWidth)))
                    best = l;
            }
            if (best == _wantedLevel || _piecesByChunk == null)
                return;
            _wantedLevel = best;
            if (!_borderQueued[best])
                QueueBorders(best);
            ShowWantedWidth();
        }

        public string Stats()
        {
            int fill = 0, glow = 0, border = 0, glowCovered = 0, fillCovered = 0;
            double glowStrength = 0, fillStrength = 0;
            foreach (Part p in _fill) if (p != null) { fill += p.Faces; fillCovered += p.Covered; fillStrength += p.Strength; }
            foreach (Part p in _fillAdd) if (p != null) fill += p.Faces;
            foreach (Part p in _glow) if (p != null) { glow += p.Faces; glowCovered += p.Covered; glowStrength += p.Strength; }
            foreach (Part p in _glowAdd) if (p != null) glow += p.Faces;
            if (_shownLevel >= 0)
                foreach (Part p in _borders[_shownLevel]) if (p != null) border += p.Faces;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "faces fill {0} ({1} coloured corners, mean strength {2:0.00}), glow {3} ({4} coloured corners, mean strength {5:0.00}), borders {6} (width {7}), additive {8}, queued jobs {9}, terrain samples {10}",
                fill, fillCovered, fillCovered > 0 ? fillStrength / fillCovered : 0, glow, glowCovered, glowCovered > 0 ? glowStrength / glowCovered : 0,
                border, _shownLevel, _add != null, _jobs.Count, FineSamples);
        }
    }
}
