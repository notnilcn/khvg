# CLAUDE.md

This folder holds notnilcn's design notes for his bullet-hell MMO roguelike (Realm of the Mad God-inspired combat, BitCraft/Factorio/Clash-of-Clans-inspired base building, torus hex-grid world, permadeath). These are **design/ideation notes, not a finalized spec** — the same project's Godot/SpacetimeDB implementation (if present in this checkout) has its own CLAUDE.md describing current build status; don't assume something written here is already implemented.

## Working style

Same rule as the rest of this codebase: **do not write large new sections in one shot**. Ask clarifying questions first, propose a small draft, and let the author correct it — these docs are him thinking out loud, and getting the nuance wrong is worse than asking.

When adding content, match the existing voice: informal, first-person ("I want", "I don't like", "idk"), casual profanity intact, contradictions and unresolved tangents left in rather than smoothed over.

## Structure

- `01 Executive Summary.md` — high concept, demographics, inspirations, selling points
- `02 Gameplay.md` — core loop, permadeath rules, endgame direction, RotMG reference videos
- `03 Classes.md` — classes are **emergent from gear + stat requirements**, not fixed archetypes; defines the dex/str/wis × dps/sup/art grid and links to per-class files
- `04 Lore.md` — currently a stub (placeholder only); lore content actually lives in `99 (outdated)...` until migrated
- `05 Biomes.md` — empty stub, not yet written
- `99 (outdated) Game Design Document.md` — original monolithic doc. Superseded by 01–05 for most topics, but still the **only** source for some content (safe hubs, tutorial hook, "players are souls in artificial bodies" lore, guild territory scope-creep notes) that hasn't been split out yet. Check here before assuming a topic is undocumented.
- `Art/` — art direction references
  - `Art style 3D.md` — primary target look: low-poly 3D + pixel-art shader (t3ssel8r-style), BitCraft-like proportions, Hollow Knight-adjacent grim-hope tone
  - `Art style 2D.md` — empty stub (fallback 2D mode is mentioned in 01 but not detailed here yet)
  - `Hollow Knight References.md` — just a palette reference image note
- `Classes/` — per-class detail docs, organized by temperament folder (DPS / SUP / ART)
  - `Classes/ART/` — crafter, gatherer, enchanter (all currently empty stubs)
  - `Classes/Other or Undeveloped/` — summoner, sorcerer (both noted as "maybe just fold into wizard")
  - `Classes/Balancing details/Overview.md` — stub with 3 bullet topics only (defence vs HP, single vs multi-shot, armor piercing)
  - **Known gap:** `03 Classes.md` wiki-links to DPS and SUP class files (Archer, Warrior, Wizard, Healer, Paladin, Tank, Trickster) that do not currently exist as files in this folder even though the class design itself is written out in `03 Classes.md`. Don't assume they're just "empty like the ART stubs" — they're referenced-but-missing, which is different from present-but-blank.

## Conventions used in these notes

- Numeric prefixes (`01`, `02`, ...) = intended reading order; `99` = deprecated/superseded, kept for reference
- `~~strikethrough~~` = an idea the author reconsidered or rejected but wants preserved for context — don't delete it when editing nearby text
- `==highlighted text==` = the author flagging something as an especially important decision or reminder to self
- Many files are intentionally near-empty (single-line stubs) — that's a placeholder waiting for content, not an accident
- Links use Obsidian wiki-link syntax (`[[Note Name]]`, `[[Note#^block-id]]`) — preserve this syntax rather than converting to standard markdown links
- Embedded `![...]` lines pointing at YouTube URLs or `Pasted image ....png` are Obsidian embeds (reference videos / pasted screenshots), not broken image syntax
