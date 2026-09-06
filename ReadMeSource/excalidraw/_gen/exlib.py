"""Tiny Excalidraw JSON + SVG-preview writer used by gen_diagrams.py.

Usage:  python3 gen_diagrams.py <out_excalidraw_dir> <out_preview_dir>
The preview SVG is only for checking layout (rsvg-convert); it is not committed.
"""
import json
import random
import html

RED = "#e03131"
BLUE = "#1971c2"
GREEN = "#2f9e44"
BLACK = "#1e1e1e"
GRAY = "#868e96"
ORANGE = "#e8590c"
BG_BLUE = "#e7f5ff"
BG_GREEN = "#ebfbee"
BG_ORANGE = "#fff4e6"
BG_YELLOW = "#fff9db"
BG_GRAY = "#f8f9fa"
BG_RED = "#fff5f5"

FONT_STACK = "Virgil, 'Segoe Print', 'Comic Sans MS', 'Apple SD Gothic Neo', 'Noto Sans KR', sans-serif"


def _is_wide(ch):
    o = ord(ch)
    return o > 0x2E80


def text_size(text, fs):
    lines = text.split("\n")
    w = 0
    for ln in lines:
        lw = sum((1.0 if _is_wide(c) else 0.58) * fs for c in ln)
        w = max(w, lw)
    return w, len(lines) * fs * 1.25


class Diagram:
    def __init__(self, name, width, height, seed=1):
        self.name = name
        self.width = width
        self.height = height
        self.els = []
        self.svg = []
        self.rng = random.Random(seed)
        self._n = 0

    # ---- ids / boilerplate -------------------------------------------------
    def _id(self):
        self._n += 1
        return f"{self.name}-{self._n:04d}"

    def _base(self, typ, x, y, w, h, stroke=BLACK, bg="transparent", fill="solid",
              sw=2, style="solid", rough=1, opacity=100, roundness=None):
        return {
            "id": self._id(), "type": typ, "x": x, "y": y, "width": w, "height": h,
            "angle": 0, "strokeColor": stroke, "backgroundColor": bg, "fillStyle": fill,
            "strokeWidth": sw, "strokeStyle": style, "roughness": rough, "opacity": opacity,
            "groupIds": [], "frameId": None, "index": None,
            "roundness": roundness, "seed": self.rng.randint(1, 2**31 - 1),
            "version": 1, "versionNonce": self.rng.randint(1, 2**31 - 1),
            "isDeleted": False, "boundElements": None, "updated": 1, "link": None, "locked": False,
        }

    # ---- primitives --------------------------------------------------------
    def text(self, cx, cy, s, fs=18, color=BLACK, align="center", bold=False, anchor="center"):
        """anchor: 'center' = (cx,cy) is centre; 'topleft' = (cx,cy) is top-left; 'top' = top-centre."""
        w, h = text_size(s, fs)
        if anchor == "center":
            x, y = cx - w / 2, cy - h / 2
        elif anchor == "top":
            x, y = cx - w / 2, cy
        else:
            x, y = cx, cy
        el = self._base("text", x, y, w, h, stroke=color)
        el.update({
            "text": s, "fontSize": fs, "fontFamily": 1, "textAlign": align,
            "verticalAlign": "top", "baseline": fs, "containerId": None,
            "originalText": s, "autoResize": True, "lineHeight": 1.25,
        })
        self.els.append(el)
        # svg
        lines = s.split("\n")
        if align == "center":
            tx, ta = x + w / 2, "middle"
        elif align == "left":
            tx, ta = x, "start"
        else:
            tx, ta = x + w, "end"
        fw = "bold" if bold else "normal"
        for i, ln in enumerate(lines):
            ty = y + fs * 1.25 * i + fs
            self.svg.append(
                f'<text x="{tx:.0f}" y="{ty:.0f}" font-size="{fs}" fill="{color}" text-anchor="{ta}" '
                f'font-family="{FONT_STACK}" font-weight="{fw}">{html.escape(ln)}</text>')
        return el

    def rect(self, x, y, w, h, stroke=BLACK, bg="transparent", fill="solid", sw=2,
             style="solid", round_=True):
        el = self._base("rectangle", x, y, w, h, stroke=stroke, bg=bg, fill=fill, sw=sw,
                        style=style, roundness={"type": 3} if round_ else None)
        self.els.append(el)
        dash = {"solid": "", "dashed": 'stroke-dasharray="14,10"', "dotted": 'stroke-dasharray="4,6"'}[style]
        if fill == "hachure" and bg != "transparent":
            fillv = f"url(#h{bg[1:]})"
            self._pattern(bg)
        else:
            fillv = bg if bg != "transparent" else "none"
        self.svg.append(
            f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{16 if round_ else 0}" '
            f'fill="{fillv}" stroke="{stroke}" stroke-width="{sw}" {dash}/>')
        return el

    _patterns = None

    def _pattern(self, color):
        if self._patterns is None:
            self._patterns = {}
        if color in self._patterns:
            return
        self._patterns[color] = (
            f'<pattern id="h{color[1:]}" patternUnits="userSpaceOnUse" width="10" height="10" '
            f'patternTransform="rotate(45)"><line x1="0" y1="0" x2="0" y2="10" stroke="{color}" '
            f'stroke-width="3"/></pattern>')

    def arrow(self, pts, color=BLACK, sw=2, style="solid", head=True, start_head=False):
        x0, y0 = pts[0]
        rel = [[px - x0, py - y0] for px, py in pts]
        xs = [p[0] for p in rel]
        ys = [p[1] for p in rel]
        el = self._base("arrow", x0, y0, max(xs) - min(xs), max(ys) - min(ys), stroke=color,
                        sw=sw, style=style, roundness=None)
        el.update({
            "points": rel, "lastCommittedPoint": None, "startBinding": None, "endBinding": None,
            "startArrowhead": "arrow" if start_head else None,
            "endArrowhead": "arrow" if head else None, "elbowed": False,
        })
        self.els.append(el)
        dash = {"solid": "", "dashed": 'stroke-dasharray="14,10"', "dotted": 'stroke-dasharray="4,6"'}[style]
        mid = f'marker-end="url(#ah{color[1:]})"' if head else ""
        mst = f'marker-start="url(#as{color[1:]})"' if start_head else ""
        self._marker(color)
        d = " ".join(f"{px},{py}" for px, py in pts)
        self.svg.append(
            f'<polyline points="{d}" fill="none" stroke="{color}" stroke-width="{sw}" {dash} {mid} {mst}/>')
        return el

    _markers = None

    def _marker(self, color):
        if self._markers is None:
            self._markers = {}
        if color in self._markers:
            return
        self._markers[color] = (
            f'<marker id="ah{color[1:]}" markerWidth="12" markerHeight="12" refX="10" refY="6" orient="auto">'
            f'<path d="M1,1 L11,6 L1,11" fill="none" stroke="{color}" stroke-width="2"/></marker>'
            f'<marker id="as{color[1:]}" markerWidth="12" markerHeight="12" refX="2" refY="6" orient="auto">'
            f'<path d="M11,1 L1,6 L11,11" fill="none" stroke="{color}" stroke-width="2"/></marker>')

    # ---- composites ----------------------------------------------------------
    def title(self, s):
        self.text(self.width / 2, 30, f"[{s}]", fs=40, anchor="top", bold=True)

    def zone(self, x, y, w, h, label, bg=BG_GRAY, label_pos="top", fs=22):
        self.rect(x, y, w, h, stroke=BLACK, bg=bg, fill="solid", sw=1.5, style="dashed")
        if label_pos == "top":
            self.text(x + w / 2, y + 12, label, fs=fs, anchor="top")
        elif label_pos == "topleft":
            self.text(x + 18, y + 12, label, fs=fs, anchor="topleft", align="left")
        elif label_pos == "bottom":
            self.text(x + w / 2, y + h - 12 - fs * 1.25, label, fs=fs, anchor="top", color=GRAY)

    def box(self, x, y, w, h, name, sub=None, fs=22, sfs=16, stroke=BLACK, bg="transparent",
            sw=2, style="solid", subcolor=GRAY):
        self.rect(x, y, w, h, stroke=stroke, bg=bg, sw=sw, style=style)
        if sub:
            _, th = text_size(name, fs)
            _, sh = text_size(sub, sfs)
            top = y + (h - th - sh - 6) / 2
            self.text(x + w / 2, top, name, fs=fs, anchor="top")
            self.text(x + w / 2, top + th + 6, sub, fs=sfs, anchor="top", color=subcolor)
        else:
            self.text(x + w / 2, y + h / 2, name, fs=fs)

    def field(self, x, y, w, h, name, lines, fs=20, lfs=16, bg="#a5d8ff", stroke=BLACK,
              fill="hachure", align="center"):
        self.rect(x, y, w, h, stroke=stroke, bg=bg, fill=fill, sw=1.5)
        self.text(x + w / 2, y + 12, name, fs=fs, anchor="top")
        _, th = text_size(name, fs)
        body = "\n".join(lines)
        if align == "center":
            self.text(x + w / 2, y + 12 + th + 8, body, fs=lfs, anchor="top")
        else:
            self.text(x + 20, y + 12 + th + 8, body, fs=lfs, anchor="topleft", align="left")

    def note(self, x, y, s, fs=16, color=BLACK, align="center", anchor="top"):
        return self.text(x, y, s, fs=fs, color=color, align=align, anchor=anchor)

    def legend(self, x, y, w, items, title="범례", fs=16):
        """items: list of (color, style, label)."""
        h = 20 + 32 + len(items) * (fs * 1.25 + 14) + 10
        self.rect(x, y, w, h, stroke=RED, bg="transparent", sw=1.5, style="dashed")
        self.text(x + w / 2, y + 12, f"<{title}>", fs=fs + 2, anchor="top")
        cy = y + 12 + (fs + 2) * 1.25 + 16
        for color, style, label in items:
            self.arrow([(x + 24, cy + fs * 0.6), (x + 90, cy + fs * 0.6)], color=color, style=style)
            self.text(x + 104, cy, label, fs=fs, anchor="topleft", align="left", color=color)
            cy += fs * 1.25 + 14
        return h

    # ---- output --------------------------------------------------------------
    def excalidraw(self):
        return {
            "type": "excalidraw", "version": 2,
            "source": "memorystone-source ReadMeSource/excalidraw/_gen/gen_diagrams.py",
            "elements": self.els,
            "appState": {"gridSize": 20, "viewBackgroundColor": "#ffffff"},
            "files": {},
        }

    def svg_text(self):
        defs = "".join((self._patterns or {}).values()) + "".join((self._markers or {}).values())
        body = "\n".join(self.svg)
        return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{self.width}" height="{self.height}" '
                f'viewBox="0 0 {self.width} {self.height}"><defs>{defs}</defs>'
                f'<rect width="100%" height="100%" fill="#ffffff"/>\n{body}\n</svg>')

    def write(self, ex_path, svg_path):
        with open(ex_path, "w", encoding="utf-8") as f:
            json.dump(self.excalidraw(), f, ensure_ascii=False, indent=1)
        with open(svg_path, "w", encoding="utf-8") as f:
            f.write(self.svg_text())
