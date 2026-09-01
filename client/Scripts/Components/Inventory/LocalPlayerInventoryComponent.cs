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

    // Ability potency/target-range and charge constants live in World/ServerConstants.cs —
    // the single home for client mirrors of main/global.rs.

    /// Mirror of the server's ability_is_charge_capable (player/methods.rs) — charge-capable
    /// abilities are hold-to-charge (BeginAbilityCharge/ReleaseAbilityCharge) instead of the
    /// instant ActivateAbility path. Both must change together.
    public static bool IsChargeCapable(AbilityBehavior ability) =>
        ability.Effect is AbilityEffect.Teleport(var p) && p.ChargeScaled
        || ability.Effect is AbilityEffect.Spell(var s) && s.ChargeScaled;

    /// Charge-capable check for an ability cell, reading the item's resolved
    /// AbilityBehavior from the catalog (following span followers to the head). Used by
    /// InventoryPanel's key routing.
    public static bool IsChargeCapableAt(LocalPlayer player, int cellIndex)
    {
        var slot = player.ResolveSlotAt(cellIndex).Slot;
        if (slot == null) return false;
        int head = (int)(slot.OccupiedBy ?? (uint)cellIndex);
        var item = player.ResolveSlotAt(head).Item;
        if (item == null) return false;
        foreach (var behavior in item.Behaviors)
            if (behavior is ItemBehavior.Ability(var a)) return IsChargeCapable(a);
        return false;
    }

    // Optimistic-cast guard (per ability head slot): a cast is marked pending for roughly
    // one round-trip after the reducer call and the TryActivateAbility pre-check rejects
    // while pending, so a fast double-click can't fire two casts — and two optimistic
    // bullet-deletes — before the server echo lands. The echo clears the slot early
    // (OnInventoryRow), so legitimate fast recasts are gated on the echo, not the window.
    private static readonly Dictionary<int, long> castPendingUntilMs = new();

    /// Roughly one round-trip; generous on a local server, tight enough that recasts are
    /// normally gated by the early-clearing echo rather than by this window.
    private const long CastPendingWindowMs = 500;

    private static bool IsCastPending(int head) =>
        castPendingUntilMs.TryGetValue(head, out var until)
        && until > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static void MarkCastPending(int head) =>
        castPendingUntilMs[head] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + CastPendingWindowMs;

    /// P4 charge path, key-down: the same cooldown/charge pre-checks as
    /// TryActivateAbility, then the BeginAbilityCharge reducer. No optimistic state —
    /// the echoed AbilityCharge row is authoritative (see AbilityChargeComponent).
    public static void TryBeginCharge(LocalPlayer player, int cellIndex)
    {
        var slot = player.ResolveSlotAt(cellIndex).Slot;
        if (slot == null) return;
        int head = (int)(slot.OccupiedBy ?? (uint)cellIndex);
        var resolved = player.ResolveSlotAt(head);
        if (resolved.Slot is not { } headSlot || resolved.Item is not { } item) return;

        AbilityBehavior? ability = null;
        foreach (var behavior in item.Behaviors)
            if (behavior is ItemBehavior.Ability(var a)) { ability = a; break; }
        if (ability == null || !IsChargeCapable(ability)) return;

        if (headSlot.Charges == 0) return;
        if (headSlot.CooldownUntil is SpacetimeDB.Timestamp until
            && until.MicrosecondsSinceUnixEpoch > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L) return;

        GameManager.Conn?.Reducers.BeginAbilityCharge((uint)head);
    }

    /// P4 charge path, key-up: the ReleaseAbilityCharge reducer with the cursor's world
    /// position — only while the echoed charge row says we're charging this slot. No
    /// optimistic teleport: the server moves the position row and the desync snap in
    /// PositionSyncComponent hard-corrects us from the echo (same as instant Teleport).
    /// A charge-scaled Spell (P5) renders its flash optimistically exactly like the
    /// instant path — the visual doesn't depend on the charge fraction (only damage does).
    public static void TryReleaseCharge(LocalPlayer player, int cellIndex)
    {
        if (player.GetComponent<AbilityChargeComponent>() is not { Charging: true } charge) return;
        var slot = player.ResolveSlotAt(cellIndex).Slot;
        if (slot == null) return;
        int head = (int)(slot.OccupiedBy ?? (uint)cellIndex);
        if (charge.ChargedSlot != head) return;

        var target = player.GetGlobalMousePosition();
        GameManager.Conn?.Reducers.ReleaseAbilityCharge((uint)head, target.X, target.Y);

        if (player.ResolveSlotAt(head).Item is { } item)
        {
            foreach (var behavior in item.Behaviors)
                if (behavior is ItemBehavior.Ability(var a) && a.Effect is AbilityEffect.Spell(var p))
                {
                    SpawnOptimisticSpellVisual(player, head, p, target);
                    break;
                }
        }
    }

    /// P5 optimistic spell flash — shared by TryActivateAbility and the charge-release
    /// path. Mirrors the server's Spell dispatch encoding so the optimistic render matches
    /// the echo it suppresses: the cursor is clamped to ServerConstants.MaxAbilityTargetRange
    /// around the
    /// player for the Circle center (with the same zero-length fallback direction),
    /// Line/Cone run their full length in the cursor's direction, and only the Circle
    /// radius is position-scaled (matching scale_ability_effect server-side).
    private static void SpawnOptimisticSpellVisual(LocalPlayer player, int head, SpellParams p, Vector2 target)
    {
        var visuals = GameManager.SpellVisuals;
        if (visuals == null) return;
        var origin = player.GlobalPosition;
        var axis = target - origin;
        var clamped = axis.Length() > ServerConstants.MaxAbilityTargetRange ? origin + axis.Normalized() * ServerConstants.MaxAbilityTargetRange : target;
        var dir = axis.LengthSquared() > 0.000001f ? axis.Normalized() : Vector2.Right;
        int position = Mathf.Clamp(head - AbilitySlotStart, 0, ServerConstants.AbilityPositionMultipliers.Length - 1);
        float multiplier = ServerConstants.AbilityPositionMultipliers[position];
        switch (p.Shape)
        {
            case SpellShape.Circle(var c):
                visuals.Render(p.Visual, origin, clamped, c.Radius * multiplier, SpellVisualComponent.DefaultDuration);
                break;
            case SpellShape.Line(var l):
                visuals.Render(p.Visual, origin, origin + dir * l.Length, l.Width, SpellVisualComponent.DefaultDuration);
                break;
            case SpellShape.Cone(var c):
                visuals.Render(p.Visual, origin, origin + dir * c.Length, c.Spread, SpellVisualComponent.DefaultDuration);
                break;
        }
    }

    /// Activates whatever occupies ability cell `cellIndex` (following span followers to
    /// the head): client-side cooldown/charge pre-check, the ActivateAbility reducer with
    /// the cursor's world position, and — for bullet-control and spell effects — the
    /// optimistic local apply (remote clients apply the same cast from the
    /// BulletControlEvent/AbilityVisualEvent echo; the caster's own echo is skipped via
    /// cast_by). Passive ability items (no Ability behavior) silently do nothing, as do
    /// charge-capable items (P4) — those route through the hold-to-charge key path
    /// instead. Shared by the hotkeys (InventoryPanel) and the slot click path
    /// (SlotComponent).
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
        if (IsChargeCapable(ability)) return; // hold-to-charge items route through the key path (InventoryPanel)

        if (headSlot.Charges == 0) return;
        if (headSlot.CooldownUntil is SpacetimeDB.Timestamp until
            && until.MicrosecondsSinceUnixEpoch > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000L) return;
        if (IsCastPending(head)) return; // a cast is already awaiting its echo — reject the double-click

        var target = player.GetGlobalMousePosition();
        GameManager.Conn?.Reducers.ActivateAbility((uint)head, target.X, target.Y);
        MarkCastPending(head);

        // Optimistic local apply for bullet-control effects, with the same position
        // multiplier the server applies to the event radius.
        int position = Mathf.Clamp(head - AbilitySlotStart, 0, ServerConstants.AbilityPositionMultipliers.Length - 1);
        float multiplier = ServerConstants.AbilityPositionMultipliers[position];
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
            case AbilityEffect.Slash(var p):
            {
                var origin = player.GlobalPosition;
                var axis = target - origin;
                // Same zero-length fallback as the server so the optimistic rect matches the echo.
                var dir = axis.LengthSquared() > 0.000001f ? axis.Normalized() : Vector2.Right;
                controller?.SlashInRect(origin, origin + dir * (p.Length * multiplier), p.Width * multiplier, p.MaxBulletDamage);
                break;
            }
            case AbilityEffect.Wall(var p):
            {
                var origin = player.GlobalPosition;
                var axis = target - origin;
                // Same zero-length fallback as the server so the optimistic rect matches the echo.
                var dir = axis.LengthSquared() > 0.000001f ? axis.Normalized() : Vector2.Right;
                controller?.SpawnWallZone(origin, origin + dir * (p.Length * multiplier), p.Width * multiplier, ability.Duration);
                break;
            }
            case AbilityEffect.Grapple(var range):
                StartGrappleTween(player, target, range * multiplier);
                break;
            case AbilityEffect.Spell(var p):
                SpawnOptimisticSpellVisual(player, head, p, target);
                break;
            case AbilityEffect.Teleport:
                // No optimistic apply: the server moves the position row itself and the
                // desync snap in PositionSyncComponent hard-corrects us from the echo.
                break;
        }
    }

    /// P4 grapple (bullet master): tween the local player toward the cursor (clamped to
    /// the position-scaled range) over ~0.25s. The pull is client-authoritative — the
    /// server arm only validates and spends, and stamps a PlayerDisplacementAllowance so
    /// the movement clamp accepts the landing jump — so the tween reports exactly twice:
    /// once at the start and once at the end (mid-tween reports would each read as an
    /// oversized jump and burn the one-shot allowance before the landing report;
    /// PositionSyncComponent suppresses its timed/edge reports while
    /// ExternalMovementActive for the same reason). Input movement stays suspended so
    /// MoveAndSlide doesn't fight the externally-set position. Remotes track the pull
    /// from the start/end reports plus interpolation; their WrapSnapThreshold
    /// self-corrects any residual snap.
    private const float GrappleTweenSeconds = 0.25f;

    private static void StartGrappleTween(LocalPlayer player, Vector2 target, float range)
    {
        var origin = player.GlobalPosition;
        var offset = target - origin;
        if (offset.Length() > range) offset = offset.Normalized() * range;
        var destination = origin + offset;

        var sync = player.GetComponent<PositionSyncComponent>();
        player.ExternalMovementActive = true;
        sync?.RequestMovementReport(); // start report: remotes see the pull begin
        var tween = player.CreateTween();
        tween.TweenMethod(Callable.From<Vector2>(pos =>
        {
            if (!GodotObject.IsInstanceValid(player)) return;
            player.GlobalPosition = pos;
            player.Velocity = Vector2.Zero;
        }), origin, destination, GrappleTweenSeconds);
        tween.Finished += () =>
        {
            if (!GodotObject.IsInstanceValid(player)) return;
            player.Velocity = Vector2.Zero;
            player.ExternalMovementActive = false;
            sync?.RequestMovementReport(); // final report carries the destination + zeroed velocity
        };
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
        // The server echo for a cast lands as a row update on the head slot — clear the
        // optimistic-cast guard early so recasts don't wait out the full pending window.
        castPendingUntilMs.Remove((int)slot.SlotIndex);
        (Entity as LocalPlayer)?.RaiseInventoryChanged();
    }

    private void OnInventoryRowDeleted()
    {
        var slot = (PlayerInventorySlot)inventoryBinder.LastDeletedRow!;
        inventorySlots.Remove((int)slot.SlotIndex);
        castPendingUntilMs.Remove((int)slot.SlotIndex);
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
