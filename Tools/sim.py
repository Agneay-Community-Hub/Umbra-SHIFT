import sys
from collections import deque

UP = [(0, 1), (-1, 0), (0, -1), (1, 0)]


def right(k):
    ux, uy = UP[k]
    return (uy, -ux)


def parse(path):
    levels, cur = [], None
    for raw in open(path, encoding="utf-8").read().splitlines():
        line = raw.rstrip()
        if line.startswith("="):
            cur = {"title": line[1:].strip(), "hint": "", "rows": []}
            levels.append(cur)
        elif line.startswith(";"):
            cur["hint"] = line[1:].strip()
        elif line.strip() and cur is not None and not line.startswith("//"):
            cur["rows"].append(line)
    return levels


class Level:
    def __init__(self, rows):
        self.H = len(rows)
        self.W = max(len(r) for r in rows)
        self.g = {}
        self.casters = []
        self.sparks = []
        for r, row in enumerate(rows):
            y = self.H - 1 - r
            for x in range(self.W):
                ch = row[x] if x < len(row) else "."
                if ch == "S":
                    self.start = (x, y)
                    ch = "."
                if ch == "C":
                    self.casters.append((x, y))
                if ch == "*":
                    self.sparks.append((x, y))
                self.g[(x, y)] = ch

    def inb(self, c):
        return 0 <= c[0] < self.W and 0 <= c[1] < self.H

    def shadows(self, k, pos):
        rx, ry = right(k)
        d = (-rx, -ry)
        s = set()
        for c in self.casters:
            p = (c[0] + d[0], c[1] + d[1])
            while self.inb(p) and self.g[p] in ".*" and p != pos:
                s.add(p)
                p = (p[0] + d[0], p[1] + d[1])
        return s

    def solid(self, c, sh):
        return self.inb(c) and (self.g[c] in "#C" or c in sh)

    def settle(self, pos, k, mask):
        """apply cell effects + fall. returns (pos,mask,status) status: ok/dead/win"""
        ux, uy = UP[k]
        while True:
            ch = self.g[pos]
            if ch == "X":
                return pos, mask, "dead"
            if ch == "*" and pos in self.sparks:
                mask |= 1 << self.sparks.index(pos)
            if ch == "G":
                return pos, mask, "win"
            below = (pos[0] - ux, pos[1] - uy)
            if not self.inb(below):
                return pos, mask, "dead"
            sh = self.shadows(k, pos)
            if self.solid(below, sh):
                return pos, mask, "ok"
            pos = below

    def step(self, state, m):
        pos, k, mask = state
        if m in "LR":
            rx, ry = right(k)
            s = 1 if m == "R" else -1
            t = (pos[0] + s * rx, pos[1] + s * ry)
            if not self.inb(t):
                return None
            if self.solid(t, self.shadows(k, pos)):
                return None
            pos = t
        else:
            k = (k + (1 if m == "E" else 3)) % 4
            # shadows may now overlap? player cell is never shadow (ray stops at player)
        pos, mask, st = self.settle(pos, k, mask)
        return (pos, k, mask), st


def solve(L, need_all=False):
    full = (1 << len(L.sparks)) - 1
    p, m, st = L.settle(L.start, 0, 0)
    if st != "ok":
        return None, st
    s0 = (p, 0, m)
    prev = {s0: None}
    q = deque([s0])
    while q:
        s = q.popleft()
        for mv in "LRQE":
            r = L.step(s, mv)
            if r is None:
                continue
            ns, st = r
            if st == "dead":
                continue
            if st == "win":
                if need_all and ns[2] != full:
                    continue
                path = [mv]
                c = s
                while prev[c]:
                    c, pm = prev[c]
                    path.append(pm)
                return "".join(reversed(path)), len(prev)
            if ns not in prev:
                prev[ns] = (s, mv)
                q.append(ns)
    return None, len(prev)


if __name__ == "__main__":
    lv = parse(sys.argv[1])
    for i, l in enumerate(lv):
        L = Level(l["rows"])
        a, n = solve(L)
        b, _ = solve(L, True) if L.sparks else (a, 0)
        print(f"{i+1:2} {l['title']:<22} states={n:<5} par={len(a) if a else '-':<3} {a}  | all*: {len(b) if b else '-'} {b}")
