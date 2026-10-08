using System;
using System.Collections.Generic;
using MiG29SMTTools;
using UnityEngine;

namespace MiG29SMTInstruments
{
    // The MiG-29SMT's left MFI-10 display as a flight display (PFD): attitude with a pitch ladder and bank scale, airspeed (km/h) and
    // altitude (m) boxes, a heading tape, and Mach, g, AoA and vertical speed. Drawn on the CPU into a small texture ~25 times a second
    // (a few hundred thousand pixel writes, well under a millisecond) and shown on the MiG29_ck_pfd quad that MiG29Cockpit builds.
    // Visual only, player's aircraft only (it reads the InstrumentDriver's FlightState).
    public class PfdDriver : MonoBehaviour
    {
        const int W = 240, H = 320;
        const float Rate = 25f, Ppd = 4.2f;                    // updates per second, pixels per degree of pitch
        static readonly Color32 Sky = new Color32(30, 104, 190, 255), Ground = new Color32(122, 82, 44, 255), White = new Color32(235, 235, 235, 255),
            Yellow = new Color32(250, 215, 40, 255), Green = new Color32(70, 230, 90, 255), Black = new Color32(6, 8, 10, 255), Grey = new Color32(70, 74, 80, 255),
            Box = new Color32(0, 0, 0, 255), Amber = new Color32(255, 160, 30, 255), Red = new Color32(235, 50, 40, 255);
        const int AttX0 = 0, AttX1 = W, AttY0 = 62, AttY1 = 290;   // attitude area; heading tape above, data rows below
        const int Cx = W / 2, Cy = 182;

        InstrumentDriver driver;
        Texture2D tex;
        Color32[] px;
        float next;

        public static bool Setup(InstrumentDriver driver, Transform cockpitRoot)
        {
            Renderer screen = null;
            foreach (var r in cockpitRoot.GetComponentsInChildren<Renderer>(true)) if (r.name == "MiG29_ck_pfd") { screen = r; break; }
            if (screen == null) return false;
            var pfd = driver.gameObject.AddComponent<PfdDriver>();
            pfd.driver = driver;
            pfd.Init(screen, cockpitRoot);
            return true;
        }

        void Init(Renderer screen, Transform cockpitRoot)
        {
            tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "MiG29SMT_pfd", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            px = new Color32[W * H];
            // an emissive copy of the game's tactical-screen material (as the cockpit lamps use) showing our texture
            Material baseMat = null;
            foreach (var r in cockpitRoot.GetComponentsInChildren<Renderer>(true)) if (r.name == "tacScreen") { baseMat = r.sharedMaterial; break; }
            Material m;
            if (baseMat != null)
            {
                m = new Material(baseMat) { name = "MiG29SMT_pfd" };
                m.SetTexture("_BaseMap", tex); m.SetTexture("_EmissionMap", tex);
                m.SetColor("_BaseColor", Color.white); m.SetColor("_EmissionColor", Color.white);
                m.EnableKeyword("_EMISSION");
                m.mainTextureScale = Vector2.one; m.mainTextureOffset = Vector2.zero;
            }
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) { enabled = false; return; }
                m = new Material(sh) { name = "MiG29SMT_pfd" };
                m.SetTexture("_BaseMap", tex);
            }
            screen.sharedMaterial = m;
            // the quad's UVs point at a black atlas cell (dark screen without the plugin): re-map them 0..1 across the screen
            var mf = screen.GetComponent<MeshFilter>();
            var mesh = Instantiate(mf.sharedMesh);
            var v = mesh.vertices; var n = mesh.normals;
            Vector3 normal = n.Length > 0 ? n[0].normalized : Vector3.back;
            Vector3 right = Vector3.ProjectOnPlane(Vector3.right, normal).normalized;
            Vector3 up = Vector3.Cross(right, normal).normalized;
            if (up.y < 0f) up = -up;
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (var p in v) { float a = Vector3.Dot(p, right), b = Vector3.Dot(p, up); minU = Mathf.Min(minU, a); maxU = Mathf.Max(maxU, a); minV = Mathf.Min(minV, b); maxV = Mathf.Max(maxV, b); }
            var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++)
                uv[i] = new Vector2(Mathf.InverseLerp(minU, maxU, Vector3.Dot(v[i], right)), Mathf.InverseLerp(minV, maxV, Vector3.Dot(v[i], up)));
            mesh.uv = uv;
            mf.sharedMesh = mesh;
            MiG29InstrumentsPlugin.Log.LogInfo($"MiG-29SMT flight display on {screen.name} ({W}x{H})");
        }

        void LateUpdate()
        {
            if (driver == null || tex == null || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f / Rate;
            try { Draw(driver.State); tex.SetPixels32(px); tex.Apply(false); }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogError(e); enabled = false; }
        }

        // ------------------------------------------------------------------------------------------------ drawing
        void Draw(FlightState s)
        {
            float r = s.rollDeg * Mathf.Deg2Rad;   // right bank (+) turns the outside world, and the horizon, counter-clockwise on the display
            float sin = Mathf.Sin(r), cos = Mathf.Cos(r);
            float off = s.pitchDeg * Ppd;
            // sky / ground: height of each pixel above the horizon along the display's rolled 'up'
            for (int y = 0; y < H; y++)
            {
                int row = y * W;
                bool att = y >= AttY0 && y < AttY1;
                for (int x = 0; x < W; x++)
                {
                    if (!att) { px[row + x] = Black; continue; }
                    float h = -(x - Cx) * sin + (y - Cy) * cos + off;
                    px[row + x] = h > 0f ? Sky : Ground;
                }
            }
            // horizon and pitch ladder (every 5 deg, longer lines every 10 with numbers)
            var dir = new Vector2(cos, sin); var nrm = new Vector2(-sin, cos);
            for (int p = -90; p <= 90; p += 5)
            {
                float d = (p - s.pitchDeg) * Ppd;
                if (Mathf.Abs(d) > 120f) continue;
                var c = new Vector2(Cx, Cy) + nrm * d;
                float half = p == 0 ? 130f : p % 10 == 0 ? 34f : 16f;
                if (p == 0) Line(c - dir * half, c + dir * half, White, 2);
                else
                {
                    float gap = p % 10 == 0 ? 12f : 6f;
                    Line(c - dir * half, c - dir * gap, White, 1); Line(c + dir * gap, c + dir * half, White, 1);
                    var tip = p > 0 ? -nrm * 5f : nrm * 5f;     // end ticks point toward the horizon
                    Line(c - dir * half, c - dir * half + tip, White, 1); Line(c + dir * half, c + dir * half + tip, White, 1);
                    if (p % 10 == 0)
                    {
                        string t = Mathf.Abs(p).ToString();
                        var at = c + dir * (half + 4f);
                        Text((int)at.x, (int)at.y - 3, t, White, 1, clipAtt: true);
                        at = c - dir * (half + 4f + t.Length * 6f);
                        Text((int)at.x, (int)at.y - 3, t, White, 1, clipAtt: true);
                    }
                }
            }
            // bank scale: ticks at 10, 20, 30, 45, 60 deg on an arc, pointer follows the roll
            for (int k = -6; k <= 6; k++)
            {
                if (k == 0) continue;
                float a = (Mathf.Abs(k) <= 3 ? k * 10f : Mathf.Sign(k) * (Mathf.Abs(k) == 4 ? 45f : Mathf.Abs(k) == 5 ? 60f : 90f)) * Mathf.Deg2Rad;
                if (Mathf.Abs(k) == 6) continue;
                var p0 = new Vector2(Cx + Mathf.Sin(a) * 96f, Cy + Mathf.Cos(a) * 96f);
                var p1 = new Vector2(Cx + Mathf.Sin(a) * (Mathf.Abs(k) == 3 ? 106f : 102f), Cy + Mathf.Cos(a) * (Mathf.Abs(k) == 3 ? 106f : 102f));
                Line(p0, p1, White, 1);
            }
            Tri(new Vector2(Cx, Cy + 97f), new Vector2(Cx - 5, Cy + 107f), new Vector2(Cx + 5, Cy + 107f), White);
            var pa = new Vector2(Cx - Mathf.Sin(r) * 94f, Cy + Mathf.Cos(r) * 94f);
            Tri(pa, pa + new Vector2(-5, -9).Rotate(r), pa + new Vector2(5, -9).Rotate(r), Yellow);

            // aircraft symbol
            Line(new Vector2(Cx - 46, Cy), new Vector2(Cx - 16, Cy), Yellow, 3);
            Line(new Vector2(Cx - 16, Cy), new Vector2(Cx - 16, Cy - 8), Yellow, 3);
            Line(new Vector2(Cx + 16, Cy), new Vector2(Cx + 46, Cy), Yellow, 3);
            Line(new Vector2(Cx + 16, Cy), new Vector2(Cx + 16, Cy - 8), Yellow, 3);
            Fill(Cx - 2, Cy - 2, 5, 5, Yellow);

            // airspeed (left) and altitude (right) boxes, with a short tape of ticks
            ValueBox(2, Cy - 11, 54, 22, Mathf.RoundToInt(s.iasKmh).ToString(), s.iasKmh > 1450f ? Red : White);
            Text(4, Cy + 14, "KM/H", Green, 1);
            ValueBox(W - 60, Cy - 11, 58, 22, Mathf.RoundToInt(s.altM).ToString(), s.radAltM < 60f && s.airborne && !s.gearDown ? Red : White);
            Text(W - 58, Cy + 14, "M", Green, 1);
            if (s.radAltM < 1500f && s.airborne) { Text(W - 58, Cy - 26, "R" + Mathf.RoundToInt(s.radAltM), Green, 1); }

            // heading tape across the top
            Fill(0, AttY1, W, H - AttY1, Black);
            float hdg = Mathf.Repeat(s.headingDeg, 360f);
            for (int dDeg = -30; dDeg <= 30; dDeg++)
            {
                float a = Mathf.Repeat(Mathf.Round(hdg) + dDeg, 360f);
                int x = Cx + Mathf.RoundToInt((a - hdg + (a - hdg > 180 ? -360 : a - hdg < -180 ? 360 : 0)) * 3.6f);
                if (x < 2 || x > W - 3) continue;
                if (Mathf.RoundToInt(a) % 10 == 0)
                {
                    Fill(x, H - 12, 1, 8, White);
                    if (Mathf.RoundToInt(a) % 30 == 0) { string t = (Mathf.RoundToInt(a) / 10).ToString("00"); Text(x - t.Length * 3, H - 23, t, White, 1); }
                }
                else if (Mathf.RoundToInt(a) % 5 == 0) Fill(x, H - 8, 1, 4, White);
            }
            ValueBox(Cx - 18, AttY1 + 2, 36, 13, Mathf.RoundToInt(hdg).ToString("000"), Green);

            // data rows under the attitude
            Fill(0, 0, W, AttY0, Black);
            Text(6, 44, "M", Green, 2); Text(22, 44, s.mach.ToString("0.00"), White, 2);
            Text(96, 44, "G", Green, 2); Text(112, 44, s.g.ToString("0.0"), s.g > 8.5f || s.g < -2.5f ? Red : White, 2);
            Text(170, 44, "A", Green, 2); Text(186, 44, Mathf.RoundToInt(s.aoaDeg).ToString(), s.aoaDeg > 24f && s.airborne ? Red : White, 2);
            Text(6, 18, "VS", Green, 2); Text(36, 18, (s.vsMs >= 0 ? "+" : "") + Mathf.RoundToInt(s.vsMs), White, 2);
            Text(130, 18, "T", Green, 2); Text(146, 18, s.hours.ToString("00") + ":" + s.minutes.ToString("00"), White, 2);
            if (s.gearMoving || s.gearDown) Text(206, 2, s.gearMoving ? "GEAR" : "", Amber, 1);
            if (s.missileIncoming && s.blink) Text(Cx - 30, AttY0 + 6, "MISSILE", Red, 2);
        }

        void ValueBox(int x, int y, int w, int h, string t, Color32 col)
        {
            Fill(x, y, w, h, Box);
            Fill(x, y, w, 1, White); Fill(x, y + h - 1, w, 1, White); Fill(x, y, 1, h, White); Fill(x + w - 1, y, 1, h, White);
            int scale = h >= 20 ? 2 : 1;
            int tw = t.Length * 6 * scale - scale;
            Text(x + (w - tw) / 2, y + (h - 7 * scale) / 2, t, col, scale);
        }

        void Fill(int x, int y, int w, int h, Color32 c)
        {
            int x0 = Mathf.Max(0, x), x1 = Mathf.Min(W, x + w), y0 = Mathf.Max(0, y), y1 = Mathf.Min(H, y + h);
            for (int yy = y0; yy < y1; yy++) { int row = yy * W; for (int xx = x0; xx < x1; xx++) px[row + xx] = c; }
        }

        void Plot(int x, int y, Color32 c, bool clipAtt = true)
        {
            if (x < 0 || x >= W || y < 0 || y >= H) return;
            if (clipAtt && (y < AttY0 || y >= AttY1)) return;
            px[y * W + x] = c;
        }

        void Line(Vector2 a, Vector2 b, Color32 c, int thick)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len * 1.5f));
            var nrm = len > 0 ? new Vector2(-(b.y - a.y), b.x - a.x) / len : Vector2.zero;
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                for (int t = 0; t < thick; t++)
                {
                    var q = p + nrm * (t - (thick - 1) * 0.5f);
                    Plot(Mathf.RoundToInt(q.x), Mathf.RoundToInt(q.y), c);
                }
            }
        }

        void Tri(Vector2 a, Vector2 b, Vector2 c, Color32 col)
        {
            int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), x1 = Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
            int y0 = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), y1 = Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
            float Edge(Vector2 p, Vector2 q, Vector2 r) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
            float area = Edge(a, b, c);
            if (Mathf.Abs(area) < 1e-3f) return;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = Edge(b, c, p) / area, w1 = Edge(c, a, p) / area, w2 = Edge(a, b, p) / area;
                    if (w0 >= 0 && w1 >= 0 && w2 >= 0) Plot(x, y, col);
                }
        }

        // 5x7 font, rows top to bottom, '#' = lit
        static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
        {
            ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
            ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
            ['3'] = new[] { "####.", "....#", "....#", ".###.", "....#", "....#", "####." },
            ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
            ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
            ['6'] = new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." },
            ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
            ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
            ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." },
            ['-'] = new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." },
            ['+'] = new[] { ".....", "..#..", "..#..", "#####", "..#..", "..#..", "....." },
            ['.'] = new[] { ".....", ".....", ".....", ".....", ".....", ".##..", ".##.." },
            [':'] = new[] { ".....", ".##..", ".##..", ".....", ".##..", ".##..", "....." },
            ['/'] = new[] { "....#", "....#", "...#.", "..#..", ".#...", "#....", "#...." },
            ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
            ['G'] = new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###." },
            ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['I'] = new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
            ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
            ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
            ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
            ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
            ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
        };

        // text with its bottom-left at (x, y)
        void Text(int x, int y, string t, Color32 c, int scale, bool clipAtt = false)
        {
            foreach (var ch in t)
            {
                if (Font.TryGetValue(char.ToUpperInvariant(ch), out var g))
                    for (int row = 0; row < 7; row++)
                        for (int col = 0; col < 5; col++)
                            if (g[row][col] == '#')
                                for (int sy = 0; sy < scale; sy++)
                                    for (int sx = 0; sx < scale; sx++)
                                        Plot(x + col * scale + sx, y + (6 - row) * scale + sy, c, clipAtt);
                x += 6 * scale;
            }
        }
    }

    static class Vec2Ext
    {
        public static Vector2 Rotate(this Vector2 v, float rad)
        {
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }
}
