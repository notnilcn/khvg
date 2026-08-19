"""
Realign note nodes in an Obsidian .canvas file.

- Note nodes (file nodes and note-sized group nodes) are snapped so that
  neighbours in the same row / column are GAP units apart.
- Leaf group nodes (groups that contain notes but no other big groups) are
  resized to wrap their contents with PADDING units on every side.
- Everything is translated so the top-left of the canvas starts at 0,0.

A backup (<file>.bak) is created before the canvas is overwritten.

Usage:
    python realign_canvas.py [path-to-canvas]
"""

import json
import shutil
import sys
from pathlib import Path

GAP = 360        # space between neighbouring note nodes
PADDING = 60     # group padding around contained nodes
ROW_TOL = 150    # max |dy| for nodes to be treated as one row
COL_TOL = 150    # max |dx| for nodes to be treated as one column
SLOT_TOL = 60    # stacked nodes closer than this share one slot
MAX_GAP = 300    # larger gaps split a row/column into separate segments
NOTE_MAX = 480   # max width/height for a node to count as note-sized
NOTE_MIN = 480   # max width/height for a node to count as note-sized

DEFAULT_CANVAS = Path(__file__).with_name("main-combat.canvas")


def is_note(node):
    """File nodes and note-sized group nodes (e.g. the white 420x420 labels)."""
    if node.get("type") == "file":
        return True
    return (
        node.get("type") == "group"
        and node.get("width", 0) <= NOTE_MAX
        and node.get("height", 0) <= NOTE_MAX
    )


def cluster(sorted_nodes, key, tol):
    """Group nodes (sorted by key) where consecutive values differ by <= tol."""
    clusters = []
    for node in sorted_nodes:
        if clusters and key(node) - key(clusters[-1][-1]) <= tol:
            clusters[-1].append(node)
        else:
            clusters.append([node])
    return clusters


def respace(nodes, pos_key, size_key):
    """Snap nodes lying on one line so consecutive slots are GAP apart."""
    moved = 0
    ordered = sorted(nodes, key=lambda n: n[pos_key])
    if not ordered:
        return moved

    # split into segments separated by large gaps
    segments = [[ordered[0]]]
    for node in ordered[1:]:
        prev = segments[-1][-1]
        if node[pos_key] - (prev[pos_key] + prev[size_key]) > MAX_GAP:
            segments.append([node])
        else:
            segments[-1].append(node)

    for segment in segments:
        # collapse stacked nodes (same position) into one slot
        slots = cluster(segment, lambda n: n[pos_key], SLOT_TOL)
        prev_right = None
        for slot in slots:
            slot_min = min(n[pos_key] for n in slot)
            new_pos = slot_min if prev_right is None else prev_right + GAP
            shift = new_pos - slot_min
            for n in slot:
                n[pos_key] += shift
                n["_delta"][0 if pos_key == "x" else 1] += shift
                moved += abs(shift)
            prev_right = new_pos + max(n[size_key] for n in slot)
    return moved


def bounds(node):
    return node["x"], node["y"], node["x"] + node["width"], node["y"] + node["height"]


def contains(outer, inner):
    x1, y1, x2, y2 = outer
    a1, b1, a2, b2 = inner
    return x1 <= a1 and y1 <= b1 and a2 <= x2 and b2 <= y2


def main():
    canvas_path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_CANVAS
    canvas = json.loads(canvas_path.read_text(encoding="utf-8"))
    nodes = canvas["nodes"]

    notes = [n for n in nodes if is_note(n)]
    anchors = [n for n in nodes if n.get("type") == "text"]
    containers = [n for n in nodes if n.get("type") == "group" and not is_note(n)]

    # remember original geometry for containment tests and delta tracking
    original = {n["id"]: bounds(n) for n in nodes}
    for n in notes:
        n["_delta"] = [0, 0]

    # --- 1. snap rows (x) and columns (y) so neighbours are GAP apart ---
    rows = cluster(sorted(notes, key=lambda n: n["y"]), lambda n: n["y"], ROW_TOL)
    moved_x = 0
    for row in rows:
        moved_x += respace(row, "x", "width")

    cols = cluster(sorted(notes, key=lambda n: n["x"]), lambda n: n["x"], COL_TOL)
    moved_y = 0
    for col in cols:
        moved_y += respace(col, "y", "height")

    # --- 2. move text anchors by the delta of their nearest note node ---
    note_deltas = []
    for n in notes:
        ox, oy, _, _ = original[n["id"]]
        note_deltas.append(
            (ox + n["width"] / 2, oy + n["height"] / 2, n["_delta"][0], n["_delta"][1])
        )
    for a in anchors:
        acx = a["x"] + a["width"] / 2
        acy = a["y"] + a["height"] / 2
        _, _, dx, dy = min(
            note_deltas, key=lambda t: (t[0] - acx) ** 2 + (t[1] - acy) ** 2
        )
        a["x"] += dx
        a["y"] += dy

    # --- 3. resize leaf groups to wrap their note nodes with PADDING ---
    resized = 0
    for group in containers:
        gb = original[group["id"]]
        children = [n for n in notes if contains(gb, original[n["id"]])]
        if not children:
            continue
        # leaf groups only: skip groups that contain another container
        if any(
            c is not group and contains(gb, original[c["id"]]) for c in containers
        ):
            continue
        min_x = min(c["x"] for c in children)
        min_y = min(c["y"] for c in children)
        max_x = max(c["x"] + c["width"] for c in children)
        max_y = max(c["y"] + c["height"] for c in children)
        group["x"] = min_x - PADDING
        group["y"] = min_y - PADDING
        group["width"] = (max_x - min_x) + 2 * PADDING
        group["height"] = (max_y - min_y) + 2 * PADDING
        resized += 1

    # --- 4. translate everything so the top-left node sits at 0,0 ---
    offset_x = min(n["x"] for n in nodes)
    offset_y = min(n["y"] for n in nodes)
    for n in nodes:
        n["x"] -= offset_x
        n["y"] -= offset_y
        n.pop("_delta", None)

    backup = canvas_path.with_suffix(canvas_path.suffix + ".bak")
    shutil.copy2(canvas_path, backup)
    canvas_path.write_text(
        json.dumps(canvas, indent="\t", ensure_ascii=False) + "\n", encoding="utf-8"
    )

    print(f"Notes respaced: {len(notes)} (x moved {moved_x}, y moved {moved_y})")
    print(f"Groups resized: {resized}")
    print(f"Canvas shifted by ({-offset_x}, {-offset_y})")
    print(f"Backup written to: {backup}")


if __name__ == "__main__":
    main()
