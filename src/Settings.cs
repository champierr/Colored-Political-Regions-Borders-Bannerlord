using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CPRB.Bannerlord
{
    // Settings file: Documents\Mount and Blade II Bannerlord\Configs\ColoredPoliticalRegionsBorders.txt
    // One "key = value" per line. Only "opacity" is meant for players (Ctrl + 1 changes it in game);
    // the rest tunes the look. Sizes marked "x spacing" are multiples of the typical distance between
    // neighbouring settlements, so the look stays the same on maps of any size.
    internal sealed class Settings
    {
        public static string ConfigDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Mount and Blade II Bannerlord", "Configs");

        public static string FilePath => Path.Combine(ConfigDirectory, "ColoredPoliticalRegionsBorders.txt");

        public int Opacity = 60;                 // fill strength in percent (Ctrl + 1 in game)
        public float FadeNear = 4f;              // camera distance with no fill, x spacing
        public float FadeFar = 10f;              // camera distance with full fill, x spacing
        public float BorderPixels = 1.6f;        // border half-width in screen pixels
        public float BorderMin = 0.0053f;        // ...but never thinner than this, x spacing
        public float BorderAlpha = 0.9f;
        public float GlowWidth = 0.16f;          // country-coloured glow inside each border, x spacing
        public float GlowAlpha = 0.65f;          // at the default opacity, follows the opacity setting
        public float GlowPower = 1f;             // glow fall-off: 1 = even fade, 2 = quickly fading
        public float Warp = 0.095f;              // how wavy the borders are, x spacing (0 = straight)
        public float Brighten = 0.8f;            // faction colours are lifted to at least this brightness (0-1)
        public int TextureSize = 2048;           // fill and glow texture size (1024, 2048 or 4096)
        public string MeshMaterial = "editor_map_border";   // copied for the overlay meshes (a multiplying material)
        public float TintGamma = 2.2f;           // faction colours (sRGB) to the linear multipliers used on the terrain
        public float FillLight = 0.65f;          // brightness of the added fill colour (0 = the fill only tints)
        public float NightLight = 0.35f;         // the added fill colour is dimmed to this at night
        public float FillLift = 1f;              // fill floats this far above the highest nearby ground (map units)
        public float NearLift = 0.12f;           // glow and borders float this far above the ground
        public int DebugLog = 0;                 // 1 = log the camera distance and mesh counts every 10 seconds
        public int FastMeshes = 1;               // 1 = build meshes with MeshBuilder, 0 = corner by corner

        public const int DefaultOpacity = 60;
        private const int CurrentVersion = 2;

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath))
                {
                    s.Save();
                    return s;
                }
                var values = new List<KeyValuePair<string, string>>();
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    values.Add(new KeyValuePair<string, string>(line.Substring(0, eq).Trim().ToLowerInvariant(), StripComment(line.Substring(eq + 1).Trim())));
                }
                // version 1 files wrote every look value as a fixed line: keep only the player's opacity
                bool current = values.Exists(kv => kv.Key == "settings_version" && F(kv.Value, 0f) >= CurrentVersion);
                foreach (var kv in values)
                {
                    if (current || kv.Key == "opacity")
                        s.Set(kv.Key, kv.Value);
                }
            }
            catch (Exception e)
            {
                Log.Error("Settings.Load", e);
            }
            s.Opacity = Math.Max(0, Math.Min(100, s.Opacity));
            s.TextureSize = s.TextureSize >= 4096 ? 4096 : s.TextureSize <= 1024 ? 1024 : 2048;
            // write it back so settings added by newer versions show up in the file
            s.Save();
            return s;
        }

        private void Set(string key, string value)
        {
            switch (key)
            {
                case "opacity": Opacity = (int)Math.Round(F(value, Opacity)); break;
                case "fade_near": FadeNear = F(value, FadeNear); break;
                case "fade_far": FadeFar = F(value, FadeFar); break;
                case "border_px": BorderPixels = F(value, BorderPixels); break;
                case "border_min": BorderMin = F(value, BorderMin); break;
                case "border_alpha": BorderAlpha = F(value, BorderAlpha); break;
                case "glow_width": GlowWidth = F(value, GlowWidth); break;
                case "glow_alpha": GlowAlpha = F(value, GlowAlpha); break;
                case "glow_power": GlowPower = F(value, GlowPower); break;
                case "warp": Warp = F(value, Warp); break;
                case "brighten": Brighten = F(value, Brighten); break;
                case "texture_size": TextureSize = (int)F(value, TextureSize); break;
                case "mesh_material": MeshMaterial = value; break;
                case "tint_gamma": TintGamma = F(value, TintGamma); break;
                case "fill_light": FillLight = F(value, FillLight); break;
                case "night_light": NightLight = F(value, NightLight); break;
                case "fill_lift": FillLift = F(value, FillLift); break;
                case "near_lift": NearLift = F(value, NearLift); break;
                case "debug_log": DebugLog = (int)F(value, DebugLog); break;
                case "fast_meshes": FastMeshes = (int)F(value, FastMeshes); break;
            }
        }

        private static float F(string value, float fallback)
        {
            float v;
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        private static void Line(StringBuilder b, string key, string value, string fallback, string comment)
        {
            string text = (value == fallback ? "# " : "") + key + " = " + value;
            if (comment.Length > 0)
                text = text.PadRight(32) + "# " + comment;
            b.AppendLine(text);
        }

        private static string S(float v)
        {
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        public void Save()
        {
            try
            {
                var b = new StringBuilder();
                b.AppendLine("# Colored Political Regions & Borders - settings");
                b.AppendLine("# Map colour strength in percent (0-100). Change it in game with Ctrl + 1 on the world map.");
                b.AppendLine("opacity = " + Opacity);
                b.AppendLine();
                b.AppendLine("settings_version = " + CurrentVersion);
                b.AppendLine();
                b.AppendLine("# Advanced look settings. A line starting with # shows the default; remove the # and change");
                b.AppendLine("# the value to use your own. \"x spacing\" = multiple of the usual distance between neighbouring");
                b.AppendLine("# settlements.");
                var d = new Settings();
                Line(b, "fade_near", S(FadeNear), S(d.FadeNear), "camera distance with no fill (x spacing)");
                Line(b, "fade_far", S(FadeFar), S(d.FadeFar), "camera distance with full fill (x spacing)");
                Line(b, "border_px", S(BorderPixels), S(d.BorderPixels), "border half-width in screen pixels");
                Line(b, "border_min", S(BorderMin), S(d.BorderMin), "minimum border half-width (x spacing)");
                Line(b, "border_alpha", S(BorderAlpha), S(d.BorderAlpha), "");
                Line(b, "glow_width", S(GlowWidth), S(d.GlowWidth), "glow inside each border (x spacing)");
                Line(b, "glow_alpha", S(GlowAlpha), S(d.GlowAlpha), "");
                Line(b, "glow_power", S(GlowPower), S(d.GlowPower), "glow fall-off (1 = even, 2 = quick)");
                Line(b, "warp", S(Warp), S(d.Warp), "how wavy the borders are (x spacing, 0 = straight)");
                Line(b, "brighten", S(Brighten), S(d.Brighten), "faction colours lifted to at least this brightness (0-1)");
                Line(b, "texture_size", TextureSize.ToString(), d.TextureSize.ToString(), "1024, 2048 or 4096");
                Line(b, "mesh_material", MeshMaterial, d.MeshMaterial, "");
                Line(b, "tint_gamma", S(TintGamma), S(d.TintGamma), "faction colour to terrain multiplier (1 = softer)");
                Line(b, "fill_light", S(FillLight), S(d.FillLight), "brightness of the fill colour (0 = tint only)");
                Line(b, "night_light", S(NightLight), S(d.NightLight), "fill colour brightness at night");
                Line(b, "fill_lift", S(FillLift), S(d.FillLift), "fill height above the ground (map units)");
                Line(b, "near_lift", S(NearLift), S(d.NearLift), "glow and border height above the ground");
                Line(b, "debug_log", DebugLog.ToString(), d.DebugLog.ToString(), "1 = more details in the log");
                Line(b, "fast_meshes", FastMeshes.ToString(), d.FastMeshes.ToString(), "0 = slower, older way of building meshes");
                Directory.CreateDirectory(ConfigDirectory);
                File.WriteAllText(FilePath, b.ToString());
            }
            catch (Exception e)
            {
                Log.Error("Settings.Save", e);
            }
        }

        // the comment after a value ("1.6  # ...") is not part of it
        public static string StripComment(string value)
        {
            int hash = value.IndexOf('#');
            return hash >= 0 ? value.Substring(0, hash).Trim() : value;
        }
    }
}
