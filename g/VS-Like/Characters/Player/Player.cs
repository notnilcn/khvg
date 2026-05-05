/*
Player code.
```c# */
using Godot;
using System.Collections.Generic;
using SpacetimeDB.Types;

public partial class Player : CharacterBody2D
{
    [Export] public ResPlayer CharacterData { get; set; }

    private Sprite2D _sprite2D;
    private Node2D _model3D;
    private Camera2D _camera2D;
    private Area2D _enemyDetection;
    private Healthbar _healthBar;
    private ProgressBar _progressBar;
    private WeaponContainerUI _weaponsContainerUi;
    public ChestUI ChestUi { get; private set; }
    private CanvasLayer _hud;

    public float MaxHealth { get; set; }
    public float Speed { get; set; }

    private float _health;
    public float Health
    {
        get => _health;
        set => _SetHealth(value);
    }

    private float _points = 0.0f;
    public float Points
    {
        get => _points;
        set => _SetPoints(value);
    }

    public int Strength  { get; set; } = 0;
    public int Wisdom    { get; set; } = 0;
    public int Dexterity { get; set; } = 0;
    public int Defense   { get; set; } = 0;
    public int Vitality  { get; set; } = 0;

    public Weapon EquippedWeapon      { get; set; } = null;
    public Weapon EquippedAbility     { get; set; } = null;
    public object EquippedArmor       { get; set; } = null;
    public List<Weapon> EquippedAccessories { get; } = new();

    public List<ResItem> Inventory   { get; } = new();
    public List<ResItem> Consumables { get; } = new();
    public int InventoryMax   { get; set; } = 6;
    public int ConsumablesMax { get; set; } = 2;

    public float ExtraExp    { get; set; } = 1.0f;
    public float ExtraDamage { get; set; } = 1.0f;
    public float ExtraSpeed  { get; set; } = 1.0f;

    public static Player Local { get; private set; }
    public bool IsLocalPlayer { get; set; } = false;

    private Vector2? _lastSentMoveInput = null;

    public InventoryUI InventoryUi { get; private set; } = null;
/*
```
*/

/*
# `_Set(property, value)`
This is kinda obvious
```c# */
    public override bool _Set(StringName property, Variant value)
    {
        if (property.ToString() == "character_data")
        {
            CharacterData = value.As<ResPlayer>();
            return true;
        }
        return base._Set(property, value);
    }
/*
```
*/

/*
# `_Ready()`
This is kinda obvious
```c# */
    public override async void _Ready()
    {
        _sprite2D         = GetNode<Sprite2D>("Sprite2D");
        _model3D          = GetNode<Node2D>("Model3D");
        _camera2D         = GetNode<Camera2D>("Camera2D");
        _camera2D.Enabled = false; // Prevent camera snap for non-authority peers; _InitPlayer() re-enables for local player.
        _enemyDetection   = GetNode<Area2D>("EnemyDetection");
        _healthBar        = GetNode<Healthbar>("HealthBar");
        _progressBar      = GetNode<ProgressBar>("%ProgressBar");
        _weaponsContainerUi = GetNode<WeaponContainerUI>("%WeaponsContainerUI");
        ChestUi             = GetNode<ChestUI>("%ChestUI");
        _hud                = GetNode<CanvasLayer>("HUD");

        if (IsLocalPlayer)
            Local = this;

        Global.Instance.Players[1] = this;

        _InitPlayer();
    }
/*
```
*/

/*
# `_InitPlayer()`
This is kinda obvious
```c# */
    private void _InitPlayer()
    {
        if (CharacterData == null)
        {
            GD.PushError($"Player {Name}: CharacterData export is null — check player.tscn character_data field.");
            return;
        }
        GD.Print("Model3D/SubViewportContainer/SubViewport/WorldEnvironment/"+CharacterData.ModelName);
        GetNode<Node3D>("Model3D/SubViewportContainer/SubViewport/WorldEnvironment/"+CharacterData.ModelName).Show();
        MaxHealth = CharacterData.MaxHealth;
        Speed     = CharacterData.MaxSpeed;
        Health    = MaxHealth;

        bool isLocal = IsLocalPlayer;
        _camera2D.Enabled               = isLocal;
        _healthBar.Visible              = isLocal;
        _progressBar.Visible            = isLocal;
        _weaponsContainerUi.Visible     = isLocal;

        var startWpn = _InstantiateWeaponNode(CharacterData.ResWeapon, isLocal);
        startWpn.IsReplicated = true; // Created on all peers via _InitPlayer, safe to broadcast _RpcFire.

        if (isLocal)
        {
            _AssignToSlot(startWpn, CharacterData.ResWeapon.ItemTypeValue);
            _weaponsContainerUi.UpdateEquipment(this);

            InventoryUi = new InventoryUI();
            _hud.AddChild(InventoryUi);

            var mgr = SpacetimeDBManager.Instance;
            if (mgr != null)
            {
                if (mgr.IsStdbConnected)
                    mgr.Conn!.Db.PlayerInventory.OnUpdate += _OnStdbInventoryUpdate;
                else
                    mgr.ConnectedToServer += _ => mgr.Conn!.Db.PlayerInventory.OnUpdate += _OnStdbInventoryUpdate;
            }
        }
    }
/*
```
*/

/*
# `_PhysicsProcess(delta)`
This is kinda obvious
```c# */
    public override void _PhysicsProcess(double delta)
    {
        if (!IsLocalPlayer)
            return;

        _HandleMovement();
        MoveAndSlide();
    }
/*
```
*/

/*
# `_UnhandledInput(@event)`
This is kinda obvious
```c# */
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsLocalPlayer)
            return;
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.I && InventoryUi != null)
            {
                if (InventoryUi.Visible)
                    InventoryUi.Close();
                else
                    InventoryUi.Open(this);
            }
        }
    }
/*
```
*/

    private void _HandleMovement()
    {
        var movementInput = Input.GetVector("Left", "Right", "Up", "Down").Normalized();
        Velocity = movementInput * (Speed * ExtraSpeed);

        if (_lastSentMoveInput == null || movementInput != _lastSentMoveInput.Value)
        {
            _lastSentMoveInput = movementInput;
            var conn = SpacetimeDBManager.Instance.Conn;
            // GD.Print($"[Player] ReportMovement({movementInput.X:F2},{movementInput.Y:F2}) — conn:{(conn == null ? "null" : conn.IsActive ? "active" : "inactive")}");
            conn?.Reducers.ReportMovement(
                GlobalPosition.X, GlobalPosition.Y,
                (sbyte)Mathf.Sign(movementInput.X),
                (sbyte)Mathf.Sign(movementInput.Y),
                (ushort)(Speed * ExtraSpeed));
        }

        if (Velocity.X > 0)
            _model3D.Scale = new Vector2(1, _model3D.Scale.Y);
        else if (Velocity.X < 0)
            _model3D.Scale = new Vector2(-1, _model3D.Scale.Y);
    }
/*
```
*/

    // TODO: server-authoritative damage delivery (BulletHitCheck reducer, Task 6)
    public void Hurt(float dmg)
    {
        Health -= dmg;

        if (Health <= 0 && IsLocalPlayer)
            GetTree().CallDeferred("change_scene_to_file", "res://VS-Like/Maps/Woods/woods_map.tscn");
    }
/*
```
*/

/*
# `_SetHealth(newHealth)`
This is kinda obvious
```c# */
    private void _SetHealth(float newHealth)
    {
        _health = Mathf.Clamp(newHealth, 0, MaxHealth);
        if (_healthBar != null && IsLocalPlayer)
            _healthBar.Value = _health;
    }
/*
```
*/

/*
# `_SetPoints(newPoints)`
This is kinda obvious
```c# */
    private void _SetPoints(float newPoints)
    {
        if (!IsLocalPlayer)
            return;

        if (newPoints >= _progressBar.MaxValue)
        {
            _points = 0;
            _progressBar.MaxValue += 100;
            _OnLevelUp();
        }
        else
        {
            _points = newPoints;
        }

        _progressBar.Value = _points;
    }
/*
```
*/

/*
# `_OnLevelUp()`
This is kinda obvious
```c# */
    private void _OnLevelUp()
    {
        Strength  += 1;
        Dexterity += 1;
        Defense   += 1;
        Vitality  += 1;
        MaxHealth += 5.0f;
        Health = Mathf.Min(Health + 5.0f, MaxHealth);

        if (InventoryUi != null && InventoryUi.Visible)
            InventoryUi.Refresh();
    }
/*
```
*/

/*
# `GetAccessorySlotCount()`
This is kinda obvious
```c# */
    public int GetAccessorySlotCount()
    {
        if (Wisdom >= 1000) return 3;
        if (Wisdom >= 500)  return 2;
        return 1;
    }
/*
```
*/

/*
# `_InstantiateWeaponNode(resWpn, isOwner)`
This is kinda obvious
```c# */
    private Weapon _InstantiateWeaponNode(ResItem resWpn, bool isOwner)
    {
        var weapon = resWpn.WeaponScene.Instantiate<Weapon>();
        weapon.Resource       = resWpn;
        weapon.IsOwnerWeapon  = isOwner;
        if (resWpn.AttachToRotation)
            _model3D.AddChild(weapon);
        else
            AddChild(weapon);
        return weapon;
    }
/*
```
*/

/*
# `_AssignToSlot(weapon, itemType)`
This is kinda obvious
```c# */
    private void _AssignToSlot(Weapon weapon, ResItem.ItemType itemType)
    {
        switch (itemType)
        {
            case ResItem.ItemType.Weapon:    EquippedWeapon = weapon; break;
            case ResItem.ItemType.Ability:   EquippedAbility = weapon; break;
            case ResItem.ItemType.Armor:     EquippedArmor = weapon; break;
            case ResItem.ItemType.Accessory: EquippedAccessories.Add(weapon); break;
        }
    }
/*
```
*/

/*
# `_EquipItem(resWpn)`
This is kinda obvious
```c# */
    public bool EquipItem(ResItem resWpn)
    {
        if (!IsLocalPlayer)
            return false;

        switch (resWpn.ItemTypeValue)
        {
            case ResItem.ItemType.Weapon:
                if (EquippedWeapon != null)
                    _UnequipToInventory(EquippedWeapon);
                EquippedWeapon = _InstantiateWeaponNode(resWpn, true);
                _weaponsContainerUi.UpdateEquipment(this);
                return true;

            case ResItem.ItemType.Ability:
                if (EquippedAbility != null)
                    _UnequipToInventory(EquippedAbility);
                EquippedAbility = _InstantiateWeaponNode(resWpn, true);
                _weaponsContainerUi.UpdateEquipment(this);
                return true;

            case ResItem.ItemType.Armor:
                return false;

            case ResItem.ItemType.Accessory:
                if (EquippedAccessories.Count < GetAccessorySlotCount())
                {
                    var acc = _InstantiateWeaponNode(resWpn, true);
                    EquippedAccessories.Add(acc);
                    _weaponsContainerUi.UpdateEquipment(this);
                    return true;
                }
                return false;
        }
        return false;
    }
/*
```
*/

/*
# `_UnequipToInventory(weapon)`
This is kinda obvious
```c# */
    public void _UnequipToInventory(Weapon weapon)
    {
        if (weapon == null)
            return;
        if (Inventory.Count < InventoryMax)
            Inventory.Add(weapon.Resource);
        weapon.RemovePassiveEffects(this);
        weapon.QueueFree();
    }
/*
```
*/

/*
# `AddToInventory(resWpn)`
This is kinda obvious
```c# */
    public bool AddToInventory(ResItem resWpn)
    {
        if (Inventory.Count < InventoryMax)
        {
            Inventory.Add(resWpn);
            return true;
        }
        return false;
    }
/*
```
*/

/*
# `AddConsumable(resWpn)`
This is kinda obvious
```c# */
    public bool AddConsumable(ResItem resWpn)
    {
        if (Consumables.Count < ConsumablesMax)
        {
            Consumables.Add(resWpn);
            return true;
        }
        return false;
    }
/*
```
*/

    public void ApplyGiveWeapon(string resPath)
    {
        var res = GD.Load<ResItem>(resPath);
        if (res != null)
            Global.Instance.PickupItem(res);
    }
/*
```
*/

    public void ApplyRemoveWeapon(string resPath)
    {
        foreach (var slotWeapon in new[] { EquippedWeapon, EquippedAbility })
        {
            if (slotWeapon != null && slotWeapon.Resource.ResourcePath == resPath)
            {
                _RemoveEquippedSlot(slotWeapon);
                return;
            }
        }

        for (int i = 0; i < EquippedAccessories.Count; i++)
        {
            if (EquippedAccessories[i].Resource.ResourcePath == resPath)
            {
                var acc = EquippedAccessories[i];
                acc.RemovePassiveEffects(this);
                acc.QueueFree();
                EquippedAccessories.RemoveAt(i);
                _weaponsContainerUi.UpdateEquipment(this);
                return;
            }
        }

        for (int i = 0; i < Inventory.Count; i++)
        {
            if (Inventory[i].ResourcePath == resPath)
            {
                Inventory.RemoveAt(i);
                return;
            }
        }
    }
/*
```
*/

/*
# `_RemoveEquippedSlot(weapon)`
This is kinda obvious
```c# */
    private void _RemoveEquippedSlot(Weapon weapon)
    {
        weapon.RemovePassiveEffects(this);
        weapon.QueueFree();
        if (weapon == EquippedWeapon)
            EquippedWeapon = null;
        else if (weapon == EquippedAbility)
            EquippedAbility = null;
        _weaponsContainerUi.UpdateEquipment(this);
    }
/*
```
*/

    private void _OnStdbInventoryUpdate(EventContext ctx, PlayerInventory oldInv, PlayerInventory newInv)
    {
        var localId = SpacetimeDBManager.Instance.LocalIdentity;
        if (!localId.HasValue || newInv.PlayerId != localId.Value) return;

        foreach (var item in newInv.BackpackSlots)
        {
            if (!string.IsNullOrEmpty(item) && !oldInv.BackpackSlots.Contains(item))
            {
                var res = GD.Load<ResItem>(item);
                if (res != null) Global.Instance.PickupItem(res);
            }
        }
    }

    public override void _ExitTree()
    {
        if (this == Local) Local = null;
        Global.Instance.Players.Remove(1);
        var conn = SpacetimeDBManager.Instance?.Conn;
        if (conn != null)
            conn.Db.PlayerInventory.OnUpdate -= _OnStdbInventoryUpdate;
    }
/*
```
*/

}
