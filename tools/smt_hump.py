"""MiG-29SMT dorsal "hump": the SMT's enlarged spine, which holds its extra fuel tank, raises the top of the fuselage behind the
canopy by about 20 cm and runs back nearly to the fins. The 9.12 model's own spine skin is lifted (not covered by a new shell),
so the livery, panel lines and antennas follow it: every body vertex above the spine's shoulder line, within 0.42 m of the
centreline, between the canopy's rear (z 5.0) and the fins (z -1.6), moves up by rise(z) * falloff(x). Normals are recomputed
for the moved vertices. Writes MiG29SMTSource/hump.json (original body vertex indices -> new position and normal), which
MiG29Builder applies before cutting the skin, so it never stacks up over repeated builds.
python smt_hump.py <MiG29SMTSource>"""
import json, os, sys
import numpy as np

SRC = sys.argv[1]
HALF_W = 0.44                                  # spine half-width it raises
FLAT_W = 0.18                                  # full rise across the middle: the spine is lifted whole (a broad, rounded hump, not a ridge)
Z_FRONT, Z_FULL0, Z_FULL1, Z_BACK = 5.0, 3.4, 0.6, -1.6
RISE = 0.22                                    # m at the top of the hump


def smooth(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3 - 2 * t)


def rise(z):
    return RISE * np.where(z > Z_FULL0, smooth((Z_FRONT - z) / (Z_FRONT - Z_FULL0)),
                           np.where(z < Z_FULL1, smooth((z - Z_BACK) / (Z_FULL1 - Z_BACK)), 1.0))


def falloff(x):
    t = np.clip((np.abs(x) - FLAT_W) / (HALF_W - FLAT_W), 0.0, 1.0)
    return np.cos(t * np.pi / 2) ** 2


mesh = json.load(open(os.path.join(SRC, "mig29_mesh.json")))
body = next(p for p in mesh["parts"] if p["name"] == "body")
V = np.array(body["vertices"]).reshape(-1, 3); N = np.array(body["normals"]).reshape(-1, 3)
T = np.array(body["triangles"]).reshape(-1, 3)
TP = V[T]; TMIN, TMAX = TP.min(1), TP.max(1)


def top(x, z):
    best = None
    for i in np.where((TMIN[:, 0] <= x) & (TMAX[:, 0] >= x) & (TMIN[:, 2] <= z) & (TMAX[:, 2] >= z))[0]:
        p = TP[i]; a, b, c = p[:, [0, 2]]
        v0, v1, v2 = b - a, c - a, np.array([x, z]) - a
        den = v0[0] * v1[1] - v1[0] * v0[1]
        if abs(den) < 1e-12: continue
        u = (v2[0] * v1[1] - v1[0] * v2[1]) / den; v = (v0[0] * v2[1] - v2[0] * v0[1]) / den
        if u < -1e-6 or v < -1e-6 or u + v > 1 + 1e-6: continue
        y = p[0, 1] + u * (p[1, 1] - p[0, 1]) + v * (p[2, 1] - p[0, 1])
        if y < 1.3 and (best is None or y > best): best = y
    return best


# shoulder line: the skin height at the hump's edge, along z; only vertices above it are spine skin
zs = np.arange(Z_BACK - 0.2, Z_FRONT + 0.21, 0.1)
shoulder = np.array([top(HALF_W, z) or -9 for z in zs])
sh = np.interp(V[:, 2], zs, shoulder)
d = rise(V[:, 2]) * falloff(V[:, 0])
sel = (np.abs(V[:, 0]) < HALF_W) & (V[:, 2] < Z_FRONT) & (V[:, 2] > Z_BACK) & (V[:, 1] > sh + 0.015) & (d > 1e-4)
V2 = V.copy(); V2[sel, 1] += d[sel]

# normals of the moved vertices, by angle as tools/normals_by_angle.py made the skin's: the mean of the (moved) face normals at
# each position that lie within ANGLE of the faces this vertex belongs to (body vertices are split at UV seams and sharp edges)
ANGLE = 50.0
key = lambda p: (round(p[0], 4), round(p[1], 4), round(p[2], 4))
moved_keys = {key(V[i]) for i in np.where(sel)[0]}
at_key, own = {}, {}
for t in T:
    k3 = [key(V[i]) for i in t]
    if not any(k in moved_keys for k in k3): continue
    fn = np.cross(V2[t[1]] - V2[t[0]], V2[t[2]] - V2[t[0]])   # length = 2 * area
    if np.dot(fn, N[t].sum(0)) < 0: fn = -fn                  # facing as the stored normals
    for i, k in zip(t, k3):
        if k in moved_keys: at_key.setdefault(k, []).append(fn); own[i] = own.get(i, 0) + fn
cos = np.cos(np.radians(ANGLE))
idx, pos, nrm = [], [], []
for i in np.where(sel)[0]:
    ref = own.get(i)
    if ref is None or np.linalg.norm(ref) < 1e-12: n = N[i]
    else:
        ref = ref / np.linalg.norm(ref)
        n = sum(f for f in at_key[key(V[i])] if np.dot(f, ref) >= cos * np.linalg.norm(f))
    n = n / np.linalg.norm(n)
    if np.dot(n, N[i]) < 0: n = -n                        # keep the stored winding's facing
    idx.append(int(i)); pos += V2[i].round(5).tolist(); nrm += n.round(5).tolist()
json.dump({"index": idx, "pos": pos, "nrm": nrm}, open(os.path.join(SRC, "hump.json"), "w"))
print(f"[hump] {len(idx)} spine vertices raised, up to {d[sel].max():.3f} m; spine top at z 2.0 now {top(0.0, 2.0) + RISE:.2f}")
