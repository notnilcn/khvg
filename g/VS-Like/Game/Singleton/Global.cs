/*
header stuff ig
```c# */
using Godot;
using System.Collections.Generic;

/*
#  

```c# */
public partial class Global : Node
{
    public static Global Instance { get; private set; } // ^1

    public Dictionary<int, Player> Players { get; } = new(); // ^2

    public static readonly ResItem[] ResItems =
    {
        GD.Load<ResItem>("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Axe/res_axe.tres"),
        GD.Load<ResItem>("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Arrow/res_arrow.tres"),
        GD.Load<ResItem>("res://VS-Like/Characters/Player/Utilities/Weapons/Types/SineArrow/res_sine_arrow.tres"),
        GD.Load<ResItem>("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Bible/res_bible.tres"),
        GD.Load<ResItem>("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Boots/res_boots.tres"),
        GD.Load<ResItem>("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Parchment/res_parchment.tres"),
        GD.Load<ResItem>("res://VS-Like/Characters/Player/Utilities/Weapons/Types/Ring/res_ring.tres"),
    };
/*
```
*/

/*
# `_Ready()`
idk what to say, just a default _ready function
```c#
*/
    public override void _Ready()
    {
        Instance = this;
    }
/*
```
*/

    public Player GetLocalPlayer() => Player.Local;

/*
# `GetAvailableItems()`
its obvious ig
```c#
*/
    public ResItem[] GetAvailableItems()
    {
        return (ResItem[])ResItems.Clone();
    }
/*
```
*/

/*
# `PickupItem()`
its obvious ig
```c#
*/
    public void PickupItem(ResItem resItm)
    {
        var player = GetLocalPlayer();
        if (player == null)
            return;
        if (_SlotIsEmpty(player, resItm))
            player.EquipItem(resItm);
        else if (!player.AddToInventory(resItm))
            GD.PushWarning("Inventory full — could not pick up: " + resItm.ItemName);
    }
/*
```
*/

/*
# `_SlotIsEmpty()`
its obvious ig
```c#
*/
    private static bool _SlotIsEmpty(Player player, ResItem resWpn)
    {
        return resWpn.ItemTypeValue switch
        {
            ResItem.ItemType.Weapon    => player.EquippedWeapon == null,
            ResItem.ItemType.Ability   => player.EquippedAbility == null,
            ResItem.ItemType.Armor     => player.EquippedArmor == null,
            ResItem.ItemType.Accessory => player.EquippedAccessories.Count < player.GetAccessorySlotCount(),
            _                          => false,
        };
    }
/*
```
*/
}
