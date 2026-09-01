#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;

/// <summary>
/// Caches the static catalog subscription views — AllItems, AllEnchantments, AllTextures —
/// and resolves lookups against them (GetItem/GetEnchantment/GetEnchantments/GetResPath2D/GetResPath3D).
/// Emits <see cref="EnchantmentsChanged"/> whenever the enchantment view changes so UI
/// observers can refresh (logic moved out of GameManager.cs). Rows arrive via the child
/// TableBinderComponents declared inline in game.tscn (signals wired in the editor;
/// ReplayExistingRows replaces the old OnConnected hookup order handling).
/// </summary>
public partial class CatalogComponent : Component
{
    /// Fired whenever the AllEnchantments view changes (insert/update/delete).
    public event Action? EnchantmentsChanged;

    /// Fired whenever an item or enchantment row changes (insert/update/delete), after
    /// <see cref="CatalogVersion"/> is bumped. Observers holding catalog-derived caches
    /// (CombatComponent's effective weapon) re-read on this — a catalog row edit does
    /// not raise InventoryChanged.
    public event Action? CatalogChanged;

    /// Bumped on every item/enchantment row insert/update/delete. CombatComponent folds
    /// it into its weapon-fingerprint comparison, so a catalog content change (e.g. an
    /// admin reseed changing an item's behaviors) re-resolves the effective weapon even
    /// when the inventory itself is untouched.
    public long CatalogVersion { get; private set; }

    private readonly Dictionary<string, string> texture2DCache = new();
    private readonly Dictionary<string, string> texture3DCache = new();
    private readonly Dictionary<string, SpacetimeDB.Types.Item> itemCache = new();
    private readonly Dictionary<string, Enchantment> enchantmentCache = new();

    /// Child binders (declared inline in game.tscn) feeding the three catalog views.
    private TableBinderComponent allTexturesBinder = null!;
    private TableBinderComponent allItemsBinder = null!;
    private TableBinderComponent allEnchantmentsBinder = null!;

    public override void _Ready()
    {
        base._Ready();
        allTexturesBinder = GetNode<TableBinderComponent>("AllTexturesBinder");
        allItemsBinder = GetNode<TableBinderComponent>("AllItemsBinder");
        allEnchantmentsBinder = GetNode<TableBinderComponent>("AllEnchantmentsBinder");
    }

    // --- TableBinderComponent signal handlers (wired in game.tscn) ---
    // Each binder has ReplayExistingRows on, so rows already in the client cache come through
    // the same insert path — no separate connection-order handling here.

    private void OnTextureRow()
    {
        var texture = (TextureEntry)allTexturesBinder.LastRow!;
        texture2DCache[texture.TextureId] = texture.ResPath2D;
        texture3DCache[texture.TextureId] = texture.ResPath3D;
    }

    private void OnTextureRowDeleted()
    {
        var textureId = ((TextureEntry)allTexturesBinder.LastDeletedRow!).TextureId;
        texture2DCache.Remove(textureId);
        texture3DCache.Remove(textureId);
    }

    private void OnItemRow()
    {
        var item = (Item)allItemsBinder.LastRow!;
        itemCache[item.ItemId] = item;
        CatalogVersion++;
        CatalogChanged?.Invoke();
    }

    private void OnItemRowDeleted()
    {
        itemCache.Remove(((Item)allItemsBinder.LastDeletedRow!).ItemId);
        CatalogVersion++;
        CatalogChanged?.Invoke();
    }

    private void OnEnchantmentRow()
    {
        var enchantment = (Enchantment)allEnchantmentsBinder.LastRow!;
        enchantmentCache[enchantment.EnchantmentId] = enchantment;
        CatalogVersion++;
        EnchantmentsChanged?.Invoke();
        CatalogChanged?.Invoke();
    }

    private void OnEnchantmentRowDeleted()
    {
        enchantmentCache.Remove(((Enchantment)allEnchantmentsBinder.LastDeletedRow!).EnchantmentId);
        CatalogVersion++;
        EnchantmentsChanged?.Invoke();
        CatalogChanged?.Invoke();
    }

    public string? GetResPath2D(string textureId) =>
        texture2DCache.TryGetValue(textureId, out var path) ? path : null;

    /// Empty res_path3d means "no 3D representation" — normalized to null like a missing row.
    public string? GetResPath3D(string textureId) =>
        texture3DCache.TryGetValue(textureId, out var path) && path.Length > 0 ? path : null;

    public Item? GetItem(string itemId) => itemCache.TryGetValue(itemId, out var item) ? item : null;

    public Enchantment? GetEnchantment(string enchantmentId) =>
        enchantmentCache.TryGetValue(enchantmentId, out var enchantment) ? enchantment : null;

    public IEnumerable<Enchantment> GetEnchantments() => enchantmentCache.Values;
}
