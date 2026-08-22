#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// One enemy's debuff chips + stagger bar, drawn at the DebuffAttach point above the
/// sprite (declared in default_enemy.tscn, counter-scaled against the enemy root's 0.5
/// scale). Chips come from the child ActiveEnemyStatusEffectBinder with
/// ReplayExistingRows on — a PERSISTENT table, so replay shows still-live debuffs when
/// the enemy instance spawns (the GroundZone rule, not the event-table rule). The raw
/// public table carries every enemy's rows, so handlers filter to this enemy's id —
/// per-instance filtering is fine at co-op scale. The stagger bar reads Enemy.Stagger,
/// mirrored from the enemy's own NearbyEnemies row.
/// </summary>
public partial class EnemyDebuffIndicatorComponent : Node2DComponent
{
    private const float ChipSize = 10f;
    private const float ChipGap = 13f;
    private const float BarWidth = 44f;
    private const float BarHeight = 5f;

    private readonly Dictionary<EnemyStatusKind, ActiveEnemyStatusEffect> debuffs = new();
    private TableBinderComponent statusBinder = null!;
    private float lastStagger = -1f;

    public override void _Ready()
    {
        base._Ready();
        statusBinder = GetNode<TableBinderComponent>("ActiveEnemyStatusEffectBinder");
    }

    private ulong OwnerEnemyId => (Entity as Enemy)?.EnemyId ?? ulong.MaxValue;

    // --- ActiveEnemyStatusEffectBinder signal handlers (wired in default_enemy.tscn) ---

    private void OnStatusRow()
    {
        var status = (ActiveEnemyStatusEffect)statusBinder.LastRow!;
        if (status.EnemyId != OwnerEnemyId) return;
        debuffs[status.Kind] = status;
        QueueRedraw();
    }

    private void OnStatusRowDeleted()
    {
        var status = (ActiveEnemyStatusEffect)statusBinder.LastDeletedRow!;
        if (status.EnemyId != OwnerEnemyId) return;
        if (debuffs.Remove(status.Kind))
            QueueRedraw();
    }

    public override void _Process(double delta)
    {
        float stagger = (Entity as Enemy)?.Stagger ?? 0f;
        if (!Mathf.IsEqualApprox(stagger, lastStagger))
        {
            lastStagger = stagger;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        // The stagger bar sits above the chips, hidden while the bar is empty.
        if (lastStagger > 0f)
        {
            float fill = BarWidth * Mathf.Clamp(lastStagger, 0f, 100f) / 100f;
            DrawRect(new Rect2(-BarWidth / 2f, -BarHeight - 3f, BarWidth, BarHeight), new Color(0f, 0f, 0f, 0.6f));
            DrawRect(new Rect2(-BarWidth / 2f, -BarHeight - 3f, fill, BarHeight), new Color(1f, 0.9f, 0.3f));
        }

        int count = debuffs.Count;
        int i = 0;
        foreach (var kind in debuffs.Keys.OrderBy(k => (int)k))
        {
            float x = (i - (count - 1) / 2.0f) * ChipGap - ChipSize / 2f;
            DrawRect(new Rect2(x, 0f, ChipSize, ChipSize), ChipColor(kind));
            i++;
        }
    }

    private static Color ChipColor(EnemyStatusKind kind) => kind switch
    {
        EnemyStatusKind.DoT => new Color(0.3f, 0.9f, 0.4f),           // green
        EnemyStatusKind.Slow => new Color(0.4f, 0.6f, 1f),            // blue
        EnemyStatusKind.Vulnerability => new Color(0.7f, 0.4f, 1f),   // purple
        EnemyStatusKind.ArmorBreak => new Color(1f, 0.6f, 0.2f),      // orange
        EnemyStatusKind.Stunned => new Color(1f, 0.95f, 0.3f),        // yellow
        _ => new Color(1f, 1f, 1f),
    };
}
