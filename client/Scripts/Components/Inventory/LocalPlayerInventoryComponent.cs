#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;
using System.Linq;

public class ResolvedSlot
{
    public InventorySlot? Slot { get; init; }
    public Item? Item { get; init; }
    public bool IsEmpty => Item == null;
}

/// <summary>
/// The local player's inventory state: mirrors LocalPlayerInventory rows (delivered by the
/// child binder declared in local_player.tscn, signals wired in the editor) into the slot
/// list and resolves items through GameManager's catalog. LocalPlayer keeps pass-through
/// accessors and raises InventoryChanged on itself so UI readers (InventoryComponent,
/// ItemSidebarComponent, CombatComponent) stay stable.
/// </summary>
public partial class LocalPlayerInventoryComponent : Component
{
    private TableBinderComponent inventoryBinder = null!;
    private readonly List<InventorySlot> inventorySlots = new();

    // Slot 0: weapon only
    public Item? EquippedWeapon => GetSlotItem(0, GameManager.GetItem);

    // Slots 1-4: consumables only hotbar
    public IReadOnlyList<Item?> HotbarSlots => BuildTypedSlots(1, 4, GameManager.GetItem);

    // Slots 5-8: accessories only
    public IReadOnlyList<Item?> AccessorySlots => BuildTypedSlots(5, 4, GameManager.GetItem);

    // Slots 9-12: armor only
    public IReadOnlyList<Item?> ArmorSlots => BuildTypedSlots(9, 4, GameManager.GetItem);

    // Slots 13-14: artifact only
    public IReadOnlyList<Item?> ArtifactSlots => BuildTypedSlots(13, 2, GameManager.GetItem);

    // Slots 15-22: general mixed
    public IReadOnlyList<ResolvedSlot> GeneralSlots => BuildResolvedSlots(15, 8);

    // Slots that count as worn equipment (enchantable): 0 weapon, 5-8 accessories, 9-12 armor, 13-14 artifacts
    public static bool IsEquipmentSlot(int index) => index == 0 || (index >= 5 && index <= 14);

    public override void _Ready()
    {
        base._Ready();
        inventoryBinder = GetNode<TableBinderComponent>("LocalPlayerInventoryBinder");
    }

    // --- TableBinderComponent signal handler (wired in local_player.tscn; insert and
    // update both replace the whole slot list, so one handler serves both signals) ---

    private void OnInventoryRow()
    {
        var inventory = (PlayerInventory)inventoryBinder.LastRow!;
        inventorySlots.Clear();
        inventorySlots.AddRange(inventory.Slots);
        (Entity as LocalPlayer)?.RaiseInventoryChanged();
    }

    private IReadOnlyList<ResolvedSlot> BuildResolvedSlots(int startIndex, int count) =>
        Enumerable.Range(startIndex, count).Select(ResolveSlotAt).ToList();

    private IReadOnlyList<T?> BuildTypedSlots<T>(int startIndex, int count, System.Func<string, T?> resolver) where T : class =>
        Enumerable.Range(startIndex, count).Select(i => GetSlotItem(i, resolver)).ToList();

    public ResolvedSlot ResolveSlotAt(int index)
    {
        if (index >= inventorySlots.Count) return new ResolvedSlot();
        var slot = inventorySlots[index];
        if (slot.ItemId == null) return new ResolvedSlot { Slot = slot };
        var item = GameManager.GetItem(slot.ItemId);
        return new ResolvedSlot { Slot = slot, Item = item };
    }

    private T? GetSlotItem<T>(int index, System.Func<string, T?> resolver) where T : class
    {
        if (index >= inventorySlots.Count) return null;
        var itemId = inventorySlots[index].ItemId;
        return itemId != null ? resolver(itemId) : null;
    }

    public string? GetSlotItemId(int index) => index < inventorySlots.Count ? inventorySlots[index].ItemId : null;
}
