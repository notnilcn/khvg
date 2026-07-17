use spacetimedb::{AnonymousViewContext, Query};
use crate::item::tables::*;

#[spacetimedb::view(accessor = all_items, public)]
fn all_items(ctx: &AnonymousViewContext) -> impl Query<Item> {
    ctx.from.item().build()
}
