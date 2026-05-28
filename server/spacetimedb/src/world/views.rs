use spacetimedb::{AnonymousViewContext, Query};
use crate::world::tables::*;

#[spacetimedb::view(accessor = all_textures, public)]
fn all_textures(ctx: &AnonymousViewContext) -> impl Query<TextureEntry> {
    ctx.from.texture_entry().build()
}
