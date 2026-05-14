/*
# whatever
```c# */
using Godot;
using System.Collections.Generic;
using SpacetimeDB.Types;

public partial class Player : CharacterBody2D
{
    [Export] public ResPlayer CharacterTemplate { get; set; }

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
    public float CameraSpeed = 75.0f;

    private const float SpeedPerStat = 4f;
    private const int   HpPerLevel  = 5; // must match Lib.cs

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
    public int SpeedStat { get; set; } = 1;

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

    public static Player Local { get; private set; }
    public bool IsLocalPlayer { get; set; } = false;

    public Timer _FireTimer;//

    public bool IsTryingToShoot { get; set; } = false;//

    public bool AllowedToShoot { get; set; } = true;//

    public Node2D _MarkerContainer;//

    private GodotObject _BulletsData;//

    [Export] private Texture2D[] _BulletTextures;

    private Vector2? _lastSentMoveInput = null;
    private ushort   _lastSentSpeed     = 0;


    public InventoryUI InventoryUi { get; private set; } = null;

    public override bool _Set(StringName property, Variant value)
    {
        if (property.ToString() == "character_data")
        {
            CharacterTemplate = value.As<ResPlayer>();
            return true;
        }
        return base._Set(property, value);
    }
/*
```
*/

/*
```c# */
    public override void _Ready()
    {
        _sprite2D           = GetNode<Sprite2D>("Sprite2D");
        _model3D            = GetNode<Node2D>("Model3D");
        _camera2D           = GetNode<Camera2D>("Camera2D");
        _camera2D.Enabled   = false;
        _enemyDetection     = GetNode<Area2D>("EnemyDetection");
        _healthBar          = GetNode<Healthbar>("HealthBar");
        _progressBar        = GetNode<ProgressBar>("%ProgressBar");
        _weaponsContainerUi = GetNode<WeaponContainerUI>("%WeaponsContainerUI");
        ChestUi             = GetNode<ChestUI>("%ChestUI");
        _hud                = GetNode<CanvasLayer>("HUD");

        if (IsLocalPlayer)
            Local = this;

        Global.Instance.Players[1] = this;

        _InitPlayer();

        _FireTimer          = GetNode<Timer>("FireTimer");//
        _FireTimer.Timeout += _OnFireTimerTimeout;//
        _BulletsData = ClassDB.Instantiate("DirectionalBulletsData2D").AsGodotObject();//
        _BulletsData.Call("set_textures", _BulletTextures);//
        _BulletsData.Set("texture_size", new Vector2(64, 64));//
        _BulletsData.Call("set_collision_layer_from_array", new Godot.Collections.Array { 2 });//world where bullets exist. anything that wants to detect this bullet has to be on this layer//
        _BulletsData.Call("set_collision_mask_from_array", new Godot.Collections.Array { 3 });//world that the bullet tracks. anything that the bullet wants to detect has to be on this layer//
        _BulletsData.Set("all_bullet_speed_data",ClassDB.Instantiate("BulletSpeedData2D").AsGodotObject().Call("generate_random_data",3,100,200,500,650, 300, 500));//
        _BulletsData.Set("collision_shape_size",new Vector2(32,32));//

        _MarkerContainer = GetNode<Node2D>("MarkerContainer");//
    }

    private void _InitPlayer()
    {
        if (CharacterTemplate == null)
        {
            GD.PushError($"Player {Name}: CharacterTemplate export is null — check player.tscn character_data field.");
            return;
        }

        GetNode<Node3D>("Model3D/SubViewportContainer/SubViewport/WorldEnvironment/"+CharacterTemplate.ModelName).Show();
        MaxHealth = CharacterTemplate.MaxHealth;
        Health    = MaxHealth;

        bool isLocal = IsLocalPlayer;
        _camera2D.Enabled               = isLocal;
        _healthBar.Visible              = isLocal;
        _progressBar.Visible            = isLocal;
        _weaponsContainerUi.Visible     = isLocal;

        var startWpn = _InstantiateWeaponNode(CharacterTemplate.ResWeapon, isLocal);
        startWpn.IsReplicated = true; // Created on all peers via _InitPlayer, safe to broadcast _RpcFire.

        if (isLocal)
        {
            _AssignToSlot(startWpn, CharacterTemplate.ResWeapon.ItemTypeValue);
            _weaponsContainerUi.UpdateEquipment(this);

            InventoryUi = new InventoryUI();
            _hud.AddChild(InventoryUi);

            var mgr = SpacetimeDBManager.Instance;
            if (mgr != null)
            {
                if (mgr.IsStdbConnected)
                {
                    mgr.Conn!.Db.PlayerInventory.OnUpdate  += _OnStdbInventoryUpdate;
                    mgr.Conn!.Db.CharacterData.OnUpdate    += _OnStdbCharacterDataUpdate;
                }
                else
                {
                    mgr.ConnectedToServer += _ =>
                    {
                        mgr.Conn!.Db.PlayerInventory.OnUpdate  += _OnStdbInventoryUpdate;
                        mgr.Conn!.Db.CharacterData.OnUpdate    += _OnStdbCharacterDataUpdate;
                    };
                }
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsLocalPlayer)
            return;

        _HandleMovement(delta);
        MoveAndSlide();
    }

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

        if (@event.IsActionPressed("player_shoot"))//
        {
            IsTryingToShoot = true;
            GD.Print("[Player] Shoot input detected");
        }

        if (@event.IsActionReleased("player_shoot"))//
        {
            IsTryingToShoot = false;
            GD.Print("[Player] Shoot input released");
        }
    }

    public override void _Process(double delta)//
    {
        // # Shoot logic
        if (AllowedToShoot && IsTryingToShoot) // If the user has pressed the left mouse button and is allowed to shoot, then go ahead and spawn bullets
        {
            // TODO spawn bullets
            AllowedToShoot = false;
            _FireTimer.Start();
            GD.Print("Player shot");
            _Shoot();
        }
    }

    private Godot.Collections.Array<Transform2D> _GetMarkerTransforms()//
    {
        var _AllMarkers = new Godot.Collections.Array<Transform2D>();

        foreach (Node2D marker in _MarkerContainer.GetChildren())
        {
            _AllMarkers.Add(marker.GlobalTransform);
        }

        return _AllMarkers;
    }

    private void _Shoot()//
    {
        if (Global.BulletFactory == null)
        {
            GD.PrintErr("[Player] BulletFactory not registered — check woods_map.tscn has BulletFactory node.");
            return;
        }
        _BulletsData.Set("transforms", _GetMarkerTransforms());
        Global.BulletFactory.Call("spawn_directional_bullets", _BulletsData);
        // This is where you would put the logic to spawn bullets, e.g. by calling ServerSpawnLinear or ServerSpawnSine with the appropriate parameters based on the equipped weapon and player stats.
        // For example:
        // ServerSpawnLinear(GlobalPosition, new Vector2(1, 0).Rotated(Mathf.DegToRad(RotationDegrees)), 300, 10);
        GD.Print("Player shoot logic executed");
    }

    private void _HandleMovement(double delta)
    {
        var movementInput = Input.GetVector("Left", "Right", "Up", "Down").Normalized().Rotated(Mathf.DegToRad(RotationDegrees));
        var cameraInput = Input.GetAxis("Camera_Left", "Camera_Right");
        var effectiveSpeed = SpeedStat * SpeedPerStat;
        Velocity = movementInput * effectiveSpeed;

        var reportedSpeed = (ushort)Mathf.Min(effectiveSpeed, 65535f);
        if (_lastSentMoveInput == null || movementInput != _lastSentMoveInput.Value || reportedSpeed != _lastSentSpeed)
        {
            _lastSentMoveInput = movementInput;
            _lastSentSpeed     = reportedSpeed;
            var conn = SpacetimeDBManager.Instance.Conn;
            // GD.Print($"[Player] ReportMovement({movementInput.X:F2},{movementInput.Y:F2}) — conn:{(conn == null ? "null" : conn.IsActive ? "active" : "inactive")}");
            conn?.Reducers.ReportMovement(
                GlobalPosition.X, GlobalPosition.Y,
                (sbyte)Mathf.Sign(movementInput.X),
                (sbyte)Mathf.Sign(movementInput.Y),
                reportedSpeed);
        }

        if (cameraInput < 0)
            RotationDegrees -= (float)(CameraSpeed*delta);
        if (cameraInput > 0)
            RotationDegrees += (float)(CameraSpeed*delta);

        if (Velocity.X > 0)
            _model3D.Scale = new Vector2(1, _model3D.Scale.Y);
        else if (Velocity.X < 0)
            _model3D.Scale = new Vector2(-1, _model3D.Scale.Y);
    }

    // TODO: server-authoritative damage delivery (BulletHitCheck reducer, Task 6)
    public void Hurt(float dmg)
    {
        Health -= dmg;

        if (Health <= 0 && IsLocalPlayer)
            GetTree().CallDeferred("change_scene_to_file", "res://VS-Like/Maps/Woods/woods_map.tscn");
    }

    private void _SetHealth(float newHealth)
    {
        _health = Mathf.Clamp(newHealth, 0, MaxHealth);
        if (_healthBar != null && IsLocalPlayer)
            _healthBar.Value = _health;
    }

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

    private void _OnLevelUp()
    {
        // Stats and HP are computed and applied server-side by GainXp.
        // _OnStdbCharacterDataUpdate applies them here once the server responds.
        if (InventoryUi != null && InventoryUi.Visible)
            InventoryUi.Refresh();
    }

    // Stats are server-authoritative: set by _OnStdbCharacterDataUpdate from CharacterData.
    // This method only clamps Health to the current MaxHealth (used after MaxHealth changes).
    public void CollectStats()
    {
        Health = Mathf.Min(Health, MaxHealth);
    }

    private void _SyncInventoryToServer()
    {
        var conn = SpacetimeDBManager.Instance?.Conn;
        if (conn == null) return;
        conn.Reducers.UpdateInventory(
            EquippedWeapon?.Resource.ResourcePath,
            EquippedAbility?.Resource.ResourcePath,
            (EquippedArmor as Weapon)?.Resource.ResourcePath,
            EquippedAccessories.ConvertAll(a => a.Resource.ResourcePath),
            Inventory.ConvertAll(i => i.ResourcePath),
            Consumables.ConvertAll(c => c.ResourcePath));
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
            _model3D.AddChild(weapon);
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
        if (!IsLocalPlayer)
            return false;

        switch (resWpn.ItemTypeValue)
        {
            case ResItem.ItemType.Weapon:
                if (EquippedWeapon != null)
                    _UnequipToInventory(EquippedWeapon);
                EquippedWeapon = _InstantiateWeaponNode(resWpn, true);
                _weaponsContainerUi.UpdateEquipment(this);
                _SyncInventoryToServer();
                return true;

            case ResItem.ItemType.Ability:
                if (EquippedAbility != null)
                    _UnequipToInventory(EquippedAbility);
                EquippedAbility = _InstantiateWeaponNode(resWpn, true);
                _weaponsContainerUi.UpdateEquipment(this);
                _SyncInventoryToServer();
                return true;

            case ResItem.ItemType.Armor:
                return false;

            case ResItem.ItemType.Accessory:
                if (EquippedAccessories.Count < GetAccessorySlotCount())
                {
                    var acc = _InstantiateWeaponNode(resWpn, true);
                    EquippedAccessories.Add(acc);
                    _weaponsContainerUi.UpdateEquipment(this);
                    _SyncInventoryToServer();
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

    public void ApplyGiveWeapon(string resPath)
    {
        var res = GD.Load<ResItem>(resPath);
        if (res != null)
            Global.Instance.PickupItem(res);
    }

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
                _SyncInventoryToServer();
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
        _SyncInventoryToServer();
    }

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

    private void _OnStdbCharacterDataUpdate(EventContext ctx, CharacterData oldData, CharacterData newData)
    {
        var localId = SpacetimeDBManager.Instance.LocalIdentity;
        if (!localId.HasValue || newData.PlayerId != localId.Value) return;

        Strength  = newData.Stats.Strength;
        Wisdom    = newData.Stats.Wisdom;
        Dexterity = newData.Stats.Dexterity;
        Defense   = newData.Stats.Defense;
        Vitality  = newData.Stats.Vitality;
        SpeedStat = newData.Stats.Speed;
        MaxHealth = newData.MaxHp;
        Health    = Mathf.Min(Health, MaxHealth);

        if (newData.Level > oldData.Level)
            Health = Mathf.Min(Health + (newData.Level - oldData.Level) * HpPerLevel, MaxHealth);

        if (InventoryUi != null && InventoryUi.Visible)
            InventoryUi.Refresh();
    }

    public void _OnFireTimerTimeout()
    {
        AllowedToShoot = true; // allow the player to shoot
        GD.Print("Player can shoot again");
    }
    
    public void _ChangeFireTimerCooldown(float newWaitTime)
    {
        _FireTimer.Stop();
        AllowedToShoot = true;

        _FireTimer.WaitTime = newWaitTime;
        if (_FireTimer.IsStopped())
        {
            _FireTimer.Start();
        }
        
    }

    public override void _ExitTree()
    {
        if (this == Local) Local = null;
        Global.Instance.Players.Remove(1);
        var conn = SpacetimeDBManager.Instance?.Conn;
        if (conn != null)
        {
            conn.Db.PlayerInventory.OnUpdate -= _OnStdbInventoryUpdate;
            conn.Db.CharacterData.OnUpdate   -= _OnStdbCharacterDataUpdate;
        }
    }
/*
```
*/
}