using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using SandBox;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace CPRB.Bannerlord
{
    // The political map on one campaign map scene: samples the land, works out the territories in the
    // background whenever ownership changes (TerritoryBuilder), and drives the meshes that show them
    // (OverlayMeshes): fill fading in when zoomed out, glow and borders at every zoom level.
    // Everything here runs on the main thread except TerritoryBuilder.Build, which gets a copy of its data.
    internal sealed class MapOverlay
    {
        private const float WarbandSpacing = 9.47f;   // median distance between neighbouring Warband centers
        private const int LandGridSize = 512;
        private const int LandSamplesPerFrame = 8192;
        private const float DefaultFov = 0.6981317f;  // MapCameraView's vertical field of view

        private readonly Settings _settings;
        private readonly MapScene _mapScene;
        private readonly Scene _scene;

        private float _minX, _minY, _maxX, _maxY;
        private float _spacing;
        private LandGrid _land;
        private byte[] _landFlags;               // bit 0: below the water level, bit 1: water terrain type
        private int _landNext;
        private bool _landReady;
        private float _waterLevel;
        private readonly int[] _terrainCounts = new int[32];

        private Task<TerritoryOutput> _build;
        private string _builtSignature, _buildingSignature;
        private float _signatureTimer;
        private int _builds;

        private OverlayMeshes _meshes;
        private bool _afterMission;
        private float _halfWidth;
        private float _time, _pendingSince;
        private const double MeshBudgetMs = 5.0;   // mesh building per frame

        private float _minDistance = float.MaxValue, _maxDistance, _distanceLogTimer;

        public MapOverlay(Settings settings, MapScene mapScene)
        {
            _settings = settings;
            _mapScene = mapScene;
            _scene = mapScene.Scene;
        }

        public Scene Scene => _scene;

        // called every frame of a mission instead of Tick
        public void OnMission()
        {
            _afterMission = true;
        }

        public void Tick(float dt)
        {
            _time += dt;
            if (_afterMission)
            {
                _afterMission = false;
                if (_meshes != null)
                    _meshes.Recover();
            }
            if (_spacing <= 0f && !Layout())
                return;
            if (!_landReady)
            {
                SampleLand();
                return;
            }

            if (_build != null && _build.IsCompleted)
            {
                var build = _build;
                _build = null;
                if (build.IsFaulted)
                    Log.Error("TerritoryBuilder", build.Exception);
                else
                    Apply(build.Result);
            }

            _signatureTimer -= dt;
            if (_build == null && _signatureTimer <= 0f)
            {
                _signatureTimer = 1f;
                StartBuildIfChanged();
            }

            PumpMeshes();
            UpdateLook(dt);
        }

        // ---- map layout -----------------------------------------------------------------------

        private static bool UsesSite(Settlement s)
        {
            return s != null && (s.IsTown || s.IsCastle || s.IsVillage) && s.MapFaction != null;
        }

        private bool Layout()
        {
            var xs = new List<float>();
            var ys = new List<float>();
            foreach (Settlement s in Settlement.All)
            {
                if (!UsesSite(s))
                    continue;
                Vec2 p = s.GetPosition2D;
                xs.Add(p.x);
                ys.Add(p.y);
            }
            if (xs.Count < 2)
                return false;

            var nearest = new List<float>(xs.Count);
            for (int i = 0; i < xs.Count; i++)
            {
                float best = float.MaxValue;
                for (int j = 0; j < xs.Count; j++)
                {
                    if (i == j)
                        continue;
                    float dx = xs[i] - xs[j], dy = ys[i] - ys[j], d = dx * dx + dy * dy;
                    if (d < best && d > 1e-6f)
                        best = d;
                }
                if (best < float.MaxValue)
                    nearest.Add((float)Math.Sqrt(best));
            }
            nearest.Sort();
            _spacing = Math.Max(1f, nearest[nearest.Count / 2]);

            float margin = 8f * _spacing;
            float sMinX = float.MaxValue, sMinY = float.MaxValue, sMaxX = float.MinValue, sMaxY = float.MinValue;
            for (int i = 0; i < xs.Count; i++)
            {
                sMinX = Math.Min(sMinX, xs[i]);
                sMinY = Math.Min(sMinY, ys[i]);
                sMaxX = Math.Max(sMaxX, xs[i]);
                sMaxY = Math.Max(sMaxY, ys[i]);
            }
            _minX = sMinX - margin;
            _minY = sMinY - margin;
            _maxX = sMaxX + margin;
            _maxY = sMaxY + margin;
            Vec2 bMin, bMax;
            float bHeight;
            _mapScene.GetMapBorders(out bMin, out bMax, out bHeight);
            if (bMax.x - bMin.x > 10f && bMax.y - bMin.y > 10f)
            {
                _minX = Math.Max(_minX, bMin.x);
                _minY = Math.Max(_minY, bMin.y);
                _maxX = Math.Min(_maxX, bMax.x);
                _maxY = Math.Min(_maxY, bMax.y);
            }
            Vec2 terrain = _mapScene.GetTerrainSize();

            _waterLevel = _scene.GetWaterLevel();
            _land = new LandGrid
            {
                Width = LandGridSize,
                Height = LandGridSize,
                MinX = _minX,
                MinY = _minY,
                StepX = (_maxX - _minX) / (LandGridSize - 1),
                StepY = (_maxY - _minY) / (LandGridSize - 1),
                Land = new float[LandGridSize * LandGridSize],
                Ground = new float[LandGridSize * LandGridSize],
            };
            _landFlags = new byte[LandGridSize * LandGridSize];
            Log.Info(string.Format(CultureInfo.InvariantCulture,
                "map: {0} settlements, spacing {1:0.00}, area ({2:0},{3:0})-({4:0},{5:0}), map borders ({6:0},{7:0})-({8:0},{9:0}), terrain {10:0}x{11:0}, water level {12:0.00}",
                xs.Count, _spacing, _minX, _minY, _maxX, _maxY, bMin.x, bMin.y, bMax.x, bMax.y, terrain.x, terrain.y, _waterLevel));
            return true;
        }

        // A few thousand terrain samples per frame, so loading the map does not stall.
        private void SampleLand()
        {
            int total = LandGridSize * LandGridSize;
            int end = Math.Min(total, _landNext + LandSamplesPerFrame);
            bool useWaterLevel = _waterLevel > -500f;
            for (int i = _landNext; i < end; i++)
            {
                var pos = new Vec2(_land.MinX + (i % LandGridSize) * _land.StepX, _land.MinY + (i / LandGridSize) * _land.StepY);
                float h = _scene.GetTerrainHeight(pos, true);
                _land.Ground[i] = h;
                byte flags = 0;
                if (useWaterLevel && h < _waterLevel - 0.25f)
                    flags |= 1;
                PathFaceRecord face = _mapScene.GetFaceIndex(new CampaignVec2(pos, true));
                if (face.IsValid())
                {
                    int type = face.FaceGroupIndex;
                    if (type >= 0 && type < _terrainCounts.Length)
                        _terrainCounts[type]++;
                    if (type == (int)TerrainType.Lake || type == (int)TerrainType.Water ||
                        type == (int)TerrainType.CoastalSea || type == (int)TerrainType.OpenSea)
                        flags |= 2;
                }
                else
                {
                    _terrainCounts[0]++;
                }
                _landFlags[i] = flags;
            }
            _landNext = end;
            if (_landNext < total)
                return;

            int below = 0, typed = 0;
            for (int i = 0; i < total; i++)
            {
                if ((_landFlags[i] & 1) != 0) below++;
                if ((_landFlags[i] & 2) != 0) typed++;
            }
            // a water level that would drown most of the map is not a real sea level
            bool trustLevel = below < total * 0.6f;
            for (int i = 0; i < total; i++)
            {
                bool water = (_landFlags[i] & 2) != 0 || (trustLevel && (_landFlags[i] & 1) != 0);
                _land.Land[i] = water ? 0f : 1f;
            }
            _landFlags = null;
            _landReady = true;
            var types = new StringBuilder();
            for (int t = 0; t < _terrainCounts.Length; t++)
            {
                if (_terrainCounts[t] > 0)
                    types.Append(t == 0 ? "none" : ((TerrainType)t).ToString()).Append('=').Append(_terrainCounts[t]).Append(' ');
            }
            float low = float.MaxValue, high = float.MinValue;
            foreach (float h in _land.Ground)
            {
                low = Math.Min(low, h);
                high = Math.Max(high, h);
            }
            Log.Info(string.Format(CultureInfo.InvariantCulture,
                "land: {0} samples, below water level {1}, water terrain {2}, water level used: {3}; ground {4:0.00}..{5:0.00}; terrain types: {6}",
                total, below, typed, trustLevel, low, high, types));
        }

        // ---- ownership -> background build ------------------------------------------------------

        private void StartBuildIfChanged()
        {
            var sx = new List<float>();
            var sy = new List<float>();
            var so = new List<int>();
            var owners = new Dictionary<IFaction, int>();
            var colors = new List<uint>();
            ulong hash = 1469598103934665603UL;
            foreach (Settlement s in Settlement.All)
            {
                if (!UsesSite(s))
                    continue;
                IFaction f = s.MapFaction;
                int index;
                if (!owners.TryGetValue(f, out index))
                {
                    index = owners.Count;
                    owners[f] = index;
                    // the colour of the faction's map labels, lifted when it is too dark to read on terrain
                    // (a faction without a colour comes out grey)
                    colors.Add(TerritoryBuilder.Brighten(f.Color & 0xFFFFFF, _settings.Brighten));
                }
                Vec2 p = s.GetPosition2D;
                sx.Add(p.x);
                sy.Add(p.y);
                so.Add(index);
                hash = (hash ^ (uint)index) * 1099511628211UL;
                hash = (hash ^ colors[index]) * 1099511628211UL;
            }
            if (sx.Count < 2)
                return;
            string signature = hash.ToString("X16") + ":" + sx.Count + ":" + _settings.TextureSize;   // look settings are read once per game
            if (signature == _builtSignature)
                return;

            var input = new TerritoryInput
            {
                SiteX = sx.ToArray(),
                SiteY = sy.ToArray(),
                SiteOwner = so.ToArray(),
                OwnerColor = colors.ToArray(),
                MinX = _minX,
                MinY = _minY,
                MaxX = _maxX,
                MaxY = _maxY,
                Size = _settings.TextureSize,
                Land = _land,
                Warp = _settings.Warp * _spacing,
                PatternScale = _spacing / WarbandSpacing,
                GlowWidth = Math.Max(0.01f, _settings.GlowWidth * _spacing),
                GlowPower = Math.Max(0.1f, _settings.GlowPower),
                ReachStart = 6f * _spacing,
                ReachEnd = 9f * _spacing,
                MaxSegment = 0.25f * _spacing,
            };
            _buildingSignature = signature;
            Log.Info("build " + (_builds + 1) + ": " + sx.Count + " settlements, " + owners.Count + " owners");
            _build = Task.Run(() => TerritoryBuilder.Build(input));
        }

        private float Opacity => _settings.Opacity / 100f;

        private float GlowStrength => _settings.GlowAlpha * TerritoryBuilder.Saturate(Opacity / (Settings.DefaultOpacity / 100f));

        private void Apply(TerritoryOutput result)
        {
            _builds++;
            if (_meshes == null)
            {
                _meshes = new OverlayMeshes(_scene, _land, _minX, _minY, _maxX, _maxY, _settings);
                if (!_meshes.CreateMaterial())
                {
                    SubModule.Disable("material " + _settings.MeshMaterial + " not found");
                    return;
                }
            }
            _meshes.Update(result, Opacity, GlowStrength, _settings.BorderAlpha);
            _builtSignature = _buildingSignature;
            _pendingSince = _time;
            Log.Info(string.Format(CultureInfo.InvariantCulture, "build {0}: territory worked out in {1} ms, {2} border segments; meshes queued",
                _builds, result.Milliseconds, result.Borders.Count));
        }

        // after Apply the meshes are built a few milliseconds per frame
        private void PumpMeshes()
        {
            if (_meshes == null)
                return;
            bool busy = _meshes.Pending > 0;
            _meshes.Pump(MeshBudgetMs);
            if (busy && _meshes.Pending == 0 && _pendingSince >= 0f)
            {
                Log.Info(string.Format(CultureInfo.InvariantCulture, "map ready {0:0.0} s after build {1}: {2}", _time - _pendingSince, _builds, _meshes.Stats()));
                _pendingSince = -1f;
                SubModule.Announce();
            }
        }

        public static string Describe(Material m, string name)
        {
            var b = new StringBuilder("material " + name + ":");
            try
            {
                Shader shader = m.GetShader();
                b.Append(" shader=").Append(shader != null ? shader.Name : "?");
                b.Append(" shaderflags=0x").Append(m.GetShaderFlags().ToString("X"));
                b.Append(" blend=").Append(m.GetAlphaBlendMode());
                foreach (Material.MBTextureType t in Enum.GetValues(typeof(Material.MBTextureType)))
                {
                    Texture tex = m.GetTexture(t);
                    if (tex != null)
                        b.Append(' ').Append(t).Append('=').Append(tex.Name);
                }
            }
            catch (Exception e)
            {
                b.Append(" (").Append(e.Message).Append(')');
            }
            return b.ToString();
        }

        // ---- every frame ------------------------------------------------------------------------

        private void UpdateLook(float dt)
        {
            if (_meshes == null)
                return;
            MapScreen screen = MapScreen.Instance;
            MapCameraView view = screen != null ? screen.MapCameraView : null;
            if (view == null)
                return;
            float distance = view.CameraDistance;
            float zoom = TerritoryBuilder.SmoothStep(_settings.FadeNear * _spacing, _settings.FadeFar * _spacing, distance);
            _meshes.SetZoom(zoom);
            _meshes.SetDaylight(Daylight());

            // border width: a fixed number of screen pixels, but never thinner than BorderMin
            float fov = DefaultFov;
            try
            {
                float f = view.Camera != null ? view.Camera.GetFovVertical() : DefaultFov;
                if (f > 0.05f && f < 3f)
                    fov = f;
            }
            catch
            {
            }
            float screenHeight = Math.Max(240f, Screen.RealScreenResolutionHeight);
            float perPixel = 2f * (distance + 2f) * (float)Math.Tan(fov * 0.5f) / screenHeight;
            _halfWidth = Math.Max(_settings.BorderPixels * perPixel, _settings.BorderMin * _spacing);
            _meshes.SetBorderWidth(_halfWidth);

            if (_settings.DebugLog != 0)
            {
                _minDistance = Math.Min(_minDistance, distance);
                _maxDistance = Math.Max(_maxDistance, distance);
                _distanceLogTimer -= dt;
                if (_distanceLogTimer <= 0f)
                {
                    _distanceLogTimer = 10f;
                    Log.Info(string.Format(CultureInfo.InvariantCulture,
                        "camera distance {0:0.0} (seen {1:0.0}..{2:0.0}), fill zoom {3:0.00}, border half-width {4:0.000}; {5}",
                        distance, _minDistance, _maxDistance, zoom, _halfWidth, _meshes.Stats()));
                }
            }
        }

        // 1 by day, night_light at night, with an hour or so of dusk and dawn (campaign day: 2:00 - 22:00)
        private float Daylight()
        {
            float hour = CampaignTime.Now.CurrentHourInDay;
            float day = TerritoryBuilder.SmoothStep(1f, 4f, hour) * (1f - TerritoryBuilder.SmoothStep(20f, 23f, hour));
            return _settings.NightLight + (1f - _settings.NightLight) * day;
        }

        // called when the opacity changes (Ctrl + 1): the fill and glow colours are rebuilt
        public void Refresh()
        {
            if (_meshes != null)
                _meshes.Restyle(Opacity, GlowStrength);
        }
    }
}
