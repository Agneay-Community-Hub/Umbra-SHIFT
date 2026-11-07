import random, sys
from sim import Level, solve

def make(W, H, wall_p, ncast, nhaz, rng):
    g = [["#" if x in (0, W-1) or y in (0, H-1) else "." for x in range(W)] for y in range(H)]
    inner = [(x, y) for y in range(1, H-1) for x in range(1, W-1)]
    for c in inner:
        if rng.random() < wall_p:
            g[c[1]][c[0]] = "#"
    free = [c for c in inner if g[c[1]][c[0]] == "."]
    rng.shuffle(free)
    if len(free) < ncast + nhaz + 3:
        return None
    for i in range(ncast):
        x, y = free.pop(); g[y][x] = "C"
    for i in range(nhaz):
        x, y = free.pop(); g[y][x] = "X"
    x, y = free.pop(); g[y][x] = "S"
    x, y = free.pop(); g[y][x] = "G"
    return ["".join(r) for r in g]

def without_shadow(rows):
    return [r.replace("C", "#") for r in rows]

def evaluate(rows):
    L = Level(rows)
    sol, n = solve(L)
    if not sol:
        return None
    L2 = Level(without_shadow(rows))
    s2, _ = solve(L2)
    if s2 and len(s2) <= len(sol):
        return None  # shadows not needed
    rot = sum(c in "QE" for c in sol)
    walk = len(sol) - rot
    if rot < 2 or walk < 1:
        return None
    # count how many casters matter: replacing each individually must hurt
    for i, ch in enumerate("".join(rows)):
        pass
    return dict(par=len(sol), sol=sol, states=n, rot=rot, walk=walk, noshadow=len(s2) if s2 else None)

if __name__ == "__main__":
    seed = int(sys.argv[1]); count = int(sys.argv[2])
    W, H = int(sys.argv[3]), int(sys.argv[4])
    ncast = int(sys.argv[5]); nhaz = int(sys.argv[6]); minpar = int(sys.argv[7])
    rng = random.Random(seed)
    found = []
    for it in range(count):
        rows = make(W, H, rng.uniform(0.15, 0.35), ncast, nhaz, rng)
        if not rows: continue
        e = evaluate(rows)
        if e and e["par"] >= minpar:
            # every caster should matter
            ok = True
            if ncast > 1:
                flat = [list(r) for r in rows]
                for y, r in enumerate(flat):
                    for x, ch in enumerate(r):
                        if ch == "C":
                            t = [row[:] for row in flat]; t[y][x] = "#"
                            s, _ = solve(Level(["".join(q) for q in t]))
                            if s and len(s) <= e["par"]:
                                ok = False
            if not ok: continue
            score = e["par"] + e["walk"] * 0.5 + (0 if e["noshadow"] is None else -3)
            found.append((score, rows, e))
    found.sort(key=lambda f: -f[0])
    for score, rows, e in found[:4]:
        print(f"score={score} {e}")
        print("\n".join(rows)); print()
