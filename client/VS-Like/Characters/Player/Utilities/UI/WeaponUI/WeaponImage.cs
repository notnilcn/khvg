using Godot;

public partial class WeaponImage : MarginContainer
{
    private TextureRect _weaponTexture;
    private ColorRect _level2;
    private ColorRect _level3;

    private ResItem _pendingResource;

    private ResItem _resource;
    public ResItem Resource
    {
        get => _resource;
        set => _SetResource(value);
    }

    private int _currWeaponLevel = 1;
    public int CurrWeaponLevel
    {
        get => _currWeaponLevel;
        set => _SetCurrWeaponLevel(value);
    }

    public override void _Ready()
    {
        _weaponTexture = GetNode<TextureRect>("%WeaponTexture");
        _level2        = GetNode<ColorRect>("%Level2");
        _level3        = GetNode<ColorRect>("%Level3");

        if (_pendingResource != null)
            _UpdateWeaponTexture(_pendingResource);
    }

    private void _SetResource(ResItem newResource)
    {
        _resource = newResource;
        if (!IsNodeReady())
            _pendingResource = newResource;
        else
            _UpdateWeaponTexture(newResource);
    }

    private void _UpdateWeaponTexture(ResItem res)
    {
        if (res != null && _weaponTexture != null)
            _weaponTexture.Texture = res.Sprite;
    }

    private void _SetCurrWeaponLevel(int newLevel)
    {
        _currWeaponLevel = newLevel;
        switch (_currWeaponLevel)
        {
            case 2: _level2.Color = Colors.White; break;
            case 3: _level3.Color = Colors.White; break;
        }
    }
}
