# Shared context for transcript → technical notes task

You are writing a technical implementation note (`.md`) for one YouTube transcript.
The note will be used later to implement the technique in the user's game.

## Target engine / language

- **Godot 4.7, C#** (the game is written in C#, not GDScript).
- Transcript code is usually GDScript or GLSL-ish shader code. Keep shader code as
  GDShader (Godot shaders are language-agnostic — usable from C# unchanged), but
  describe all node/scene setup, scripts, and math in **C# terms** (PascalCase APIs:
  `FastNoiseLite`, `GetViewport()`, `MeshInstance3D`, `_Process(double delta)`, etc.).
- Godot 4.7 API docs live locally at
  `C:/Users/Clinton/g/code_examples/godot-docs-html-stable/`
  - Class references: `classes/class_<classname>.html` (lowercase, e.g.
    `classes/class_fastnoiselite.html`, `classes/class_compositoreffect.html`).
  - Tutorials: `tutorials/...`.
  - **Do NOT read doc pages wholesale** — they are huge. Only `Grep` a specific class
    page when you genuinely need to verify a method/property name or an enum value.
    Prefer your own knowledge; use the docs to double-check anything you're unsure of.

## Output

- Write the `.md` file **next to the transcript**, with the same basename but `.en.md`
  instead of `.en.txt` (e.g. `Foo [abc123].en.txt` → `Foo [abc123].en.md`).
- Audience: the user (and future AI agents) implementing this in their game. Be concrete:
  algorithms, formulas, shader code, node structures, tunable parameters with the values
  the video used, and step-by-step implementation order. Skip sponsorships, intros,
  channel banter, and story fluff.
- Match the style/depth of the existing example
  `Youtube Transcripts and stuff/The Trick I Used to Make Combat Fun! ｜ Devlog [6BrZryMz-ac].en.md`
  (sectioned markdown, bold key terms, bullet lists, code blocks for formulas/shaders).

## YouTube comments

- Some transcripts include a comments section at the bottom. Scan it for **technical**
  comments: corrections, optimizations, alternative approaches, clarifications from the
  creator, version gotchas.
- Include the ones that are genuinely useful for implementation (clearly marked as coming
  from comments, e.g. a "From the comments" section). Ignore praise, jokes, and off-topic
  comments. Use your judgment on whether a suggestion is appropriate for Godot 4.7/C# —
  say briefly why you include or reject a notable one.

## Open questions

- If the transcript leaves anything ambiguous or undecided for an implementation in the
  user's game (missing parameters, unclear ordering, choices that depend on the user's
  game design, things to verify in-engine), collect them in an **"Open questions"**
  section at the end of the md file. Do not invent answers.
