using System;
using System.Collections.Generic;
using System.IO;
using MiG29SMTTools;
using UnityEngine;

namespace MiG29SMTInstruments
{
    // Custom MFD pages made in the cockpit editor's page designer (Documents\CockpitEditor): mfd_pages.json next to this DLL.
    // A page is a list of widgets, each in a box given as fractions of the screen (x, y from the top-left, w, h). Custom screens
    // show one with the page name "c_<id>"; "assign" puts a custom page on a built-in screen (pfd, eng, sms, stby, ufc).
    // The editor draws the same widgets with the same canvas and font (static/mfd.js), so its preview matches the game.
    [Serializable] public class MfdFile { public MfdPage[] pages; public MfdAssign[] assign; }
    [Serializable] public class MfdAssign { public string screen, page; }
    [Serializable] public class MfdPage { public string id, name, bg; public float rate; public MfdWidget[] widgets; }
    [Serializable]
    public class MfdWidget
    {
        public string type, field, label, text, color, align;
        public float x, y, w, h;
        public int size, thick;
        public bool frame, fill;
        [NonSerialized] public Dictionary<string, object> opt;   // every option as written (newer widgets read theirs from here)
    }

    static partial class Pages
    {
        public static MfdFile LoadCustom()
        {
            try
            {
                string dir = Path.GetDirectoryName(typeof(Pages).Assembly.Location);
                string file = Path.Combine(dir ?? "", "mfd_pages.json");
                if (!File.Exists(file)) return null;
                var f = FromJson(MiG29Json.Parse(File.ReadAllText(file)));
                MiG29InstrumentsPlugin.Log.LogInfo($"MiG-29SMT custom MFD pages: {f.pages.Length}, on built-in screens: {f.assign.Length} ({file})");
                return f;
            }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogError($"mfd_pages.json: {e.Message}"); return null; }
        }

        /// <summary>Pages and assignments from parsed JSON (mfd_pages.json, or a cockpit setup's "mfd").</summary>
        public static MfdFile FromJson(object root)
        {
            var f = new MfdFile();
            var pages = new List<MfdPage>();
            foreach (var pd in MiG29Json.Arr(root, "pages"))
            {
                var p = new MfdPage { id = MiG29Json.Str(pd, "id"), name = MiG29Json.Str(pd, "name"), bg = MiG29Json.Str(pd, "bg"), rate = MiG29Json.Num(pd, "rate") };
                var ws = new List<MfdWidget>();
                foreach (var wd in MiG29Json.Arr(pd, "widgets"))
                    ws.Add(new MfdWidget
                    {
                        type = MiG29Json.Str(wd, "type"), field = MiG29Json.Str(wd, "field"), label = MiG29Json.Str(wd, "label"), text = MiG29Json.Str(wd, "text"),
                        color = MiG29Json.Str(wd, "color"), align = MiG29Json.Str(wd, "align"),
                        x = MiG29Json.Num(wd, "x"), y = MiG29Json.Num(wd, "y"), w = MiG29Json.Num(wd, "w"), h = MiG29Json.Num(wd, "h"),
                        size = (int)MiG29Json.Num(wd, "size"), thick = (int)MiG29Json.Num(wd, "thick"), frame = MiG29Json.Bool(wd, "frame"), fill = MiG29Json.Bool(wd, "fill"),
                        opt = wd as Dictionary<string, object>,
                    });
                p.widgets = ws.ToArray();
                pages.Add(p);
            }
            f.pages = pages.ToArray();
            var asg = new List<MfdAssign>();
            foreach (var ad in MiG29Json.Arr(root, "assign")) asg.Add(new MfdAssign { screen = MiG29Json.Str(ad, "screen"), page = MiG29Json.Str(ad, "page") });
            f.assign = asg.ToArray();
            return f;
        }

        public static Action<Canvas, FlightState> Custom(MfdPage p) => (c, s) => DrawCustom(c, s, p);

        static Color32 Col(string hex, Color32 fallback)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            return fallback;
        }

        static void DrawCustom(Canvas c, FlightState s, MfdPage p)
        {
            c.Clip(0, 0, c.W, c.H);
            c.Fill(0, 0, c.W, c.H, Col(p.bg, Black));
            if (p.widgets == null) return;
            foreach (var w in p.widgets)
            {
                int x0 = Mathf.RoundToInt(w.x * c.W), x1 = Mathf.RoundToInt((w.x + w.w) * c.W);
                int y1 = Mathf.RoundToInt((1f - w.y) * c.H), y0 = Mathf.RoundToInt((1f - w.y - w.h) * c.H);
                if (x1 - x0 < 4 || y1 - y0 < 4) continue;
                c.Clip(x0, y0, x1, y1);
                var bgc = OS(w, "bg"); if (bgc.Length > 0) c.Fill(x0, y0, x1 - x0, y1 - y0, Col(bgc, Black));
                try { Widget(c, s, w, x0, y0, x1, y1); }
                catch (Exception e) { MiG29InstrumentsPlugin.Log.LogWarning($"widget {w.type}: {e.Message}"); }
                c.Clip(x0, y0, x1, y1);
                var bdc = OS(w, "border"); if (bdc.Length > 0) Frame(c, x0, y0, x1, y1, Col(bdc, Grey), Math.Max(1, (int)O(w, "borderW", 2f)));
                c.Clip(0, 0, c.W, c.H);
            }
        }

        // a widget's own options, as written by the page designer (missing = default)
        static float O(MfdWidget w, string k, float def) => MiG29Json.Num(w.opt, k, def);
        static string OS(MfdWidget w, string k) => MiG29Json.Str(w.opt, k);
        static bool OB(MfdWidget w, string k, bool def = false) => MiG29Json.Bool(w.opt, k, def);

        static int Fit(string t, int w, int h, int max)
        {
            for (int sc = Math.Max(1, max); sc > 1; sc--) if (TW(t, sc) <= w && 7 * sc <= h) return sc;
            return 1;
        }

        static void Frame(Canvas c, int x0, int y0, int x1, int y1, Color32 col, int t = 2)
        {
            c.Fill(x0, y0, x1 - x0, t, col); c.Fill(x0, y1 - t, x1 - x0, t, col); c.Fill(x0, y0, t, y1 - y0, col); c.Fill(x1 - t, y0, t, y1 - y0, col);
        }

        static void Widget(Canvas c, FlightState s, MfdWidget w, int x0, int y0, int x1, int y1)
        {
            int W = x1 - x0, H = y1 - y0, cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
            var main = Col(w.color, White);
            switch (w.type)
            {
                case "attitude":
                {
                    float ppd = H / 45f;
                    float r = s.rollDeg * Mathf.Deg2Rad, sin = Mathf.Sin(r), cos = Mathf.Cos(r);
                    {
                        Color32 sky = Col(OS(w, "sky"), Sky), gnd = Col(OS(w, "ground"), Ground); float off = s.pitchDeg * ppd;
                        for (int yy = Math.Max(0, y0); yy < Math.Min(c.H, y1); yy++)
                            for (int xx = Math.Max(0, x0); xx < Math.Min(c.W, x1); xx++)
                                c.px[yy * c.W + xx] = -(xx - cx) * sin + (yy - cy) * cos + off > 0f ? sky : gnd;
                    }
                    int step = Math.Max(5, (int)O(w, "ladderStep", 5f));
                    var dir = new Vector2(cos, sin); var nrm = new Vector2(-sin, cos);
                    int ts = W >= 200 ? 2 : 1;
                    for (int pp = -90; pp <= 90; pp += step)
                    {
                        float d = (pp - s.pitchDeg) * ppd;
                        if (Mathf.Abs(d) > H * 0.45f) continue;
                        var m = new Vector2(cx, cy) + nrm * d;
                        if (pp == 0) { c.Line(m - dir * W, m + dir * W, White, 3); continue; }
                        float half = pp % 10 == 0 ? W * 0.18f : W * 0.09f, gap = W * 0.06f;
                        c.Line(m - dir * half, m - dir * gap, White, 2); c.Line(m + dir * gap, m + dir * half, White, 2);
                        var tip = pp > 0 ? -nrm * 6f : nrm * 6f;
                        c.Line(m - dir * half, m - dir * half + tip, White, 2); c.Line(m + dir * half, m + dir * half + tip, White, 2);
                        if (pp % 10 == 0) { var at = m + dir * (half + 5f); c.Text((int)at.x, (int)at.y - 3 * ts, Mathf.Abs(pp).ToString(), White, ts); }
                    }
                    float br = Mathf.Min(W, H) / 2f - 14f;
                    if (br > 20f && !OB(w, "noBank"))
                    {
                        foreach (var k in new[] { -60f, -45f, -30f, -20f, -10f, 0f, 10f, 20f, 30f, 45f, 60f })
                        {
                            float a = k * Mathf.Deg2Rad, r1 = Mathf.Abs(k) % 30f == 0f ? br + 12f : br + 7f;
                            c.Line(new Vector2(cx + Mathf.Sin(a) * br, cy + Mathf.Cos(a) * br), new Vector2(cx + Mathf.Sin(a) * r1, cy + Mathf.Cos(a) * r1), White, 2);
                        }
                        var pa = new Vector2(cx - Mathf.Sin(r) * (br - 3f), cy + Mathf.Cos(r) * (br - 3f));
                        c.Tri(pa, pa + Rot(new Vector2(-7, -12), r), pa + Rot(new Vector2(7, -12), r), Yellow);
                    }
                    float wg = W * 0.2f, st = Mathf.Max(3f, W * 0.015f);
                    c.Line(new Vector2(cx - wg, cy), new Vector2(cx - wg * 0.35f, cy), Yellow, (int)st); c.Line(new Vector2(cx - wg * 0.35f, cy), new Vector2(cx - wg * 0.35f, cy - wg * 0.2f), Yellow, (int)st);
                    c.Line(new Vector2(cx + wg * 0.35f, cy), new Vector2(cx + wg, cy), Yellow, (int)st); c.Line(new Vector2(cx + wg * 0.35f, cy), new Vector2(cx + wg * 0.35f, cy - wg * 0.2f), Yellow, (int)st);
                    c.Fill(cx - 3, cy - 3, 7, 7, Yellow);
                    if (w.frame) Frame(c, x0, y0, x1, y1, Grey);
                    break;
                }
                case "speed_tape":
                case "alt_tape":
                {
                    bool spd = w.type == "speed_tape";
                    float v = spd ? s.iasKmh : s.altM, span = Mathf.Max(20f, O(w, "span", spd ? 300f : 1400f)), tick = spd ? 10f : 50f, lab = spd ? 50f : 200f;
                    if (spd && span > 800f) { tick = 50f; lab = 200f; } else if (!spd && span > 4000f) { tick = 200f; lab = 1000f; }
                    float ppu = H / span;
                    c.Fill(x0, y0, W, H, Black);
                    int ts = W >= 70 ? 2 : 1;
                    for (float u = Mathf.Floor((v - span / 2f) / tick) * tick; u <= v + span / 2f; u += tick)
                    {
                        if (spd && u < 0f) continue;
                        int y = cy + Mathf.RoundToInt((u - v) * ppu);
                        bool big = Mathf.Abs(u % (tick * 5f)) < 0.01f;
                        int len = big ? 12 : 7;
                        if (spd) c.Fill(x1 - len, y - 1, len, 3, main); else c.Fill(x0, y - 1, len, 3, main);
                        if (Mathf.Abs(u % lab) < 0.01f)
                        {
                            string t = Mathf.RoundToInt(u).ToString();
                            if (spd) c.Text(x1 - 15 - TW(t, ts), y - 7 * ts / 2, t, main, ts); else c.Text(x0 + 15, y - 7 * ts / 2, t, main, ts);
                        }
                    }
                    string val = Mathf.RoundToInt(v).ToString();
                    int bh = Math.Min(36, H / 4);
                    var vcol = spd ? (s.iasKmh > 1450f ? Red : White) : (s.radAltM < 60f && s.airborne && !s.gearDown ? Red : White);
                    c.Clip(0, 0, c.W, c.H);
                    ValueBox(c, x0, cy - bh / 2, W, bh, val, vcol, Fit(val, W - 8, bh - 8, 3));
                    if (w.frame) Frame(c, x0, y0, x1, y1, Grey);
                    break;
                }
                case "heading_tape":
                {
                    float hdg = Mathf.Repeat(s.headingDeg, 360f), ppd = W / 60f;
                    c.Fill(x0, y0, W, H, Black);
                    int ts = H >= 60 ? 2 : 1;
                    for (int dd = -32; dd <= 32; dd++)
                    {
                        float a = Mathf.Repeat(Mathf.Round(hdg) + dd, 360f);
                        int x = cx + Mathf.RoundToInt(Mathf.DeltaAngle(hdg, a) * ppd), ai = Mathf.RoundToInt(a) % 360;
                        if (ai % 10 == 0)
                        {
                            c.Fill(x - 1, y0 + 2, 3, 12, main);
                            if (ai % 20 == 0 || ppd > 6f) { string t = (ai / 10).ToString("00"); c.Text(x - TW(t, ts) / 2, y0 + 18, t, main, ts); }
                        }
                        else if (ai % 5 == 0) c.Fill(x, y0 + 2, 2, 7, main);
                    }
                    string hv = Mathf.RoundToInt(hdg).ToString("000");
                    int bh = Math.Min(34, H / 2), bw = TW(hv, 3) + 16;
                    ValueBox(c, cx - bw / 2, y1 - bh, bw, bh, hv, Green, Fit(hv, bw - 8, bh - 8, 3));
                    break;
                }
                case "value":
                {
                    string label = string.IsNullOrEmpty(w.label) ? FieldLabel(w.field) : w.label;
                    string val = FieldText(w.field, s);
                    int dec = (int)O(w, "decimals", -1f); float num = FieldNum(w.field, s);
                    if (dec >= 0 && !float.IsNaN(num)) val = num.ToString("F" + Math.Min(dec, 4), System.Globalization.CultureInfo.InvariantCulture);
                    val += OS(w, "unit").ToUpperInvariant();
                    var col = FieldWarn(w.field, s) ?? main;
                    if (w.frame) Frame(c, x0, y0, x1, y1, Grey);
                    int ls = label.Length > 0 ? Math.Max(1, Math.Min(2, (H - 8) / 22)) : 0;
                    if (ls > 0) c.Text(x0 + 6, y1 - 6 - 7 * ls, label, Col(OS(w, "labelColor"), Cyan), ls);
                    int vh = H - 10 - (ls > 0 ? 7 * ls + 4 : 0);
                    int vs = w.size > 0 ? w.size : Fit(val, W - 12, vh, 5);
                    c.Text(x1 - 6 - TW(val, vs), y0 + 5, val, col, vs);
                    break;
                }
                case "bar":
                {
                    float f = FieldFrac(w.field, s);
                    var col = FieldWarn(w.field, s) ?? Col(w.color, Green);
                    c.Fill(x0, y0, W, H, Grey); c.Fill(x0 + 1, y0 + 1, W - 2, H - 2, Black);
                    if (H > W) c.Fill(x0 + 1, y0 + 1, W - 2, Mathf.RoundToInt((H - 2) * Mathf.Clamp01(f)), col);
                    else c.Fill(x0 + 1, y0 + 1, Mathf.RoundToInt((W - 2) * Mathf.Clamp01(f)), H - 2, col);
                    break;
                }
                case "text":
                {
                    string t = (w.text ?? "").ToUpperInvariant();
                    int sc = w.size > 0 ? w.size : Fit(t, W - 4, H - 4, 3);
                    int x = w.align == "center" ? cx - TW(t, sc) / 2 : w.align == "right" ? x1 - 2 - TW(t, sc) : x0 + 2;
                    c.Text(x, cy - 7 * sc / 2, t, main, sc);
                    break;
                }
                case "box":
                {
                    var col = Col(w.color, Grey);
                    if (w.fill) c.Fill(x0, y0, W, H, col); else Frame(c, x0, y0, x1, y1, col, Math.Max(1, w.thick));
                    break;
                }
                case "clock":
                {
                    string t = s.hours.ToString("00") + ":" + s.minutes.ToString("00") + ":" + Mathf.FloorToInt(s.seconds).ToString("00");
                    int sc = w.size > 0 ? w.size : Fit(t, W - 4, H - 4, 5);
                    c.Text(cx - TW(t, sc) / 2, cy - 7 * sc / 2, t, Col(w.color, Cyan), sc);
                    break;
                }
                case "rwr":
                {
                    float R = Mathf.Min(W, H) / 2f - 6f;
                    c.Circle(cx, cy, R, DimGreen); c.Circle(cx, cy, R - 1, DimGreen); c.Circle(cx, cy, R * 0.5f, DimGreen);
                    for (int k = 0; k < 12; k++)
                    {
                        float a = k * 30f * Mathf.Deg2Rad;
                        c.Line(new Vector2(cx + Mathf.Sin(a) * R * 0.9f, cy + Mathf.Cos(a) * R * 0.9f), new Vector2(cx + Mathf.Sin(a) * R, cy + Mathf.Cos(a) * R), Green, 3);
                    }
                    c.Line(new Vector2(cx, cy - 10), new Vector2(cx, cy + 12), Green, 3); c.Line(new Vector2(cx - 12, cy + 3), new Vector2(cx + 12, cy + 3), Green, 3);
                    int ts = R > 80f ? 3 : 2;
                    foreach (var (bearing, type) in s.rwr)
                    {
                        float a = bearing * Mathf.Deg2Rad;
                        float rr = type < 0 ? R * 0.42f : R * (0.92f - 0.55f * Mathf.Clamp01(s.rwrPower));
                        int x = cx + Mathf.RoundToInt(Mathf.Sin(a) * rr), y = cy + Mathf.RoundToInt(Mathf.Cos(a) * rr);
                        if (type < 0) { if (s.blink) { c.Circle(x, y, 5 * ts, Red); c.Text(x - 3 * ts, y - 7 * ts / 2, "M", Red, ts); } continue; }
                        string t = type < ThreatLetter.Length ? ThreatLetter[type] : "U";
                        c.Text(x - TW(t, ts) / 2, y - 7 * ts / 2, t, s.lockedOn ? Red : Yellow, ts);
                        if (s.lockedOn) c.Circle(x, y, 6 * ts, Red);
                    }
                    if (s.lockedOn && s.blink) c.Text(x0 + 4, y1 - 18, "LOCK", Red, 2);
                    break;
                }
                case "stores":
                {
                    int sc = w.size > 0 ? w.size : (H >= 120 && W >= 200 ? 3 : 2), rh = 7 * sc + 9, y = y1 - rh;
                    if (w.frame) { c.Text(x0 + 4, y1 - 18, "STORES", Cyan, 2); y = y1 - 20 - rh; }
                    if (s.stations.Count == 0) c.Text(x0 + 8, y, "NONE", DimGreen, sc);
                    int maxRows = (int)O(w, "rows", 0f), shown = 0;
                    foreach (var (name, ammo, full, selected) in s.stations)
                    {
                        if (y < y0 || (maxRows > 0 && shown++ >= maxRows)) break;
                        if (selected) { c.Fill(x0 + 2, y - 4, W - 4, rh - 2, DarkCyan); c.Fill(x0 + 2, y - 4, 4, rh - 2, Cyan); }
                        string n = (name.Length > 8 ? name.Substring(0, 8) : name).ToUpperInvariant(), a = ammo.ToString();
                        c.Text(x0 + 10, y, n, selected ? Cyan : White, sc);
                        c.Text(x1 - 6 - TW(a, sc), y, a, ammo == 0 ? Grey : selected ? Cyan : Green, sc);
                        y -= rh;
                    }
                    break;
                }
                case "cms":
                {
                    int y = y1 - 18;
                    c.Text(x0 + 4, y, "CMS", Cyan, 2); y -= 24;
                    foreach (var (name, ammo) in s.countermeasures)
                    {
                        if (y < y0) break;
                        string n = (name ?? "").ToUpperInvariant().Replace("RADAR ", "").Replace("IR ", ""); if (n.Length > 6) n = n.Substring(0, 6);
                        c.Text(x0 + 4, y, n, White, 2);
                        c.Text(x1 - 4 - TW(ammo.ToString(), 2), y, ammo.ToString(), ammo == 0 ? Red : Green, 2);
                        y -= 22;
                    }
                    break;
                }
                case "target":
                {
                    c.Text(x0 + 4, y1 - 18, "TGT", Cyan, 2);
                    if (s.target != null)
                    {
                        string t = s.target.ToUpperInvariant(); if (t.Length > 11) t = t.Substring(0, 11);
                        c.Text(x0 + 4, y1 - 42, t, White, 2);
                        string km = s.targetKm.ToString(s.targetKm < 10f ? "0.0" : "0") + "KM";
                        c.Text(x0 + 4, y0 + 4, km, Yellow, Fit(km, W - 8, Math.Max(8, H - 50), 3));
                    }
                    else c.Text(x0 + 4, y1 - 42, "NONE", DimGreen, 2);
                    break;
                }
                case "engines":
                {
                    if (w.frame) Frame(c, x0, y0, x1, y1, Grey);
                    int colW = (W - 30) / 2, sc = H >= 100 ? 3 : 2;
                    foreach (var (k, rpm, egt, side) in new[] { (0, s.rpmL, s.egtL, "L"), (1, s.rpmR, s.egtR, "R") })
                    {
                        int xa = x0 + 8 + k * (colW + 14);
                        string rv = Mathf.RoundToInt(rpm) + "%";
                        int top = y1 - 8 - 7 * sc;
                        c.Text(xa, top, side, Cyan, sc);
                        c.Text(xa + colW - TW(rv, sc), top, rv, rpm >= 104 ? Red : White, sc);
                        Bar(c, xa, top - 16, colW, 10, rpm / 110f, rpm >= 104 ? Red : Green, 104f / 110f);
                        string ev = "EGT " + Mathf.RoundToInt(egt);
                        if (top - 40 > y0) { c.Text(xa + colW - TW(ev, 2), top - 38, ev, egt >= 950 ? Red : White, 2); Bar(c, xa, top - 50, colW, 7, (egt - 200f) / 800f, egt >= 950 ? Red : Amber, 750f / 800f); }
                    }
                    break;
                }
                case "fuel":
                {
                    if (w.frame) Frame(c, x0, y0, x1, y1, Grey);
                    string fk = Mathf.RoundToInt(s.fuelKg).ToString();
                    var fcol = s.fuelFrac < 0.10f ? Red : s.fuelFrac < 0.25f ? Amber : White;
                    int sc = Fit(fk + "KG", W - 16, Math.Max(10, H - 34), 3);
                    c.Text(x0 + 8, y1 - 8 - 7 * sc, fk, fcol, sc); c.Text(x0 + 14 + TW(fk, sc), y1 - 8 - 7 * 2, "KG", Green, 2);
                    Bar(c, x0 + 6, y1 - 24 - 7 * sc, W - 12, 10, s.fuelFrac, fcol.Equals(White) ? Green : fcol, -1f);
                    if ((s.extFuelKg > 0.5f || s.ptbEmpty) && y1 - 50 - 7 * sc > y0) { string pt = "PTB " + Mathf.RoundToInt(s.extFuelKg); c.Text(x0 + 8, y1 - 46 - 7 * sc, pt, s.ptbEmpty ? Amber : White, 2); }
                    break;
                }
                case "signature":
                {
                    if (w.frame) Frame(c, x0, y0, x1, y1, Grey);
                    int by = y1 - 24;
                    string irTxt = s.irFrac < 0.35f ? "LOW" : s.irFrac < 0.7f ? "MED" : "HIGH";
                    var irCol = s.irFrac < 0.35f ? Green : s.irFrac < 0.7f ? Amber : Red;
                    c.Text(x0 + 6, by, "IR", White, 2);
                    Bar(c, x0 + 34, by - 1, Math.Max(10, W - 110), 16, s.irFrac, irCol, -1f);
                    c.Text(x1 - 6 - TW(irTxt, 2), by, irTxt, irCol, 2);
                    float extra = s.rcsClean > 0f ? s.rcs / s.rcsClean - 1f : 0f;
                    string rv = s.rcs.ToString(s.rcs < 10f ? "0.00" : "0.0");
                    c.Text(x0 + 6, by - 28, "RCS", White, 2); c.Text(x0 + 50, by - 28, rv, Cyan, 2);
                    if (extra > 0.01f) c.Text(x0 + 58 + TW(rv, 2), by - 28, "+" + Mathf.RoundToInt(extra * 100f) + "%", Amber, 2);
                    string rd = s.radarOn ? "RDR ON" : "RDR OFF";
                    c.Text(x1 - 6 - TW(rd, 2), by - 28, rd, s.radarOn ? Green : Grey, 2);
                    break;
                }
                case "cautions":
                {
                    var on = MiG29InstrumentMath.Lamps(s);
                    int cols = W >= 220 ? 2 : 1, cw = (W - 4 - 4 * cols) / cols, row = 0;
                    foreach (var (id, text, warn) in Cautions)
                    {
                        if (!on.Contains("cau_" + id) && !(id == "fire_l" && s.fireL) && !(id == "fire_r" && s.fireR) && !(id == "missile" && s.missileIncoming)) continue;
                        int col = row % cols, line = row / cols, y = y1 - 22 - line * 23;
                        if (y < y0) break;
                        string tt = text.Length > 9 ? text.Substring(0, 9) : text;
                        if (warn) c.Fill(x0 + 2 + col * (cw + 4), y - 3, cw, 20, WarnBack);
                        c.Text(x0 + 6 + col * (cw + 4), y, tt, warn ? Red : Amber, 2);
                        row++;
                    }
                    if (row == 0) c.Text(x0 + 6, y1 - 22, "NO CAUTIONS", DimGreen, 2);
                    break;
                }
                case "dial":
                {
                    float v = FieldNum(w.field, s); if (float.IsNaN(v)) v = 0f;
                    var (dlo, dhi) = FieldRange(w.field);
                    float lo = O(w, "min", dlo), hi = O(w, "max", dhi), red = O(w, "red", hi + 1f);
                    float R = Mathf.Min(W, H) / 2f - 4f;
                    if (R < 8f) break;
                    for (int k = 0; k <= 10; k++)   // scale ticks, 270 degrees from 7:30 to 4:30
                    {
                        float a = (-135f + 27f * k) * Mathf.Deg2Rad; var d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                        c.Line(new Vector2(cx, cy) + d * (R - (k % 5 == 0 ? 9f : 5f)), new Vector2(cx, cy) + d * R, main, 2);
                    }
                    Dial(c, cx, cy, R - 12f, v, lo, hi, red);
                    string lbl = string.IsNullOrEmpty(w.label) ? FieldLabel(w.field) : w.label.ToUpperInvariant();
                    int ls = R > 50f ? 2 : 1;
                    c.Text(cx - TW(lbl, ls) / 2, cy + (int)(R * 0.35f), lbl, Col(OS(w, "labelColor"), Cyan), ls);
                    if (OB(w, "showValue", true)) { string t = FieldText(w.field, s); int vs = Fit(t, (int)(R * 1.2f), (int)(R * 0.4f), 3); c.Text(cx - TW(t, vs) / 2, cy - (int)(R * 0.55f), t, FieldWarn(w.field, s) ?? White, vs); }
                    break;
                }
                case "compass":
                {
                    float hdg = Mathf.Repeat(s.headingDeg, 360f), R = Mathf.Min(W, H) / 2f - 4f;
                    if (R < 12f) break;
                    c.Circle(cx, cy, R, Grey);
                    int ts = R > 70f ? 2 : 1;
                    for (int d = 0; d < 360; d += 5)
                    {
                        float a = (d - hdg) * Mathf.Deg2Rad; var u = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                        float len = d % 30 == 0 ? 10f : d % 10 == 0 ? 6f : 3f;
                        c.Line(new Vector2(cx, cy) + u * (R - len), new Vector2(cx, cy) + u * R, main, d % 10 == 0 ? 2 : 1);
                        if (d % 30 == 0)
                        {
                            string t = d == 0 ? "N" : d == 90 ? "E" : d == 180 ? "S" : d == 270 ? "W" : (d / 10).ToString();
                            var at = new Vector2(cx, cy) + u * (R - 12f - 5f * ts);
                            c.Text((int)at.x - TW(t, ts) / 2, (int)at.y - 7 * ts / 2, t, d % 90 == 0 ? Yellow : main, ts);
                        }
                    }
                    c.Tri(new Vector2(cx, cy + R + 1), new Vector2(cx - 6, cy + R - 9), new Vector2(cx + 6, cy + R - 9), Yellow);   // lubber line
                    c.Line(new Vector2(cx, cy - 10), new Vector2(cx, cy + 12), Yellow, 3); c.Line(new Vector2(cx - 10, cy + 4), new Vector2(cx + 10, cy + 4), Yellow, 3);
                    c.Line(new Vector2(cx - 5, cy - 9), new Vector2(cx + 5, cy - 9), Yellow, 2);
                    break;
                }
                case "aoa_index":
                {
                    float lo = O(w, "lo", 8f), hi = O(w, "hi", 14f), a = s.aoaDeg;
                    int sz = Math.Min(W - 4, H / 3 - 4), hh = sz / 2;
                    if (sz < 6) break;
                    var dim = new Color32(40, 44, 48, 255);
                    int yt = y1 - H / 6, ym = cy, yb = y0 + H / 6;
                    var top = a > hi ? Red : dim; var mid = a >= lo && a <= hi ? Green : dim; var bot = a < lo ? Amber : dim;
                    c.Line(new Vector2(cx - hh, yt + hh / 2), new Vector2(cx, yt - hh / 2), top, 4); c.Line(new Vector2(cx, yt - hh / 2), new Vector2(cx + hh, yt + hh / 2), top, 4);
                    c.Circle(cx, ym, hh * 0.7f, mid); c.Circle(cx, ym, hh * 0.7f - 1, mid); c.Circle(cx, ym, hh * 0.7f - 2, mid);
                    c.Line(new Vector2(cx - hh, yb - hh / 2), new Vector2(cx, yb + hh / 2), bot, 4); c.Line(new Vector2(cx, yb + hh / 2), new Vector2(cx + hh, yb - hh / 2), bot, 4);
                    break;
                }
                case "vtape":
                {
                    float v = FieldNum(w.field, s); if (float.IsNaN(v)) v = 0f;
                    float span = Mathf.Max(1f, O(w, "span", FieldSpan(w.field))), tick = O(w, "tick", span / 20f);
                    if (tick <= 0f) tick = span / 20f;
                    float ppu = H / span;
                    int ts = W >= 70 ? 2 : 1; bool left = w.align == "right";
                    c.Fill(x0, y0, W, H, Black);
                    for (int k = Mathf.FloorToInt((v - span / 2f) / tick); k * tick <= v + span / 2f; k++)
                    {
                        float u = k * tick;
                        int y = cy + Mathf.RoundToInt((u - v) * ppu);
                        bool big = ((k % 5) + 5) % 5 == 0;
                        int len = big ? 12 : 7;
                        if (left) c.Fill(x0, y - 1, len, 3, main); else c.Fill(x1 - len, y - 1, len, 3, main);
                        if (big) { string t = Mathf.RoundToInt(u).ToString(); if (left) c.Text(x0 + 15, y - 7 * ts / 2, t, main, ts); else c.Text(x1 - 15 - TW(t, ts), y - 7 * ts / 2, t, main, ts); }
                    }
                    string val = FieldText(w.field, s); int bh = Math.Min(36, H / 4);
                    c.Clip(0, 0, c.W, c.H);
                    ValueBox(c, x0, cy - bh / 2, W, bh, val, FieldWarn(w.field, s) ?? White, Fit(val, W - 8, bh - 8, 3));
                    break;
                }
                case "line":
                {
                    var col = Col(w.color, Grey); int th = Math.Max(1, w.thick);
                    string d = string.IsNullOrEmpty(w.align) || w.align == "left" ? "h" : w.align;
                    if (d == "h") c.Fill(x0, cy - th / 2, W, th, col);
                    else if (d == "v") c.Fill(cx - th / 2, y0, th, H, col);
                    else if (d == "d1") c.Line(new Vector2(x0, y1 - 1), new Vector2(x1 - 1, y0), col, th);
                    else c.Line(new Vector2(x0, y0), new Vector2(x1 - 1, y1 - 1), col, th);
                    break;
                }
                case "image":
                {
                    var img = Image(OS(w, "img"));
                    if (img.w == 0) break;
                    for (int yy = 0; yy < H; yy++)
                    {
                        int sy = Mathf.Clamp((int)((yy + 0.5f) * img.h / H), 0, img.h - 1), y = y1 - 1 - yy;   // yy from the top
                        for (int xx = 0; xx < W; xx++)
                        {
                            var px = img.px[(img.h - 1 - sy) * img.w + Mathf.Clamp((int)((xx + 0.5f) * img.w / W), 0, img.w - 1)];
                            if (px.a > 127) c.Plot(x0 + xx, y, px);
                        }
                    }
                    break;
                }
                case "warnings":
                {
                    int y = y1 - 26, sc = Fit("MISSILE", W - 8, 22, 3);
                    if (s.missileIncoming && s.blink) { c.Fill(cx - TW("MISSILE", sc) / 2 - 4, y - 4, TW("MISSILE", sc) + 8, 7 * sc + 8, WarnBack); c.Text(cx - TW("MISSILE", sc) / 2, y, "MISSILE", Red, sc); y -= 7 * sc + 12; }
                    if (s.lockedOn) { c.Text(cx - TW("LOCK", 2) / 2, y, "LOCK", Red, 2); y -= 22; }
                    if (s.gearDown || s.gearMoving) { c.Text(cx - TW("GEAR", 2) / 2, y, "GEAR", s.gearMoving ? Amber : Green, 2); y -= 22; }
                    if (s.throttle > 0.9f && (s.rpmL > 90 || s.rpmR > 90)) c.Text(cx - TW("A/B", 2) / 2, y, "A/B", Amber, 2);
                    break;
                }
            }
        }

        static readonly Dictionary<string, (int w, int h, Color32[] px)> images = new Dictionary<string, (int, int, Color32[])>();
        static (int w, int h, Color32[] px) Image(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return (0, 0, null);
            if (images.TryGetValue(b64, out var cached)) return cached;
            (int, int, Color32[]) r = (0, 0, null);
            try
            {
                string data = b64; int k = data.IndexOf(','); if (data.StartsWith("data:") && k > 0) data = data.Substring(k + 1);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (ImageConversion.LoadImage(tex, Convert.FromBase64String(data), false)) r = (tex.width, tex.height, tex.GetPixels32());   // rows bottom-up
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogWarning($"widget image: {e.Message}"); }
            images[b64] = r;
            return r;
        }

        static float FieldNum(string f, FlightState s)
        {
            switch (f)
            {
                case "ias": return s.iasKmh; case "alt": return s.altM; case "radalt": return s.radAltM; case "mach": return s.mach; case "g": return s.g;
                case "aoa": return s.aoaDeg; case "vs": return s.vsMs; case "heading": return Mathf.Repeat(s.headingDeg, 360f); case "pitch": return s.pitchDeg; case "roll": return s.rollDeg;
                case "throttle": return s.throttle * 100f; case "rpm_l": return s.rpmL; case "rpm_r": return s.rpmR; case "egt_l": return s.egtL; case "egt_r": return s.egtR;
                case "fuel": return s.fuelKg; case "fuel_pct": return s.fuelFrac * 100f; case "ptb": return s.extFuelKg; case "hyd": return s.hydraulic; case "oxy": return s.oxygen;
                case "target_km": return s.target != null ? s.targetKm : float.NaN; case "rcs": return s.rcs; case "ir": return s.irFrac * 100f;
                default: return float.NaN;
            }
        }

        static (float, float) FieldRange(string f)
        {
            switch (f)
            {
                case "ias": return (0f, 1600f); case "alt": return (0f, 15000f); case "radalt": return (0f, 1500f); case "mach": return (0f, 2.5f); case "g": return (-3f, 9f);
                case "aoa": return (-5f, 30f); case "vs": return (-100f, 100f); case "heading": return (0f, 360f); case "pitch": return (-90f, 90f); case "roll": return (-180f, 180f);
                case "rpm_l": case "rpm_r": return (0f, 110f); case "egt_l": case "egt_r": return (0f, 1000f); case "fuel": return (0f, 4500f); case "ptb": return (0f, 3000f);
                case "hyd": return (0f, 250f); case "oxy": return (0f, 150f); case "target_km": return (0f, 100f); case "rcs": return (0f, 5f);
                default: return (0f, 100f);
            }
        }

        static float FieldSpan(string f) { var (lo, hi) = FieldRange(f); return Mathf.Max(1f, (hi - lo) / 5f); }

        static string FieldLabel(string f)
        {
            switch (f)
            {
                case "ias": return "IAS"; case "alt": return "ALT"; case "radalt": return "RALT"; case "mach": return "MACH"; case "g": return "G";
                case "aoa": return "AOA"; case "vs": return "V/S"; case "heading": return "HDG"; case "pitch": return "PITCH"; case "roll": return "ROLL";
                case "throttle": return "THR"; case "rpm_l": return "RPM L"; case "rpm_r": return "RPM R"; case "egt_l": return "EGT L"; case "egt_r": return "EGT R";
                case "fuel": return "FUEL KG"; case "fuel_pct": return "FUEL"; case "ptb": return "PTB"; case "hyd": return "HYD"; case "oxy": return "OXY";
                case "time": return "TIME"; case "target_km": return "TGT KM"; case "rcs": return "RCS"; case "ir": return "IR";
                default: return (f ?? "").ToUpperInvariant();
            }
        }

        static string FieldText(string f, FlightState s)
        {
            switch (f)
            {
                case "ias": return Mathf.RoundToInt(s.iasKmh).ToString();
                case "alt": return Mathf.RoundToInt(s.altM).ToString();
                case "radalt": return Mathf.RoundToInt(s.radAltM).ToString();
                case "mach": return s.mach.ToString("0.00");
                case "g": return s.g.ToString("0.0");
                case "aoa": return Mathf.RoundToInt(s.aoaDeg).ToString();
                case "vs": return (s.vsMs >= 0 ? "+" : "") + Mathf.RoundToInt(s.vsMs);
                case "heading": return Mathf.RoundToInt(Mathf.Repeat(s.headingDeg, 360f)).ToString("000");
                case "pitch": return Mathf.RoundToInt(s.pitchDeg).ToString();
                case "roll": return Mathf.RoundToInt(s.rollDeg).ToString();
                case "throttle": return Mathf.RoundToInt(s.throttle * 100f) + "%";
                case "rpm_l": return Mathf.RoundToInt(s.rpmL) + "%";
                case "rpm_r": return Mathf.RoundToInt(s.rpmR) + "%";
                case "egt_l": return Mathf.RoundToInt(s.egtL).ToString();
                case "egt_r": return Mathf.RoundToInt(s.egtR).ToString();
                case "fuel": return Mathf.RoundToInt(s.fuelKg).ToString();
                case "fuel_pct": return Mathf.RoundToInt(s.fuelFrac * 100f) + "%";
                case "ptb": return Mathf.RoundToInt(s.extFuelKg).ToString();
                case "hyd": return Mathf.RoundToInt(s.hydraulic).ToString();
                case "oxy": return Mathf.RoundToInt(s.oxygen).ToString();
                case "time": return s.hours.ToString("00") + ":" + s.minutes.ToString("00") + ":" + Mathf.FloorToInt(s.seconds).ToString("00");
                case "target_km": return s.target != null ? s.targetKm.ToString("0.0") : "-";
                case "rcs": return s.rcs.ToString(s.rcs < 10f ? "0.00" : "0.0");
                case "ir": return Mathf.RoundToInt(s.irFrac * 100f) + "%";
                default: return "";
            }
        }

        static float FieldFrac(string f, FlightState s)
        {
            switch (f)
            {
                case "ias": return s.iasKmh / 1600f; case "alt": return s.altM / 15000f; case "radalt": return s.radAltM / 1500f;
                case "mach": return s.mach / 2.5f; case "g": return (s.g + 3f) / 12f; case "aoa": return s.aoaDeg / 30f;
                case "throttle": return s.throttle; case "rpm_l": return s.rpmL / 110f; case "rpm_r": return s.rpmR / 110f;
                case "egt_l": return (s.egtL - 200f) / 800f; case "egt_r": return (s.egtR - 200f) / 800f;
                case "fuel": case "fuel_pct": return s.fuelFrac; case "ptb": return s.extFuelKg / 3000f;
                case "hyd": return s.hydraulic / 250f; case "oxy": return s.oxygen / 150f; case "ir": return s.irFrac;
                default: return 0f;
            }
        }

        static Color32? FieldWarn(string f, FlightState s)
        {
            switch (f)
            {
                case "g": return s.g > 8.5f || s.g < -2.5f ? Red : (Color32?)null;
                case "aoa": return s.aoaDeg > 24f && s.airborne ? Red : (Color32?)null;
                case "ias": return s.iasKmh > 1450f ? Red : (Color32?)null;
                case "rpm_l": return s.rpmL >= 104f ? Red : (Color32?)null;
                case "rpm_r": return s.rpmR >= 104f ? Red : (Color32?)null;
                case "egt_l": return s.egtL >= 950f ? Red : (Color32?)null;
                case "egt_r": return s.egtR >= 950f ? Red : (Color32?)null;
                case "fuel": case "fuel_pct": return s.fuelFrac < 0.10f ? Red : s.fuelFrac < 0.25f ? Amber : (Color32?)null;
                case "hyd": return s.hydraulic < 150f ? Amber : (Color32?)null;
                case "oxy": return s.oxygen < 30f ? Amber : (Color32?)null;
                case "ir": return s.irFrac < 0.35f ? Green : s.irFrac < 0.7f ? Amber : Red;
                default: return null;
            }
        }
    }
}
