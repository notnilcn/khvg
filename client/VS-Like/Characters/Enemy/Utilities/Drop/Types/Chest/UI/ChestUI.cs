/*
ChestUI code.
```c# */
using Godot;

public partial class ChestUI : Control
{
    private static readonly Texture2D FoodImage =
        GD.Load<Texture2D>("res://VS-Like/Characters/Enemy/Utilities/Drop/Types/Food/sprite_food.tres");

    private HBoxContainer _itemsContainer;
    private AudioStreamPlayer _audioPlayer;
/*
```
*/

/*
# `_Ready()`
This is kinda obvious
```c# */
    public override void _Ready()
    {
        _itemsContainer = GetNode<HBoxContainer>("%ItemsContainer");
        _audioPlayer    = GetNode<AudioStreamPlayer>("AudioPlayer");
    }
/*
```
*/

/*
# `_OpenChest()`
This is kinda obvious
```c# */
    public void OpenChest()
    {
        GetTree().Paused = true;
        Visible = true;
        _audioPlayer.Play();
        _PopulateItems();
    }
/*
```
*/

/*
# `_PopulateItems()`
This is kinda obvious
```c# */
    private void _PopulateItems()
    {
        var pool = Global.Instance.GetAvailableItems();

        if (pool.Length == 0)
        {
            _ShowFoodFallback();
            return;
        }

        // Shuffle
        for (int i = pool.Length - 1; i > 0; i--)
        {
            int j = (int)(GD.Randi() % (uint)(i + 1));
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        int count = Mathf.Min(pool.Length, 3);
        for (int i = 0; i < count; i++)
            _CreateItemButton(pool[i]);
    }
/*
```
*/

/*
# `_CreateItemButton(res)`
This is kinda obvious
```c# */
    private void _CreateItemButton(ResItem res)
    {
        var btn = new Button();
        btn.CustomMinimumSize              = new Vector2(96, 112);
        btn.Icon                           = res.Sprite;
        btn.ExpandIcon                     = true;
        btn.Text                           = res.ItemName;
        btn.IconAlignment                  = HorizontalAlignment.Center;
        btn.VerticalIconAlignment          = VerticalAlignment.Top;
        btn.TooltipText                    = res.ItemDescription;
        btn.Pressed += () => _OnItemChosen(res);
        _itemsContainer.AddChild(btn);
    }
/*
```
*/

/*
# `_ShowFoodFallback()`
This is kinda obvious
```c# */
    private void _ShowFoodFallback()
    {
        var img = new TextureRect();
        img.CustomMinimumSize = new Vector2(64, 64);
        img.Texture = FoodImage;
        _itemsContainer.AddChild(img);
        Global.Instance.GetLocalPlayer().Health += 20;
    }
/*
```
*/

/*
# `_OnItemChosen(res)`
This is kinda obvious
```c# */
    private void _OnItemChosen(ResItem res)
    {
        Global.Instance.PickupItem(res);
        CloseChest();
    }
/*
```
*/

/*
# `CloseChest()`
This is kinda obvious
```c# */
    public void CloseChest()
    {
        foreach (var child in _itemsContainer.GetChildren())
            child.QueueFree();
        Visible = false;
        GetTree().Paused = false;
    }
/*
```
*/

/*
# `_OnDoneButtonPressed()`
This is kinda obvious
```c# */
    private void _OnDoneButtonPressed()
    {
        CloseChest();
    }
/*
```
*/

}
