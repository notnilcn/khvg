#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;

/// <summary>
/// Caches the static catalog subscription views — AllItems, AllEnchantments, AllTextures —
/// and resolves lookups against them (GetItem/GetEnchantment/GetEnchantments/GetResPath).
/// Emits <see cref="EnchantmentsChanged"/> whenever the enchantment view changes so UI
/// observers can refresh (logic moved out of GameManager.cs). Rows arrive via the child
/// TableBinderComponents declared in catalog_component.tscn (signals wired in the editor;
/// ReplayExistingRows replaces the old OnConnected hookup order handling).
/// </summary>
public partial class CatalogComponent : Component
{
    /// Fired whenever the AllEnchantments view changes (insert/update/delete).
    public event Action? EnchantmentsChanged;

    private readonly Dictionary<string, string> textureCache = new();
    private readonly Dictionary<string, SpacetimeDB.Types.Item> itemCache = new();
    private readonly Dictionary<string, Enchantment> enchantmentCache = new();

    /// Child binders (declared in catalog_component.tscn) feeding the three catalog views.
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

    // --- TableBinderComponent signal handlers (wired in catalog_component.tscn) ---
    // Each binder has ReplayExistingRows on, so rows already in the client cache come through
    // the same insert path — no separate connection-order handling here.

    private void OnTextureRow()
    {
        var texture = (TextureEntry)allTexturesBinder.LastRow!;
        textureCache[texture.TextureId] = texture.ResPath;
    }

    private void OnTextureRowDeleted()
    {
        textureCache.Remove(((TextureEntry)allTexturesBinder.LastDeletedRow!).TextureId);
    }

    private void OnItemRow()
    {
        var item = (Item)allItemsBinder.LastRow!;
        itemCache[item.ItemId] = item;
    }

    private void OnItemRowDeleted()
    {
        itemCache.Remove(((Item)allItemsBinder.LastDeletedRow!).ItemId);
    }

    private void OnEnchantmentRow()
    {
        var enchantment = (Enchantment)allEnchantmentsBinder.LastRow!;
        enchantmentCache[enchantment.EnchantmentId] = enchantment;
        EnchantmentsChanged?.Invoke();
    }

    private void OnEnchantmentRowDeleted()
    {
        enchantmentCache.Remove(((Enchantment)allEnchantmentsBinder.LastDeletedRow!).EnchantmentId);
        EnchantmentsChanged?.Invoke();
    }

    public string? GetResPath(string textureId) =>
        textureCache.TryGetValue(textureId, out var path) ? path : null;

    public Item? GetItem(string itemId) => itemCache.TryGetValue(itemId, out var item) ? item : null;

    public Enchantment? GetEnchantment(string enchantmentId) =>
        enchantmentCache.TryGetValue(enchantmentId, out var enchantment) ? enchantment : null;

    public IEnumerable<Enchantment> GetEnchantments() => enchantmentCache.Values;
}
