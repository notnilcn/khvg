using SpacetimeDB;
using System.Collections.Generic;

// ──────────────────────────────────────────────
// Custom types
// ──────────────────────────────────────────────

[SpacetimeDB.Type]
public partial struct RpgStats
{
    public int Strength;
    public int Wisdom;
    public int Dexterity;
    public int Defense;
    public int Vitality;
}

/// Batch position snapshot sent by the host every 100 ms.
[SpacetimeDB.Type]
public partial struct EnemySnapshot
{
    public ulong EnemyId;
    public float X;
    public float Y;
    public float MoveX;
    public float MoveY;
    public float Speed;
}

// ──────────────────────────────────────────────
// Tables
// ──────────────────────────────────────────────

/// One row per player, created on first connect. Persists across sessions.
[SpacetimeDB.Table(Accessor = "PlayerAccount", Public = true)]
public partial struct PlayerAccount
{
    [SpacetimeDB.PrimaryKey]
    public Identity PlayerId;
    public string Username;
    public bool IsAdmin;
    public Timestamp CreatedAt;
}

/// RPG stats, level, and HP. One row per player.
[SpacetimeDB.Table(Accessor = "CharacterData", Public = true)]
public partial struct CharacterData
{
    [SpacetimeDB.PrimaryKey]
    public Identity PlayerId;
    public int Level;
    public int Xp;
    public int MaxHp;
    public int CurrentHp;
    public RpgStats Stats;
}

/// All equipment and inventory slots. Item IDs match Godot .tres resource names.
/// Lists are ordered — position in list is slot index. Empty string = empty slot.
[SpacetimeDB.Table(Accessor = "PlayerInventory", Public = true)]
public partial struct PlayerInventory
{
    [SpacetimeDB.PrimaryKey]
    public Identity PlayerId;
    public string? EquippedWeapon;
    public string? EquippedAbility;
    public string? EquippedArmor;
    public List<string> EquippedAccessories; // max 3, scales with Wisdom
    public List<string> BackpackSlots;        // max 12, first 6 active by default
    public List<string> ConsumableSlots;      // max 8, first 2 active by default
}

/// Dead-reckoning snapshot for each connected player.
/// X/Y is the confirmed position at EventTime. MoveX/MoveY is the normalised direction
/// the player was moving in at that moment. Speed is world-units-per-second.
/// Clients extrapolate: pos = (X,Y) + (MoveX,MoveY)*Speed*(now - EventTime).
/// ChunkX/ChunkY enable AoI: clients subscribe to a chunk range rather than the full table.
[SpacetimeDB.Table(Accessor = "PlayerPosition", Public = true)]
public partial struct PlayerPosition
{
    [SpacetimeDB.PrimaryKey]
    public Identity PlayerId;
    public float X;
    public float Y;
    public float MoveX;
    public float MoveY;
    public float Speed;
    public Timestamp EventTime;
    public int ChunkX;
    public int ChunkY;
}

/// Singleton row written by the elevated server on connect.
/// Enemy reducers are open to any caller when this table is empty (dev mode).
/// Once claimed, only the matching identity may call them.
[SpacetimeDB.Table(Accessor = "ElevatedClient", Public = true)]
public partial struct ElevatedClient
{
    [SpacetimeDB.PrimaryKey]
    public Identity Identity;
    public Timestamp ClaimedAt;
}

/// One row per active bullet. SlotId is a ring-buffer index managed by the elevated server.
/// The elevated server upserts the slot on fire and deletes it on expiry or hit.
/// Clients reconstruct bullet positions each frame using deterministic parametric math.
/// PatternType: 0=none, 1=straight-up, 2=sine-up.
[SpacetimeDB.Table(Accessor = "BulletPattern", Public = true)]
public partial struct BulletPattern
{
    [SpacetimeDB.PrimaryKey]
    public ushort SlotId;
    public byte PatternType;
    public float OriginX;
    public float OriginY;
    public float TargetX;   // hint — unused by current patterns, reserved for future
    public float TargetY;
    public Timestamp SpawnTime;
    public Timestamp DespawnTime;
}

/// One row per live enemy. Host inserts on spawn, updates every 100 ms with a dead-reckoning
/// snapshot, and deletes on despawn. Clients simulate locally and correct on each update.
/// EnemyId is assigned by the host (matches the local Godot int EnemyId cast to ulong).
[SpacetimeDB.Table(Accessor = "EnemyState", Public = true)]
public partial struct EnemyState
{
    [SpacetimeDB.PrimaryKey]
    public ulong EnemyId;
    public float X;
    public float Y;
    public float MoveX;
    public float MoveY;
    public float Speed;
    public Timestamp EventTime;
    public string ResPath;
    public bool IsElite;
    public bool Immortal;
    public int Hp;
    public string TargetId;          // STDB identity string of the player this enemy is chasing
    public byte BulletPatternType;   // 0=none, 1=straight-up, 2=sine-up
}

// ──────────────────────────────────────────────
// Module
// ──────────────────────────────────────────────

public static partial class Module
{
    private const int BaseMaxHp = 100;
    private const int HpPerLevel = 5;

    /// Sanity cap on reported speed. Rejects clearly cheated values.
    /// Set high enough to allow speed accessories (base ~100, cap at 4×).
    private const float MaxSpeed = 400f;

    /// World units per chunk side. Must match the client constant.
    private const float ChunkSize = 320f;

    private static RpgStats StartingStats() => new RpgStats
    {
        Strength = 1,
        Wisdom = 1,
        Dexterity = 1,
        Defense = 1,
        Vitality = 1,
    };

    // ── Lifecycle ──────────────────────────────

    [SpacetimeDB.Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext ctx)
    {
        if (ctx.Db.PlayerAccount.PlayerId.Find(ctx.Sender) is null)
        {
            ctx.Db.PlayerAccount.Insert(new PlayerAccount
            {
                PlayerId = ctx.Sender,
                Username = "",
                IsAdmin = false,
                CreatedAt = ctx.Timestamp,
            });

            ctx.Db.CharacterData.Insert(new CharacterData
            {
                PlayerId = ctx.Sender,
                Level = 1,
                Xp = 0,
                MaxHp = BaseMaxHp,
                CurrentHp = BaseMaxHp,
                Stats = StartingStats(),
            });

            ctx.Db.PlayerInventory.Insert(new PlayerInventory
            {
                PlayerId = ctx.Sender,
                EquippedWeapon = null,
                EquippedAbility = null,
                EquippedArmor = null,
                EquippedAccessories = new List<string>(),
                BackpackSlots = new List<string>(),
                ConsumableSlots = new List<string>(),
            });

            Log.Info($"New player: {ctx.Sender}");
        }
        else
        {
            Log.Info($"Returning player: {ctx.Sender}");
        }

        ctx.Db.PlayerPosition.Insert(new PlayerPosition
        {
            PlayerId = ctx.Sender,
            X = 0f,
            Y = 0f,
            MoveX = 0f,
            MoveY = 0f,
            Speed = 0f,
            EventTime = ctx.Timestamp,
            ChunkX = 0,
            ChunkY = 0,
        });
    }

    [SpacetimeDB.Reducer(ReducerKind.ClientDisconnected)]
    public static void ClientDisconnected(ReducerContext ctx)
    {
        ctx.Db.PlayerPosition.PlayerId.Delete(ctx.Sender);
        ctx.Db.ElevatedClient.Identity.Delete(ctx.Sender);
        Log.Info($"Player disconnected: {ctx.Sender}");
    }

    // ── Elevated client ────────────────────────

    /// Returns true if the caller is the registered elevated client,
    /// OR if no elevated client has been registered yet (dev / offline mode).
    private static bool CanControlEnemies(ReducerContext ctx)
    {
        bool anyElevated = false;
        foreach (var row in ctx.Db.ElevatedClient.Iter())
        {
            if (row.Identity == ctx.Sender) return true;
            anyElevated = true;
        }
        return !anyElevated;
    }

    /// Called by the elevated server immediately after connecting.
    /// The first identity to call this claims the elevated seat.
    /// The same identity may call again to refresh the timestamp after a reconnect.
    /// A different identity is rejected while the seat is held.
    [SpacetimeDB.Reducer]
    public static void ClaimElevated(ReducerContext ctx)
    {
        if (ctx.Db.ElevatedClient.Identity.Find(ctx.Sender) is ElevatedClient existing)
        {
            ctx.Db.ElevatedClient.Identity.Update(existing with { ClaimedAt = ctx.Timestamp });
            Log.Info($"Elevated seat refreshed by {ctx.Sender}");
            return;
        }
        foreach (var _ in ctx.Db.ElevatedClient.Iter())
            throw new Exception("Elevated seat already claimed by a different identity.");
        ctx.Db.ElevatedClient.Insert(new ElevatedClient { Identity = ctx.Sender, ClaimedAt = ctx.Timestamp });
        Log.Info($"Elevated seat claimed by {ctx.Sender}");
    }

    // ── Movement ───────────────────────────────

    /// Called by the client only when movement input changes (keypress / release).
    /// x/y: confirmed world position at the moment input changed.
    /// moveX/moveY: new normalised direction (clamped server-side to unit length).
    /// speed: world-units-per-second the player is moving at (Speed * ExtraSpeed in Godot).
    /// Other clients extrapolate this snapshot forward until the next update arrives.
    [SpacetimeDB.Reducer]
    public static void ReportMovement(ReducerContext ctx, float x, float y, float moveX, float moveY, float speed)
    {
        if (ctx.Db.PlayerPosition.PlayerId.Find(ctx.Sender) is not PlayerPosition pos)
            throw new Exception($"Position not found for {ctx.Sender}");

        if (speed > MaxSpeed)
            throw new Exception($"Speed {speed:F1} exceeds MaxSpeed {MaxSpeed}");

        float len = MathF.Sqrt(moveX * moveX + moveY * moveY);
        float nx = len > 0f ? moveX / len : 0f;
        float ny = len > 0f ? moveY / len : 0f;

        int chunkX = (int)MathF.Floor(x / ChunkSize);
        int chunkY = (int)MathF.Floor(y / ChunkSize);

        ctx.Db.PlayerPosition.PlayerId.Update(pos with
        {
            X = x,
            Y = y,
            MoveX = nx,
            MoveY = ny,
            Speed = speed,
            EventTime = ctx.Timestamp,
            ChunkX = chunkX,
            ChunkY = chunkY,
        });
    }

    // ── Account ────────────────────────────────

    [SpacetimeDB.Reducer]
    public static void SetUsername(ReducerContext ctx, string username)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 20)
            throw new Exception("Username must be 1–20 non-whitespace characters.");

        if (ctx.Db.PlayerAccount.PlayerId.Find(ctx.Sender) is not PlayerAccount account)
            throw new Exception("Account not found.");

        ctx.Db.PlayerAccount.PlayerId.Update(account with { Username = username });
    }

    // ── Character ──────────────────────────────

    /// Called by client after level-up. Stats are client-reported for now;
    /// server-side validation against actual level formula is a TODO.
    [SpacetimeDB.Reducer]
    public static void SaveCharacterProgress(ReducerContext ctx, int level, int xp, int currentHp, RpgStats stats)
    {
        if (ctx.Db.CharacterData.PlayerId.Find(ctx.Sender) is not CharacterData data)
            throw new Exception("Character not found.");

        if (level < data.Level)
            throw new Exception("Level cannot decrease.");

        int maxHp = BaseMaxHp + (level - 1) * HpPerLevel;

        ctx.Db.CharacterData.PlayerId.Update(data with
        {
            Level = level,
            Xp = xp,
            MaxHp = maxHp,
            CurrentHp = Math.Min(currentHp, maxHp),
            Stats = stats,
        });
    }

    // ── Inventory ──────────────────────────────

    /// Replaces the full inventory state. Called by client after any item change.
    /// Item IDs are Godot .tres resource names (e.g. "res_axe", "res_boots").
    [SpacetimeDB.Reducer]
    public static void UpdateInventory(
        ReducerContext ctx,
        string? equippedWeapon,
        string? equippedAbility,
        string? equippedArmor,
        List<string> equippedAccessories,
        List<string> backpackSlots,
        List<string> consumableSlots)
    {
        if (ctx.Db.PlayerInventory.PlayerId.Find(ctx.Sender) is not PlayerInventory inv)
            throw new Exception("Inventory not found.");

        if (equippedAccessories.Count > 3)
            throw new Exception("Too many accessories.");
        if (backpackSlots.Count > 12)
            throw new Exception("Too many backpack slots.");
        if (consumableSlots.Count > 8)
            throw new Exception("Too many consumable slots.");

        ctx.Db.PlayerInventory.PlayerId.Update(inv with
        {
            EquippedWeapon = equippedWeapon,
            EquippedAbility = equippedAbility,
            EquippedArmor = equippedArmor,
            EquippedAccessories = equippedAccessories,
            BackpackSlots = backpackSlots,
            ConsumableSlots = consumableSlots,
        });
    }

    // ── Enemies ────────────────────────────────

    /// Called by the Godot host when it spawns an enemy locally.
    /// EnemyId matches the host's local int ID (cast to ulong) so both sides share the same key.
    [SpacetimeDB.Reducer]
    public static void SpawnEnemy(ReducerContext ctx, ulong enemyId, float x, float y,
        string resPath, bool isElite, bool immortal, int hp, byte bulletPatternType)
    {
        if (!CanControlEnemies(ctx))
            throw new Exception("Unauthorized: only the elevated client may spawn enemies.");
        if (ctx.Db.EnemyState.EnemyId.Find(enemyId) is not null)
            throw new Exception($"Enemy {enemyId} already exists");

        ctx.Db.EnemyState.Insert(new EnemyState
        {
            EnemyId           = enemyId,
            X                 = x,
            Y                 = y,
            MoveX             = 0f,
            MoveY             = 0f,
            Speed             = 0f,
            EventTime         = ctx.Timestamp,
            ResPath           = resPath,
            IsElite           = isElite,
            Immortal          = immortal,
            Hp                = hp,
            TargetId          = "",
            BulletPatternType = bulletPatternType,
        });
    }

    /// Called by the host every 100 ms with a batch of dead-reckoning snapshots.
    /// Clients receive OnUpdate and correct any local simulation drift.
    [SpacetimeDB.Reducer]
    public static void UpdateEnemySnapshots(ReducerContext ctx, List<EnemySnapshot> snapshots)
    {
        if (!CanControlEnemies(ctx))
            throw new Exception("Unauthorized: only the elevated client may update enemy snapshots.");
        foreach (var snap in snapshots)
        {
            if (ctx.Db.EnemyState.EnemyId.Find(snap.EnemyId) is not EnemyState enemy)
                continue;
            ctx.Db.EnemyState.EnemyId.Update(enemy with
            {
                X         = snap.X,
                Y         = snap.Y,
                MoveX     = snap.MoveX,
                MoveY     = snap.MoveY,
                Speed     = snap.Speed,
                EventTime = ctx.Timestamp,
            });
        }
    }

    /// Called by the host immediately when an enemy switches targets.
    /// Kept separate from UpdateEnemySnapshots so target changes aren't
    /// rate-limited to the 100 ms position tick.
    [SpacetimeDB.Reducer]
    public static void UpdateEnemyTarget(ReducerContext ctx, ulong enemyId, string targetId)
    {
        if (!CanControlEnemies(ctx))
            throw new Exception("Unauthorized: only the elevated client may update enemy targets.");
        if (ctx.Db.EnemyState.EnemyId.Find(enemyId) is not EnemyState enemy)
            return;
        ctx.Db.EnemyState.EnemyId.Update(enemy with { TargetId = targetId, EventTime = ctx.Timestamp });
    }

    /// Called by the host when an enemy is despawned.
    [SpacetimeDB.Reducer]
    public static void DespawnEnemy(ReducerContext ctx, ulong enemyId)
    {
        if (!CanControlEnemies(ctx))
            throw new Exception("Unauthorized: only the elevated client may despawn enemies.");
        ctx.Db.EnemyState.EnemyId.Delete(enemyId);
    }

    // ── Bullets ────────────────────────────────

    /// Called by the elevated server when an enemy fires. Upserts the ring-buffer slot so the
    /// table row count is bounded by the number of concurrent bullets, not total bullets fired.
    /// lifetimeSecs is how long the bullet lives before the elevated server calls DespawnBullet.
    [SpacetimeDB.Reducer]
    public static void FireBullet(ReducerContext ctx, ushort slotId, byte patternType,
        float originX, float originY, float targetX, float targetY, float lifetimeSecs)
    {
        if (!CanControlEnemies(ctx))
            throw new Exception("Unauthorized: only the elevated client may fire bullets.");
        var row = new BulletPattern
        {
            SlotId      = slotId,
            PatternType = patternType,
            OriginX     = originX,
            OriginY     = originY,
            TargetX     = targetX,
            TargetY     = targetY,
            SpawnTime   = ctx.Timestamp,
            DespawnTime = ctx.Timestamp + TimeSpan.FromSeconds(lifetimeSecs),
        };
        if (ctx.Db.BulletPattern.SlotId.Find(slotId) is not null)
            ctx.Db.BulletPattern.SlotId.Update(row);
        else
            ctx.Db.BulletPattern.Insert(row);
    }

    /// Called by the elevated server when a bullet expires or hits a player.
    [SpacetimeDB.Reducer]
    public static void DespawnBullet(ReducerContext ctx, ushort slotId)
    {
        if (!CanControlEnemies(ctx))
            throw new Exception("Unauthorized: only the elevated client may despawn bullets.");
        ctx.Db.BulletPattern.SlotId.Delete(slotId);
    }

    /// Called by the elevated server when a bullet hits a player.
    /// Deducts HP from CharacterData. Only the elevated client (or host in dev mode) may call this.
    [SpacetimeDB.Reducer]
    public static void DamagePlayer(ReducerContext ctx, Identity playerId, int damage)
    {
        if (!CanControlEnemies(ctx))
            throw new Exception("Unauthorized: only the elevated client may deal damage.");
        if (ctx.Db.CharacterData.PlayerId.Find(playerId) is not CharacterData data)
            return; // player may have disconnected
        int newHp = Math.Max(0, data.CurrentHp - damage);
        ctx.Db.CharacterData.PlayerId.Update(data with { CurrentHp = newHp });
    }
}
