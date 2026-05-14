using Godot;

public partial class Healthbar : ProgressBar
{
    private void _OnValueChanged(float value)
    {
        Visible = true;
        GetNode<Timer>("Timer").Start();
    }

    private void _OnTimerTimeout()
    {
        Visible = false;
    }
}
