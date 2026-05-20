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

impl Seed for ItemStats {
    fn seed(ctx: &ReducerContext, data: ItemStats) {
        if ctx.db.item_stats().item_id().find(&data.item_id).is_some() {
            ctx.db.item_stats().item_id().update(data);
        } else {
            ctx.db.item_stats().insert(data);
        }
    }
}
