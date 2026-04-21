using Godot;
using Godot.Collections;
using System.Collections.Generic;

// Overall Description
// Handles spawning of enemies AND players.
//
// ENEMY SPAWNING
// Only the server spawns enemies, but enemies need to EXIST on all peers so that clients can see
// them and weapon hit-detection works client-side. We solve this with explicit RPCs:
// - When the SERVER calls _CreateEnemy(), it broadcasts _RpcSpawnEnemy() to all peers.
// - When the server despawns an enemy, it broadcasts _RpcDespawnEnemy() to all peers.
// We no longer use MultiplayerSpawner because its built-in late-join catch-up fires at
// peer_connected time — before the client's game scene exists — so packets are silently dropped.
// Instead we use an explicit RPC that only fires when we KNOW the client is ready.
//
// PLAYER SPAWNING
// When a peer connects, the server broadcasts an RPC to create a Player node on ALL peers.
// Each peer sets the correct multiplayer_authority on that node so only the owning peer
// processes input for it.

/*
```c# */
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

    // Array of every live enemy on the SERVER.
    // Used to send a catch-up list to clients that join after enemies already exist.
    // Entries are removed when the enemy is despawned via _RpcDespawnEnemy.
    private readonly List<Dictionary> _activeEnemyData = new();

    private static readonly Vector2[] PlayerSpawnPositions =
    {
        new(0,    0),
        new(200,  0),
        new(-200, 0),
        new(0,    200),
    };

    // Admin management.
    // The host (peer_id == 1) is always implicitly admin. Only the host can grant or revoke admin.
    // Empty _adminPassword string means disabled. Set with /setpass; players claim with /adminpass.
    private readonly List<int> _admins        = new();
    private          string    _adminPassword = "";

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

        if (Multiplayer.IsServer())
        {
            // If any clients were ALREADY connected before this _Ready() ran
            // (happens when the host dies and the scene reloads while a client
            // is still in the game), notify those clients to clean up stale nodes
            // and re-join the fresh scene.
            foreach (int peerId in Multiplayer.GetPeers())
                RpcId(peerId, nameof(_NotifySceneReload));

            // Clean up player nodes when a peer disconnects.
            Network.Instance.PlayerLeft += _OnPlayerLeft;

            // Spawn the host's own player (peer 1 never calls _RequestPlayerSpawn).
            _SpawnPlayerForPeer(1);
        }
        else
        {
            // Client-side: tell the server we are in the scene and ready to be spawned.
            GD.Print("Spawner: client ready, requesting player spawn from server");
            RpcId(1, nameof(_RequestPlayerSpawn));
        }

        _SetupChatConsole();

        // Populate visuals for SpacetimeDB remote players that were already in the DB
        // before this map scene loaded (their OnInsert fired while we were in the lobby).
        StdbPlayerSync.Instance?.OnGameSceneLoaded();
    }

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

    private void _OnCommandSubmitted(string raw)
    {
        string text  = raw.Trim();
        var    input = _vbox.GetChild(_vbox.GetChildCount() - 1) as LineEdit;
        input?.Clear();

        if (string.IsNullOrEmpty(text))
            return;
        _ParseCommand(text);
    }

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
            case "/admin":
            case "/unadmin":
            case "/setpass":
            case "/spawn":
            case "/chest":
                // Route all admin commands through the server so it can validate permissions.
                if (Multiplayer.GetUniqueId() == 1)
                    _ExecuteAdminCmd(1, verb, args);
                else
                    RpcId(1, nameof(_RpcAdminCmd), verb, args);
                break;

            case "/stats":
            case "/inv":
                // Read-only self-inspection — anyone can run these locally.
                _CmdSelfinfo(verb);
                break;

            case "/adminpass":
            {
                string password = args.Length > 0 ? args[0] : "";
                if (Multiplayer.GetUniqueId() == 1)
                    _TryAdminPassword(1, password);
                else
                    RpcId(1, nameof(_RpcTryAdminPassword), password);
                break;
            }

            default:
                _ShowFeedback("Unknown command. Try /stats, /inv, /spawner, /give, /remove, /spawn, /chest, /admin, /unadmin, /setpass, /adminpass");
                break;
        }
    }

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

    // /adminpass — any player: claim admin if the password matches.
    // Separate RPC so non-admins can reach the server without going through _RpcAdminCmd.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void _RpcTryAdminPassword(string password)
    {
        _TryAdminPassword(Multiplayer.GetRemoteSenderId(), password);
    }

    private void _TryAdminPassword(int sender, string password)
    {
        if (string.IsNullOrEmpty(_adminPassword))
        {
            _SendFeedback(sender, "No admin password is set");
            return;
        }
        if (password != _adminPassword)
        {
            _SendFeedback(sender, "Incorrect password");
            return;
        }
        if (_IsAdmin(sender))
        {
            _SendFeedback(sender, "You are already an admin");
            return;
        }
        _admins.Add(sender);
        _SendFeedback(sender, "Admin granted");
    }

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

    // The server validates admin status before executing; clients cannot bypass this by calling
    // _ExecuteAdminCmd directly because that function only runs server-side.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void _RpcAdminCmd(string verb, string[] args)
    {
        _ExecuteAdminCmd(Multiplayer.GetRemoteSenderId(), verb, args);
    }

    // Server → client RPC to deliver command feedback.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void _RpcFeedback(string msg)
    {
        _ShowFeedback(msg);
    }

    // Sends feedback to the given peer (or locally if peer_id == 1).
    private void _SendFeedback(int peerId, string msg)
    {
        if (peerId == 1)
            _ShowFeedback(msg);
        else
            RpcId(peerId, nameof(_RpcFeedback), msg);
    }

    private bool _IsAdmin(int peerId) => peerId == 1 || _admins.Contains(peerId);

    // Validates admin status then dispatches to the appropriate command handler.
    // Always runs on the server.
    private void _ExecuteAdminCmd(int sender, string verb, string[] args)
    {
        if (!_IsAdmin(sender))
        {
            _SendFeedback(sender, "Permission denied: you are not an admin");
            return;
        }
        switch (verb)
        {
            case "/spawner": _CmdSpawner(sender, args); break;
            case "/give":    _CmdGive(sender, args);    break;
            case "/remove":  _CmdRemove(sender, args);  break;
            case "/admin":   _CmdAdmin(sender, args);   break;
            case "/unadmin": _CmdUnadmin(sender, args); break;
            case "/setpass": _CmdSetpass(sender, args); break;
            case "/spawn":   _CmdSpawn(sender, args);   break;
            case "/chest":   _CmdChest(sender, args);   break;
        }
    }

    // /spawner on|off
    private void _CmdSpawner(int sender, string[] args)
    {
        if (args.Length == 0)
        {
            _SendFeedback(sender, "Usage: /spawner on|off");
            return;
        }
        switch (args[0].ToLower())
        {
            case "on":
                SpawningEnabled = true;
                _SendFeedback(sender, "Enemy spawning enabled");
                break;
            case "off":
                SpawningEnabled = false;
                _SendFeedback(sender, "Enemy spawning disabled");
                break;
            default:
                _SendFeedback(sender, "Usage: /spawner on|off");
                break;
        }
    }

    // /give <weapon> [peer_id] — give a weapon to self or another peer
    private void _CmdGive(int sender, string[] args)
    {
        if (args.Length == 0)
        {
            _SendFeedback(sender, "Usage: /give <weapon> [peer_id]\nWeapons: " + string.Join(", ", WeaponResPaths.Keys));
            return;
        }
        string weaponName = args[0].ToLower();
        if (!WeaponResPaths.TryGetValue(weaponName, out string resPath))
        {
            _SendFeedback(sender, $"Unknown weapon '{weaponName}'. Options: {string.Join(", ", WeaponResPaths.Keys)}");
            return;
        }
        int    targetId = args.Length >= 2 && int.TryParse(args[1], out int tid) ? tid : sender;
        if (!Global.Instance.Players.TryGetValue(targetId, out var target))
        {
            _SendFeedback(sender, $"No player found with peer_id {targetId}");
            return;
        }
        if (targetId == Multiplayer.GetUniqueId())
            target.RpcGiveWeapon(resPath);
        else
            target.RpcId(targetId, nameof(Player.RpcGiveWeapon), resPath);
        _SendFeedback(sender, $"Gave {weaponName} to peer {targetId}");
    }

    // /remove <weapon> [peer_id] — remove a weapon from self or another peer
    private void _CmdRemove(int sender, string[] args)
    {
        if (args.Length == 0)
        {
            _SendFeedback(sender, "Usage: /remove <weapon> [peer_id]\nWeapons: " + string.Join(", ", WeaponResPaths.Keys));
            return;
        }
        string weaponName = args[0].ToLower();
        if (!WeaponResPaths.TryGetValue(weaponName, out string resPath))
        {
            _SendFeedback(sender, $"Unknown weapon '{weaponName}'. Options: {string.Join(", ", WeaponResPaths.Keys)}");
            return;
        }
        int targetId = args.Length >= 2 && int.TryParse(args[1], out int tid) ? tid : sender;
        if (!Global.Instance.Players.TryGetValue(targetId, out var target))
        {
            _SendFeedback(sender, $"No player found with peer_id {targetId}");
            return;
        }
        if (targetId == Multiplayer.GetUniqueId())
            target.RpcRemoveWeapon(resPath);
        else
            target.RpcId(targetId, nameof(Player.RpcRemoveWeapon), resPath);
        _SendFeedback(sender, $"Removed {weaponName} from peer {targetId}");
    }

    // /admin <peer_id> — host only: grant admin to a connected player
    private void _CmdAdmin(int sender, string[] args)
    {
        if (sender != 1)
        {
            _SendFeedback(sender, "Only the host can grant admin");
            return;
        }
        if (args.Length == 0)
        {
            _SendFeedback(sender, "Usage: /admin <peer_id>");
            return;
        }
        if (!int.TryParse(args[0], out int targetId))
            return;
        if (targetId == 1)
        {
            _SendFeedback(sender, "The host is always admin");
            return;
        }
        if (!Global.Instance.Players.ContainsKey(targetId))
        {
            _SendFeedback(sender, $"No player found with peer_id {targetId}");
            return;
        }
        if (_admins.Contains(targetId))
        {
            _SendFeedback(sender, $"Peer {targetId} is already an admin");
            return;
        }
        _admins.Add(targetId);
        _SendFeedback(sender,   $"Granted admin to peer {targetId}");
        _SendFeedback(targetId, "You have been granted admin privileges");
    }

    // /unadmin <peer_id> — host only: revoke admin from a player
    private void _CmdUnadmin(int sender, string[] args)
    {
        if (sender != 1)
        {
            _SendFeedback(sender, "Only the host can revoke admin");
            return;
        }
        if (args.Length == 0)
        {
            _SendFeedback(sender, "Usage: /unadmin <peer_id>");
            return;
        }
        if (!int.TryParse(args[0], out int targetId))
            return;
        if (targetId == 1)
        {
            _SendFeedback(sender, "Cannot remove admin from the host");
            return;
        }
        if (!_admins.Contains(targetId))
        {
            _SendFeedback(sender, $"Peer {targetId} is not an admin");
            return;
        }
        _admins.Remove(targetId);
        _SendFeedback(sender,   $"Revoked admin from peer {targetId}");
        _SendFeedback(targetId, "Your admin privileges have been revoked");
    }

    // /setpass [password] — host only: set (or clear) the admin password
    private void _CmdSetpass(int sender, string[] args)
    {
        if (sender != 1)
        {
            _SendFeedback(sender, "Only the host can set the admin password");
            return;
        }
        if (args.Length == 0)
        {
            _adminPassword = "";
            _SendFeedback(sender, "Admin password cleared");
        }
        else
        {
            _adminPassword = args[0];
            _SendFeedback(sender, "Admin password set");
        }
    }

    // /spawn [x y] — spawn a stationary immortal dummy at the given world position (default 0,0)
    private void _CmdSpawn(int sender, string[] args)
    {
        if (args.Length == 1)
        {
            _SendFeedback(sender, "Usage: /spawn [x y]");
            return;
        }
        var pos = Vector2.Zero;
        if (args.Length >= 2 &&
            float.TryParse(args[0], out float px) &&
            float.TryParse(args[1], out float py))
            pos = new Vector2(px, py);

        var res = GD.Load<ResEnemy>(DummyEnemyRes);
        _CreateEnemy(pos, res, isElite: false, immortal: true);
        _SendFeedback(sender, $"Spawned dummy at ({(int)pos.X}, {(int)pos.Y})");
    }

    // /chest [x y] — spawn an elite enemy guaranteed to drop a chest when killed.
    // Uses a random enemy from the current pool, or a goblin if the pool is empty.
    // If no position is given, spawns near the first available player.
    private void _CmdChest(int sender, string[] args)
    {
        if (args.Length == 1)
        {
            _SendFeedback(sender, "Usage: /chest [x y]");
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
                _SendFeedback(sender, "No players in the world to spawn near");
                return;
            }
            pos = _GetRandomSpawnPos(refPlayer.GlobalPosition, MinSpawnRadius);
        }

        ResEnemy res = AllResEnemy.Count > 0 ? _GetEnemy() : GD.Load<ResEnemy>(GoblinEnemyRes);
        _CreateEnemy(pos, res, isElite: true);
        _SendFeedback(sender, $"Spawned elite enemy (chest drop) at ({(int)pos.X}, {(int)pos.Y})");
    }

    // ── RPCs ──────────────────────────────────────────────────────────────────────

    // Sent by the server to already-connected clients when the server scene reloads.
    // The client removes all stale player nodes from the previous session and re-requests a spawn.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void _NotifySceneReload()
    {
        GD.Print("Spawner: server scene reloaded — clearing stale nodes and rejoining");

        foreach (int peerId in Global.Instance.Players.Keys)
        {
            var player = Global.Instance.Players[peerId];
            if (GodotObject.IsInstanceValid(player))
                player.QueueFree();
        }
        Global.Instance.Players.Clear();

        foreach (var child in GetChildren())
        {
            if (child is Enemy enemy)
                enemy.QueueFree();
        }

        RpcId(1, nameof(_RequestPlayerSpawn));
    }

    // Client calls this to the server once the client's game scene is fully loaded.
    // The server then spawns a player node for that client on all peers.
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void _RequestPlayerSpawn()
    {
        // This runs on the SERVER.
        int newPeerId = Multiplayer.GetRemoteSenderId();
        GD.Print($"Spawner: server received spawn request from peer {newPeerId}");

        // Catch-up: send all already-existing players to the new client.
        foreach (int existingPeerId in Global.Instance.Players.Keys)
        {
            var existingPlayer = Global.Instance.Players[existingPeerId];
            GD.Print($"Spawner: sending existing player {existingPeerId} to new peer {newPeerId}");
            RpcId(newPeerId, nameof(_CreatePlayerNode), existingPeerId, existingPlayer.GlobalPosition);
        }

        // Spawn the new player for EVERYONE (broadcast).
        _SpawnPlayerForPeer(newPeerId);

        // Catch-up: send all currently-alive enemies to the new client.
        GD.Print($"Spawner: sending {_activeEnemyData.Count} existing enemies to peer {newPeerId}");
        foreach (var data in _activeEnemyData)
            RpcId(newPeerId, nameof(_RpcSpawnEnemy), data);
    }

    private void _OnPlayerLeft(int peerId)
    {
        Rpc(nameof(_RemovePlayer), peerId);
        _admins.Remove(peerId);
    }

    private void _SpawnPlayerForPeer(int peerId)
    {
        var pos = PlayerSpawnPositions[_spawnPositionIndex % PlayerSpawnPositions.Length];
        _spawnPositionIndex++;
        GD.Print($"Spawner: spawning player for peer {peerId} at {pos}");
        Rpc(nameof(_CreatePlayerNode), peerId, pos);
    }

    // Runs on ALL peers. Creates the player node with the correct authority.
    private static readonly ResPlayer DefaultCharacterData =
        GD.Load<ResPlayer>("res://VS-Like/Characters/Player/Types/Dwarf/res_dwarf.tres");

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void _CreatePlayerNode(int peerId, Vector2 spawnPos)
    {
        // Guard: if this peer already has a node for this player, skip.
        if (GetNodeOrNull($"Player_{peerId}") != null)
            return;
        var player = PlayerScene.Instantiate<Player>();
        player.Name               = $"Player_{peerId}";
        player.CharacterData      = DefaultCharacterData;
        player.SetMultiplayerAuthority(peerId);
        player.GlobalPosition     = spawnPos;
        AddChild(player);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void _RemovePlayer(int peerId)
    {
        var node = GetNodeOrNull($"Player_{peerId}");
        node?.QueueFree();
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
    private void _CreateEnemy(Vector2 spawnPos, ResEnemy resEnemy, bool isElite = false, bool immortal = false)
    {
        int eid = _nextEnemyId++;

        var data = new Dictionary
        {
            ["res_path"] = resEnemy.ResourcePath,
            ["pos"]      = spawnPos,
            ["is_elite"] = isElite,
            ["immortal"] = immortal,
            ["eid"]      = eid,
        };

        // Track for late-join catch-up (sent inside _RequestPlayerSpawn).
        _activeEnemyData.Add(data);

        // Broadcast to server (call_local) and all currently-connected clients.
        Rpc(nameof(_RpcSpawnEnemy), data);
    }

    // Runs on ALL peers (server via CallLocal, clients via network).
    // Creates, names, and adds the enemy node to this spawner.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void _RpcSpawnEnemy(Dictionary data)
    {
        var enemy              = EnemyScene.Instantiate<Enemy>();
        enemy.ResEnemy         = GD.Load<ResEnemy>((string)data["res_path"]);
        enemy.IsElite          = (bool)data["is_elite"];
        enemy.Immortal         = data.ContainsKey("immortal") && (bool)data["immortal"];
        enemy.GlobalPosition   = (Vector2)data["pos"];
        enemy.EnemyId          = (int)data["eid"];
        enemy.Name             = $"Enemy_{(int)data["eid"]}";

        AddChild(enemy);

        if (Multiplayer.IsServer())
        {
            _enemiesCount++;
            _UpdateEnemiesCountLabel();
            enemy.Died            += EnemyDied;
            enemy.RequestDespawn  += _OnEnemyRequestDespawn;
        }
    }

    // Called on the server when an enemy's Die() method fires.
    // Broadcasts removal to ALL peers so every machine frees the node.
    private void _OnEnemyRequestDespawn(int eid)
    {
        Rpc(nameof(_RpcDespawnEnemy), eid);
    }

    // SERVER ONLY helper called by Enemy.SpawnDrop().
    // Broadcasts a drop spawn to ALL peers so every player can collect it independently.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void _RpcSpawnDrop(string resPath, Vector2 pos)
    {
        var drop            = DropScene.Instantiate<Drop>();
        drop.Resource       = GD.Load<ResDrop>(resPath);
        drop.GlobalPosition = pos;
        GetTree().CurrentScene.CallDeferred("add_child", drop);
    }

    // Runs on ALL peers. Finds the enemy node by name and frees it.
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void _RpcDespawnEnemy(int eid)
    {
        if (Multiplayer.IsServer())
        {
            _activeEnemyData.RemoveAll(d => (int)d["eid"] == eid);
        }

        var enemy = GetNodeOrNull($"Enemy_{eid}");
        enemy?.QueueFree();
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
        if (Multiplayer.IsServer())
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

        // Broadcast the current time to all clients so their displays stay in sync.
        if (Multiplayer.IsServer())
            Rpc(nameof(_RpcSyncTime), _minutes, _seconds);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void _RpcSyncTime(int minutes, int seconds)
    {
        _minutes = minutes;
        _seconds = seconds;
        GetNode<Label>("%Minutes").Text = _SetDigits(_minutes);
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
        if (!Multiplayer.IsServer())
            return;
        _SetSeconds(_seconds + 1);
    }

    private void _OnSpawnTimerTimeout()
    {
        if (!Multiplayer.IsServer() || !SpawningEnabled)
            return;
        if (_enemiesCount < MaxEnemies)
            _SpawnSingleEnemy();
    }

    private void _OnPatternTimerTimeout()
    {
        if (!Multiplayer.IsServer() || !SpawningEnabled)
            return;
        if (_enemiesCount < MaxEnemies)
            _SpawnCirclePattern(Mathf.Min(40, MaxEnemies - _enemiesCount));
    }

    private void _OnEliteTimerTimeout()
    {
        if (!Multiplayer.IsServer() || !SpawningEnabled)
            return;
        _SpawnSingleEnemy(isElite: true);
    }

    private void _OnFoodTimerTimeout()
    {
        if (!Multiplayer.IsServer())
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
}
/*
```
*/