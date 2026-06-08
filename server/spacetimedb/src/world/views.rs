use spacetimedb::{AnonymousViewContext, Query};
use crate::world::tables::*;

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
