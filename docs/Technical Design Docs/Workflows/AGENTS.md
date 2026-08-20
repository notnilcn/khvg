# Workflows/ AGENTS.md

Step-by-step contributor tutorials ("from idea to in-game"). Two exist: *Adding a Boss*,
*Adding an Ability*. When writing a new one, match their style:

- **Title:** `Adding a X (X Authoring Workflow).md`.
- **Opening:** 2–3 lines stating what the tutorial builds, then `**Audience:**` (who +
  what knowledge is assumed — usually "none, you'll write data-shaped Rust").
- **"The 30-second mental model"** section right after: the core insight (e.g. "enemies
  are data, not code") plus which of the five pipeline steps the task touches.
- **Structure:** `Part 1 — The authoring vocabulary` (the data shapes with short real code
  snippets), `Part 2+ — The workflow` (numbered steps, each ending in verifiable state),
  optional case-study part, then `## Pitfalls (each of these has bitten someone)` and a
  `## New-X checklist` (`- [ ]` items) at the end.
- **Voice:** seasoned, plain, second person. State traps directly ("Gotcha that actually
  happened: …"). No flattery, no filler.
- **Code snippets:** copied from real files with real values, trimmed to the relevant
  fields, with inline comments naming the file it lives in.
- **Facts must be checked against the code** (reducer signatures, file paths, slot
  indices) — these docs are tutorials; wrong commands waste a contributor's day.
- Include the build/verify loop: `cargo check` → `server/build.sh` →
  `dotnet build client/khvg.csproj` → in-game manual test (two clients for anything
  networked).
- Cross-reference related docs with Obsidian `[[...]]` links where the existing docs do;
  point to `AGENTS.md` files for authoritative rules rather than duplicating them.
