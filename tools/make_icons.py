"""Makes the menu rail's icons (UI redesign Stage 2): Resources/icon_<name>.png, 48 x 48 white line drawings on clear,
tinted in game with GUI.color. Drawn four times bigger and scaled down for smooth edges, like tools/make_frog.py.

    python tools/make_icons.py
"""
import math
import pathlib

from PIL import Image, ImageDraw

OUT = pathlib.Path(__file__).resolve().parent.parent / "Resources"
S = 4            # drawn at 4x
N = 48 * S       # canvas
W = int(3.4 * S) # line width
WHITE = (255, 255, 255, 255)


def canvas():
    img = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def p(x, y):
    return (x * S, y * S)


def line(d, pts):
    d.line([p(*q) for q in pts], fill=WHITE, width=W, joint="curve")
    for q in (pts[0], pts[-1]):  # round ends
        x, y = p(*q)
        r = W / 2
        d.ellipse([x - r, y - r, x + r, y + r], fill=WHITE)


def circle(d, cx, cy, r, fill=False):
    box = [(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S]
    if fill:
        d.ellipse(box, fill=WHITE)
    else:
        d.ellipse(box, outline=WHITE, width=W)


def home(d):
    line(d, [(8, 22), (24, 9), (40, 22)])
    line(d, [(13, 19), (13, 39), (35, 39), (35, 19)])
    line(d, [(21, 39), (21, 29), (27, 29), (27, 39)])


def tunes(d):  # three sliders
    for x, knob in ((13, 18), (24, 30), (35, 14)):
        line(d, [(x, 8), (x, 40)])
        circle(d, x, knob, 4.5, fill=True)


def specs(d):  # a sheet with a tick
    d.rounded_rectangle([11 * S, 7 * S, 37 * S, 41 * S], radius=4 * S, outline=WHITE, width=W)
    line(d, [(17, 25), (22, 30), (31, 19)])


def time(d):  # stopwatch
    circle(d, 24, 27, 13)
    line(d, [(24, 27), (30, 21)])
    line(d, [(20, 8), (28, 8)])
    line(d, [(24, 8), (24, 13)])


def battles(d):  # chequered flag
    line(d, [(11, 7), (11, 42)])
    d.rectangle([13 * S, 9 * S, 38 * S, 27 * S], outline=WHITE, width=W)
    for r in range(3):
        for c in range(4):
            if (r + c) % 2 == 0:
                x, y = 13 + c * 6.25, 9 + r * 6
                d.rectangle([x * S, y * S, (x + 6.25) * S, (y + 6) * S], fill=WHITE)


def roam(d):  # a road going into the distance
    line(d, [(16, 8), (8, 40)])
    line(d, [(32, 8), (40, 40)])
    for y0, y1 in ((10, 15), (20, 26), (31, 38)):
        line(d, [(24, y0), (24, y1)])


def tools(d):  # wrench
    cx, cy, r = 30, 18, 9.5
    d.arc([(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S], 75, 345, fill=WHITE, width=int(W * 1.25))
    line(d, [(23, 25), (10, 38)])
    line(d, [(23, 25), (12, 36)])


def options(d):  # a gear: 8 teeth round a ring, a hole in the middle
    cx, cy, teeth = 24, 24, 8
    pts = []
    for k in range(teeth * 4):
        a = (k / (teeth * 4)) * 2 * math.pi - math.pi / 2
        r = 17 if k % 4 in (1, 2) else 13
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    pts.append(pts[0])
    d.line([p(*q) for q in pts], fill=WHITE, width=W, joint="curve")
    circle(d, cx, cy, 5)


for name, draw in (("home", home), ("options", options), ("tunes", tunes), ("specs", specs), ("time", time),
                   ("battles", battles), ("roam", roam), ("tools", tools)):
    img, d = canvas()
    draw(d)
    img.resize((48, 48), Image.LANCZOS).save(OUT / f"icon_{name}.png")
    print("wrote", OUT / f"icon_{name}.png")
