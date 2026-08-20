#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;
using System.Linq;

public class ResolvedSlot
{
    public PlayerInventorySlot? Slot { get; init; }
    public Item? Item { get; init; }
    public bool IsEmpty => Item == null;
}

/// <summary>
/// The local player's inventory state: mirrors LocalPlayerInventory rows (delivered by the
/// child binder declared in local_player.tscn, signals wired in the editor) into a per-slot
/// dictionary and resolves items through GameManager's catalog. The server stores one
/// PlayerInventorySlot row per slot, so insert/update/delete each touch a single entry —
/// no whole-list replacement. LocalPlayer keeps pass-through accessors and raises
/// InventoryChanged on itself so UI readers (InventoryPanel, ItemSidebar,
/// CombatComponent) stay stable.
/// </summary>
public partial class LocalPlayerInventoryComponent : Component
{
    private TableBinderComponent inventoryBinder = null!;
    private readonly Dictionary<int, PlayerInventorySlot> inventorySlots = new();

    // Slot 0: weapon only
    public Item? EquippedWeapon => GetSlotItem(0, GameManager.GetItem);

    // Slots 1-4: consumables only hotbar
    public IReadOnlyList<Item?> HotbarSlots => BuildTypedSlots(1, 4, GameManager.GetItem);

    // Slot 5: accessory only
    public IReadOnlyList<Item?> AccessorySlots => BuildTypedSlots(5, 1, GameManager.GetItem);

    // Slot 6: armor only
    public IReadOnlyList<Item?> ArmorSlots => BuildTypedSlots(6, 1, GameManager.GetItem);

    // Slots 7-30: general mixed (backpack — accepts every item type)
    public IReadOnlyList<ResolvedSlot> GeneralSlots => BuildResolvedSlots(7, 24);

    // Slot 31: bag
    public const int BagSlotIndex = 31;

    // Slots 32-37: abilities (artifacts are 1-cost abilities)
    public const int AbilitySlotStart = 32;
    public const int AbilitySlotCount = 6;
    public IReadOnlyList<ResolvedSlot> AbilitySlots => BuildResolvedSlots(AbilitySlotStart, AbilitySlotCount);
    public static bool IsAbilitySlot(int index) => index >= AbilitySlotStart && index < AbilitySlotStart + AbilitySlotCount;

    // Mirrors ABILITY_POSITION_MULTIPLIERS in server/spacetimedb/src/main/global.rs
    // ("first ability stronger") — both must change together.
    public static readonly float[] AbilityPositionMultipliers = [1.5f, 1.25f, 1.1f, 1.0f, 0.9f, 0.8f];

    /// Activates whatever occupies ability cell `cellIndex` (following span followers to
    /// the head): client-side cooldown/charge pre-check, the ActivateAbility reducer with
    /// the cursor's world position, and — for bullet-control effects — the optimistic
    /// local apply (remote clients apply the same cast from the BulletControlEvent echo;
    /// the caster's own echo is skipped via cast_by). Passive ability items (no Ability
    /// behavior) silently do nothing. Shared by the hotkeys (InventoryPanel) and the
    /// slot click path (SlotComponent).
    public static void TryActivateAbility(LocalPlayer player, int cellIndex)
    {
        var slot = player.ResolveSlotAt(cellIndex).Slot;
        if (slot == null) return;
        int head = (int)(slot.OccupiedBy ?? (uint)cellIndex);
        var resolved = player.ResolveSlotAt(head);
        if (resolved.Slot is not { } headSlot || resolved.Item is not { } item) return;

        AbilityBehavior? ability = null;
        foreach (var behavior in item.Behaviors)
            if (behavior is ItemBehavior.Ability(var a)) { ability = a; break; }
        if (ability == null) return; // passive item — nothing to activate

        if (headSlot.Charges == 0) return;
        if (headSlot.CooldownUntil is SpacetimeDB.Timestamp until
            && until.MicrosecondsSinceUnixEpoch > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L) return;

        var target = player.GetGlobalMousePosition();
        GameManager.Conn?.Reducers.ActivateAbility((uint)head, target.X, target.Y);

        // Optimistic local apply for bullet-control effects, with the same position
        // multiplier the server applies to the event radius.
        int position = Mathf.Clamp(head - AbilitySlotStart, 0, AbilityPositionMultipliers.Length - 1);
        float multiplier = AbilityPositionMultipliers[position];
        var controller = BulletManager.Instance?.GetComponent<BulletControllerComponent>();
        switch (ability.Effect)
        {
            case AbilityEffect.DeleteBullets(var radius): controller?.DeleteNear(target, radius * multiplier); break;
            case AbilityEffect.AttractBullets(var radius):
                if (ability.Duration > 0f) controller?.SpawnAttractZone(target, radius * multiplier, target, ability.Duration);
                else controller?.AttractNear(target, radius * multiplier, target);
                break;
            case AbilityEffect.SplitBullets(var p):
            {
                var origin = player.GlobalPosition;
                var axis = target - origin;
                // Same zero-length fallback as the server so the optimistic rect matches the echo.
                var dir = axis.LengthSquared() > 0.000001f ? axis.Normalized() : Vector2.Right;
                controller?.SplitInRect(origin, origin + dir * (p.Length * multiplier), p.Width * multiplier);
                break;
            }
            case AbilityEffect.DeleteBulletsInRect(var p):
            {
                var origin = player.GlobalPosition;
                var axis = target - origin;
                // Same zero-length fallback as the server so the optimistic rect matches the echo.
                var dir = axis.LengthSquared() > 0.000001f ? axis.Normalized() : Vector2.Right;
                controller?.DeleteInRect(origin, origin + dir * (p.Length * multiplier), p.Width * multiplier);
                break;
            }
        }
    }

    // Slots that count as worn equipment (enchantable): 0 weapon, 5 accessory, 6 armor, 32-37 abilities
    public static bool IsEquipmentSlot(int index) => index == 0 || index == 5 || index == 6 || IsAbilitySlot(index);

    public override void _Ready()
    {
        base._Ready();
        inventoryBinder = GetNode<TableBinderComponent>("LocalPlayerInventoryBinder");
    }

    // --- TableBinderComponent signal handlers (wired in local_player.tscn) ---

    private void OnInventoryRow()
    {
        var slot = (PlayerInventorySlot)inventoryBinder.LastRow!;
        inventorySlots[(int)slot.SlotIndex] = slot;
        (Entity as LocalPlayer)?.RaiseInventoryChanged();
    }

    private void OnInventoryRowDeleted()
    {
        var slot = (PlayerInventorySlot)inventoryBinder.LastDeletedRow!;
        inventorySlots.Remove((int)slot.SlotIndex);
        (Entity as LocalPlayer)?.RaiseInventoryChanged();
    }

    private IReadOnlyList<ResolvedSlot> BuildResolvedSlots(int startIndex, int count) =>
        Enumerable.Range(startIndex, count).Select(ResolveSlotAt).ToList();

    private IReadOnlyList<T?> BuildTypedSlots<T>(int startIndex, int count, System.Func<string, T?> resolver) where T : class =>
        Enumerable.Range(startIndex, count).Select(i => GetSlotItem(i, resolver)).ToList();

    public ResolvedSlot ResolveSlotAt(int index)
    {
        if (!inventorySlots.TryGetValue(index, out var slot)) return new ResolvedSlot();
        if (slot.ItemId == null) return new ResolvedSlot { Slot = slot };
        var item = GameManager.GetItem(slot.ItemId);
        return new ResolvedSlot { Slot = slot, Item = item };
    }

    private T? GetSlotItem<T>(int index, System.Func<string, T?> resolver) where T : class
    {
        if (!inventorySlots.TryGetValue(index, out var slot)) return null;
        return slot.ItemId != null ? resolver(slot.ItemId) : null;
    }

    public string? GetSlotItemId(int index) => inventorySlots.TryGetValue(index, out var slot) ? slot.ItemId : null;
}
