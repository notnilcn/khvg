
/* 
# Importing stuff and defining custom types
```c# */
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
    public sbyte MoveX;
    public sbyte MoveY;
    public ushort Speed;
}
/*
```
*/

/*
# Tables
```c# */
/// One row per player, created on first connect. Persists across sessions.
[SpacetimeDB.Table(Accessor = "PlayerAccount", Public = true)] // ^PlayerAccount
public partial struct PlayerAccount
{
    [SpacetimeDB.PrimaryKey]
    public Identity PlayerId;
    public string Username;
    public bool IsAdmin;
    public Timestamp CreatedAt;
}

/// RPG stats, level, and HP. One row per player.
[SpacetimeDB.Table(Accessor = "CharacterData", Public = true)] // ^CharacterData
public partial struct CharacterData
{
    [SpacetimeDB.PrimaryKey]
    public Identity PlayerId;
    public uint Level;
    public uint Xp;
    public uint MaxHp;
    public uint CurrentHp;
    public RpgStats Stats;
}

/// All equipment and inventory slots. Item IDs match Godot .tres resource names.
/// Lists are ordered — position in list is slot index. Empty string = empty slot.
[SpacetimeDB.Table(Accessor = "PlayerInventory", Public = true)] // ^PlayerInventory
public partial struct PlayerInventory
{
    [SpacetimeDB.PrimaryKey]
    public Identity PlayerId;
    public string? EquippedWeapon; // Deferred: This should be enumerated later on. Type should be uint.
    public string? EquippedAbility; // Deferred: This should be enumerated later on. Type should be uint.
    public string? EquippedArmor; // Deferred: This should be enumerated later on. Type should be uint.
    public List<string> EquippedAccessories; // max 3, scales with Wisdom. Deferred: This should be enumerated later on. Type should be List<uint>.
    public List<string> BackpackSlots;        // max 12, first 6 active by default. Deferred: This should be enumerated later on. Type should be List<uint>.
    public List<string> ConsumableSlots;      // max 8, first 2 active by default. Deferred: This should be enumerated later on. Type should be List<uint>.
}

/// Dead-reckoning snapshot for each connected player.
/// X/Y is the confirmed position at EventTime. MoveX/MoveY is the normalised direction
/// the player was moving in at that moment. Speed is world-units-per-second.
/// Clients extrapolate: pos = (X,Y) + (MoveX,MoveY)*Speed*(now - EventTime).
/// ChunkX/ChunkY enable AoI: clients subscribe to a chunk range rather than the full table.
[SpacetimeDB.Table(Accessor = "PlayerPosition", Public = true)] // Deferred: I might want to implement input buffering later on, so this table should take in a list of inputs rather than a single set of inputs. ^PlayerPosition
public partial struct PlayerPosition
{
    [SpacetimeDB.PrimaryKey]
    public Identity PlayerId;
    public float X;
    public float Y;
    public sbyte MoveX;
    public sbyte MoveY;
    public ushort Speed;
    public Timestamp EventTime;
    public int ChunkX;
    public int ChunkY;
}

/// Singleton row written by the elevated server on connect.
/// Enemy reducers are open to any caller when this table is empty (dev mode).
/// Once claimed, only the matching identity may call them.
[SpacetimeDB.Table(Accessor = "ElevatedClient", Public = true)] // ^ElevatedClient
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
[SpacetimeDB.Table(Accessor = "BulletPattern", Public = true)] // ^BulletPattern
public partial struct BulletPattern
{
    [SpacetimeDB.PrimaryKey]
    public ushort SlotId;
    public byte PatternType;
    public float OriginX;
    public float OriginY;
    public float TargetX;
    public float TargetY;
    public Timestamp SpawnTime;
    public Timestamp DespawnTime;
}

/// One row per live enemy. Host inserts on spawn, updates every 100 ms with a dead-reckoning
/// snapshot, and deletes on despawn. Clients simulate locally and correct on each update.
/// EnemyId is assigned by the host (matches the local Godot int EnemyId cast to ulong).
[SpacetimeDB.Table(Accessor = "EnemyState", Public = true)] // ^EnemyState
public partial struct EnemyState
{
    [SpacetimeDB.PrimaryKey]
    public ulong EnemyId;
    public float X;
    public float Y;
    public sbyte MoveX;
    public sbyte MoveY;
    public ushort Speed;
    public Timestamp EventTime;
    public string ResPath; // Deferred: This should be enumerated later on. Type should be uint.
    public bool IsElite;
    public bool Immortal;
    public uint Hp;
    public string? TargetId;          // Nullable STDB identity string of the player this enemy is chasing. If null, then TargetX and TargetY needs to be provided
    //public float? TargetX;          // 
    //public float? TargetY;          // 
    public byte BulletPatternType;   // 0=none, 1=straight-up, 2=sine-up
}
/*
```
*/

/*
# Defining base stats
Probably make the variables persistent table entries that can be changed by a reducer because why not
```c# */
public static partial class Module
{
    private const int BaseMaxHp = 100;
    private const int HpPerLevel = 5;

    /// Sanity cap on reported speed. Rejects clearly cheated values.
    /// Set high enough to allow speed accessories (base ~100, cap at 4×).
    private const ushort MaxSpeed = 400;

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
/*
```
*/

/*
# ClientConnected and ClientDisconnected
```c# */
    [SpacetimeDB.Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext ctx) // ^ClientConnected
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
                MaxHp = (uint)BaseMaxHp,
                CurrentHp = (uint)BaseMaxHp,
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
            MoveX = 0,
            MoveY = 0,
            Speed = 0,
            EventTime = ctx.Timestamp,
            ChunkX = 0,
            ChunkY = 0,
        });
    }

    [SpacetimeDB.Reducer(ReducerKind.ClientDisconnected)]
    public static void ClientDisconnected(ReducerContext ctx) // ^ClientDisconnected
    {
        ctx.Db.PlayerPosition.PlayerId.Delete(ctx.Sender);
        ctx.Db.ElevatedClient.Identity.Delete(ctx.Sender);
        Log.Info($"Player disconnected: {ctx.Sender}");
    }
/*
```
*/


/*
# Elevated client
```c# */
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
    public static void ClaimElevated(ReducerContext ctx) // ^ClaimElevated
    {
        if (ctx.Db.ElevatedClient.Identity.Find(ctx.Sender) is ElevatedClient existing)
        {
            ctx.Db.ElevatedClient.Identity.Update(existing with { ClaimedAt = ctx.Timestamp });
            if (ctx.Db.PlayerAccount.PlayerId.Find(ctx.Sender) is PlayerAccount refreshAcc)
                ctx.Db.PlayerAccount.PlayerId.Update(refreshAcc with { IsAdmin = true });
            Log.Info($"Elevated seat refreshed by {ctx.Sender}");
            return;
        }
        foreach (var _ in ctx.Db.ElevatedClient.Iter())
            throw new Exception("Elevated seat already claimed by a different identity.");
        ctx.Db.ElevatedClient.Insert(new ElevatedClient { Identity = ctx.Sender, ClaimedAt = ctx.Timestamp });
        if (ctx.Db.PlayerAccount.PlayerId.Find(ctx.Sender) is PlayerAccount claimAcc)
            ctx.Db.PlayerAccount.PlayerId.Update(claimAcc with { IsAdmin = true });
        Log.Info($"Elevated seat claimed by {ctx.Sender}");
    }
/*
```
*/

/*
# Position and movement sync
Dead reckoning sync. You should be able to implement rollback netcode with this.
```c# */
    /// Called by the client only when movement input changes (keypress / release).
    /// x/y: confirmed world position at the moment input changed.
    /// moveX/moveY: WASD direction components, each -1/0/1. Stored directly; client normalises for dead-reckoning.
    /// speed: world-units-per-second the player is moving at (Speed * ExtraSpeed in Godot).
    /// Other clients extrapolate this snapshot forward until the next update arrives.
    [SpacetimeDB.Reducer]
    public static void ReportMovement(ReducerContext ctx, float x, float y, sbyte moveX, sbyte moveY, ushort speed) // ^ReportMovement
    {
        if (ctx.Db.PlayerPosition.PlayerId.Find(ctx.Sender) is not PlayerPosition pos)
            throw new Exception($"Position not found for {ctx.Sender}");

        if (speed > MaxSpeed)
            throw new Exception($"Speed {speed} exceeds MaxSpeed {MaxSpeed}");

        int chunkX = (int)MathF.Floor(x / ChunkSize);
        int chunkY = (int)MathF.Floor(y / ChunkSize);

        ctx.Db.PlayerPosition.PlayerId.Update(pos with
        {
            X = x,
            Y = y,
            MoveX = moveX,
            MoveY = moveY,
            Speed = speed,
            EventTime = ctx.Timestamp,
            ChunkX = chunkX,
            ChunkY = chunkY,
        });
    }
/*
```
*/

/*
# Account
```c# */
    [SpacetimeDB.Reducer]
    public static void SetUsername(ReducerContext ctx, string username) // ^SetUsername
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 20)
            throw new Exception("Username must be 1–20 non-whitespace characters.");

        if (ctx.Db.PlayerAccount.PlayerId.Find(ctx.Sender) is not PlayerAccount account)
            throw new Exception("Account not found.");

        ctx.Db.PlayerAccount.PlayerId.Update(account with { Username = username });
    }
/*
```
*/

/*
# Character
```c# */
    /// Called by client after level-up. Stats are client-reported for now;
    /// server-side validation against actual level formula is a TODO.
    [SpacetimeDB.Reducer]
    public static void SaveCharacterProgress(ReducerContext ctx, uint level, uint xp, uint currentHp, RpgStats stats) // ^SaveCharacterProgress
    {
        if (ctx.Db.CharacterData.PlayerId.Find(ctx.Sender) is not CharacterData data)
            throw new Exception("Character not found.");

        if (level < data.Level)
            throw new Exception("Level cannot decrease.");

        uint maxHp = (uint)(BaseMaxHp + ((int)level - 1) * HpPerLevel);

        ctx.Db.CharacterData.PlayerId.Update(data with
        {
            Level = level,
            Xp = xp,
            MaxHp = maxHp,
            CurrentHp = Math.Min(currentHp, maxHp),
            Stats = stats,
        });
    }
/*
```
*/

/*
# Inventory
```c# */
    /// Replaces the full inventory state. Called by client after any item change.
    /// Item IDs are Godot .tres resource names (e.g. "res_axe", "res_boots").
    [SpacetimeDB.Reducer]
    public static void UpdateInventory( // ^UpdateInventory
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
/*
```
*/

/*
# Enemies
```c# */
    /// Called by the Godot host when it spawns an enemy locally.
    /// EnemyId matches the host's local int ID (cast to ulong) so both sides share the same key.
    [SpacetimeDB.Reducer]
    public static void SpawnEnemy(ReducerContext ctx, ulong enemyId, float x, float y, // ^SpawnEnemy
        string resPath, bool isElite, bool immortal, uint hp, byte bulletPatternType)
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
            MoveX             = 0,
            MoveY             = 0,
            Speed             = 0,
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
    public static void UpdateEnemySnapshots(ReducerContext ctx, List<EnemySnapshot> snapshots) // ^UpdateEnemySnapshots
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
    public static void UpdateEnemyTarget(ReducerContext ctx, ulong enemyId, string targetId) // ^UpdateEnemyTarget
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
/*
```
*/

/*
# Admin item commands
```c# */
    [SpacetimeDB.Reducer]
    public static void GiveWeapon(ReducerContext ctx, string targetUsername, string itemResPath) // ^GiveWeapon
    {
        if (ctx.Db.PlayerAccount.PlayerId.Find(ctx.Sender) is not PlayerAccount caller)
            throw new Exception("Account not found.");
        if (!caller.IsAdmin)
            throw new Exception("Only admins can give weapons.");

        PlayerInventory? found = null;
        foreach (var account in ctx.Db.PlayerAccount.Iter())
        {
            if (account.Username.Equals(targetUsername, System.StringComparison.OrdinalIgnoreCase))
            {
                found = ctx.Db.PlayerInventory.PlayerId.Find(account.PlayerId);
                break;
            }
        }
        if (found is not PlayerInventory inv)
            throw new Exception($"Player '{targetUsername}' not found.");

        var backpack = new List<string>(inv.BackpackSlots) { itemResPath };
        ctx.Db.PlayerInventory.PlayerId.Update(inv with { BackpackSlots = backpack });
    }

    [SpacetimeDB.Reducer]
    public static void RemoveWeapon(ReducerContext ctx, string targetUsername, string itemResPath) // ^RemoveWeapon
    {
        if (ctx.Db.PlayerAccount.PlayerId.Find(ctx.Sender) is not PlayerAccount caller)
            throw new Exception("Account not found.");
        if (!caller.IsAdmin)
            throw new Exception("Only admins can remove weapons.");

        PlayerInventory? found = null;
        foreach (var account in ctx.Db.PlayerAccount.Iter())
        {
            if (account.Username.Equals(targetUsername, System.StringComparison.OrdinalIgnoreCase))
            {
                found = ctx.Db.PlayerInventory.PlayerId.Find(account.PlayerId);
                break;
            }
        }
        if (found is not PlayerInventory inv)
            throw new Exception($"Player '{targetUsername}' not found.");

        var accessories = new List<string>(inv.EquippedAccessories);
        accessories.Remove(itemResPath);
        var backpack = new List<string>(inv.BackpackSlots);
        backpack.Remove(itemResPath);
        var consumables = new List<string>(inv.ConsumableSlots);
        consumables.Remove(itemResPath);

        ctx.Db.PlayerInventory.PlayerId.Update(inv with
        {
            EquippedWeapon      = inv.EquippedWeapon  == itemResPath ? null : inv.EquippedWeapon,
            EquippedAbility     = inv.EquippedAbility == itemResPath ? null : inv.EquippedAbility,
            EquippedArmor       = inv.EquippedArmor   == itemResPath ? null : inv.EquippedArmor,
            EquippedAccessories = accessories,
            BackpackSlots       = backpack,
            ConsumableSlots     = consumables,
        });
    }
/*
```
*/

/*
# Bullets
```c# */
    /// Called by the elevated server when an enemy fires. Upserts the ring-buffer slot so the
    /// table row count is bounded by the number of concurrent bullets, not total bullets fired.
    /// lifetimeSecs is how long the bullet lives before the elevated server calls DespawnBullet.
    [SpacetimeDB.Reducer]
    public static void FireBullet(ReducerContext ctx, ushort slotId, byte patternType, // ^FireBullet
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
    public static void DespawnBullet(ReducerContext ctx, ushort slotId) // ^DespawnBullet
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
        uint newHp = (uint)Math.Max(0, (int)data.CurrentHp - damage);
        ctx.Db.CharacterData.PlayerId.Update(data with { CurrentHp = newHp });
    }
/*
```
*/
}
