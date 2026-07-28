//! World module: map grid math, terrain generation, and the tables both run on.
//!
//! Composition mapping (structure-only, no schema changes): each table is a component — one
//! concern per table — and ids like `profile_id`/`region_id` act as entity ids joining a
//! logical entity's rows across tables. Generation is a fixed pipeline of composable passes,
//! see `terrain`.
//!
//! - `prng` — deterministic SplitMix64 + weighted picks (shared with the client's math)
//! - `noise` — deterministic value noise fields for texture clustering
//! - `hex` — hex/chunk grid math and world-hex enumeration
//! - `wrap` — torus wrapping for chunk coords, positions, and distances
//! - `aoi` — area-of-interest chunk selection

pub mod aoi;
pub mod def_tables;
pub mod hex;
pub mod instance_tables;
pub mod noise;
pub mod prng;
pub mod reducers;
pub mod tables;
pub mod terrain;
pub mod views;
pub mod wrap;
