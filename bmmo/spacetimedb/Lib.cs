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

/// Live position and current movement input for each connected player.
/// ChunkX/ChunkY enable AoI: clients subscribe to a range of chunks
/// rather than the full table. CHUNK_SIZE must match the client constant.
[SpacetimeDB.Table(Accessor = "PlayerPosition", Public = true)]
public partial struct PlayerPosition
{
    [SpacetimeDB.PrimaryKey]
    public Identity PlayerId;
    public float X;
    public float Y;
    /// Normalized movement direction set by client on keypress/release (-1, 0, or 1).
    public float MoveX;
    public float MoveY;
    public int ChunkX;
    public int ChunkY;
}

/// Drives the 20 Hz player movement tick. One row inserted at Init; never deleted.
[SpacetimeDB.Table(Accessor = "MoveTickSchedule", Scheduled = "PlayerMoveTick")]
public partial struct MoveTickSchedule
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    public ScheduleAt ScheduledAt;
}

// ──────────────────────────────────────────────
// Module
// ──────────────────────────────────────────────

public static partial class Module
{
    private const int BaseMaxHp = 100;
    private const int HpPerLevel = 5;

    /// Units per second. Must match the player speed constant in Godot.
    private const float PlayerSpeed = 100f;

    /// Seconds between movement ticks (1 / 20 Hz).
    private const float TickDelta = 1f / 20f;

    /// World units per chunk side. Clients subscribe to chunk ranges for AoI.
    /// Must match the client constant used to compute subscription bounds.
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

    [SpacetimeDB.Reducer(ReducerKind.Init)]
    public static void Init(ReducerContext ctx)
    {
        ctx.Db.MoveTickSchedule.Insert(new MoveTickSchedule
        {
            Id = 0,
            ScheduledAt = new ScheduleAt.Interval(TimeSpan.FromMilliseconds(50)),
        });
    }

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

    /// Called by client on keypress/release with the current normalized direction.
    /// MoveX/MoveY should each be -1, 0, or 1. The server clamps them.
    [SpacetimeDB.Reducer]
    public static void SetMoveInput(ReducerContext ctx, float moveX, float moveY)
    {
        Log.Info($"SetMoveInput({moveX:F2},{moveY:F2}) from {ctx.Sender}");
        if (ctx.Db.PlayerPosition.PlayerId.Find(ctx.Sender) is not PlayerPosition pos)
            throw new Exception($"Position not found for {ctx.Sender}");

        ctx.Db.PlayerPosition.PlayerId.Update(pos with
        {
            MoveX = Math.Clamp(moveX, -1f, 1f),
            MoveY = Math.Clamp(moveY, -1f, 1f),
        });
    }

    /// 20 Hz tick. Moves every player according to their stored input direction.
    /// Wall collision is client-side only — server movement is physics-free for now.
    [SpacetimeDB.Reducer]
    public static void PlayerMoveTick(ReducerContext ctx, MoveTickSchedule schedule)
    {
        foreach (var pos in ctx.Db.PlayerPosition.Iter())
        {
            if (pos.MoveX == 0f && pos.MoveY == 0f)
                continue;

            // Normalize diagonal movement so speed is consistent in all directions.
            float len = MathF.Sqrt(pos.MoveX * pos.MoveX + pos.MoveY * pos.MoveY);
            float nx = pos.MoveX / len;
            float ny = pos.MoveY / len;

            float newX = pos.X + nx * PlayerSpeed * TickDelta;
            float newY = pos.Y + ny * PlayerSpeed * TickDelta;
            int chunkX = (int)MathF.Floor(newX / ChunkSize);
            int chunkY = (int)MathF.Floor(newY / ChunkSize);

            ctx.Db.PlayerPosition.PlayerId.Update(pos with
            {
                X = newX,
                Y = newY,
                ChunkX = chunkX,
                ChunkY = chunkY,
            });
        }
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
