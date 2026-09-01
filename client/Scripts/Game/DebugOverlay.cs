#nullable enable
using Godot;
using System.Linq;

public partial class DebugOverlay : CanvasLayer
{
	private Label label = null!;
	private double accumulator;
	private const double UpdateInterval = 0.25;

	// Same fallback as ElevationMapComponent before the first MapConfig row arrives.
	private const float DefaultHexOuterRadius = 96.0f;

	public override void _Ready()
	{
		label = GetNode<Label>("Label");
	}

	public override void _Input(InputEvent @event)
	{
		if (Input.IsActionJustPressed("toggle_debug_overlay"))
			Visible = !Visible;
			// GetNode<Node2D>("../HexGridOverlayComponent").Visible = !GetNode<Node2D>("../HexGridOverlayComponent").Visible;
	}

	public override void _Process(double delta)
	{
		if (!Visible) return;

		accumulator += delta;
		if (accumulator < UpdateInterval) return;
		accumulator = 0.0;

		var fps = Performance.GetMonitor(Performance.Monitor.TimeFps);
		var frameTimeMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
		var memoryMb = Performance.GetMonitor(Performance.Monitor.MemoryStatic) / (1024.0 * 1024.0);
		var objectCount = Performance.GetMonitor(Performance.Monitor.ObjectCount);
		var nodeCount = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
		var drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
		var enemyCount = GameManager.EnemyCount;
		var conn = GameManager.Conn;
		var connected = conn?.IsActive == true;

		label.Text =
			$"FPS: {fps:0} ({frameTimeMs:0.00} ms)\n" +
			$"Memory: {memoryMb:0.0} MB\n" +
			$"Objects: {objectCount:0}  Nodes: {nodeCount:0}  Draw Calls: {drawCalls:0}\n" +
			$"Enemies: {enemyCount}\n" +
			$"Connection: {(connected ? "connected" : "disconnected")} ({GameManager.Username})\n" +
			PlayerInfo(conn);

		// One-shot read of the already-subscribed MapConfig row (RemotePlayer's profile
		// lookup pattern) — the chunk radius is needed to resolve the player's chunk.
		static string PlayerInfo(SpacetimeDB.Types.DbConnection? conn)
		{
			var player = LocalPlayer.Local;
			if (player == null || !IsInstanceValid(player))
				return "Player: not in world";

			var pos = player.GlobalPosition;
			var elevationMap = GameManager.ElevationMap;
			var outerRadius = elevationMap?.OuterRadius ?? DefaultHexOuterRadius;
			var hex = HexMath.WorldToHex(pos.X, pos.Y, outerRadius);
			var elevation = elevationMap?.GetElevation(hex.X, hex.Y) ?? 0;

			var chunk = "n/a (no MapConfig)";
			var cfg = conn?.Db.MapConfig.Iter().FirstOrDefault();
			if (cfg != null)
			{
				var c = HexMath.ToLowerRes(hex.X, hex.Y, cfg.ChunkHexRadius);
				chunk = $"({c.X}, {c.Y}) #{HexMath.SpiralIndex(c.X, c.Y)}";
			}

			return
				$"Position: ({pos.X:0.0}, {pos.Y:0.0})  Velocity: ({player.Velocity.X:0.0}, {player.Velocity.Y:0.0})\n" +
				$"Hex: ({hex.X}, {hex.Y})  Elevation: {elevation}\n" +
				$"Chunk: {chunk}";
		}
	}
}
