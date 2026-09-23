use spacetimedb::{ReducerContext, Table};
use std::collections::HashMap;
use crate::main::global::*;
use crate::world::aoi::surrounding_chunk_indices;
use crate::world::hex::{spiral_chunk_index, world_to_chunk};
use crate::world::pathing::HexTerrain;
use crate::world::wrap::{wrap_world_pos, wrapped_distance_sq};
use crate::world::tables::MapConfig;
use crate::player::methods::*;
use crate::item::methods::resolve_effective_weapon;
use crate::player::tables::{logged_in_player, player_chunk, player_position};
use crate::combat::deal_damage_to_enemy;
use crate::enemy::def_tables::*;
use crate::enemy::instance_tables::*;
use crate::enemy::methods::*;
#[spacetimedb::reducer]
pub fn tick_enemy_spawn(ctx: &ReducerContext, _job: EnemySpawnSchedule) {
    spawn_from_biome_regions(ctx);
}

#[spacetimedb::reducer]
pub fn tick_enemy_behavior(ctx: &ReducerContext, _job: EnemyBehaviorSchedule) {
    let cfg = MapConfig::load(ctx, 0, 0);
    let (chunk_hex_radius, hex_outer_radius, chunk_cols, chunk_rows) =
        (cfg.chunk_hex_radius, cfg.hex_outer_radius, cfg.chunk_cols, cfg.chunk_rows);

    // Simulation set, chunk-gated: only enemies inside SIMULATION_CHUNK_RINGS of a
    // logged-in player simulate at 10 Hz — no full enemy-table scan. PlayerChunk is the
    // right source (written only on real chunk crossings, not on every movement tick).
    // SIMULATION_CHUNK_RINGS covers the largest move/attack_sim_factor range (see
    // global.rs), so anything outside this set would fail the exact wrap-distance check
    // below anyway. Distant enemies keep was_simulating=true while unvisited; that is
    // harmless — re-entry retargets via the stale-target check, and stale step_timers
    // are clamped by BACKLOG_CAP.
    let mut active_chunks: Vec<i64> = Vec::new();
    for player in ctx.db.logged_in_player().iter() {
        if let Some(chunk) = ctx.db.player_chunk().profile_id().find(&player.profile_id) {
            active_chunks.extend(surrounding_chunk_indices(
                chunk.chunk_q, chunk.chunk_r, chunk_cols, chunk_rows, Some(SIMULATION_CHUNK_RINGS),
            ));
        }
    }
    active_chunks.sort_unstable();
    active_chunks.dedup();
    let mut candidates: Vec<Enemy> = Vec::new();
    for idx in &active_chunks {
        candidates.extend(ctx.db.enemy().chunk_index().filter(idx));
    }
    // Per-tick simulation budget: bounds the admin-spawn flood path (biome spawning caps
    // live enemies per region; spawn_enemy does not). Lowest enemy_ids win; the overflow
    // is logged, not silent.
    if candidates.len() > BEHAVIOR_TICK_ENEMY_BUDGET {
        log::warn!("tick_enemy_behavior over budget ({} enemies, budget {}); lowest ids simulate this tick", candidates.len(), BEHAVIOR_TICK_ENEMY_BUDGET);
        candidates.truncate(BEHAVIOR_TICK_ENEMY_BUDGET);
    }

    // Terrain snapshot shared by every path recompute this tick (elevation + edge masks):
    // built lazily on the first recompute, then reused, so the two full-table scans happen
    // at most once per tick instead of once per path. Terrain is static between ticks.
    let mut terrain_snapshot: Option<HashMap<(i32, i32), HexTerrain>> = None;

    for enemy in candidates {
        let mut behavior = match ctx.db.enemy_behavior().behavior_id().find(&enemy.behavior_id) {
            Some(b) => b,
            None => continue,
        };
        // Delta-check snapshot: the end-of-tick row update is skipped when nothing changed.
        let behavior_snapshot = (
            behavior.aggro_target, behavior.aggro_locked_until, behavior.was_simulating,
            behavior.active_phase_index, behavior.velocity_x, behavior.velocity_y,
            behavior.path.clone(), behavior.path_next, behavior.path_goal_q, behavior.path_goal_r, behavior.path_tick,
        );
        let template = match ctx.db.enemy_template().template_id().find(&enemy.template_id) {
            Some(t) => t,
            None => continue,
        };
        let move_range = template.move_sim_factor * CAMERA_VIEW_RADIUS;
        if !has_player_in_simulation_range(ctx, enemy.x, enemy.y, move_range) {
            if behavior.was_simulating {
                ctx.db.enemy_behavior().behavior_id().update(EnemyBehavior { was_simulating: false, ..behavior });
            }
            continue;
        }

        // Enemy-status consult: a Stunned row on a Pause template freezes the enemy — no
        // attacks and no movement this tick (Slowed/Enraged templates keep simulating with
        // scaled movement/pacing instead, via apply_movement/effective_interval). Checked
        // before the aggro recompute so frozen enemies skip that work too.
        if template.stun_behavior == StunBehavior::Pause
            && crate::status::methods::stunned_magnitude(ctx, enemy.enemy_id).is_some()
        {
            continue;
        }

        // Retarget on the tick an enemy *enters* move range — and immediately whenever the
        // current aggro target goes stale (logged out / died / left sim range), instead of
        // chasing a ghost until the next re-entry. Steady-state mid-fight ticks still skip
        // the recompute.
        if !behavior.was_simulating {
            let (aggro_target, aggro_locked_until) = recompute_aggro(ctx, enemy.x, enemy.y, template.aggro_lock_seconds);
            behavior.aggro_target = aggro_target;
            behavior.aggro_locked_until = aggro_locked_until;
        } else if let Some(target) = behavior.aggro_target {
            let stale = match find_player_pos_by_id(ctx, target) {
                Some((px, py)) => wrapped_distance_sq(px, py, enemy.x, enemy.y, (cfg.lap_q_x, cfg.lap_q_y), (cfg.lap_r_x, cfg.lap_r_y)) > move_range * move_range,
                None => true,
            };
            if stale {
                let (aggro_target, aggro_locked_until) = recompute_aggro(ctx, enemy.x, enemy.y, template.aggro_lock_seconds);
                behavior.aggro_target = aggro_target;
                behavior.aggro_locked_until = aggro_locked_until;
            }
        }
        behavior.was_simulating = true;

        let phase_idx = enemy.phase as usize;
        let phase_def = match template.phases.get(phase_idx) {
            Some(p) => p,
            None => continue,
        };
        let mut phase = match ctx.db.enemy_phases().behavior_id().filter(&enemy.behavior_id).find(|p| p.phase_index == enemy.phase) {
            Some(p) => p,
            None => continue,
        };
        // Delta-check snapshot for the end-of-tick update.
        let phase_snapshot = (phase.phase_waiting, phase.phase_loop_timer, phase.move_cycle_started_at);

        // enemy.phase is advanced by report_enemy_hit when HP crosses a threshold;
        // detect that transition here and reset the new phase's attack sequences.
        let mut active_phase_index = behavior.active_phase_index;
        if enemy.phase != active_phase_index {
            active_phase_index = enemy.phase;
            begin_phase_cycle(ctx, &mut phase, phase_def);
        }

        // Movement runs from move_sim_factor so the enemy is already approaching by the time
        // it's close — but it only opens fire once a player is within attack_sim_factor, so it
        // never shoots from off-screen.
        if has_player_in_simulation_range(ctx, enemy.x, enemy.y, template.attack_sim_factor * CAMERA_VIEW_RADIUS) {
            for ev in tick_phase(ctx, enemy.enemy_id, &template.stun_behavior, &mut phase, phase_def) {
                let target = resolve_step_target(ev.target, behavior.aggro_target);
                // Damage is stamped here so clients (and report_hit, via the attestation
                // row) never resolve step defs themselves.
                let damage = step_damage(ctx, &ev.source_step).unwrap_or(0);
                let fired = ctx.db.bullet_pattern_event().insert(BulletPatternEvent {
                    event_id: 0, enemy_id: enemy.enemy_id,
                    source_step: ev.source_step.clone(),
                    damage,
                    origin_x: enemy.x, origin_y: enemy.y,
                    origin_offset_x: ev.offset_x, origin_offset_y: ev.offset_y,
                    base_angle_offset: ev.base_angle_offset,
                    pattern_type: ev.pattern_type,
                    target, texture_id: ev.texture_id, lifetime: ev.lifetime,
                    angular_speed: ev.angular_speed,
                    speed_acceleration: ev.speed_acceleration,
                    chunk_index: enemy.chunk_index,
                });
                // Prove this volley was really fired so report_hit can attest the
                // victim's claim against it instead of trusting a client-chosen step.
                attest_bullet_fire(ctx, fired.event_id, enemy.enemy_id, damage, &fired.pattern_type);
            }
        }

        let (new_x, new_y, new_dir_x, new_dir_y, new_move_dir, new_move_speed) = apply_movement(ctx, &enemy, &mut behavior, &phase, &template.stun_behavior, &mut terrain_snapshot);
        let (new_x, new_y) = wrap_world_pos(new_x, new_y, chunk_hex_radius, chunk_cols, chunk_rows, hex_outer_radius);
        let (cq, cr) = world_to_chunk(new_x, new_y, chunk_hex_radius, hex_outer_radius);
        let chunk_index = spiral_chunk_index(cq, cr);

        // Delta-checked writes: rows are only updated when a field actually changed.
        if (phase.phase_waiting, phase.phase_loop_timer, phase.move_cycle_started_at) != phase_snapshot {
            ctx.db.enemy_phases().phase_id().update(phase);
        }
        if (
            behavior.aggro_target, behavior.aggro_locked_until, behavior.was_simulating,
            active_phase_index, new_dir_x, new_dir_y,
            behavior.path.clone(), behavior.path_next, behavior.path_goal_q, behavior.path_goal_r, behavior.path_tick,
        ) != behavior_snapshot {
            ctx.db.enemy_behavior().behavior_id().update(EnemyBehavior {
                active_phase_index, velocity_x: new_dir_x, velocity_y: new_dir_y,
                ..behavior
            });
        }
        if new_x != enemy.x || new_y != enemy.y || chunk_index != enemy.chunk_index
            || new_move_dir != enemy.movement_direction || new_move_speed != enemy.movement_speed
        {
            ctx.db.enemy().enemy_id().update(Enemy { x: new_x, y: new_y, movement_direction: new_move_dir, movement_speed: new_move_speed, chunk_index, ..enemy });
        }
    }
}

#[spacetimedb::reducer]
pub fn report_enemy_hit(ctx: &ReducerContext, enemy_id: u64) -> Result<(), String> {
    let player = require_in_world(ctx)?;

    // Sustained-rate budget (PlayerHitRate token bucket, 1s window): equipped weapon
    // fire_rate × effective shot_count × HIT_RATE_SLACK. Over budget = spam/farming —
    // rejected before any damage math. Damage math itself is unchanged.
    let cap = resolve_effective_weapon(ctx, player.profile_id)
        .map(|w| (w.behavior.fire_rate * w.behavior.shot_count.max(1) as f32 * HIT_RATE_SLACK).ceil() as u32)
        .unwrap_or(0);
    if !hit_rate_allow(ctx, player.profile_id, true, cap) {
        return Err("Hit report rate exceeded.".to_string());
    }

    let enemy = match ctx.db.enemy().enemy_id().find(&enemy_id) {
        Some(e) => e,
        None => return Ok(()),
    };
    if enemy.immortal { return Ok(()); }

    // Wrap-aware range check, attacker position row ↔ enemy — report_enemy_hit stays
    // self-reported (see the trust model); this only rejects nonsense attributions,
    // same radius report_hit uses.
    let pos = ctx.db.player_position().profile_id().find(&player.profile_id).ok_or("Position not found.")?;
    let cfg = MapConfig::load(ctx, 0, 0);
    let dist_sq = wrapped_distance_sq(pos.x, pos.y, enemy.x, enemy.y, (cfg.lap_q_x, cfg.lap_q_y), (cfg.lap_r_x, cfg.lap_r_y));
    if dist_sq > HIT_VALIDATION_RADIUS * HIT_VALIDATION_RADIUS {
        return Err("Enemy is not near the player.".to_string());
    }

    deal_damage_to_enemy(ctx, player.profile_id, enemy);
    Ok(())
}
