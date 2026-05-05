/*
Network code
```c# */
using Godot;
using Godot.Collections;
using System.Collections.Generic;
using SpacetimeDB.Types;

// Overall Description
// Handles spawning of enemies AND players.
//
// ENEMY SPAWNING
// Only the elevated server spawns and despawns enemies via SpacetimeDB reducers.
// StdbEnemySync receives EnemyState table callbacks and spawns/frees nodes
// identically on every client.
//
// PLAYER SPAWNING
// Each instance spawns exactly one local player in _Ready() (skipped on the elevated server).


public partial class Spawner : Node2D
{
	private static readonly PackedScene DropScene =
		GD.Load<PackedScene>("res://VS-Like/Characters/Enemy/Utilities/Drop/drop.tscn");
	private static readonly ResDropFood ResFood =
		GD.Load<ResDropFood>("res://VS-Like/Characters/Enemy/Utilities/Drop/Types/Food/res_food.tres");
	private static readonly PackedScene EnemyScene =
		GD.Load<PackedScene>("res://VS-Like/Characters/Enemy/enemy.tscn");
	private static readonly PackedScene PlayerScene =
		GD.Load<PackedScene>("res://VS-Like/Characters/Player/player.tscn");

	private const int MinSpawnRadius = 400;
	private const int MaxEnemies     = 300;
	private const string DummyEnemyRes  = "res://VS-Like/Characters/Enemy/Types/Dummy/res_dummy.tres";
	private const string GoblinEnemyRes = "res://VS-Like/Characters/Enemy/Types/Globlin/res_goblin.tres";

	// Maps friendly command names to ResItem resource paths.
	private static readonly System.Collections.Generic.Dictionary<string, string> WeaponResPaths = new()
	{
		{ "axe",       "res://VS-Like/Characters/Player/Utilities/Weapons/Types/Axe/res_axe.tres" },
		{ "arrow",     "res://VS-Like/Characters/Player/Utilities/Weapons/Types/Arrow/res_arrow.tres" },
		{ "bible",     "res://VS-Like/Characters/Player/Utilities/Weapons/Types/Bible/res_bible.tres" },
		{ "boots",     "res://VS-Like/Characters/Player/Utilities/Weapons/Types/Boots/res_boots.tres" },
		{ "parchment", "res://VS-Like/Characters/Player/Utilities/Weapons/Types/Parchment/res_parchment.tres" },
		{ "ring",      "res://VS-Like/Characters/Player/Utilities/Weapons/Types/Ring/res_ring.tres" },
		{ "sinebow",   "res://VS-Like/Characters/Player/Utilities/Weapons/Types/SineArrow/res_sine_arrow.tres" },
	};

	[Export] public Array<ResEnemy> AllResEnemy { get; set; } = new();

	private Timer         _spawnTimer;
	private VBoxContainer _vbox;

	private int  _enemiesCount;
	private int  _minutes;
	private int  _seconds;
	public  bool SpawningEnabled { get; set; } = true;

	private int  _nextEnemyId;
	private int  _spawnPositionIndex;
	private bool _initialized;
	private Label _chatFeedback;
	private Timer _stdbSnapshotTimer;

	// Tracks the last reported target per enemy so we can fire UpdateEnemyTarget immediately on change.
	private readonly System.Collections.Generic.Dictionary<int, string> _lastTargetIds = new();

	private static readonly Vector2[] PlayerSpawnPositions =
	{
		new(0,    0),
		new(200,  0),
		new(-200, 0),
		new(0,    200),
	};
/*
```
*/


/*
# `_Ready()`
```c# */
	public override void _Ready()
	{
		// Guard against _Ready() being called a second time (e.g. scene reload quirk).
		if (_initialized)
			return;
		_initialized = true;

		_spawnTimer = GetNode<Timer>("SpawnTimer");
		_vbox       = GetNode<VBoxContainer>("CanvasLayer/MarginContainer/VBoxContainer");

		// Seed the initial enemy pool if the scene didn't populate it (typed-array scene format is unreliable for C# GlobalClass types).
		if (AllResEnemy.Count == 0)
			AllResEnemy.Add(GD.Load<ResEnemy>(GoblinEnemyRes));

		if (!ElevatedClient.IsActive)
			_SpawnLocalPlayer();

		_SetupChatConsole();

		// Populate visuals for SpacetimeDB remote players that were already in the DB
		// before this map scene loaded (their OnInsert fired while we were in the lobby).
		StdbPlayerSync.Instance?.OnGameSceneLoaded();

		// Wire up STDB enemy sync. OnGameSceneLoaded handles late-join catch-up for clients.
		StdbEnemySync.Instance?.OnGameSceneLoaded(this);

		// Elevated server: clear stale enemy rows from previous sessions and start snapshot reporting.
		if (ElevatedClient.IsActive)
		{
			var stdb = SpacetimeDBManager.Instance;
			if (stdb.IsSubscriptionReady)
				_OnStdbReady();
			else
				stdb.SubscriptionReady += _OnStdbReady;
		}
	}

	private void _OnStdbReady()
	{
		SpacetimeDBManager.Instance.SubscriptionReady -= _OnStdbReady;
		if (!ElevatedClient.IsActive) return;

		var conn = SpacetimeDBManager.Instance.Conn;
		if (conn == null) return;

		// Delete any rows left over from a previous session before spawning new enemies.
		foreach (var row in conn.Db.EnemyState.Iter())
			conn.Reducers.DespawnEnemy(row.EnemyId);

		_stdbSnapshotTimer = new Timer();
		_stdbSnapshotTimer.WaitTime = 0.1;
		_stdbSnapshotTimer.Timeout += _OnStdbSnapshotTimer;
		AddChild(_stdbSnapshotTimer);
		_stdbSnapshotTimer.Start();
		GD.Print("[Spawner] STDB enemy reporting started");
	}

	private void _OnStdbSnapshotTimer()
	{
		if (!ElevatedClient.IsActive) return;
		var conn = SpacetimeDBManager.Instance.Conn;
		if (conn == null) return;

		var snapshots = new List<SpacetimeDB.Types.EnemySnapshot>();

		foreach (var child in GetChildren())
		{
			if (child is not Enemy enemy || enemy.EnemyId < 0) continue;

			// Fire UpdateEnemyTarget immediately if the target changed.
			string targetId = _GetStdbTargetId(enemy);
			if (!_lastTargetIds.TryGetValue(enemy.EnemyId, out var lastTarget) || lastTarget != targetId)
			{
				_lastTargetIds[enemy.EnemyId] = targetId;
				conn.Reducers.UpdateEnemyTarget((ulong)enemy.EnemyId, targetId);
			}

			snapshots.Add(new SpacetimeDB.Types.EnemySnapshot
			{
				EnemyId = (ulong)enemy.EnemyId,
				X       = enemy.GlobalPosition.X,
				Y       = enemy.GlobalPosition.Y,
				MoveX   = (sbyte)Mathf.Sign(enemy.MoveDir.X),
				MoveY   = (sbyte)Mathf.Sign(enemy.MoveDir.Y),
				Speed   = (ushort)enemy.Speed,
			});
		}

		if (snapshots.Count > 0)
			conn.Reducers.UpdateEnemySnapshots(snapshots);
	}

	private string _GetStdbTargetId(Enemy enemy)
	{
		return StdbPlayerSync.Instance?.GetIdentityForNode(enemy.Target) ?? "";
	}
/*
```
*/



/*
# `_SetupChatConsole()`
```c# */
	// ── Chat console ─────────────────────────────────────────────────────────────

	private void _SetupChatConsole()
	{
		_chatFeedback = new Label();
		_chatFeedback.AddThemeFontSizeOverride("font_size", 8);
		_vbox.AddChild(_chatFeedback);

		var input = new LineEdit();
		input.PlaceholderText = "press T to chat";
		input.AddThemeFontSizeOverride("font_size", 8);
		input.TextSubmitted += _OnCommandSubmitted;
		input.TextSubmitted += _ => input.ReleaseFocus();
		input.FocusExited   += () => GetViewport().GuiReleaseFocus();
		_vbox.AddChild(input);
	}
/*
```
*/


/*

```c# */
	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event is not InputEventKey ev || !ev.Pressed || ev.Echo)
			return;
		var input = _vbox.GetChild(_vbox.GetChildCount() - 1) as LineEdit;
		if (input == null || input.HasFocus())
			return;
		if (ev.Keycode == Key.T)
		{
			input.GrabFocus();
			GetViewport().SetInputAsHandled();
		}
	}
/*
```
*/


/*

```c# */
	private void _OnCommandSubmitted(string raw)
	{
		string text  = raw.Trim();
		var    input = _vbox.GetChild(_vbox.GetChildCount() - 1) as LineEdit;
		input?.Clear();

		if (string.IsNullOrEmpty(text))
			return;
		_ParseCommand(text);
	}
/*
```
*/


/*

```c# */
	private void _ParseCommand(string cmd)
	{
		string[] parts = cmd.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
		string   verb  = parts[0].ToLower();
		string[] args  = parts[1..];

		switch (verb)
		{
			case "/spawner":
			case "/give":
			case "/remove":
			case "/spawn":
			case "/chest":
			case "/tickrate":
				_ExecuteAdminCmd(verb, args);
				break;

			case "/stats":
			case "/inv":
				_CmdSelfinfo(verb);
				break;

			case "/stdbpos":
				_CmdStdbPos(args);
				break;

			default:
				_ShowFeedback("Unknown command. Try /stats, /inv, /stdbpos, /spawner, /give, /remove, /spawn, /chest, /tickrate");
				break;
		}
	}
/*
```
*/



/*

```c# */
	// /stats — print RPG stats; /inv — print equipment and inventory. Both run locally, no admin needed.
	private void _CmdSelfinfo(string verb)
	{
		var player = Global.Instance.GetLocalPlayer();
		if (player == null)
		{
			_ShowFeedback("No local player found");
			return;
		}

		string msg;
		switch (verb)
		{
			case "/stats":
				msg = string.Format(
					"STR {0}  WIS {1}  DEX {2}  DEF {3}  VIT {4}\nHP {5}/{6}   Slots: {7} acc",
					player.Strength, player.Wisdom, player.Dexterity,
					player.Defense,  player.Vitality,
					(int)player.Health, (int)player.MaxHealth,
					player.GetAccessorySlotCount());
				break;

			case "/inv":
			{
				string wpn = player.EquippedWeapon  != null ? player.EquippedWeapon.Resource.ItemName  : "—";
				string abl = player.EquippedAbility != null ? player.EquippedAbility.Resource.ItemName : "—";
				string arm = "—";
				var    accNames = new List<string>();
				foreach (var acc in player.EquippedAccessories)
					accNames.Add(acc.Resource.ItemName);
				string accStr = accNames.Count > 0 ? string.Join(", ", accNames) : "—";
				var    invNames = new List<string>();
				foreach (var res in player.Inventory)
					invNames.Add(res.ItemName);
				string invStr = invNames.Count > 0 ? string.Join(", ", invNames) : "empty";
				msg = string.Format(
					"Wpn:{0}  Abl:{1}  Arm:{2}  Acc:{3}\nInv({4}/{5}): {6}",
					wpn, abl, arm, accStr,
					player.Inventory.Count, player.InventoryMax, invStr);
				break;
			}

			default:
				return;
		}
		_ShowFeedback(msg);
	}
/*
```
*/

/*

```c# */
	// /stdbpos [prefix] -- read a PlayerPosition row from the local STDB cache.
	// With no argument: shows the local player's row.
	// With a prefix argument: finds the first row whose identity string starts with that prefix.
	private void _CmdStdbPos(string[] args)
	{
		var conn = SpacetimeDBManager.Instance?.Conn;
		if (conn == null)
		{
			_ShowFeedback("SpacetimeDB not connected");
			return;
		}

		PlayerPosition? row;

		if (args.Length == 0)
		{
			var localId = SpacetimeDBManager.Instance.LocalIdentity;
			if (!localId.HasValue)
			{
				_ShowFeedback("No local STDB identity yet");
				return;
			}
			row = conn.Db.PlayerPosition.PlayerId.Find(localId.Value);
			if (row is null)
			{
				_ShowFeedback("No PlayerPosition row found (not connected or not spawned)");
				return;
			}
		}
		else
		{
			string prefix = args[0].ToLower();
			PlayerPosition? match = null;
			int matchCount = 0;
			foreach (var r in conn.Db.PlayerPosition.Iter())
			{
				if (r.PlayerId.ToString().ToLower().StartsWith(prefix))
				{
					match = r;
					matchCount++;
				}
			}
			if (matchCount == 0)
			{
				_ShowFeedback($"No player found with identity prefix '{args[0]}'");
				return;
			}
			if (matchCount > 1)
			{
				_ShowFeedback($"Ambiguous prefix '{args[0]}' matches {matchCount} players — use more characters");
				return;
			}
			row = match;
		}

		var id = row!.PlayerId.ToString();
		string label = id.Length >= 8 ? id[..8] : id;
		_ShowFeedback($"[{label}] pos:({row.X:F1},{row.Y:F1})  move:({row.MoveX:F2},{row.MoveY:F2})  speed:{row.Speed:F1}  chunk:({row.ChunkX},{row.ChunkY})");
	}
/*
```
*/

/*

```c# */

	private void _ShowFeedback(string msg)
	{
		if (_chatFeedback == null)
			return;
		_chatFeedback.Text = msg;
		GetTree().CreateTimer(3.0).Timeout += () =>
		{
			if (GodotObject.IsInstanceValid(_chatFeedback))
				_chatFeedback.Text = "";
		};
	}

	// ── Admin chat ───────────────────────────────────────────────────────────────

	private bool _IsAdmin()
	{
		var localId = SpacetimeDBManager.Instance?.LocalIdentity;
		if (!localId.HasValue) return false;
		var acc = SpacetimeDBManager.Instance.Conn?.Db.PlayerAccount.PlayerId.Find(localId.Value);
		return acc?.IsAdmin ?? false;
	}

	private void _SendFeedback(string msg) => _ShowFeedback(msg);

	private void _ExecuteAdminCmd(string verb, string[] args)
	{
		if (!_IsAdmin())
		{
			_ShowFeedback("Permission denied: you are not an admin");
			return;
		}
		switch (verb)
		{
			case "/spawner":   _CmdSpawner(args);   break;
			case "/give":      _CmdGive(args);      break;
			case "/remove":    _CmdRemove(args);    break;
			case "/spawn":     _CmdSpawn(args);     break;
			case "/chest":     _CmdChest(args);     break;
			case "/tickrate":  _CmdTickrate(args);  break;
		}
	}

	// /spawner on|off
	private void _CmdSpawner(string[] args)
	{
		if (args.Length == 0)
		{
			_ShowFeedback("Usage: /spawner on|off");
			return;
		}
		switch (args[0].ToLower())
		{
			case "on":
				SpawningEnabled = true;
				_ShowFeedback("Enemy spawning enabled");
				break;
			case "off":
				SpawningEnabled = false;
				_ShowFeedback("Enemy spawning disabled");
				break;
			default:
				_ShowFeedback("Usage: /spawner on|off");
				break;
		}
	}

	// /give <weapon> [username] — give a weapon to self or another player by username
	private void _CmdGive(string[] args)
	{
		if (args.Length == 0)
		{
			_ShowFeedback("Usage: /give <weapon> [username]\nWeapons: " + string.Join(", ", WeaponResPaths.Keys));
			return;
		}
		string weaponName = args[0].ToLower();
		if (!WeaponResPaths.TryGetValue(weaponName, out string resPath))
		{
			_ShowFeedback($"Unknown weapon '{weaponName}'. Options: {string.Join(", ", WeaponResPaths.Keys)}");
			return;
		}

		var conn = SpacetimeDBManager.Instance.Conn;
		if (conn == null) { _ShowFeedback("Not connected to SpacetimeDB."); return; }

		if (args.Length >= 2)
		{
			// Remote target by username — updates STDB only; they pick it up via OnUpdate callback.
			conn.Reducers.GiveWeapon(args[1], resPath);
			_ShowFeedback($"Gave {weaponName} to {args[1]}.");
		}
		else
		{
			// Self — apply immediately and persist.
			Global.Instance.GetLocalPlayer()?.ApplyGiveWeapon(resPath);
			var localId = SpacetimeDBManager.Instance.LocalIdentity;
			if (localId.HasValue)
			{
				var acc = conn.Db.PlayerAccount.PlayerId.Find(localId.Value);
				if (acc != null && !string.IsNullOrEmpty(acc.Username))
					conn.Reducers.GiveWeapon(acc.Username, resPath);
			}
			_ShowFeedback($"Gave {weaponName}.");
		}
	}

	// /remove <weapon> [username] — remove a weapon from self or another player by username
	private void _CmdRemove(string[] args)
	{
		if (args.Length == 0)
		{
			_ShowFeedback("Usage: /remove <weapon> [username]\nWeapons: " + string.Join(", ", WeaponResPaths.Keys));
			return;
		}
		string weaponName = args[0].ToLower();
		if (!WeaponResPaths.TryGetValue(weaponName, out string resPath))
		{
			_ShowFeedback($"Unknown weapon '{weaponName}'. Options: {string.Join(", ", WeaponResPaths.Keys)}");
			return;
		}

		var conn = SpacetimeDBManager.Instance.Conn;
		if (conn == null) { _ShowFeedback("Not connected to SpacetimeDB."); return; }

		if (args.Length >= 2)
		{
			// Remote target by username — updates STDB persistence only.
			conn.Reducers.RemoveWeapon(args[1], resPath);
			_ShowFeedback($"Removed {weaponName} from {args[1]}.");
		}
		else
		{
			// Self — apply immediately and persist.
			Global.Instance.GetLocalPlayer()?.ApplyRemoveWeapon(resPath);
			var localId = SpacetimeDBManager.Instance.LocalIdentity;
			if (localId.HasValue)
			{
				var acc = conn.Db.PlayerAccount.PlayerId.Find(localId.Value);
				if (acc != null && !string.IsNullOrEmpty(acc.Username))
					conn.Reducers.RemoveWeapon(acc.Username, resPath);
			}
			_ShowFeedback($"Removed {weaponName}.");
		}
	}

	// /spawn [straight|sine] [x y] — fire bullets directly from pos (no enemy drift) or spawn a plain dummy.
	private void _CmdSpawn(string[] args)
	{
		byte patternType = StdbBulletSync.PatternNone;
		var remaining = args;

		if (remaining.Length > 0 && (remaining[0] == "straight" || remaining[0] == "sine"))
		{
			patternType = remaining[0] == "straight" ? StdbBulletSync.PatternStraight : StdbBulletSync.PatternSine;
			remaining = remaining[1..];
		}

		if (remaining.Length == 1)
		{
			_ShowFeedback("Usage: /spawn [straight|sine] [x y]");
			return;
		}

		var pos = Vector2.Zero;
		if (remaining.Length >= 2 &&
			float.TryParse(remaining[0], out float px) &&
			float.TryParse(remaining[1], out float py))
			pos = new Vector2(px, py);

		if (patternType != StdbBulletSync.PatternNone)
		{
			Vector2 firePos = pos;
			byte    pattern = patternType;
			var     timer   = new Timer();
			timer.WaitTime  = 1.0;
			timer.Autostart = true;
			timer.Timeout  += () => StdbBulletSync.Instance?.FireBullet(firePos, pattern);
			AddChild(timer);
			string typeName = patternType == StdbBulletSync.PatternStraight ? "straight" : "sine";
			_ShowFeedback($"Firing {typeName} bullets from ({(int)pos.X},{(int)pos.Y}) every 1s");
			return;
		}

		var res = GD.Load<ResEnemy>(DummyEnemyRes);
		_CreateEnemy(pos, res, isElite: false, immortal: true);
		_ShowFeedback($"Spawned dummy at ({(int)pos.X}, {(int)pos.Y})");
	}

	// /chest [x y] — spawn an elite enemy guaranteed to drop a chest when killed.
	private void _CmdChest(string[] args)
	{
		if (args.Length == 1)
		{
			_ShowFeedback("Usage: /chest [x y]");
			return;
		}

		Vector2 pos;
		if (args.Length >= 2 &&
			float.TryParse(args[0], out float px) &&
			float.TryParse(args[1], out float py))
		{
			pos = new Vector2(px, py);
		}
		else
		{
			var refPlayer = _GetReferencePlayer();
			if (refPlayer == null)
			{
				_ShowFeedback("No players in the world to spawn near");
				return;
			}
			pos = _GetRandomSpawnPos(refPlayer.GlobalPosition, MinSpawnRadius);
		}

		ResEnemy res = AllResEnemy.Count > 0 ? _GetEnemy() : GD.Load<ResEnemy>(GoblinEnemyRes);
		_CreateEnemy(pos, res, isElite: true);
		_ShowFeedback($"Spawned elite enemy (chest drop) at ({(int)pos.X}, {(int)pos.Y})");
	}

	private void _CmdTickrate(string[] args)
	{
		_ShowFeedback("/tickrate not yet implemented (requires a scheduled reducer)");
	}

	// ── Player spawning ───────────────────────────────────────────────────────────

	private static readonly ResPlayer DefaultCharacterData =
		GD.Load<ResPlayer>("res://VS-Like/Characters/Player/Types/Dwarf/res_dwarf.tres");

	private void _SpawnLocalPlayer()
	{
		if (GetNodeOrNull("Player_Local") != null)
			return;
		var pos    = PlayerSpawnPositions[_spawnPositionIndex++ % PlayerSpawnPositions.Length];
		var player = PlayerScene.Instantiate<Player>();
		player.Name          = "Player_Local";
		player.CharacterData = DefaultCharacterData;
		player.IsLocalPlayer = true;
		AddChild(player);
		player.GlobalPosition = pos;
	}

	// ── Enemy spawning ────────────────────────────────────────────────────────────

	private void _SpawnSingleEnemy(bool isElite = false)
	{
		var refPlayer = _GetReferencePlayer();
		if (refPlayer == null)
			return;
		var spawnPos = _GetRandomSpawnPos(refPlayer.GlobalPosition, MinSpawnRadius);
		_CreateEnemy(spawnPos, _GetEnemy(), isElite);
	}

	private void _SpawnCirclePattern(int enemiesToSpawn)
	{
		var refPlayer = _GetReferencePlayer();
		if (refPlayer == null)
			return;
		Vector2 playerPos = refPlayer.GlobalPosition;
		float   angleStep = Mathf.Tau / enemiesToSpawn;
		for (int i = 0; i < enemiesToSpawn; i++)
		{
			float   angle    = i * angleStep;
			Vector2 spawnPos = playerPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * MinSpawnRadius;
			_CreateEnemy(spawnPos, AllResEnemy[0]);
		}
	}

	// SERVER ONLY: spawn an enemy and replicate it to all connected clients.
	private void _CreateEnemy(Vector2 spawnPos, ResEnemy resEnemy, bool isElite = false, bool immortal = false, byte bulletPatternType = 0)
	{
		int eid = _nextEnemyId++;

		var conn = SpacetimeDBManager.Instance.IsStdbConnected ? SpacetimeDBManager.Instance.Conn : null;
		uint hp = (uint)(isElite ? resEnemy.Health * 10 : resEnemy.Health);
		conn?.Reducers.SpawnEnemy(
			(ulong)eid, spawnPos.X, spawnPos.Y,
			resEnemy.ResourcePath, isElite, immortal, hp, bulletPatternType);
	}

	// Called on the server when an enemy's Die() method fires.
	// Broadcasts removal to ALL peers so every machine frees the node.
	private void _OnEnemyRequestDespawn(int eid)
	{
		var conn = SpacetimeDBManager.Instance.IsStdbConnected ? SpacetimeDBManager.Instance.Conn : null;
		conn?.Reducers.DespawnEnemy((ulong)eid);
	}

	// Called by StdbEnemySync when the host receives its own enemy via OnInsert.
	// Connects the signals that the host needs to track HP and trigger despawn.
	public void RegisterStdbEnemy(Enemy enemy)
	{
		_enemiesCount++;
		_UpdateEnemiesCountLabel();
		enemy.Died           += EnemyDied;
		enemy.RequestDespawn += _OnEnemyRequestDespawn;
	}

	public void SpawnDrop(string resPath, Vector2 pos)
	{
		var drop            = DropScene.Instantiate<Drop>();
		drop.Resource       = GD.Load<ResDrop>(resPath);
		drop.GlobalPosition = pos;
		GetTree().CurrentScene.CallDeferred("add_child", drop);
	}

	// ── Helpers ───────────────────────────────────────────────────────────────────

	private Player _GetReferencePlayer()
	{
		if (Global.Instance.Players.Count == 0)
			return null;
		foreach (var p in Global.Instance.Players.Values)
			return p;
		return null;
	}

	private Vector2 _GetRandomSpawnPos(Vector2 globalPos, int radiusDistance)
	{
		float angle    = GD.Randf() * Mathf.Tau;
		float distance = radiusDistance + GD.Randf() * 100;
		return globalPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
	}

	private ResEnemy _GetEnemy()
	{
		return AllResEnemy[GD.RandRange(0, AllResEnemy.Count - 1)];
	}

	public void EnemyDied()
	{
		_enemiesCount--;
		_UpdateEnemiesCountLabel();
	}

	private void _AddEnemyType(int minute)
	{
		switch (minute)
		{
			case 1:
				AllResEnemy.Add(GD.Load<ResEnemy>("res://VS-Like/Characters/Enemy/Types/Zombie/res_zombie.tres"));
				_spawnTimer.WaitTime = 0.75;
				break;
			case 2:
				AllResEnemy.Add(GD.Load<ResEnemy>("res://VS-Like/Characters/Enemy/Types/Bat/res_bat.tres"));
				_spawnTimer.WaitTime = 0.5;
				break;
			case 3:
				AllResEnemy.Add(GD.Load<ResEnemy>("res://VS-Like/Characters/Enemy/Types/Cyclops/res_cyclops.tres"));
				_spawnTimer.WaitTime = 0.35;
				break;
		}
	}

	// ── Setters / UI ──────────────────────────────────────────────────────────────

	private void _UpdateEnemiesCountLabel()
	{
		GetNode<Label>("%EnemiesCountLabel").Text = "ENEMIES: " + _enemiesCount;
	}

	private void _SetMinutes(int value)
	{
		_minutes = value;
		if (ElevatedClient.IsActive)
			_AddEnemyType(_minutes);
		GetNode<Label>("%Minutes").Text = _SetDigits(_minutes);
	}

	private void _SetSeconds(int value)
	{
		_seconds = value;
		if (_seconds >= 60)
		{
			_seconds = 0;
			_SetMinutes(_minutes + 1);
		}
		GetNode<Label>("%Seconds").Text = _SetDigits(_seconds);
	}

	private static string _SetDigits(int n)
	{
		string s = Mathf.Abs(n).ToString();
		return (s.Length == 1 ? "0" : "") + n;
	}

	// ── Timer callbacks ───────────────────────────────────────────────────────────

	private void _OnTimerTimeout()
	{
		_SetSeconds(_seconds + 1);
	}

	private void _OnSpawnTimerTimeout()
	{
		if (!ElevatedClient.IsActive || !SpawningEnabled)
			return;
		if (_enemiesCount < MaxEnemies)
			_SpawnSingleEnemy();
	}

	private void _OnPatternTimerTimeout()
	{
		if (!ElevatedClient.IsActive || !SpawningEnabled)
			return;
		if (_enemiesCount < MaxEnemies)
			_SpawnCirclePattern(Mathf.Min(40, MaxEnemies - _enemiesCount));
	}

	private void _OnEliteTimerTimeout()
	{
		if (!ElevatedClient.IsActive || !SpawningEnabled)
			return;
		_SpawnSingleEnemy(isElite: true);
	}

	private void _OnFoodTimerTimeout()
	{
		if (!ElevatedClient.IsActive)
			return;
		var refPlayer = _GetReferencePlayer();
		if (refPlayer == null)
			return;
		var spawnPos = _GetRandomSpawnPos(
			refPlayer.GlobalPosition, GD.RandRange(MinSpawnRadius, 600));
		var food            = DropScene.Instantiate<Drop>();
		food.Resource       = ResFood;
		food.GlobalPosition = spawnPos;
		AddChild(food);
	}
/*
```
*/
}
