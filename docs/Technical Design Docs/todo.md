# bullets and abilities optimization

  • P1: player movement/inventory/abilities/stats submodule carve-out; single chunk source of truth; static-vs-runtime inventory slot split; item/seeds.rs dead-tunable triage.
  • P2: bullet pooling + typed interop wrappers; oversized-component extraction (TerrainComponent, LocalPlayer, ItemSidebar); binder row-args signals starting with the bullet path; singletons out of hot paths; dirty-only overlay redraw.

