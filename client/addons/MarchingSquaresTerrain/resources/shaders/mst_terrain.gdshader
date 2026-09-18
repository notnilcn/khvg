shader_type spatial;
// cull_back (not disabled): the chunk builder winds every triangle for
// outside/above visibility (see "Front faces wind clockwise" in
// marching_squares_terrain_chunk.gd). With culling disabled, shallow ortho
// pitches put the bottom of the frustum below the ground sheet, and those
// rays hit mesh undersides/interiors rendered as a flat brown band along the
// bottom of the frame. Backface culling lets those rays pass through to the
// far side instead, so no ground clipping is ever visible.
render_mode diffuse_toon, depth_draw_opaque, alpha_to_coverage, cull_back;

#include "mst_terrain.gdshaderinc"
