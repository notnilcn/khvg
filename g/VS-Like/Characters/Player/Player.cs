using Godot;
using System.Collections.Generic;

public partial class Player : CharacterBody2D
{
    [Export] public ResPlayer Resource { get; set; }

    private Sprite2D _sprite2D;
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

    public InventoryUI InventoryUi { get; private set; } = null;

    public override async void _Ready()
    {
        _sprite2D         = GetNode<Sprite2D>("Sprite2D");
        _camera2D         = GetNode<Camera2D>("Camera2D");
        _enemyDetection   = GetNode<Area2D>("EnemyDetection");
        _healthBar        = GetNode<Healthbar>("HealthBar");
        _progressBar      = GetNode<ProgressBar>("%ProgressBar");
        _weaponsContainerUi = GetNode<WeaponContainerUI>("%WeaponsContainerUI");
        ChestUi           = GetNode<ChestUI>("%ChestUI");
        _hud              = GetNode<CanvasLayer>("HUD");

        int myPeerId = GetMultiplayerAuthority();
        Global.Instance.Players[myPeerId] = this;

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _InitPlayer();
    }

    private void _InitPlayer()
    {
        _sprite2D.Texture = Resource.Sprite;
        MaxHealth = Resource.MaxHealth;
        Speed     = Resource.MaxSpeed;
        Health    = MaxHealth;

        bool isLocal = IsMultiplayerAuthority();
        _camera2D.Enabled               = isLocal;
        _healthBar.Visible              = isLocal;
        _progressBar.Visible            = isLocal;
        _weaponsContainerUi.Visible     = isLocal;

        var startWpn = _InstantiateWeaponNode(Resource.ResWeapon, isLocal);

        if (isLocal)
        {
            _AssignToSlot(startWpn, Resource.ResWeapon.ItemTypeValue);
            _weaponsContainerUi.UpdateEquipment(this);

            InventoryUi = new InventoryUI();
            _hud.AddChild(InventoryUi);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsMultiplayerAuthority())
            return;

        _HandleMovement();
        MoveAndSlide();
        Rpc(nameof(_SyncState), GlobalPosition, _sprite2D.Scale.X);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsMultiplayerAuthority())
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

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
    private void _SyncState(Vector2 pos, float scaleX)
    {
        if (Multiplayer.GetRemoteSenderId() != GetMultiplayerAuthority())
            return;
        GlobalPosition   = pos;
        _sprite2D.Scale  = new Vector2(scaleX, _sprite2D.Scale.Y);
    }

    private void _HandleMovement()
    {
        var movementInput = Input.GetVector("Left", "Right", "Up", "Down").Normalized();
        Velocity = movementInput * (Speed * ExtraSpeed);

        if (Velocity.X > 0)
            _sprite2D.Scale = new Vector2(1, _sprite2D.Scale.Y);
        else if (Velocity.X < 0)
            _sprite2D.Scale = new Vector2(-1, _sprite2D.Scale.Y);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void Hurt(float dmg)
    {
        int sender = Multiplayer.GetRemoteSenderId();
        if (sender != 0 && sender != 1)
            return;

        Health -= dmg;

        if (Health <= 0 && IsMultiplayerAuthority())
            GetTree().CallDeferred("change_scene_to_file", "res://VS-Like/Maps/Woods/woods_map.tscn");
    }

    private void _SetHealth(float newHealth)
    {
        _health = Mathf.Clamp(newHealth, 0, MaxHealth);
        if (_healthBar != null && IsMultiplayerAuthority())
            _healthBar.Value = _health;
    }

    private void _SetPoints(float newPoints)
    {
        if (!IsMultiplayerAuthority())
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

    public int GetAccessorySlotCount()
    {
        if (Wisdom >= 1000) return 3;
        if (Wisdom >= 500)  return 2;
        return 1;
    }

    private Weapon _InstantiateWeaponNode(ResItem resWpn, bool isOwner)
    {
        var weapon = resWpn.WeaponScene.Instantiate<Weapon>();
        weapon.Resource       = resWpn;
        weapon.IsOwnerWeapon  = isOwner;
        if (resWpn.AttachToRotation)
            _sprite2D.AddChild(weapon);
        else
            AddChild(weapon);
        return weapon;
    }

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

    public bool EquipItem(ResItem resWpn)
    {
        if (!IsMultiplayerAuthority())
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

    public void _UnequipToInventory(Weapon weapon)
    {
        if (weapon == null)
            return;
        if (Inventory.Count < InventoryMax)
            Inventory.Add(weapon.Resource);
        weapon.RemovePassiveEffects(this);
        weapon.QueueFree();
    }

    public bool AddToInventory(ResItem resWpn)
    {
        if (Inventory.Count < InventoryMax)
        {
            Inventory.Add(resWpn);
            return true;
        }
        return false;
    }

    public bool AddConsumable(ResItem resWpn)
    {
        if (Consumables.Count < ConsumablesMax)
        {
            Consumables.Add(resWpn);
            return true;
        }
        return false;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RpcGiveWeapon(string resPath)
    {
        if (Multiplayer.GetRemoteSenderId() is not (0 or 1))
            return;
        if (!IsMultiplayerAuthority())
            return;
        var res = GD.Load<ResItem>(resPath);
        if (res != null)
            Global.Instance.PickupItem(res);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void RpcRemoveWeapon(string resPath)
    {
        if (Multiplayer.GetRemoteSenderId() is not (0 or 1))
            return;
        if (!IsMultiplayerAuthority())
            return;

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

    public override void _ExitTree()
    {
        int myPeerId = GetMultiplayerAuthority();
        Global.Instance.Players.Remove(myPeerId);
    }
}
