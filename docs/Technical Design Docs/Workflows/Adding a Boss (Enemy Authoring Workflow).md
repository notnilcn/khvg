# Adding a Boss (Enemy Authoring Workflow)

A step-by-step tutorial for adding an enemy to the game — from "I have an idea" to "it's
shooting at me in-game". It uses the Gorgon boss (a port of another game's main boss) as the
running example, but you don't need that source material — every step stands alone.

**Audience:** contributors adding enemies/bosses to the server module. No prior knowledge of
the codebase assumed. You will write Rust, but only *data-shaped* Rust — no engine work.

---

## The 30-second mental model

Enemies are **data, not code**. You never write AI. You declare:

1. a **template** (`EnemyTemplate`) — hp, defense, sim ranges, and a list of **phases**;
2. each **phase** — an hp threshold (the phase activates when hp% drops to it), one movement
   behavior, and a list of **attack sequences** that run *concurrently*;
3. each **attack sequence** — an ordered list of **steps**;
4. each **step** — one bullet pattern firing (a ring, a volley, a fan…), with timing.

The server's 100 ms behavior tick advances every enemy's current phase and appends one
`BulletPatternEvent` row per shot. The client does zero AI: it reads those rows and draws
bullets. So authoring an enemy = filling in this data shape, usually via the `Emitter`
helper layer in [[server/spacetimedb/src/enemy/emitters.rs]].

Damage, hit detection, death, xp, phase transitions: already handled. You only describe what
the fight *looks like*.

---

## Part 1 — The authoring vocabulary

Everything in this section lives in `server/spacetimedb/src/enemy/`. The three files you'll
touch: `emitters.rs` (the vocabulary), `defs/mod.rs` (raw helpers), and your own new file in
`defs/` (the content).

### Emitters

An `Emitter` is one gun: a pattern, a fire rate, optional spin, optional burst cadence,
optional curving/acceleration, an optional offset mount, and optional difficulty scaling.
One emitter compiles to one attack sequence.

```rust
use crate::enemy::emitters::{Difficulty, DifficultySteps, Emitter};

// A 12-way ring, bullets at 100 u/s, fired twice per second, spinning 30°/s counter-clockwise:
let e = Emitter::ring(12, 100.0, 2.0).spin(-30.0);

// An aimed 5-bullet fan (40° wide) at 180 u/s, three-round bursts every 2.5s:
let e2 = Emitter::shotgun(5, 40.0, 180.0, 1.0)
    .target(EnemyTarget::AggroTarget)
    .burst(3, 0.12, 2.5);
```

Builder methods (all optional, chainable):

| Method | Meaning | Default |
|---|---|---|
| `target(EnemyTarget::AggroTarget)` | Aim at the aggroed player (`Idle` = fixed direction) | `Idle` |
| `spin(deg_per_sec)` | Rotate the emitter; each shot fires at the current angle | 0 |
| `burst(count, pause, delay)` | Fire `count` shots `pause`s apart, then `delay`s of silence | continuous |
| `window(start, duration)` | Active only during `[start, start+duration]` of the phase | whole phase |
| `line(count, speed_step)` | Stack `count` bullets per direction, each `speed_step` faster — a "speed ladder" that strings out into a line | 1 / 0 |
| `curve(deg_per_sec)` | Bullets curve mid-flight | 0 |
| `accel(units_per_sec2)` | Bullets speed up (or slow, if negative) mid-flight | 0 |
| `offset(x, y)` | Fire from a mount point; **orbits with `spin`** | (0, 0) |
| `angle(deg)` | Rest firing angle | 0 |
| `start_delay(secs)` | Wait this long after phase start before first fire | 0 |
| `damage(n)` / `lifetime(s)` / `texture(id)` | Per-step bullet damage / seconds before expiry / sprite | 10 / 3.0 / `"Arrow"` |
| `difficulty(DifficultySteps{..})` | Per-difficulty-step deltas (see Part 3) | none |

Constructors: `Emitter::ring(count, speed, rate)`, `::volley(count, speed, rate)` (aimed
pellets with jitter), `::shotgun(count, spread_deg, speed, rate)` (fan), or
`::new(PatternType::Curtain(..) / ::Explosion(..), rate)` for the other pattern families.

### Phases

A phase = hp threshold + movement + a vec of compiled emitters:

```rust
use crate::enemy::defs::make_phase;

make_phase(ctx, 1, 0.8, MovementBehavior::Stationary, 2.0, vec![
    tentacles().compile(ctx, difficulty),
    aimed_fan().window(0.0, 5.0).compile(ctx, difficulty),
])
```

- `1, 0.8` — phase index 1, activates at ≤ 80% hp. Phase 0 (threshold `0.0`) is the opener.
  The highest crossed threshold wins, so thresholds descend: 0.9, 0.8, …, 0.1.
- `2.0` — **phase loop delay**: seconds of silence after every sequence in the phase
  finishes, before the phase restarts. This is the boss "rest between attack patterns"
  beat. Every sequence in a phase should be *finite* (windowed emitters) so the phase can
  end and loop — one infinite emitter would pin the phase open forever.
- Movement: `Stationary`, `Wander`, `Chase`, `Flee` — see `MovementBehavior` in
  `def_tables.rs` for the params.

### The template

```rust
EnemyTemplate::seed(ctx, EnemyTemplate {
    template_id: "MyBoss_Normal".to_string(),   // spawn key — unique per difficulty variant
    texture_id: "Enemy".to_string(),            // sprite from the TextureEntry catalog
    display_name: "My Boss (Normal)".to_string(),
    max_hp: 1000,
    defense: 8,                                 // flat per-bullet damage subtraction
    move_sim_factor: 2.75,                      // starts simulating at 2.75 view radii
    attack_sim_factor: 1.35,                    // opens fire at 1.35 view radii
    aggro_lock_seconds: 30.0,
    phases,
});
```

---

## Part 2 — The workflow, start to finish

### Step 1: Design the fight on paper

Two small tables before any code:

**Emitter roster** — every distinct gun in the fight: pattern shape, bullet speed, fire
rate, spin, aimed?, and any special behavior (curving, bursts, ladders).

**Phase table** — for each phase: hp threshold, which emitters are active, and *when*
(start/end of each activation window), plus the rest gap before it loops.

Speed sanity: existing content flies at 50–200 world units/s; players move ~100. Bullet
lifetime ~2–4 s for fast patterns, up to ~10 s for slow ones — the Gorgon file derives
lifetime from speed so every pattern reaches ~400 units (`reach()`); steal that trick.

### Step 2: Write one function per emitter

Create `server/spacetimedb/src/enemy/defs/my_boss.rs`. One `fn` per roster entry, returning
the base `Emitter` (windows get applied at the phase site, so the same emitter can appear
in several phases/windows):

```rust
fn whirlpool() -> Emitter {
    Emitter::ring(4, 100.0, 3.0).spin(45.0).line(1, 25.0).lifetime(4.0)
        .difficulty(DifficultySteps { bullet_count: 1, fire_rate: 0.5, ..none() })
}
```

### Step 3: Assemble the phases

Translate the phase table directly — one `make_phase` per row, one `.window()` per
activation window, generating repetitive windows with loops instead of copy-paste:

```rust
make_phase(ctx, 7, 0.3, MovementBehavior::Stationary, 2.0, {
    let mut attacks: Vec<AttackSequence> = (0..10)   // ten interleaved 0.5s pulses
        .flat_map(|i| [
            sway_cw().window(1.5 + i as f32, 0.5).compile(ctx, difficulty),
            sway_ccw().window(2.0 + i as f32, 0.5).compile(ctx, difficulty),
        ])
        .collect();
    attacks.push(big_bullets().window(6.0, 0.92).compile(ctx, difficulty));
    attacks
}),
```

### Step 4: Difficulty — one builder, four variants

Difficulty is **compile-time**: you write per-step deltas, the builder is run once per
`Difficulty` tier, and each tier seeds as its own template row (`MyBoss_Easy`, …). Spawning
picks a tier by template name — no runtime machinery.

```rust
pub fn seed(ctx: &ReducerContext) {
    for difficulty in Difficulty::ALL {   // Easy, Normal, Hard, Madness
        build(ctx, difficulty);
    }
}

fn build(ctx: &ReducerContext, difficulty: Difficulty) {
    // ... assemble phases (each .compile(ctx, difficulty) applies the deltas) ...
    EnemyTemplate::seed(ctx, EnemyTemplate {
        template_id: format!("MyBoss_{}", difficulty.suffix()),
        max_hp: 750 + 250 * difficulty.step() as u32,   // your hp ladder
        // ...
    });
}
```

`DifficultySteps { fire_rate, bullet_count, line_count, bullet_speed, angular_speed }` —
each field is **added** `difficulty.step()` times (Easy = 0 → unmodified baseline) to the
emitter. A `bullet_count: 1` on a ring means +1 way per tier: 12/13/14/15-way across the
four difficulties. Scale hp separately in the template, as above.

### Step 5: Wire the seed

- Declare the module in `enemy/defs/mod.rs`: `pub mod my_boss;`
- Call it at publish time in `main/lifecycle.rs::init`:
  `crate::enemy::defs::my_boss::seed(ctx);`

### Step 6: Build, publish, spawn

```bash
cargo check --manifest-path server/spacetimedb/Cargo.toml   # compile gate
server/build.sh                                             # regen client bindings + publish (wipes DB, reseeds)
dotnet build client/khvg.csproj                             # client compile gate

# In-game: claim the single admin slot, then spawn by template id:
spacetime call bullethell claim_admin
spacetime call bullethell spawn_enemy MyBoss_Normal 0 0 false false

# Inspect what got seeded:
spacetime sql bullethell "SELECT template_id, max_hp FROM enemy_template"
spacetime sql bullethell "SELECT repeat_interval, repeat_target, angle_step FROM repeat_step_def LIMIT 10"
```

### Step 7: Tune and iterate

- Slow iteration (schema/content shape): edit, republish (every publish wipes and reseeds —
  that's the intended loop, no migrations).
- Live tuning without republish: the admin `upsert_enemy_template` / `upsert_*_def`
  reducers edit rows in place (ids via `spacetime sql`).

---

## Part 3 — Porting a boss from another game (the Gorgon case study)

This is the process used for the reference boss (`defs/gorgon_boss.rs`). The port is ~90%
data extraction discipline, 10% code.

1. **Get ground truth, not vibes.** The source was a compiled Unity game: decompiled code
   gave the *semantics* (how Arc/Line/fire rate actually work), ripped scene files gave the
   *numbers*, and the animator controller + clips gave the *fight structure* (9 HP-gated
   phases, per-phase emitter on/off windows). A recording can substitute for numbers, but
   pause-and-count everything.
2. **Extract four things:** the emitter roster (per gun: shape, speed, rate, spin, aim),
   the phase map (hp thresholds × activation windows), the difficulty rules (the source had
   additive per-step deltas — they map 1:1 onto `DifficultySteps`), and hp scaling.
3. **Map semantics, not numbers.** The traps that mattered (check for these in any source):
   - *Inclusive arcs*: the source fired an N-way 360° arc with endpoints inclusive — N−1
     unique directions plus a stacked duplicate. Our `Ring` has no seam duplicate, so
     counts were decremented by one. Fans (`Shotgun`) match inclusive semantics already.
   - *"Line" meant speed ladder*, not spatial copies: each stacked bullet flies faster.
     That's what `line`/`line_speed_step` models.
   - *Inverted rate units*: one component's "fire rate" was seconds-per-shot, another's
     shots-per-second.
   - *Unit scales*: source speeds ran 1–8, ours 50–200 — a constant scale factor
     (`AOTMK_SPEED_SCALE = 25.0`) at the call sites keeps the table readable in source
     numbers.
4. **Decide what *not* to port.** Boss root movement (hops/dashes/orbit), cinematics,
   minions, and per-emitter bullet colors were all dropped — they need client systems or
   art that don't exist yet. Say so in the file header so the next person knows the gaps
   are deliberate.
5. **Approximate the residue honestly.** A clip-animated oscillating spin became two
   constant-spin half-windows; a 0.03 s aim-snap was dropped. Each approximation is a
   comment at the phase site.

---

## Part 4 — Beyond emitters: raw step choreography

Emitters cover continuous fire, spins, bursts, and windows. If you need scripted
choreography (fire A, wait 0.5s, fire B and C simultaneously, repeat twice), drop to the
raw helpers in `enemy/defs/mod.rs`:

- `seq_single(pattern, target, …)` — fire once, optional `next_step_delay` before the next step.
- `seq_repeat(pattern, …, interval, target, angle_step, next_step_delay)` — re-fire on
  `interval`; `target: 0` = **forever**; `angle_step` rotates each shot (and orbits `offset`).
- `seq_multi(vec![multi_shot(..), …], delay)` — several patterns simultaneously.
- `make_sequence(steps, start_delay)` — runs once per phase cycle.
- `make_looping_sequence(steps, start_delay, loop_delay)` — restarts itself `loop_delay`
  seconds after finishing, independent of siblings (this is what `Emitter::burst` uses).

Steps in one sequence run in order; sequences in one phase run concurrently.

---

## Pitfalls (each of these has bitten someone)

- `repeat_target: 0` means **fire forever**, not "skip this step".
- Angles are **degrees** everywhere — *except* `spread` in the raw `ShotgunParams` (radians;
  `Emitter::shotgun` takes degrees and converts). `CurtainParams.angle_span` is radians too.
- A phase only restarts when **every** sequence in it finishes. One infinite emitter in a
  phase = the phase never loops (fine for HP-driven fights, wrong for scheduled loops —
  use `window()`).
- Fire intervals below the 100 ms behavior tick fire via bounded catch-up
  (`MAX_CATCHUP_SHOTS = 8`/tick → ~80 shots/s ceiling per sequence). Re-entering simulation
  range after a stall dumps at most one capped catch-up burst.
- Spin is baked **per shot at fire time**; an `offset()` mount orbits with the same spin.
- Difficulty is compile-time. There is no runtime difficulty flag — spawn the variant by name.
- Client changes are only needed when you add a *field* the client must render
  (`module_bindings` regenerate via `server/build.sh` — never hand-edit them).
- Every publish wipes the DB. Content = seeds. If it's not seeded, it's gone.

## New-enemy checklist

- [ ] Roster + phase table on paper (speeds in the 50–200 range, lifetimes sane for speed)
- [ ] `enemy/defs/my_enemy.rs`: one fn per emitter, phases assembled, `seed()` over `Difficulty::ALL` if it's a boss
- [ ] `pub mod my_enemy;` in `enemy/defs/mod.rs`, seed call in `main/lifecycle.rs::init`
- [ ] `cargo check` → `server/build.sh` → `dotnet build client/khvg.csproj`
- [ ] `spawn_enemy` each variant; watch a full phase loop; verify hp-threshold transitions
- [ ] File header documents what was deliberately left out
