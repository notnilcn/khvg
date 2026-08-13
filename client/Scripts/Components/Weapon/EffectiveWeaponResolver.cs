#nullable enable
using SpacetimeDB.Types;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>
/// The weapon the local player is actually firing: the weapon slot's item, the
/// active_toggle-selected pattern (the item's own Weapon behaviors first, then
/// WeaponToggle enchantment grants), with shot-count/damage enchantment behaviors folded
/// in. Client mirror of resolve_effective_weapon in server/spacetimedb/src/player/
/// methods.rs — the fold order (slot index ascending, innate enchantments before
/// socketed) must match the server exactly, since both sides index toggle options.
/// </summary>
public class EffectiveWeapon
{
    public required WeaponBehavior Behavior { get; init; }
    public float FlatBulletMod { get; init; }
    public float TrueFlat { get; init; }
    public float TruePct { get; init; }
    public int ToggleCount { get; init; }
}

public static class EffectiveWeaponResolver
{
    // Equipped head indices in the server's fold order: weapon, accessory, armor, abilities.
    private static IEnumerable<int> EquippedIndices()
    {
        yield return 0;
        yield return 5;
        yield return 6;
        for (int i = LocalPlayerInventoryComponent.AbilitySlotStart; i < LocalPlayerInventoryComponent.AbilitySlotStart + LocalPlayerInventoryComponent.AbilitySlotCount; i++)
            yield return i;
    }

    /// Every enchantment behavior from equipped items (innate before socketed per slot).
    public static List<EnchantmentBehavior> EquippedEnchantmentBehaviors(LocalPlayer player)
    {
        var result = new List<EnchantmentBehavior>();
        foreach (var index in EquippedIndices())
        {
            var resolved = player.ResolveSlotAt(index);
            if (resolved.Slot is not { } slot || slot.OccupiedBy != null || resolved.Item is not { } item) continue;
            foreach (var id in item.InnateEnchantmentIds.Concat(slot.EnchantmentIds))
            {
                var enchantment = GameManager.GetEnchantment(id);
                if (enchantment != null) result.AddRange(enchantment.Behaviors);
            }
        }
        return result;
    }

    /// The item's own Weapon behaviors plus WeaponToggle grants from equipped enchantments.
    public static List<WeaponBehavior> ToggleOptions(LocalPlayer player)
    {
        var options = new List<WeaponBehavior>();
        var resolved = player.ResolveSlotAt(0);
        if (resolved.Item is not { } item) return options;
        foreach (var behavior in item.Behaviors)
            if (behavior is ItemBehavior.Weapon(var weapon)) options.Add(weapon);
        foreach (var behavior in EquippedEnchantmentBehaviors(player))
            if (behavior is EnchantmentBehavior.WeaponToggle(var weapon)) options.Add(weapon);
        return options;
    }

    public static EffectiveWeapon? Resolve(LocalPlayer player)
    {
        var resolved = player.ResolveSlotAt(0);
        if (resolved.Slot is not { } slot || resolved.Item is null) return null;
        var enchantments = EquippedEnchantmentBehaviors(player);
        var options = ToggleOptions(player);
        if (options.Count == 0) return null;

        // Clamp like the server does — removing a toggle-granting enchantment can leave
        // ActiveToggle pointing past the end.
        var selected = options[System.Math.Min((int)slot.ActiveToggle, options.Count - 1)];

        int addShots = 0;
        float shotMult = 0f, flatBulletMod = 0f, trueFlat = 0f, truePct = 0f;
        foreach (var behavior in enchantments)
        {
            switch (behavior)
            {
                case EnchantmentBehavior.AddShots(var n): addShots += n; break;
                case EnchantmentBehavior.ShotCountMult(var m): shotMult += m; break;
                case EnchantmentBehavior.FlatBulletDamageMod(var v): flatBulletMod += v; break;
                case EnchantmentBehavior.TrueDamageFlat(var v): trueFlat += v; break;
                case EnchantmentBehavior.TrueDamagePercent(var v): truePct += v; break;
            }
        }
        uint effectiveShots = (uint)System.MathF.Max(1f, System.MathF.Round(System.MathF.Max(1, (int)selected.ShotCount + addShots) * (1f + shotMult)));

        return new EffectiveWeapon
        {
            // Never mutate the catalog's shared WeaponBehavior — copy with the folded count.
            Behavior = new WeaponBehavior(selected.Damage, selected.Range, selected.FireRate, selected.ProjectileSpeed,
                effectiveShots, selected.ZoneCount, selected.Pierce, selected.ProjectileTextureId, selected.Pattern, selected.SpreadAngle),
            FlatBulletMod = flatBulletMod,
            TrueFlat = trueFlat,
            TruePct = truePct,
            ToggleCount = options.Count,
        };
    }

    /// Cache key covering everything Resolve reads — item ids, the weapon's ActiveToggle,
    /// and socketed/innate enchantment ids on every equipped slot. Compared by
    /// CombatComponent on InventoryChanged so toggles and socketing re-resolve the weapon.
    public static string Fingerprint(LocalPlayer player)
    {
        var sb = new StringBuilder();
        foreach (var index in EquippedIndices())
        {
            var resolved = player.ResolveSlotAt(index);
            sb.Append(index).Append(':').Append(resolved.Item?.ItemId).Append(':')
                .Append(resolved.Slot?.ActiveToggle ?? 0).Append(':');
            if (resolved.Slot != null) sb.Append(string.Join(",", resolved.Slot.EnchantmentIds));
            sb.Append('/');
            if (resolved.Item != null) sb.Append(string.Join(",", resolved.Item.InnateEnchantmentIds));
            sb.Append(';');
        }
        return sb.ToString();
    }
}
