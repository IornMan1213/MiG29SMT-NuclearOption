using System;
using System.Collections.Generic;
using MiG29SMTTools;
using UnityEngine;

namespace MiG29SMTInstruments
{
    // The MiG-29SMT's plugin-drawn screens (quads MiG29_ck_<name> that MiG29Cockpit builds, dark without the plugin):
    //   pfd   left MFI-10: flight display (attitude, pitch ladder, bank scale, airspeed, altitude, heading tape, Mach, g, AoA, VS, time)
    //   eng   right MFI-10, under the tactical display: engines (RPM, EGT), fuel, hydraulics and the caution messages
    //   rwr   radar warning display (L-150 style): threats around the aircraft by bearing and type, inbound missiles, lock
    //   data  small readout: fuel, clock, drop tanks
    // Each is drawn on the CPU into a small texture ~20 times a second and shown with an emissive copy of the game's tactical-screen
    // material. Visual only, player's aircraft only (reads the InstrumentDriver's FlightState).
    public class ScreenDriver : MonoBehaviour
    {
        class Screen { public string name; public Canvas c; public Texture2D tex; public Action<Canvas, FlightState> draw; }

        const float Rate = 20f;
        InstrumentDriver driver;
        readonly List<Screen> screens = new List<Screen>();
        int turn;
        float next;

        public static bool Setup(InstrumentDriver driver, Transform cockpitRoot)
        {
            var found = new Dictionary<string, Renderer>();
            Material baseMat = null;
            foreach (var r in cockpitRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.StartsWith("MiG29_ck_")) found[r.name.Substring("MiG29_ck_".Length)] = r;
                if (r.name == "tacScreen") baseMat = r.sharedMaterial;
            }
            var sd = driver.gameObject.AddComponent<ScreenDriver>();
            sd.driver = driver;
            foreach (var (name, width, draw) in new (string, int, Action<Canvas, FlightState>)[]
                     { ("pfd", 240, Pages.Pfd), ("eng", 240, Pages.Eng), ("rwr", 160, Pages.Rwr), ("data", 190, Pages.Data) })
                if (found.TryGetValue(name, out var r)) sd.Add(name, r, baseMat, width, draw);
            MiG29InstrumentsPlugin.Log.LogInfo($"MiG-29SMT screens: {string.Join(", ", sd.screens.ConvertAll(s => $"{s.name} {s.c.W}x{s.c.H}"))}");
            return sd.screens.Count > 0;
        }

        void Add(string name, Renderer screen, Material baseMat, int width, Action<Canvas, FlightState> draw)
        {
            // the quad's UVs point at a black atlas cell (a dark screen without the plugin): re-map them 0..1 across the screen
            var mf = screen.GetComponent<MeshFilter>();
            var mesh = Instantiate(mf.sharedMesh);
            var v = mesh.vertices; var n = mesh.normals;
            Vector3 normal = n.Length > 0 ? n[0].normalized : Vector3.back;
            // the screen's own horizontal edge: of all vertex-to-vertex directions, the most nearly horizontal one (on the panel's
            // folded wings the screen is tilted and yawed, and the world x axis projected onto it is skewed against its edges)
            Vector3 right = Vector3.right; float bestH = -1f;
            for (int i = 0; i < v.Length; i++)
                for (int j = i + 1; j < v.Length; j++)
                {
                    var d = v[j] - v[i];
                    if (d.sqrMagnitude < 1e-8f) continue;
                    float h = Mathf.Abs(Vector3.Dot(d.normalized, Vector3.right)) - Mathf.Abs(d.normalized.y);
                    if (h > bestH) { bestH = h; right = d.normalized; }
                }
            if (Vector3.Dot(right, Vector3.right) < 0f) right = -right;
            right = Vector3.ProjectOnPlane(right, normal).normalized;
            Vector3 up = Vector3.Cross(right, normal).normalized;
            if (up.y < 0f) up = -up;
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (var p in v) { float a = Vector3.Dot(p, right), b = Vector3.Dot(p, up); minU = Mathf.Min(minU, a); maxU = Mathf.Max(maxU, a); minV = Mathf.Min(minV, b); maxV = Mathf.Max(maxV, b); }
            var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++)
                uv[i] = new Vector2(Mathf.InverseLerp(minU, maxU, Vector3.Dot(v[i], right)), Mathf.InverseLerp(minV, maxV, Vector3.Dot(v[i], up)));
            mesh.uv = uv;
            mf.sharedMesh = mesh;
            int height = Mathf.Max(16, Mathf.RoundToInt(width * (maxV - minV) / Mathf.Max(maxU - minU, 1e-4f)));

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "MiG29SMT_" + name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Material m;
            if (baseMat != null)
            {
                m = new Material(baseMat) { name = "MiG29SMT_" + name };
                m.SetTexture("_BaseMap", tex); m.SetTexture("_EmissionMap", tex);
                m.SetColor("_BaseColor", Color.white); m.SetColor("_EmissionColor", Color.white);
                m.EnableKeyword("_EMISSION");
                m.mainTextureScale = Vector2.one; m.mainTextureOffset = Vector2.zero;
            }
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) return;
                m = new Material(sh) { name = "MiG29SMT_" + name };
                m.SetTexture("_BaseMap", tex);
            }
            screen.sharedMaterial = m;
            screens.Add(new Screen { name = name, c = new Canvas(width, height), tex = tex, draw = draw });
        }

        void LateUpdate()
        {
            if (driver == null || screens.Count == 0 || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f / (Rate * screens.Count);        // one screen per tick, each ~Rate times a second
            var s = screens[turn++ % screens.Count];
            try { s.c.Clip(0, 0, s.c.W, s.c.H); s.draw(s.c, driver.State); s.tex.SetPixels32(s.c.px); s.tex.Apply(false); }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogError($"screen {s.name}: {e}"); screens.Remove(s); }
        }
    }

    // ------------------------------------------------------------------------------------------------ the pages
    static class Pages
    {
        static readonly Color32 Sky = new Color32(30, 104, 190, 255), Ground = new Color32(122, 82, 44, 255), White = new Color32(235, 235, 235, 255),
            Yellow = new Color32(250, 215, 40, 255), Green = new Color32(70, 230, 90, 255), Black = new Color32(6, 8, 10, 255), Grey = new Color32(70, 74, 80, 255),
            DimGreen = new Color32(25, 90, 35, 255), Amber = new Color32(255, 160, 30, 255), Red = new Color32(235, 50, 40, 255), Cyan = new Color32(60, 210, 230, 255);

        // ---------- flight display ----------
        public static void Pfd(Canvas c, FlightState s)
        {
            int W = c.W, H = c.H;
            int attY0 = 62, attY1 = H - 30, cx = W / 2, cy = (attY0 + attY1) / 2 + 2;
            const float ppd = 4.2f;
            float r = s.rollDeg * Mathf.Deg2Rad;   // right bank (+) turns the outside world, and the horizon, counter-clockwise
            float sin = Mathf.Sin(r), cos = Mathf.Cos(r), off = s.pitchDeg * ppd;
            c.Fill(0, 0, W, H, Black);
            for (int y = attY0; y < attY1; y++)
            {
                int row = y * W;
                for (int x = 0; x < W; x++)
                    c.px[row + x] = -(x - cx) * sin + (y - cy) * cos + off > 0f ? Sky : Ground;
            }
            c.Clip(0, attY0, W, attY1);
            var dir = new Vector2(cos, sin); var nrm = new Vector2(-sin, cos);
            for (int p = -90; p <= 90; p += 5)
            {
                float d = (p - s.pitchDeg) * ppd;
                if (Mathf.Abs(d) > 120f) continue;
                var m = new Vector2(cx, cy) + nrm * d;
                float half = p == 0 ? 130f : p % 10 == 0 ? 34f : 16f;
                if (p == 0) { c.Line(m - dir * half, m + dir * half, White, 2); continue; }
                float gap = p % 10 == 0 ? 12f : 6f;
                c.Line(m - dir * half, m - dir * gap, White, 1); c.Line(m + dir * gap, m + dir * half, White, 1);
                var tip = p > 0 ? -nrm * 5f : nrm * 5f;
                c.Line(m - dir * half, m - dir * half + tip, White, 1); c.Line(m + dir * half, m + dir * half + tip, White, 1);
                if (p % 10 == 0)
                {
                    string t = Mathf.Abs(p).ToString();
                    var at = m + dir * (half + 4f); c.Text((int)at.x, (int)at.y - 3, t, White, 1);
                    at = m - dir * (half + 4f + t.Length * 6f); c.Text((int)at.x, (int)at.y - 3, t, White, 1);
                }
            }
            foreach (var k in new[] { -60f, -45f, -30f, -20f, -10f, 10f, 20f, 30f, 45f, 60f })
            {
                float a = k * Mathf.Deg2Rad, r1 = Mathf.Abs(k) == 30f ? 106f : 102f;
                c.Line(new Vector2(cx + Mathf.Sin(a) * 96f, cy + Mathf.Cos(a) * 96f), new Vector2(cx + Mathf.Sin(a) * r1, cy + Mathf.Cos(a) * r1), White, 1);
            }
            c.Tri(new Vector2(cx, cy + 97f), new Vector2(cx - 5, cy + 107f), new Vector2(cx + 5, cy + 107f), White);
            var pa = new Vector2(cx - Mathf.Sin(r) * 94f, cy + Mathf.Cos(r) * 94f);
            c.Tri(pa, pa + Rot(new Vector2(-5, -9), r), pa + Rot(new Vector2(5, -9), r), Yellow);
            // aircraft symbol
            c.Line(new Vector2(cx - 46, cy), new Vector2(cx - 16, cy), Yellow, 3); c.Line(new Vector2(cx - 16, cy), new Vector2(cx - 16, cy - 8), Yellow, 3);
            c.Line(new Vector2(cx + 16, cy), new Vector2(cx + 46, cy), Yellow, 3); c.Line(new Vector2(cx + 16, cy), new Vector2(cx + 16, cy - 8), Yellow, 3);
            c.Fill(cx - 2, cy - 2, 5, 5, Yellow);
            c.Clip(0, 0, W, H);

            ValueBox(c, 2, cy - 11, 54, 22, Mathf.RoundToInt(s.iasKmh).ToString(), s.iasKmh > 1450f ? Red : White);
            c.Text(4, cy + 14, "KM/H", Green, 1);
            ValueBox(c, W - 60, cy - 11, 58, 22, Mathf.RoundToInt(s.altM).ToString(), s.radAltM < 60f && s.airborne && !s.gearDown ? Red : White);
            c.Text(W - 58, cy + 14, "M", Green, 1);
            if (s.radAltM < 1500f && s.airborne) c.Text(W - 58, cy - 26, "R" + Mathf.RoundToInt(s.radAltM), Green, 1);

            // heading tape
            float hdg = Mathf.Repeat(s.headingDeg, 360f);
            for (int dDeg = -30; dDeg <= 30; dDeg++)
            {
                float a = Mathf.Repeat(Mathf.Round(hdg) + dDeg, 360f);
                int x = cx + Mathf.RoundToInt(Mathf.DeltaAngle(hdg, a) * 3.6f);
                if (x < 2 || x > W - 3) continue;
                int ai = Mathf.RoundToInt(a) % 360;
                if (ai % 10 == 0)
                {
                    c.Fill(x, H - 12, 1, 8, White);
                    if (ai % 30 == 0) { string t = (ai / 10).ToString("00"); c.Text(x - t.Length * 3, H - 23, t, White, 1); }
                }
                else if (ai % 5 == 0) c.Fill(x, H - 8, 1, 4, White);
            }
            ValueBox(c, cx - 18, attY1 + 2, 36, 13, Mathf.RoundToInt(hdg).ToString("000"), Green);

            c.Text(6, 44, "M", Green, 2); c.Text(22, 44, s.mach.ToString("0.00"), White, 2);
            c.Text(96, 44, "G", Green, 2); c.Text(112, 44, s.g.ToString("0.0"), s.g > 8.5f || s.g < -2.5f ? Red : White, 2);
            c.Text(170, 44, "A", Green, 2); c.Text(186, 44, Mathf.RoundToInt(s.aoaDeg).ToString(), s.aoaDeg > 24f && s.airborne ? Red : White, 2);
            c.Text(6, 18, "VS", Green, 2); c.Text(36, 18, (s.vsMs >= 0 ? "+" : "") + Mathf.RoundToInt(s.vsMs), White, 2);
            c.Text(130, 18, "T", Green, 2); c.Text(146, 18, s.hours.ToString("00") + ":" + s.minutes.ToString("00"), White, 2);
            if (s.missileIncoming && s.blink) c.Text(cx - 42, attY0 + 6, "MISSILE", Red, 2);
        }

        // ---------- engines, fuel, cautions ----------
        static readonly (string id, string text, bool warn)[] Cautions =
        {
            ("fire_l", "FIRE L", true), ("fire_r", "FIRE R", true), ("missile", "MISSILE", true), ("lowalt", "LOW ALT", true), ("overg", "OVER G", true),
            ("aoa", "AOA", true), ("fuel_res", "FUEL RES", true), ("fuel_low", "FUEL LOW", false), ("lock", "LOCK", true), ("hyd", "HYD", false),
            ("gen_l", "GEN L", false), ("gen_r", "GEN R", false), ("pump", "PUMP", false), ("oil", "OIL", false), ("vib", "VIB", false),
            ("canopy", "CANOPY", false), ("gear", "GEAR", false), ("speed", "SPEED", false), ("ptb", "PTB EMPTY", false), ("flaps", "FLAPS", false),
            ("brake", "BRAKE", false), ("fod", "INTAKE", false), ("ab", "AFTERBRN", false),
        };

        public static void Eng(Canvas c, FlightState s)
        {
            int W = c.W, H = c.H;
            c.Fill(0, 0, W, H, Black);
            int top = H - 12;
            c.Text(4, top, "ENG", Cyan, 1); c.Text(W - 40, top, "FUEL", Cyan, 1);
            // RPM and EGT as vertical bars, left and right engine
            void Bar(int x, string label, float v, float lo, float hi, float red, string val)
            {
                int bh = H - 64, by = 30;
                c.Fill(x, by, 14, bh, Grey); c.Fill(x + 1, by + 1, 12, bh - 2, Black);
                int h = Mathf.RoundToInt(Mathf.Clamp01((v - lo) / (hi - lo)) * (bh - 2));
                c.Fill(x + 1, by + 1, 12, h, v >= red ? Red : Green);
                c.Text(x - 2, by - 10, label, White, 1);
                c.Text(x - 4, by + bh + 3, val, White, 1);
            }
            Bar(8, "N L", s.rpmL, 0, 110, 104, Mathf.RoundToInt(s.rpmL).ToString());
            Bar(34, "N R", s.rpmR, 0, 110, 104, Mathf.RoundToInt(s.rpmR).ToString());
            Bar(66, "T L", s.egtL, 200, 1000, 950, Mathf.RoundToInt(s.egtL).ToString());
            Bar(92, "T R", s.egtR, 200, 1000, 950, Mathf.RoundToInt(s.egtR).ToString());
            if (s.throttle > 0.9f && (s.rpmL > 90 || s.rpmR > 90)) c.Text(10, H - 26, "AB", Amber, 1);
            // fuel and hydraulics
            int fx = 132;
            c.Text(fx, H - 28, Mathf.RoundToInt(s.fuelKg).ToString(), s.fuelFrac < 0.10f ? Red : s.fuelFrac < 0.25f ? Amber : White, 2);
            c.Text(fx + 72, H - 24, "KG", Green, 1);
            c.Fill(fx, H - 40, 100, 6, Grey); c.Fill(fx + 1, H - 39, Mathf.RoundToInt(98 * Mathf.Clamp01(s.fuelFrac)), 4, s.fuelFrac < 0.25f ? Amber : Green);
            c.Text(fx, H - 54, "HYD " + Mathf.RoundToInt(s.hydraulic), s.hydraulic < 150 ? Amber : White, 1);
            // caution messages (two columns), warnings red, cautions amber
            var on = MiG29InstrumentMath.Lamps(s);
            int row = 0;
            foreach (var (id, text, warn) in Cautions)
            {
                if (!on.Contains("cau_" + id) && !(id == "fire_l" && s.fireL) && !(id == "fire_r" && s.fireR) && !(id == "missile" && s.missileIncoming)) continue;
                int col = row % 2, line = row / 2;
                int y = H - 70 - line * 11;
                if (y < 4) break;
                c.Text(fx + col * 54, y, text.Length > 8 ? text.Substring(0, 8) : text, warn ? Red : Amber, 1);
                row++;
            }
            if (row == 0) c.Text(fx, H - 70, "NO CAUTIONS", DimGreen, 1);
        }

        // ---------- radar warning ----------
        static readonly string[] ThreatLetter = { "F", "SL", "SS", "U", "U", "N" };   // fighter, long / short range SAM, unknown, ship

        public static void Rwr(Canvas c, FlightState s)
        {
            int W = c.W, H = c.H, cx = W / 2, cy = H / 2;
            float R = Mathf.Min(W, H) * 0.42f;
            c.Fill(0, 0, W, H, Black);
            c.Circle(cx, cy, R, DimGreen); c.Circle(cx, cy, R * 0.5f, DimGreen);
            for (int k = 0; k < 12; k++)
            {
                float a = k * 30f * Mathf.Deg2Rad;
                c.Line(new Vector2(cx + Mathf.Sin(a) * R * 0.93f, cy + Mathf.Cos(a) * R * 0.93f), new Vector2(cx + Mathf.Sin(a) * R, cy + Mathf.Cos(a) * R), Green, 1);
            }
            // own aircraft
            c.Line(new Vector2(cx, cy - 7), new Vector2(cx, cy + 9), Green, 2); c.Line(new Vector2(cx - 8, cy + 1), new Vector2(cx + 8, cy + 1), Green, 2);
            c.Line(new Vector2(cx - 4, cy - 6), new Vector2(cx + 4, cy - 6), Green, 2);
            foreach (var (bearing, type) in s.rwr)
            {
                float a = bearing * Mathf.Deg2Rad;
                float rr = type < 0 ? R * 0.4f : R * (0.95f - 0.6f * Mathf.Clamp01(s.rwrPower));
                int x = cx + Mathf.RoundToInt(Mathf.Sin(a) * rr), y = cy + Mathf.RoundToInt(Mathf.Cos(a) * rr);
                if (type < 0) { if (s.blink) { c.Circle(x, y, 7, Red); c.Text(x - 2, y - 3, "M", Red, 1); } continue; }
                string t = type < ThreatLetter.Length ? ThreatLetter[type] : "U";
                c.Text(x - t.Length * 3, y - 3, t, s.lockedOn ? Red : Yellow, 1);
                if (s.lockedOn) c.Circle(x, y, 8, Red);
            }
            c.Text(3, H - 10, "RWR", Cyan, 1);
            if (s.lockedOn) c.Text(W - 28, H - 10, "LOCK", Red, 1);
            if (s.missileIncoming && s.blink) c.Text(cx - 21, 3, "MISSILE", Red, 1);
        }

        // ---------- fuel / clock readout ----------
        public static void Data(Canvas c, FlightState s)
        {
            int W = c.W, H = c.H;
            c.Fill(0, 0, W, H, Black);
            c.Text(6, H - 12, "FUEL", Cyan, 1);
            c.Text(6, H - 36, Mathf.RoundToInt(s.fuelKg).ToString(), s.fuelFrac < 0.10f ? Red : s.fuelFrac < 0.25f ? Amber : Green, 2);
            c.Text(W - 22, H - 32, "KG", Green, 1);
            if (s.ptbEmpty) c.Text(W - 58, H - 12, "PTB", Amber, 1);
            c.Text(6, 8, s.hours.ToString("00") + ":" + s.minutes.ToString("00") + ":" + Mathf.FloorToInt(s.seconds).ToString("00"), White, 2);
        }

        static Vector2 Rot(Vector2 v, float rad) { float co = Mathf.Cos(rad), si = Mathf.Sin(rad); return new Vector2(v.x * co - v.y * si, v.x * si + v.y * co); }

        static void ValueBox(Canvas c, int x, int y, int w, int h, string t, Color32 col)
        {
            c.Fill(x, y, w, h, Black);
            c.Fill(x, y, w, 1, White); c.Fill(x, y + h - 1, w, 1, White); c.Fill(x, y, 1, h, White); c.Fill(x + w - 1, y, 1, h, White);
            int scale = h >= 20 ? 2 : 1;
            c.Text(x + (w - (t.Length * 6 * scale - scale)) / 2, y + (h - 7 * scale) / 2, t, col, scale);
        }
    }

    // ------------------------------------------------------------------------------------------------ a tiny software canvas
    public class Canvas
    {
        public readonly int W, H;
        public readonly Color32[] px;
        int cx0, cy0, cx1, cy1;   // clip rectangle

        public Canvas(int w, int h) { W = w; H = h; px = new Color32[w * h]; Clip(0, 0, w, h); }

        public void Clip(int x0, int y0, int x1, int y1) { cx0 = Math.Max(0, x0); cy0 = Math.Max(0, y0); cx1 = Math.Min(W, x1); cy1 = Math.Min(H, y1); }

        public void Fill(int x, int y, int w, int h, Color32 c)
        {
            int x0 = Math.Max(cx0, x), x1 = Math.Min(cx1, x + w), y0 = Math.Max(cy0, y), y1 = Math.Min(cy1, y + h);
            for (int yy = y0; yy < y1; yy++) { int row = yy * W; for (int xx = x0; xx < x1; xx++) px[row + xx] = c; }
        }

        public void Plot(int x, int y, Color32 c)
        {
            if (x < cx0 || x >= cx1 || y < cy0 || y >= cy1) return;
            px[y * W + x] = c;
        }

        public void Line(Vector2 a, Vector2 b, Color32 c, int thick)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len * 1.5f));
            var nrm = len > 0 ? new Vector2(-(b.y - a.y), b.x - a.x) / len : Vector2.zero;
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                for (int t = 0; t < thick; t++) { var q = p + nrm * (t - (thick - 1) * 0.5f); Plot(Mathf.RoundToInt(q.x), Mathf.RoundToInt(q.y), c); }
            }
        }

        public void Circle(float x, float y, float r, Color32 c)
        {
            int n = Mathf.Max(16, Mathf.CeilToInt(r * 6.3f));
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                Plot(Mathf.RoundToInt(x + Mathf.Cos(a) * r), Mathf.RoundToInt(y + Mathf.Sin(a) * r), c);
            }
        }

        public void Tri(Vector2 a, Vector2 b, Vector2 c, Color32 col)
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

        // text with its bottom-left at (x, y); 5x7 font, '#' = lit
        public void Text(int x, int y, string t, Color32 c, int scale)
        {
            foreach (var ch in t)
            {
                if (Font.TryGetValue(char.ToUpperInvariant(ch), out var g))
                    for (int row = 0; row < 7; row++)
                        for (int col = 0; col < 5; col++)
                            if (g[row][col] == '#')
                                for (int sy = 0; sy < scale; sy++)
                                    for (int sx = 0; sx < scale; sx++)
                                        Plot(x + col * scale + sx, y + (6 - row) * scale + sy, c);
                x += 6 * scale;
            }
        }

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
            ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
            ['C'] = new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." },
            ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
            ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
            ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
            ['G'] = new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".###." },
            ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
            ['I'] = new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." },
            ['J'] = new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." },
            ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
            ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
            ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
            ['N'] = new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" },
            ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
            ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
            ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
            ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
            ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
            ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
            ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
            ['W'] = new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "##.##", "#...#" },
            ['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
            ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
            ['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },
        };
    }
}
