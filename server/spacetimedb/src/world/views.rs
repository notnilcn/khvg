use spacetimedb::{AnonymousViewContext, ViewContext, Query};
use crate::world::tables::*;
use crate::main::global::DEFAULT_TERRAIN_AOI_CHUNK_RADIUS;
use crate::world::instance_tables::*;
use crate::player::views::nearby_indices_from_chunk;

#[spacetimedb::view(accessor = all_textures, public)]
fn all_textures(ctx: &AnonymousViewContext) -> impl Query<TextureEntry> {
    ctx.from.texture_entry().build()
}

#[spacetimedb::view(accessor = all_items, public)]
fn all_items(ctx: &AnonymousViewContext) -> impl Query<Item> {
    ctx.from.item().build()
}

#[spacetimedb::view(accessor = all_weapons, public)]
fn all_weapons(ctx: &AnonymousViewContext) -> impl Query<Weapon> {
    ctx.from.weapon().build()
}

#[spacetimedb::view(accessor = all_armor, public)]
fn all_armor(ctx: &AnonymousViewContext) -> impl Query<Armor> {
    ctx.from.armor().build()
}

#[spacetimedb::view(accessor = all_artifacts, public)]
fn all_artifacts(ctx: &AnonymousViewContext) -> impl Query<Artifact> {
    ctx.from.artifact().build()
}

#[spacetimedb::view(accessor = all_accessories, public)]
fn all_accessories(ctx: &AnonymousViewContext) -> impl Query<Accessory> {
    ctx.from.accessory().build()
}

#[spacetimedb::view(accessor = all_consumables, public)]
fn all_consumables(ctx: &AnonymousViewContext) -> impl Query<Consumable> {
    ctx.from.consumable().build()
}

#[spacetimedb::view(accessor = nearby_terrain_tiles, public)]
fn nearby_terrain_tiles(ctx: &ViewContext) -> impl Query<TriangleTile> {
    let empty = ctx.from.triangle_tile().r#where(|t| t.tile_id.eq(u64::MAX)).build();
    let indices = match nearby_indices_from_chunk(ctx, DEFAULT_TERRAIN_AOI_CHUNK_RADIUS) {
        Some(i) if !i.is_empty() => i,
        _ => return empty,
    };
    ctx.from.triangle_tile()
        .r#where(move |t| {
            let mut cond = t.chunk_index.eq(indices[0]);
            for &idx in &indices[1..] { cond = cond.or(t.chunk_index.eq(idx)); }
            cond
        })
        .build()
}

#[spacetimedb::view(accessor = nearby_hex_decor, public)]
fn nearby_hex_decor(ctx: &ViewContext) -> impl Query<HexDecor> {
    let empty = ctx.from.hex_decor().r#where(|d| d.decor_id.eq(u64::MAX)).build();
    let indices = match nearby_indices_from_chunk(ctx, DEFAULT_TERRAIN_AOI_CHUNK_RADIUS) {
        Some(i) if !i.is_empty() => i,
        _ => return empty,
    };
    ctx.from.hex_decor()
        .r#where(move |d| {
            let mut cond = d.chunk_index.eq(indices[0]);
            for &idx in &indices[1..] { cond = cond.or(d.chunk_index.eq(idx)); }
            cond
        })
        .build()
}
