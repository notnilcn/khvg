# CLAUDE.md

This folder holds notnilcn's design notes for his bullet-hell MMO roguelike (Realm of the Mad God-inspired combat, BitCraft/Factorio/Clash-of-Clans-inspired base building, torus hex-grid world, permadeath). These are **design/ideation notes, not a finalized spec** — the same project's Godot/SpacetimeDB implementation (if present in this checkout) has its own CLAUDE.md describing current build status; don't assume something written here is already implemented.

## Working style

Use CriticMarkup syntax when making changes to these documents.

When adding content, match the existing voice: informal, first-person ("I want", "I don't like", "idk"), casual profanity intact, contradictions and unresolved tangents left in rather than smoothed over.

## Structure

- `01 Executive Summary.md` — high concept, demographics, inspirations, selling points
- `02 Gameplay.md` — core loop, permadeath rules, damage/buff/status-condition math, endgame direction, RotMG reference videos
- `03 Class system, Item system, and Equipment system.md` — classes are **emergent from gear + stat requirements**, not fixed archetypes; covers the dex/str/wis × dps/sup/art stat grid, the item/enchantment/swap-out system (Borderlands-inspired), and the full DPS/SUP class writeups (Archer, Warrior, Wizard, Healer, Paladin, Tank, Trickster) inline in one doc, plus older superseded "Old notes" tank-subclass sections at the bottom
- `04 Lore.md` — now has real content: theme/message (institutions-are-good allegory), the pre-merge world and the Alice/Bob/Charlie characters, guild territory lore, torus-world waypoint lore. Overlaps with `99 (outdated)...`'s lore sections (some of it is the same material reworked) — cross-check both when touching lore
- `05 Biomes.md` — empty stub, not yet written
- `99 (outdated) Game Design Document.md` — original monolithic doc. Superseded by 01–04 for most topics, but still the **only** source for some content (safe hubs, tutorial hook, "players are souls in artificial bodies" lore, guild safe-hub end-game competition mechanics, apocalypse-epicentre scope-creep notes) that hasn't been split out yet. Check here before assuming a topic is undocumented.
- `Art/` — art direction references
  - `Art style 3D.md` — primary target look: low-poly 3D + pixel-art shader (t3ssel8r-style), BitCraft-like proportions, Hollow Knight-adjacent grim-hope tone; has reference video/image examples plus "current state" (using free/open assets) and "what needs to be done" sections (both still sparse)
  - `Art style 2D.md` — empty stub (fallback 2D mode is mentioned in 01 but not detailed here yet)
  - `Hollow Knight References.md` — palette reference image with a clockwise legend of which Hollow Knight location each colour comes from
- `Classes/` — per-class detail docs, organized by temperament folder (DPS / SUP / ART)
  - `Classes/ART/` — crafter, gatherer, enchanter (all currently empty stubs — the ART temperament itself is discussed conceptually in `03 Class system...md`, but none of these three files have content yet)
  - `Classes/Other or Undeveloped/` — summoner and sorcerer, both noted as "maybe just fold into wizard" (summoner → give wizard maces, sorcerer → give wizard sceptres); summoner file also has two unrelated devlog video embeds about enemy-vs-enemy combat, seemingly a loose research note rather than summoner content
  - `Classes/Balancing details/Overview.md` — stub with 3 bullet topics only (defence vs HP, single vs multi-shot, armor piercing)

## Conventions used in these notes

- Numeric prefixes (`01`, `02`, ...) = intended reading order; `99` = deprecated/superseded, kept for reference
- `~~strikethrough~~` = an idea the author reconsidered or rejected but wants preserved for context — don't delete it when editing nearby text
- `==highlighted text==` = the author flagging something as an especially important decision or reminder to self
- Many files are intentionally near-empty (single-line stubs) — that's a placeholder waiting for content, not an accident
- Links use Obsidian wiki-link syntax (`[[Note Name]]`, `[[Note#^block-id]]`) — preserve this syntax rather than converting to standard markdown links
- Embedded `![...]` lines pointing at YouTube URLs or `Pasted image ....png` are Obsidian embeds (reference videos / pasted screenshots), not broken image syntax
