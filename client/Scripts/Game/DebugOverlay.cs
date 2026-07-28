#nullable enable
using Godot;

public partial class DebugOverlay : CanvasLayer
{
	private Label label = null!;
	private double accumulator;
	private const double UpdateInterval = 0.25;

	public override void _Ready()
	{
		label = GetNode<Label>("Label");
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false } key && key.PhysicalKeycode == Key.P)
			Visible = !Visible;
	}

	public override void _Process(double delta)
	{
		if (!Visible) return;

		accumulator += delta;
		if (accumulator < UpdateInterval) return;
		accumulator = 0.0;

		var fps         = Performance.GetMonitor(Performance.Monitor.TimeFps);
		var frameTimeMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
		var memoryMb    = Performance.GetMonitor(Performance.Monitor.MemoryStatic) / (1024.0 * 1024.0);
		var objectCount = Performance.GetMonitor(Performance.Monitor.ObjectCount);
		var nodeCount   = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
		var drawCalls   = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
		var enemyCount  = GameManager.EnemyCount;

		label.Text =
			$"FPS: {fps:0} ({frameTimeMs:0.00} ms)\n" +
			$"Memory: {memoryMb:0.0} MB\n" +
			$"Objects: {objectCount:0}  Nodes: {nodeCount:0}  Draw Calls: {drawCalls:0}\n" +
			$"Enemies: {enemyCount}";
	}
}
