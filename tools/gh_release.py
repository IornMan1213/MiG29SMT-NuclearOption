"""Create a GitHub release for a version and attach its zip (.nobp + plugin + credits), using the token from git's credential helper.

python tools/gh_release.py 0.6.0 [--prerelease] [--name="Update 8.5"]
Notes come from that version's CHANGELOG.md section. The token is never printed.
"""
import json, os, re, subprocess, sys, urllib.request

REPO = "IornMan1213/MiG29SMT-NuclearOption"
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def token():
    out = subprocess.run(["git", "credential", "fill"], input="protocol=https\nhost=github.com\n\n", capture_output=True, text=True, cwd=ROOT).stdout
    return next(l.split("=", 1)[1] for l in out.splitlines() if l.startswith("password="))


def api(method, url, tok, data=None, ctype="application/json"):
    req = urllib.request.Request(url, data=data, method=method, headers={"Authorization": f"Bearer {tok}", "Accept": "application/vnd.github+json", "Content-Type": ctype})
    with urllib.request.urlopen(req) as r:
        return json.load(r)


def main():
    ver = sys.argv[1]; pre = "--prerelease" in sys.argv
    log = open(os.path.join(ROOT, "CHANGELOG.md"), encoding="utf-8").read()
    m = re.search(rf"^## (?:SMT )?{re.escape(ver)}[^\n]*\n(.*?)(?=^## |\Z)", log, re.S | re.M)
    notes = m.group(1).strip() if m else ""
    zipf = os.path.join(ROOT, "builds", f"MiG-29SMT-Mod_{ver}.zip")
    if not os.path.exists(zipf): sys.exit(f"{zipf} not found: build with tools/build_mig29.ps1 -Release first")
    notes += (f"\n\n**Install:** with a NOMNOM mod manager (NOMM), or unzip `MiG-29SMT-Mod_{ver}.zip` into `BepInEx/plugins/MiG-29SMT/` "
              f"(it holds `MiG-29SMT_{ver}.nobp` and `MiG29SMTInstruments.dll`; delete older versions first). "
              "Requires BepInEx 5 and Blueprinter 2.1.1+.\n\nThe mod contains a modified version of \"MiG-29 - Fighter Jet - Free\" by bohmerang "
              "(CC BY-NC-SA 4.0).")
    tok = token()
    sha = subprocess.run(["git", "rev-parse", "HEAD"], capture_output=True, text=True, cwd=ROOT).stdout.strip()
    rel = api("POST", f"https://api.github.com/repos/{REPO}/releases", tok, json.dumps(
        {"tag_name": f"v{ver}", "target_commitish": sha, "name": next((a.split("=", 1)[1] for a in sys.argv if a.startswith("--name=")), f"v{ver}"), "body": notes, "prerelease": pre, "make_latest": "true"}).encode())
    print("release:", rel["html_url"])
    base = rel["upload_url"].split("{")[0]
    # Only the all-in-one zip (NOMNOM / NOMM take the first asset): one download, one file name for hand installs.
    uploads = [(zipf, os.path.basename(zipf), "application/zip")]
    for path, name, ctype in uploads:
        a = api("POST", base + "?name=" + urllib.request.quote(name), tok, open(path, "rb").read(), ctype)
        print("asset:", a["browser_download_url"], a["size"])


if __name__ == "__main__":
    main()
