use spacetimedb::{ViewContext, Query, AnonymousViewContext};
use crate::player::views::nearby_indices;
use crate::enemy::tables::*;

#[spacetimedb::view(accessor = nearby_enemies, public)]
fn nearby_enemies(ctx: &ViewContext) -> impl Query<Enemy> {
    let empty = ctx.from.enemy().r#where(|e| e.enemy_id.eq(u64::MAX)).build();
    let indices = match nearby_indices(ctx) {
        Some(i) if !i.is_empty() => i,
        _ => return empty,
    };
    ctx.from.enemy().r#where(move |e| {
        let mut cond = e.chunk_index.eq(indices[0]);
        for &idx in &indices[1..] { cond = cond.or(e.chunk_index.eq(idx)); }
        cond
    }).build()
}

#[spacetimedb::view(accessor = enemy_templates, public)]
fn enemy_templates(ctx: &AnonymousViewContext) -> impl Query<EnemyTemplate> {
    ctx.from.enemy_template().build()
}
