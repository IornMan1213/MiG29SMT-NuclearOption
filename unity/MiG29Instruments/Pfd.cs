using System;
using System.Collections.Generic;
using MiG29SMTTools;
using UnityEngine;

namespace MiG29SMTInstruments
{
    // The MiG-29SMT's plugin-drawn screens on its all-glass panel (quads MiG29_ck_<name> that MiG29Cockpit builds, dark without the plugin):
    //   pfd   left MFD: flight display (attitude, pitch ladder, bank scale, speed and altitude tapes, heading tape, Mach, g, AoA, VS, time)
    //   eng   right MFD: radar warning (threats by bearing and type, inbound missiles, lock), engine dials (RPM, EGT), fuel, systems, cautions
    //   sms   centre MFD, under the game's tactical display: weapon stations and rounds, countermeasures, the current target
    //   stby  small standby flight display: attitude, airspeed, altitude, heading
    //   ufc   up-front control panel window: clock, selected weapon
    // Each is drawn on the CPU into a small texture ~20 times a second and shown with an emissive copy of the game's tactical-screen
    // material. Visual only, player's aircraft only (reads the InstrumentDriver's FlightState).
    public class ScreenDriver : MonoBehaviour
    {
        class Screen { public string name; public Canvas c; public Texture2D tex; public Action<Canvas, FlightState> draw; public float rate, due; }

        InstrumentDriver driver;
        readonly List<Screen> screens = new List<Screen>();

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
            // texture width and redraws per second (the flight displays fast, the slow-changing pages less often)
            foreach (var (name, width, rate, draw) in new (string, int, float, Action<Canvas, FlightState>)[]
                     { ("pfd", 320, 20f, Pages.Pfd), ("eng", 320, 8f, Pages.Eng), ("sms", 400, 4f, Pages.Sms), ("stby", 120, 12f, Pages.Stby), ("ufc", 250, 2f, Pages.Ufc) })
                if (found.TryGetValue(name, out var r)) sd.Add(name, r, baseMat, width, rate, draw);
            MiG29InstrumentsPlugin.Log.LogInfo($"MiG-29SMT screens: {string.Join(", ", sd.screens.ConvertAll(s => $"{s.name} {s.c.W}x{s.c.H}"))}");
            return sd.screens.Count > 0;
        }

        void Add(string name, Renderer screen, Material baseMat, int width, float rate, Action<Canvas, FlightState> draw)
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

            // mipmapped, trilinear and anisotropic: a screen covers only ~150 px of the player's view, and without mips the text shimmered
            // and broke up (user screenshot, SMT 0.2.0)
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, true) { name = "MiG29SMT_" + name, filterMode = FilterMode.Trilinear, anisoLevel = 8, wrapMode = TextureWrapMode.Clamp };
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
            var canvas = new Canvas(width, height);
            canvas.SetShape(Array.ConvertAll(uv, p => new Vector2(p.x * width, p.y * height)));   // shaped screens (the wing displays): pages lay out inside
            screens.Add(new Screen { name = name, c = canvas, tex = tex, draw = draw, rate = rate, due = screens.Count * 0.01f });
        }

        void LateUpdate()
        {
            if (driver == null || screens.Count == 0) return;
            Screen s = null;                                               // at most one screen per frame: the most overdue
            foreach (var x in screens) if (Time.unscaledTime >= x.due && (s == null || x.due < s.due)) s = x;
            if (s == null) return;
            s.due = Time.unscaledTime + 1f / s.rate;
            try { s.c.Clip(0, 0, s.c.W, s.c.H); s.draw(s.c, driver.State); s.tex.SetPixels32(s.c.px); s.tex.Apply(true); }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogError($"screen {s.name}: {e}"); screens.Remove(s); }
        }
    }

    // ------------------------------------------------------------------------------------------------ the pages
    // Sized for the tall displays (~300 x 680 px on the wing screens): in the cockpit view a screen covers only ~150 px of the
    // player's monitor, so values are 3x (21 px) and labels 2x (14 px) text, lines 2-3 px, one item per line (user, SMT 0.2.0:
    // "the text is hard to read", the engine labels ran into the fuel line).
    static class Pages
    {
        static readonly Color32 Sky = new Color32(30, 104, 190, 255), Ground = new Color32(122, 82, 44, 255), White = new Color32(235, 235, 235, 255),
            Yellow = new Color32(250, 215, 40, 255), Green = new Color32(70, 230, 90, 255), Black = new Color32(6, 8, 10, 255), Grey = new Color32(70, 74, 80, 255),
            DimGreen = new Color32(25, 90, 35, 255), Amber = new Color32(255, 160, 30, 255), Red = new Color32(235, 50, 40, 255), Cyan = new Color32(60, 210, 230, 255),
            DarkCyan = new Color32(10, 60, 70, 255), WarnBack = new Color32(80, 12, 10, 255);

        // ---------- flight display (left MFD) ----------
        public static void Pfd(Canvas c, FlightState s)
        {
            int W = c.W, H = c.H;
            // the attitude area sits in the screen's widest part; on the shaped wing screen the readouts go above it
            int attY0 = 112, attY1 = Mathf.Min(H - 80, 432);
            int X0 = c.Left(attY0, attY1), X1 = c.Right(attY0, attY1);
            int cx = (X0 + X1) / 2, cy = (attY0 + attY1) / 2;
            const float ppd = 6.5f;
            c.Fill(0, 0, W, H, Black);
            Horizon(c, X0, attY0, X1, attY1, cx, cy, s, ppd);
            c.Clip(X0, attY0, X1, attY1);
            float r = s.rollDeg * Mathf.Deg2Rad;
            float sin = Mathf.Sin(r), cos = Mathf.Cos(r);
            var dir = new Vector2(cos, sin); var nrm = new Vector2(-sin, cos);
            for (int p = -90; p <= 90; p += 5)
            {
                float d = (p - s.pitchDeg) * ppd;
                if (Mathf.Abs(d) > 200f) continue;
                var m = new Vector2(cx, cy) + nrm * d;
                float half = p == 0 ? 260f : p % 10 == 0 ? 46f : 22f;
                if (p == 0) { c.Line(m - dir * half, m + dir * half, White, 3); continue; }
                float gap = p % 10 == 0 ? 16f : 10f;
                c.Line(m - dir * half, m - dir * gap, White, 2); c.Line(m + dir * gap, m + dir * half, White, 2);
                var tip = p > 0 ? -nrm * 7f : nrm * 7f;
                c.Line(m - dir * half, m - dir * half + tip, White, 2); c.Line(m + dir * half, m + dir * half + tip, White, 2);
                if (p % 10 == 0)
                {
                    string t = Mathf.Abs(p).ToString();
                    var at = m + dir * (half + 6f); c.Text((int)at.x, (int)at.y - 7, t, White, 2);
                }
            }
            float br = Mathf.Min(120f, (X1 - X0) / 2f - 16f);
            foreach (var k in new[] { -60f, -45f, -30f, -20f, -10f, 0f, 10f, 20f, 30f, 45f, 60f })
            {
                float a = k * Mathf.Deg2Rad, r1 = Mathf.Abs(k) % 30f == 0f ? br + 14f : br + 8f;
                c.Line(new Vector2(cx + Mathf.Sin(a) * br, cy + Mathf.Cos(a) * br), new Vector2(cx + Mathf.Sin(a) * r1, cy + Mathf.Cos(a) * r1), White, 3);
            }
            var pa = new Vector2(cx - Mathf.Sin(r) * (br - 3f), cy + Mathf.Cos(r) * (br - 3f));
            c.Tri(pa, pa + Rot(new Vector2(-8, -14), r), pa + Rot(new Vector2(8, -14), r), Yellow);
            c.Line(new Vector2(cx - 64, cy), new Vector2(cx - 22, cy), Yellow, 5); c.Line(new Vector2(cx - 22, cy), new Vector2(cx - 22, cy - 12), Yellow, 5);
            c.Line(new Vector2(cx + 22, cy), new Vector2(cx + 64, cy), Yellow, 5); c.Line(new Vector2(cx + 22, cy), new Vector2(cx + 22, cy - 12), Yellow, 5);
            c.Fill(cx - 4, cy - 4, 9, 9, Yellow);

            // speed tape (km/h) left, altitude tape (m) right, on darkened strips
            const int tapeW = 66;
            c.Shade(X0, attY0, tapeW, attY1 - attY0, 0.3f);
            c.Shade(X1 - tapeW, attY0, tapeW, attY1 - attY0, 0.3f);
            const float pxKmh = 1.3f, pxM = 0.36f;
            for (int v = Mathf.FloorToInt((s.iasKmh - 200f) / 10f) * 10; v <= s.iasKmh + 200f; v += 10)
            {
                if (v < 0) continue;
                int y = cy + Mathf.RoundToInt((v - s.iasKmh) * pxKmh);
                bool big = v % 50 == 0;
                c.Fill(X0 + tapeW - (big ? 12 : 7), y - 1, big ? 12 : 7, 3, White);
                if (v % 100 == 0) { string t = v.ToString(); c.Text(X0 + tapeW - 15 - TW(t, 2), y - 7, t, White, 2); }
            }
            for (int v = Mathf.FloorToInt((s.altM - 700f) / 50f) * 50; v <= s.altM + 700f; v += 50)
            {
                int y = cy + Mathf.RoundToInt((v - s.altM) * pxM);
                bool big = v % 100 == 0;
                c.Fill(X1 - tapeW, y - 1, big ? 12 : 7, 3, White);
                if (v % 200 == 0) c.Text(X1 - tapeW + 15, y - 7, v.ToString(), White, 2);
            }
            if (s.missileIncoming && s.blink) { string t = "MISSILE"; c.Fill(cx - TW(t, 3) / 2 - 4, attY1 - 40, TW(t, 3) + 8, 29, WarnBack); c.Text(cx - TW(t, 3) / 2, attY1 - 36, t, Red, 3); }
            if (s.gearDown || s.gearMoving) c.Text(X0 + tapeW + 6, attY0 + 8, "GEAR", s.gearMoving ? Amber : Green, 2);
            c.Clip(0, 0, W, H);
            string ias = Mathf.RoundToInt(s.iasKmh).ToString(), alt = Mathf.RoundToInt(s.altM).ToString();
            ValueBox(c, X0, cy - 18, tapeW + 16, 36, ias, s.iasKmh > 1450f ? Red : White, 3);
            ValueBox(c, X1 - tapeW - 22, cy - 18, tapeW + 22, 36, alt, s.radAltM < 60f && s.airborne && !s.gearDown ? Red : White, alt.Length > 4 ? 2 : 3);
            if (s.radAltM < 1500f && s.airborne) { string t = "R" + Mathf.RoundToInt(s.radAltM); c.Text(X1 - 4 - TW(t, 2), cy - 40, t, Green, 2); }

            // above the attitude, in the screen's shaped top: data tiles (Mach, g, angle of attack, vertical speed), then the clock
            const int th = 60;
            for (int row = 0; row < 3; row++)
            {
                int y0 = attY1 + 8 + row * (th + 6), y1 = Mathf.Min(y0 + th, H - 4);
                if (y1 - y0 < 40) break;
                int x0 = c.Left(y0, y1) + 3, x1 = c.Right(y0, y1) - 3, mid = (x0 + x1) / 2;
                if (row == 0) { Tile(c, x0, y0, mid - 3, y1, "MACH", s.mach.ToString("0.00"), White); Tile(c, mid + 3, y0, x1, y1, "G", s.g.ToString("0.0"), s.g > 8.5f || s.g < -2.5f ? Red : White); }
                else if (row == 1) { Tile(c, x0, y0, mid - 3, y1, "AOA", Mathf.RoundToInt(s.aoaDeg).ToString(), s.aoaDeg > 24f && s.airborne ? Red : White); Tile(c, mid + 3, y0, x1, y1, "V/S", (s.vsMs >= 0 ? "+" : "") + Mathf.RoundToInt(s.vsMs), White); }
                else Tile(c, x0, y0, x1, y1, "TIME", s.hours.ToString("00") + ":" + s.minutes.ToString("00") + ":" + Mathf.FloorToInt(s.seconds).ToString("00"), Cyan);
            }

            // heading tape and box, clock
            float hdg = Mathf.Repeat(s.headingDeg, 360f);
            const int tapeY = 60;
            for (int dDeg = -30; dDeg <= 30; dDeg++)
            {
                float a = Mathf.Repeat(Mathf.Round(hdg) + dDeg, 360f);
                int x = cx + Mathf.RoundToInt(Mathf.DeltaAngle(hdg, a) * 5f);
                if (x < 2 || x > W - 3) continue;
                int ai = Mathf.RoundToInt(a) % 360;
                if (ai % 10 == 0)
                {
                    c.Fill(x - 1, tapeY + 34, 3, 14, White);
                    if (ai % 20 == 0) { string t = (ai / 10).ToString("00"); c.Text(x - TW(t, 2) / 2, tapeY + 16, t, White, 2); }
                }
                else if (ai % 5 == 0) c.Fill(x, tapeY + 40, 2, 8, White);
            }
            ValueBox(c, cx - 34, tapeY - 26, 68, 34, Mathf.RoundToInt(hdg).ToString("000"), Green, 3);
        }

        // ---------- radar warning, engines, fuel, cautions (right MFD) ----------
        static readonly (string id, string text, bool warn)[] Cautions =
        {
            ("fire_l", "FIRE L", true), ("fire_r", "FIRE R", true), ("missile", "MISSILE", true), ("lowalt", "LOW ALT", true), ("overg", "OVER G", true),
            ("aoa", "AOA", true), ("fuel_res", "FUEL RES", true), ("fuel_low", "FUEL LOW", false), ("lock", "LOCK", true), ("hyd", "HYD", false),
            ("gen_l", "GEN L", false), ("gen_r", "GEN R", false), ("pump", "PUMP", false), ("oil", "OIL", false), ("vib", "VIB", false),
            ("canopy", "CANOPY", false), ("gear", "GEAR", false), ("speed", "SPEED", false), ("ptb", "PTB EMPTY", false), ("flaps", "FLAPS", false),
            ("brake", "BRAKE", false), ("fod", "INTAKE", false), ("ab", "AFTERBRN", false),
        };
        static readonly string[] ThreatLetter = { "F", "SL", "SS", "U", "U", "N" };   // fighter, long / short range SAM, unknown, ship

        public static void Eng(Canvas c, FlightState s)
        {
            int W = c.W, H = c.H;
            c.Fill(0, 0, W, H, Black);
            // sections, bottom up: cautions, fuel and systems, engines, signature; the radar warning circle in the shaped top
            const int cauTop = 98, fuelTop = 180, engTop = 300, sigTop = 394;

            // ---- radar warning: the largest circle (up to r 122) that fits the screen's top part
            int rwrY0 = sigTop + 4;
            int cy = (rwrY0 + H) / 2 - 4, cx = W / 2; float R = 122f;
            for (; R > 60f; R -= 3f)
            {
                int lo = int.MinValue, hi = int.MaxValue;
                for (int oy = -(int)R; oy <= (int)R; oy += 4)
                {
                    int y = Mathf.Clamp(cy + oy, 0, H - 1); float hw = Mathf.Sqrt(R * R - oy * oy);
                    lo = Math.Max(lo, (int)(c.L[y] + 6 + hw)); hi = Math.Min(hi, (int)(c.R[y] - 6 - hw));
                }
                if (lo <= hi) { cx = Mathf.Clamp(W / 2, lo, hi); break; }
            }
            c.Circle(cx, cy, R, DimGreen); c.Circle(cx, cy, R - 1, DimGreen); c.Circle(cx, cy, R * 0.5f, DimGreen);
            for (int k = 0; k < 12; k++)
            {
                float a = k * 30f * Mathf.Deg2Rad;
                c.Line(new Vector2(cx + Mathf.Sin(a) * R * 0.9f, cy + Mathf.Cos(a) * R * 0.9f), new Vector2(cx + Mathf.Sin(a) * R, cy + Mathf.Cos(a) * R), Green, 3);
            }
            c.Line(new Vector2(cx, cy - 12), new Vector2(cx, cy + 14), Green, 3); c.Line(new Vector2(cx - 14, cy + 3), new Vector2(cx + 14, cy + 3), Green, 3);
            c.Line(new Vector2(cx - 7, cy - 11), new Vector2(cx + 7, cy - 11), Green, 3);
            foreach (var (bearing, type) in s.rwr)
            {
                float a = bearing * Mathf.Deg2Rad;
                float rr = type < 0 ? R * 0.42f : R * (0.92f - 0.55f * Mathf.Clamp01(s.rwrPower));
                int x = cx + Mathf.RoundToInt(Mathf.Sin(a) * rr), y = cy + Mathf.RoundToInt(Mathf.Cos(a) * rr);
                if (type < 0) { if (s.blink) { c.Circle(x, y, 15, Red); c.Circle(x, y, 14, Red); c.Text(x - 8, y - 10, "M", Red, 3); } continue; }
                string t = type < ThreatLetter.Length ? ThreatLetter[type] : "U";
                c.Text(x - TW(t, 3) / 2, y - 10, t, s.lockedOn ? Red : Yellow, 3);
                if (s.lockedOn) { c.Circle(x, y, 19, Red); c.Circle(x, y, 18, Red); }
            }
            c.Text(c.Left(H - 22, H - 6) + 6, H - 20, "RWR", Cyan, 2);
            if (s.lockedOn && s.blink) c.Text(c.Left(rwrY0, rwrY0 + 16) + 6, rwrY0 + 2, "LOCK", Red, 2);

            // ---- signature: engine heat, radar cross-section, own radar
            var (sx0, sx1) = Section(c, sigTop - 90, sigTop, "SIGNATURE");
            int by = sigTop - 46, bw = sx1 - sx0 - 104;
            string irTxt = s.irFrac < 0.35f ? "LOW" : s.irFrac < 0.7f ? "MED" : "HIGH";
            var irCol = s.irFrac < 0.35f ? Green : s.irFrac < 0.7f ? Amber : Red;
            c.Text(sx0 + 6, by, "IR", White, 2);
            Bar(c, sx0 + 34, by - 1, bw, 16, s.irFrac, irCol, -1f);
            c.Text(sx1 - 6 - TW(irTxt, 2), by, irTxt, irCol, 2);
            float extra = s.rcsClean > 0f ? s.rcs / s.rcsClean - 1f : 0f;
            c.Text(sx0 + 6, by - 30, "RCS", White, 2);
            string rv = s.rcs.ToString(s.rcs < 10f ? "0.00" : "0.0");
            c.Text(sx0 + 50, by - 30, rv, Cyan, 2);
            if (extra > 0.01f) c.Text(sx0 + 58 + TW(rv, 2), by - 30, "+" + Mathf.RoundToInt(extra * 100f) + "%", Amber, 2);
            string rd = s.radarOn ? "RDR ON" : "RDR OFF";
            c.Text(sx1 - 6 - TW(rd, 2), by - 30, rd, s.radarOn ? Green : Grey, 2);

            // ---- engines: RPM and EGT bars, left and right
            var (ex0, ex1) = Section(c, fuelTop + 4, engTop - 4, "ENGINES");
            if (s.throttle > 0.9f && (s.rpmL > 90 || s.rpmR > 90)) c.Text((ex0 + ex1) / 2 - TW("A/B", 2) / 2 + 30, engTop - 26, "A/B", Amber, 2);
            string thr = "THR " + Mathf.RoundToInt(s.throttle * 100f); c.Text(ex1 - 6 - TW(thr, 2), engTop - 26, thr, White, 2);
            int colW = (ex1 - ex0 - 36) / 2;
            foreach (var (k, rpm, egt, side) in new[] { (0, s.rpmL, s.egtL, "L"), (1, s.rpmR, s.egtR, "R") })
            {
                int x0 = ex0 + 8 + k * (colW + 20);
                string rv2 = Mathf.RoundToInt(rpm) + "%";
                c.Text(x0, engTop - 58, side, Cyan, 3);
                c.Text(x0 + colW - TW(rv2, 3), engTop - 58, rv2, rpm >= 104 ? Red : White, 3);
                Bar(c, x0, engTop - 74, colW, 10, rpm / 110f, rpm >= 104 ? Red : Green, 104f / 110f);
                string ev = "EGT " + Mathf.RoundToInt(egt);
                c.Text(x0 + colW - TW(ev, 2), engTop - 96, ev, egt >= 950 ? Red : White, 2);
                Bar(c, x0, engTop - 108, colW, 7, (egt - 200f) / 800f, egt >= 950 ? Red : Amber, 750f / 800f);
            }

            // ---- fuel and systems
            var (fx0, fx1) = Section(c, cauTop + 4, fuelTop, "FUEL");
            string fk = Mathf.RoundToInt(s.fuelKg).ToString();
            var fcol = s.fuelFrac < 0.10f ? Red : s.fuelFrac < 0.25f ? Amber : White;
            c.Text(fx0 + 70, fuelTop - 22, fk, fcol, 3); c.Text(fx0 + 76 + TW(fk, 3), fuelTop - 22, "KG", Green, 2);
            if (s.extFuelKg > 0.5f || s.ptbEmpty) { string pt = "PTB " + Mathf.RoundToInt(s.extFuelKg); c.Text(fx1 - 6 - TW(pt, 2), fuelTop - 22, pt, s.ptbEmpty ? Amber : White, 2); }
            Bar(c, fx0 + 6, fuelTop - 42, fx1 - fx0 - 12, 10, s.fuelFrac, fcol.Equals(White) ? Green : fcol, -1f);
            c.Text(fx0 + 6, cauTop + 12, "HYD " + Mathf.RoundToInt(s.hydraulic), s.hydraulic < 150 ? Amber : White, 2);
            string ox = "OXY " + Mathf.RoundToInt(s.oxygen); c.Text(fx1 - 6 - TW(ox, 2), cauTop + 12, ox, s.oxygen < 30 ? Amber : White, 2);

            // ---- cautions, two columns: warnings red on dark red, cautions amber
            var on = MiG29InstrumentMath.Lamps(s);
            int row = 0, cw = (W - 12) / 2;
            foreach (var (id, text, warn) in Cautions)
            {
                if (!on.Contains("cau_" + id) && !(id == "fire_l" && s.fireL) && !(id == "fire_r" && s.fireR) && !(id == "missile" && s.missileIncoming)) continue;
                int col = row % 2, line = row / 2;
                int y = cauTop - 24 - line * 23;
                if (y < 2) break;
                string tt = text.Length > 9 ? text.Substring(0, 9) : text;
                if (warn) c.Fill(4 + col * (cw + 4), y - 3, cw, 20, WarnBack);
                c.Text(8 + col * (cw + 4), y, tt, warn ? Red : Amber, 2);
                row++;
            }
            if (row == 0) c.Text(8, cauTop - 24, "NO CAUTIONS", DimGreen, 2);
        }

        // a titled section frame across the screen's width between rows y0 and y1; returns its inner left and right edges
        static (int, int) Section(Canvas c, int y0, int y1, string title)
        {
            int x0 = c.Left(y0, y1) + 3, x1 = c.Right(y0, y1) - 3;
            c.Fill(x0, y0, x1 - x0, 2, Grey); c.Fill(x0, y1 - 2, x1 - x0, 2, Grey); c.Fill(x0, y0, 2, y1 - y0, Grey); c.Fill(x1 - 2, y0, 2, y1 - y0, Grey);
            c.Fill(x0 + 2, y1 - 22, TW(title, 2) + 10, 20, DarkCyan);
            c.Text(x0 + 7, y1 - 19, title, Cyan, 2);
            return (x0 + 2, x1 - 2);
        }

        // horizontal bar gauge: frame, fill to f (0..1), and a red-line tick at `mark` (0..1; negative for none)
        static void Bar(Canvas c, int x, int y, int w, int h, float f, Color32 col, float mark)
        {
            c.Fill(x, y, w, h, Grey); c.Fill(x + 1, y + 1, w - 2, h - 2, Black);
            c.Fill(x + 1, y + 1, Mathf.RoundToInt((w - 2) * Mathf.Clamp01(f)), h - 2, col);
            if (mark >= 0f && mark <= 1f) c.Fill(x + Mathf.RoundToInt(w * mark) - 1, y - 2, 2, h + 4, Red);
        }

        // a labelled value tile (the flight display's data blocks)
        static void Tile(Canvas c, int x0, int y0, int x1, int y1, string label, string value, Color32 col)
        {
            c.Fill(x0, y0, x1 - x0, 2, Grey); c.Fill(x0, y1 - 2, x1 - x0, 2, Grey); c.Fill(x0, y0, 2, y1 - y0, Grey); c.Fill(x1 - 2, y0, 2, y1 - y0, Grey);
            c.Text(x0 + 6, y1 - 20, label, Cyan, 2);
            int sc = TW(value, 3) <= x1 - x0 - 12 ? 3 : 2;
            c.Text(x1 - 6 - TW(value, sc), y0 + 8, value, col, sc);
        }

        // ---------- stores (centre MFD, under the tactical display) ----------
        public static void Sms(Canvas c, FlightState s)
        {
            int W = c.W, H = c.H;
            c.Fill(0, 0, W, H, Black);
            const int colX = 262;
            c.Text(4, H - 18, "STORES", Cyan, 2);
            int y = H - 48;
            if (s.stations.Count == 0) c.Text(8, y, "NONE", DimGreen, 3);
            foreach (var (name, ammo, full, selected) in s.stations)
            {
                if (y < 2) break;
                if (selected) { c.Fill(2, y - 4, colX - 8, 29, DarkCyan); c.Fill(2, y - 4, 4, 29, Cyan); }
                string n = (name.Length > 8 ? name.Substring(0, 8) : name).ToUpperInvariant();
                c.Text(10, y, n, selected ? Cyan : White, 3);
                string a = ammo.ToString();
                c.Text(colX - 10 - TW(a, 3), y, a, ammo == 0 ? Grey : selected ? Cyan : Green, 3);
                y -= 32;
            }
            c.Fill(colX - 2, 4, 2, H - 8, Grey);
            int ry = H - 18;
            c.Text(colX + 6, ry, "CMS", Cyan, 2); ry -= 26;
            foreach (var (name, ammo) in s.countermeasures)
            {
                if (ry < 96) break;
                string n = (name ?? "").ToUpperInvariant(); n = n.Replace("RADAR ", "").Replace("IR ", ""); if (n.Length > 6) n = n.Substring(0, 6);
                c.Text(colX + 6, ry, n, White, 2);
                c.Text(W - 4 - TW(ammo.ToString(), 2), ry, ammo.ToString(), ammo == 0 ? Red : Green, 2);
                ry -= 22;
            }
            c.Text(colX + 6, 70, "TGT", Cyan, 2);
            if (s.target != null)
            {
                string t = s.target.ToUpperInvariant(); if (t.Length > 11) t = t.Substring(0, 11);
                c.Text(colX + 6, 46, t, White, 2);
                string km = s.targetKm.ToString(s.targetKm < 10f ? "0.0" : "0") + "KM";
                c.Text(colX + 6, 8, km, Yellow, 3);
            }
            else c.Text(colX + 6, 46, "NONE", DimGreen, 2);
        }

        // ---------- standby flight display (small screen right of the centre MFD) ----------
        public static void Stby(Canvas c, FlightState s)
        {
            int W = c.W, H = c.H, cx = W / 2, cy = H / 2;
            c.Fill(0, 0, W, H, Black);
            Horizon(c, 0, 0, W, H, cx, cy, s, 2.2f);
            c.Line(new Vector2(cx - 34, cy), new Vector2(cx - 10, cy), Yellow, 4); c.Line(new Vector2(cx + 10, cy), new Vector2(cx + 34, cy), Yellow, 4);
            c.Fill(cx - 3, cy - 3, 7, 7, Yellow);
            string ias = Mathf.RoundToInt(s.iasKmh).ToString(), alt = Mathf.RoundToInt(s.altM).ToString();
            c.Fill(0, H - 20, TW(ias, 2) + 6, 20, Black); c.Text(3, H - 17, ias, White, 2);
            int asz = alt.Length > 4 ? 1 : 2;
            c.Fill(W - TW(alt, asz) - 6, H - 20, TW(alt, asz) + 6, 20, Black); c.Text(W - TW(alt, asz) - 3, H - 17, alt, White, asz);
        }

        // ---------- up-front control panel window: time and the selected weapon ----------
        public static void Ufc(Canvas c, FlightState s)
        {
            int W = c.W, H = c.H;
            c.Fill(0, 0, W, H, Black);
            string tm = s.hours.ToString("00") + ":" + s.minutes.ToString("00") + ":" + Mathf.FloorToInt(s.seconds).ToString("00");
            c.Text((W - TW(tm, 3)) / 2, H - 30, tm, Green, 3);
            string w = "NO WPN";
            foreach (var (name, ammo, full, selected) in s.stations)
                if (selected) { w = (name.Length > 9 ? name.Substring(0, 9) : name).ToUpperInvariant() + " " + ammo; break; }
            c.Text((W - TW(w, 3)) / 2, 12, w, Green, 3);
        }

        // ---------- helpers ----------
        static int TW(string t, int scale) => t.Length * 6 * scale - scale;

        // sky / ground split over a rectangle, rolled and pitched as the outside world
        static void Horizon(Canvas c, int x0, int y0, int x1, int y1, int cx, int cy, FlightState s, float ppd)
        {
            float r = s.rollDeg * Mathf.Deg2Rad;   // right bank (+) turns the outside world, and the horizon, counter-clockwise
            float sin = Mathf.Sin(r), cos = Mathf.Cos(r), off = s.pitchDeg * ppd;
            for (int y = Math.Max(0, y0); y < Math.Min(c.H, y1); y++)
            {
                int row = y * c.W;
                for (int x = Math.Max(0, x0); x < Math.Min(c.W, x1); x++)
                    c.px[row + x] = -(x - cx) * sin + (y - cy) * cos + off > 0f ? Sky : Ground;
            }
        }

        // round dial, 270 degrees from 7:30 clockwise to 4:30: value arc filled, red above `red`, white pointer
        static void Dial(Canvas c, int cx, int cy, float r, float v, float lo, float hi, float red)
        {
            const float a0 = -135f, a1 = 135f;   // degrees from 12 o'clock, clockwise
            float f = Mathf.Clamp01((v - lo) / (hi - lo)), fr = Mathf.Clamp01((red - lo) / (hi - lo));
            for (float a = a0; a <= a1; a += 1.5f)
            {
                float t = (a - a0) / (a1 - a0);
                var col = t >= fr ? Red : t <= f ? (v >= red ? Red : Green) : Grey;
                float rad = a * Mathf.Deg2Rad;
                var d = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
                c.Line(new Vector2(cx, cy) + d * (t <= f ? r - 10f : r - 4f), new Vector2(cx, cy) + d * r, col, 2);
            }
            float na = (a0 + f * (a1 - a0)) * Mathf.Deg2Rad;
            c.Line(new Vector2(cx, cy), new Vector2(cx + Mathf.Sin(na) * (r - 3f), cy + Mathf.Cos(na) * (r - 3f)), White, 3);
        }

        static Vector2 Rot(Vector2 v, float rad) { float co = Mathf.Cos(rad), si = Mathf.Sin(rad); return new Vector2(v.x * co - v.y * si, v.x * si + v.y * co); }

        static void ValueBox(Canvas c, int x, int y, int w, int h, string t, Color32 col, int scale)
        {
            c.Fill(x, y, w, h, Black);
            c.Fill(x, y, w, 2, White); c.Fill(x, y + h - 2, w, 2, White); c.Fill(x, y, 2, h, White); c.Fill(x + w - 2, y, 2, h, White);
            c.Text(x + (w - TW(t, scale)) / 2, y + (h - 7 * scale) / 2, t, col, scale);
        }
    }

    // ------------------------------------------------------------------------------------------------ a tiny software canvas
    public class Canvas
    {
        public readonly int W, H;
        public readonly Color32[] px;
        int cx0, cy0, cx1, cy1;   // clip rectangle

        public Canvas(int w, int h) { W = w; H = h; px = new Color32[w * h]; Clip(0, 0, w, h); L = new int[h]; R = new int[h]; for (int y = 0; y < h; y++) R[y] = w; }

        // the screen's shape: per row, the first and one past the last pixel inside it (a rectangle unless SetShape says otherwise)
        public readonly int[] L, R;

        /// <summary>The screen outline from its mesh's vertices in canvas pixels (a convex polygon; their hull is taken).</summary>
        public void SetShape(Vector2[] pts)
        {
            var hull = Hull(pts);
            if (hull.Count < 3) return;
            for (int y = 0; y < H; y++)
            {
                float yc = y + 0.5f, lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < hull.Count; i++)
                {
                    var a = hull[i]; var b = hull[(i + 1) % hull.Count];
                    if ((a.y - yc) * (b.y - yc) > 0f || Mathf.Approximately(a.y, b.y)) continue;
                    float x = a.x + (yc - a.y) / (b.y - a.y) * (b.x - a.x);
                    lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x);
                }
                if (lo > hi) { lo = 0; hi = W; }                       // past the top or bottom vertex: keep the row whole
                L[y] = Mathf.Clamp(Mathf.CeilToInt(lo), 0, W); R[y] = Mathf.Clamp(Mathf.FloorToInt(hi), 0, W);
            }
        }

        /// <summary>Widest left edge / narrowest right edge over rows y0..y1: a box there stays on the screen.</summary>
        public int Left(int y0, int y1) { int v = 0; for (int y = Math.Max(0, y0); y <= Math.Min(H - 1, y1); y++) v = Math.Max(v, L[y]); return v; }
        public int Right(int y0, int y1) { int v = W; for (int y = Math.Max(0, y0); y <= Math.Min(H - 1, y1); y++) v = Math.Min(v, R[y]); return v; }

        static List<Vector2> Hull(Vector2[] p)
        {
            var s = new List<Vector2>(p); s.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var h = new List<Vector2>();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            foreach (var q in s) { while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
            int lower = h.Count + 1;
            for (int i = s.Count - 2; i >= 0; i--) { var q = s[i]; while (h.Count >= lower && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
            h.RemoveAt(h.Count - 1);
            return h;
        }

        public void Clip(int x0, int y0, int x1, int y1) { cx0 = Math.Max(0, x0); cy0 = Math.Max(0, y0); cx1 = Math.Min(W, x1); cy1 = Math.Min(H, y1); }

        public void Fill(int x, int y, int w, int h, Color32 c)
        {
            int x0 = Math.Max(cx0, x), x1 = Math.Min(cx1, x + w), y0 = Math.Max(cy0, y), y1 = Math.Min(cy1, y + h);
            for (int yy = y0; yy < y1; yy++) { int row = yy * W; for (int xx = x0; xx < x1; xx++) px[row + xx] = c; }
        }

        // darken what is already drawn (tape backgrounds over the attitude display)
        public void Shade(int x, int y, int w, int h, float keep)
        {
            int x0 = Math.Max(cx0, x), x1 = Math.Min(cx1, x + w), y0 = Math.Max(cy0, y), y1 = Math.Min(cy1, y + h);
            for (int yy = y0; yy < y1; yy++)
                for (int xx = x0; xx < x1; xx++)
                {
                    var p = px[yy * W + xx];
                    px[yy * W + xx] = new Color32((byte)(p.r * keep), (byte)(p.g * keep), (byte)(p.b * keep), 255);
                }
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
            ['%'] = new[] { "##..#", "##..#", "...#.", "..#..", ".#...", "#..##", "#..##" },
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
