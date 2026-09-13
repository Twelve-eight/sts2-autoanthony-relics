"""Generate the 60 per-slot relic icons for AutoAnthonyRelics (东尼算法-遗物).

Same procedural "relic coin" family as QuriousCraftingRelics
(mod/QuriousCraftingRelics/images/relics, generated 2026-09-08) but with a
distinct accent scheme so the two mods are tellable apart at a glance:
Qurious = pastel ring + warm cream disc; AAR = dark slate ring + pale disc +
angular cyan/amber sigils. Deterministic per slot (golden-angle hue walk).

Outputs (94x94 normal/outline, 282x282 big):
  mod/AutoAnthonyRelics/images/relics/anthony_relic{NNN}.png
  mod/AutoAnthonyRelics/images/relics/anthony_relic{NNN}_outline.png
  mod/AutoAnthonyRelics/images/relics/big/anthony_relic{NNN}.png
"""
from __future__ import annotations

import colorsys
import math
import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "mod", "AutoAnthonyRelics", "images", "relics")
BIG = os.path.join(OUT, "big")
SLOTS = 60


def hsv(h_deg: float, s: float, v: float) -> tuple[int, int, int]:
    r, g, b = colorsys.hsv_to_rgb((h_deg % 360.0) / 360.0, s, v)
    return (int(r * 255), int(g * 255), int(b * 255), 255)


def draw_coin(size: int, slot: int) -> Image.Image:
    scale = size / 94.0
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx = cy = size / 2.0
    r_outer = 45.0 * scale
    r_inner = 36.0 * scale

    hue = (slot * 137.508 + 200.0) % 360.0  # offset so AAR ≠ Qurious hues
    ring_a = hsv(hue, 0.42, 0.38)
    ring_b = hsv(hue + 18.0, 0.30, 0.62)
    accent = hsv(hue + 150.0, 0.62, 0.85)
    disc = (238, 236, 226, 255)

    # dashed outer ring (beads), then solid rim, then inner disc
    beads = 28
    for i in range(beads):
        a0 = i * 360.0 / beads
        color = ring_a if i % 2 == 0 else ring_b
        d.arc(
            [cx - r_outer, cy - r_outer, cx + r_outer, cy + r_outer],
            a0, a0 + 360.0 / beads * 0.72, fill=color, width=max(1, int(4 * scale)),
        )
    d.ellipse(
        [cx - r_outer + 3 * scale, cy - r_outer + 3 * scale,
         cx + r_outer - 3 * scale, cy + r_outer - 3 * scale],
        outline=ring_a, width=max(1, int(2 * scale)),
    )
    d.ellipse(
        [cx - r_inner, cy - r_inner, cx + r_inner, cy + r_inner],
        fill=disc, outline=ring_b, width=max(1, int(2 * scale)),
    )

    # central sigil: n-gon (n = 3..8 by slot) with an inner dot + two orbit dots
    n = 3 + (slot % 6)
    rot = math.radians((slot * 47.0) % 360.0)
    r_sig = 20.0 * scale
    pts = [
        (cx + r_sig * math.sin(rot + 2 * math.pi * k / n),
         cy - r_sig * math.cos(rot + 2 * math.pi * k / n))
        for k in range(n)
    ]
    d.polygon(pts, outline=accent, width=max(1, int(3 * scale)))
    d.ellipse(
        [cx - 4 * scale, cy - 4 * scale, cx + 4 * scale, cy + 4 * scale],
        fill=accent,
    )
    for sign in (-1, 1):
        orb = r_inner * 0.74
        ox = cx + sign * orb * math.sin(rot + math.pi / 2)
        oy = cy - sign * orb * math.cos(rot + math.pi / 2)
        d.ellipse(
            [ox - 2.5 * scale, oy - 2.5 * scale, ox + 2.5 * scale, oy + 2.5 * scale],
            fill=ring_b,
        )
    return img


def draw_outline(size: int, slot: int) -> Image.Image:
    """Silhouette of the coin rim: solid ring disc, transparent elsewhere."""
    scale = size / 94.0
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = size / 2.0
    r = 46.0 * scale
    hue = (slot * 137.508 + 200.0) % 360.0
    d.ellipse([c - r, c - r, c + r, c + r], fill=hsv(hue, 0.42, 0.38))
    return img


def main() -> None:
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(BIG, exist_ok=True)
    for slot in range(SLOTS):
        name = f"anthony_relic{slot:03d}"
        big = draw_coin(282, slot)
        big.save(os.path.join(BIG, f"{name}.png"))
        normal = big.resize((94, 94), Image.LANCZOS)
        normal.save(os.path.join(OUT, f"{name}.png"))
        draw_outline(94, slot).save(os.path.join(OUT, f"{name}_outline.png"))
    print(f"generated {SLOTS} icon sets under {OUT}")


if __name__ == "__main__":
    main()
