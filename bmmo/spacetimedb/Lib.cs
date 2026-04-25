using SpacetimeDB;

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
        Log.Info($"Player disconnected: {ctx.Sender}");
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
}
