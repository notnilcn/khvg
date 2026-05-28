use spacetimedb::{ReducerContext, Table};
use crate::world::tables::*;

pub trait Seed: Sized {
    fn seed(ctx: &ReducerContext, data: Self);
}

impl Seed for MapConfig {
    fn seed(ctx: &ReducerContext, data: MapConfig) {
        if ctx.db.map_config().id().find(&0u32).is_some() {
            ctx.db.map_config().id().update(data);
        } else {
            ctx.db.map_config().insert(data);
        }
    }
}

impl Seed for TextureEntry {
    fn seed(ctx: &ReducerContext, data: TextureEntry) {
        if ctx.db.texture_entry().texture_id().find(&data.texture_id).is_some() {
            ctx.db.texture_entry().texture_id().update(data);
        } else {
            ctx.db.texture_entry().insert(data);
        }
    }
}

pub fn seed_default_textures(ctx: &ReducerContext) {
    TextureEntry::seed(ctx, TextureEntry {
        texture_id: "Enemy".to_string(),
        res_path: "res://Assets/Tiny Swords (Free Pack)/Units/Yellow Units/Warrior/Knight.tres".to_string(),
        kind: TextureKind::Enemy,
    });
    TextureEntry::seed(ctx, TextureEntry {
        texture_id: "Knight".to_string(),
        res_path: "res://Assets/Tiny Swords (Free Pack)/Units/Yellow Units/Warrior/Knight.tres".to_string(),
        kind: TextureKind::Player,
    });
    TextureEntry::seed(ctx, TextureEntry {
        texture_id: "bullet".to_string(),
        res_path: "res://Assets/player_bullets/Bullet.png".to_string(),
        kind: TextureKind::Bullet,
    });
}



impl Seed for ItemStats {
    fn seed(ctx: &ReducerContext, data: ItemStats) {
        if ctx.db.item_stats().item_id().find(&data.item_id).is_some() {
            ctx.db.item_stats().item_id().update(data);
        } else {
            ctx.db.item_stats().insert(data);
        }
    }
}
