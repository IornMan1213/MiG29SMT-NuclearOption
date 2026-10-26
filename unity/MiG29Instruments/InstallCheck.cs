using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MiG29SMTInstruments
{
    // The install mistakes players report most: two copies of the mod in BepInEx/plugins (an old one left behind), and a
    // plugin from one release with a .nobp from another. Checked once at start;
    // the problem goes to the log at once and on screen the first time the player flies the SMT (InstrumentDriver).
    static class InstallCheck
    {
        // the name the build gives this mod's .nobp: "MiG-29SMT_x.y.z.nobp"
        static readonly Regex Nobp = new Regex(@"^MiG-29SMT_(\d+\.\d+\.\d+)\.nobp$", RegexOptions.IgnoreCase);

        public static string Problem { get; private set; }

        public static void Run()
        {
            try
            {
                var found = Directory.GetFiles(BepInEx.Paths.PluginPath, "*.nobp", SearchOption.AllDirectories)
                    .Select(f => (file: f, m: Nobp.Match(Path.GetFileName(f)))).Where(x => x.m.Success).ToList();
                if (found.Count > 1)
                    Problem = $"MiG-29SMT: {found.Count} copies of the mod are installed ({string.Join(", ", found.Select(x => x.m.Groups[1].Value))}). " +
                              "Keep only the newest .nobp in BepInEx/plugins.";
                else if (found.Count == 1 && found[0].m.Groups[1].Value != MiG29InstrumentsPlugin.ModVersion)
                    Problem = $"MiG-29SMT: the mod is {found[0].m.Groups[1].Value} but MiG29SMTInstruments.dll is {MiG29InstrumentsPlugin.ModVersion}. " +
                              "Install both from the same release.";
                if (Problem != null)
                {
                    MiG29InstrumentsPlugin.Log.LogWarning(Problem);
                    foreach (var (file, _) in found) MiG29InstrumentsPlugin.Log.LogWarning($"  found: {file}");
                }
                else if (found.Count == 0)
                    MiG29InstrumentsPlugin.Log.LogInfo("install check: no MiG-29SMT .nobp under BepInEx/plugins (renamed, or not installed)");
            }
            catch (Exception e) { MiG29InstrumentsPlugin.Log.LogWarning($"install check: {e.Message}"); }
        }
    }
}
