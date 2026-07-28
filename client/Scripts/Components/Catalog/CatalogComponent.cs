#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;
using System.Collections.Generic;

/// <summary>
/// Caches the static catalog subscription views — AllItems, AllEnchantments, AllTextures —
/// and resolves lookups against them (GetItem/GetEnchantment/GetEnchantments/GetResPath).
/// Emits <see cref="EnchantmentsChanged"/> whenever the enchantment view changes so UI
/// observers can refresh (logic moved out of GameManager.cs).
/// </summary>
public partial class CatalogComponent : Component
{
    /// Fired whenever the AllEnchantments view changes (insert/update/delete).
    public event Action? EnchantmentsChanged;

    private readonly Dictionary<string, string> textureCache = new();
    private readonly Dictionary<string, SpacetimeDB.Types.Item> itemCache = new();
    private readonly Dictionary<string, Enchantment> enchantmentCache = new();

    protected override void OnRegistered()
    {
        if (GetSibling<ConnectionComponent>() is { } connection)
            connection.Connected += OnConnected;
    }

    private void OnConnected()
    {
        var conn = GetSibling<ConnectionComponent>()?.Conn;
        if (conn == null) return;

        conn.Db.AllTextures.OnInsert += (_, texture) => textureCache[texture.TextureId] = texture.ResPath;
        conn.Db.AllTextures.OnUpdate += (_, _, texture) => textureCache[texture.TextureId] = texture.ResPath;
        conn.Db.AllTextures.OnDelete += (_, texture) => textureCache.Remove(texture.TextureId);

        conn.Db.AllItems.OnInsert += (_, item) => itemCache[item.ItemId] = item;
        conn.Db.AllItems.OnUpdate += (_, _, item) => itemCache[item.ItemId] = item;
        conn.Db.AllItems.OnDelete += (_, item) => itemCache.Remove(item.ItemId);

        conn.Db.AllEnchantments.OnInsert += (_, enchantment) => { enchantmentCache[enchantment.EnchantmentId] = enchantment; EnchantmentsChanged?.Invoke(); };
        conn.Db.AllEnchantments.OnUpdate += (_, _, enchantment) => { enchantmentCache[enchantment.EnchantmentId] = enchantment; EnchantmentsChanged?.Invoke(); };
        conn.Db.AllEnchantments.OnDelete += (_, enchantment) => { enchantmentCache.Remove(enchantment.EnchantmentId); EnchantmentsChanged?.Invoke(); };
    }

    public string? GetResPath(string textureId) =>
        textureCache.TryGetValue(textureId, out var path) ? path : null;

    public Item? GetItem(string itemId) => itemCache.TryGetValue(itemId, out var item) ? item : null;

    public Enchantment? GetEnchantment(string enchantmentId) =>
        enchantmentCache.TryGetValue(enchantmentId, out var enchantment) ? enchantment : null;

    public IEnumerable<Enchantment> GetEnchantments() => enchantmentCache.Values;
}
