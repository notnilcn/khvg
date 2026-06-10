# client/CLAUDE.md

Guidance for Claude Code when working inside the Godot client (`client/`). See the root `CLAUDE.md` for the project-wide domain map and architecture rules.

The client is 100% C# (Godot 4.6 mono).

## Do not read

`Scripts/module_bindings/` — generated SpacetimeDB bindings. Ask first if you need to look, with a reason.

## Module Map (`Scripts/`)

### `Game/`
- `GameManager.cs` — root singleton (`GameManager.Instance`), root node of `Scenes/main.tscn`. Owns the `DbConnection`, the subscription builder, and instantiates/tracks player/enemy/drop/bullet nodes. Connection flow: "Join World" → `Connect()` → `OnConnected()` → subscription builder → `OnSubscriptionApplied()`.
- `BulletManager.cs` — bridges `BulletPatternEvent` rows to the **BlastBullets2D** GDExtension plugin (`BulletFactory2D`), dispatching per `PatternType`.
- `LobbyGui.cs` — lobby/menu UI (`Scenes/lobby_gui.tscn`). Currently scene navigation only — not yet wired to `create_profile`/`join_world` reducers.
- `DebugOverlay.cs` — debug HUD (`CanvasLayer`); paired with server `main/debug.rs`.

### `Players/Local/`
- `LocalPlayer.cs` — `CharacterBody2D`; WASD movement, mouse-aim rotation, sends `ReportMovement`, subscribes to local player data/stats views.
- `LocalPlayerCombat.cs` — `Node2D`; subscribes to `NearbyEnemies`, calls `ReportEnemyHit`.
- `LocalPlayerInventory.cs` — empty stub, not yet implemented.

### `Players/Remote/`
- `RemotePlayer.cs` — `Node2D`; lerps position/rotation toward values from remote player position subscriptions.

### `Players/Enemies/`
- `Enemy.cs` — `CharacterBody2D`; lerps toward server-authoritative position from `NearbyEnemies`. Listens for `BulletPatternEvent` rows and forwards matching events to `BulletManager`.

### `Items/`
- `Drop.cs` — `Area2D`; calls `PickupDrop(drop_id)` reducer when the local player enters its collision area.

### `World/`
- `HexGridOverlay2D.cs` — `Node2D`; hex grid debug overlay, reads `MapConfig`.

## Setup notes

- No autoloads — `GameManager` is instantiated as the root node of `Scenes/main.tscn`.
- `VampireSurvivorsTemplate.csproj` (project root) has the `SpacetimeDB.ClientSDK` package reference — see the NuGet warning in the root `CLAUDE.md` if it's ever lost.
- **BlastBullets2D** GDExtension plugin (`client/addons/blastbullets2d`) must be present; `BulletManager` calls `ClassDB.Instantiate("DirectionalBulletsData2D")` and `ClassDB.Instantiate("BulletSpeedData2D")` at runtime.
