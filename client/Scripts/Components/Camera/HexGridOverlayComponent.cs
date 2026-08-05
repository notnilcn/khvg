#nullable enable
using Godot;
using SpacetimeDB.Types;
using System;

/// <summary>
/// Hex grid debug overlay (2D), child of Main. Hidden by default. MapConfig rows
/// arrive via a child TableBinderComponent (declared in hex_grid_overlay_component.tscn,
/// signals wired in the editor); draws chunk/hex lines around the camera.
/// </summary>
public partial class HexGridOverlayComponent : Node2DComponent
{
	private const float Sqrt3 = 1.7320508075688772f;
	private const int DefaultChunkHexRadius = 5;
	private const float DefaultHexOuterRadius = 32.0f;

	private static readonly Color LineColor = new(0f, 0f, 0f, 0.3f);
	private const float LineWidth = 1f;
	private static readonly Color LabelColor = new(0f, 0f, 0f, 0.75f);
	private const int FontSize = 8;

	// 3-coloring: (cq - cr) mod 3 guarantees no two adjacent chunks share a colour.
	private static readonly Color[] ChunkColors = {
		new(0f, 1f, 1f, 0.5f),  // Cyan
		new(1f, 0f, 1f, 0.5f),  // Magenta
		new(1f, 1f, 0f, 0.5f),  // Yellow
	};

	private float _outerRadius = DefaultHexOuterRadius;
	private int _chunkRadius = DefaultChunkHexRadius;
	private int _chunkCols = 0;   // 0 = unknown (no MapConfig yet)
	private int _chunkRows = 0;

	private static readonly Color OutOfBoundsColor = new(0.15f, 0.15f, 0.15f, 0.45f);
	private static readonly Color SeamLineColor = new(1f, 0.2f, 0.2f, 0.85f);
	private const float SeamLineWidth = 2f;

	private Vector2 _camPos = Vector2.Zero;
	private Vector2 _camZoom = Vector2.One;
	private Font? _font;

	private TableBinderComponent _mapConfigBinder = null!;

	public override void _Ready()
	{
		base._Ready();
		_mapConfigBinder = GetNode<TableBinderComponent>("MapConfigBinder");
	}

	protected override void OnRegistered()
	{
		ZIndex = 0;
		_font = ThemeDB.FallbackFont;
	}

	public override void _Process(double delta)
	{
		var cam = GetViewport().GetCamera2D();
		if (cam == null) return;
		if (cam.GlobalPosition != _camPos || cam.Zoom != _camZoom)
		{
			_camPos = cam.GlobalPosition;
			_camZoom = cam.Zoom;
			QueueRedraw();
		}
	}

	// --- TableBinderComponent signal handlers (wired in hex_grid_overlay_component.tscn) ---

	private void OnMapConfigRow()
	{
		ApplyMapConfig((MapConfig)_mapConfigBinder.LastRow!);
	}

	private void ApplyMapConfig(MapConfig cfg)
	{
		_chunkRadius = cfg.ChunkHexRadius;
		_outerRadius = cfg.HexOuterRadius;
		_chunkCols = (int)cfg.ChunkCols;
		_chunkRows = (int)cfg.ChunkRows;
		QueueRedraw();
	}

	public override void _Draw()
	{
		var cam = GetViewport().GetCamera2D();
		if (cam == null) return;

		var half = GetViewportRect().Size / cam.Zoom;// * 0.5f;
		var worldMin = _camPos - half;
		var worldMax = _camPos + half;

		var hMin = WorldToHex(worldMin.X, worldMin.Y);
		var hMax = WorldToHex(worldMax.X, worldMax.Y);
		int qMin = Mathf.Min(hMin.X, hMax.X) - 1;
		int qMax = Mathf.Max(hMin.X, hMax.X) + 1;
		int rMin = Mathf.Min(hMin.Y, hMax.Y) - 1;
		int rMax = Mathf.Max(hMin.Y, hMax.Y) + 1;

		for (int r = rMin; r <= rMax; r++)
			for (int q = qMin; q <= qMax; q++)
				DrawHex(q, r);

		// DrawHudText(cam);
	}

	private static readonly Color HudWhite = new(1f, 1f, 1f, 1f);
	private static readonly Color HudBlack = new(0f, 0f, 0f, 1f);
	private const int HudFontSize = 14;

	private void DrawHudText(Camera2D cam)
	{
		if (_font == null) return;
		var player = LocalPlayer.Local;
		if (player == null) return;

		var pos = player.GlobalPosition;
		var fineHex = WorldToHex(pos.X, pos.Y);
		var chunk = ToLowerRes(fineHex.X, fineHex.Y, _chunkRadius);
		long cidx = HexSpiralIndex(chunk.X, chunk.Y);

		var center = HexToWorld(fineHex.X, fineHex.Y);

		var verts = new Vector2[6];
		for (int i = 0; i < 6; i++)
		{
			float angle = Mathf.DegToRad(30f + 60f * i);
			verts[i] = center + new Vector2(
				_outerRadius * Mathf.Cos(angle),
				_outerRadius * Mathf.Sin(angle));
		}

		string[] lines = {
			$"pos  ({pos.X:F1}, {pos.Y:F1})",
			$"hex  ({fineHex.X}, {fineHex.Y})",
			$"chunk ({chunk.X}, {chunk.Y})  idx {cidx}",
			$"hex2world ({center.X:F1}, {center.Y:F1})",
			$"verts[0] ({verts[0].X:F1}, {verts[0].Y:F1})",
			$"verts[1] ({verts[1].X:F1}, {verts[1].Y:F1})",
			$"verts[2] ({verts[2].X:F1}, {verts[2].Y:F1})",
			$"verts[3] ({verts[3].X:F1}, {verts[3].Y:F1})",
			$"verts[4] ({verts[4].X:F1}, {verts[4].Y:F1})",
			$"verts[5] ({verts[5].X:F1}, {verts[5].Y:F1})"
		};

		float invZoom = 1f / cam.Zoom.X;
		float lineH = (HudFontSize + 3f) * invZoom;
		float margin = 8f * invZoom;
		float px = invZoom;

		var half = GetViewportRect().Size / cam.Zoom * 0.5f;
		var topRight = _camPos + new Vector2(half.X, -half.Y);

		for (int i = 0; i < lines.Length; i++)
		{
			float wWorld = _font.GetStringSize(lines[i], HorizontalAlignment.Left, -1, HudFontSize).X * invZoom;
			var origin = new Vector2(
				topRight.X - wWorld - margin,
				topRight.Y + margin + HudFontSize * invZoom + lineH * i);

			for (int dy = -1; dy <= 1; dy++)
				for (int dx = -1; dx <= 1; dx++)
				{
					if (dx == 0 && dy == 0) continue;
					DrawString(_font, origin + new Vector2(dx * px, dy * px), lines[i],
						HorizontalAlignment.Left, -1, HudFontSize, HudBlack);
				}
			DrawString(_font, origin, lines[i], HorizontalAlignment.Left, -1, HudFontSize, HudWhite);
		}
	}

	private void DrawHex(int q, int r)
	{
		var center = HexToWorld(q, r);
		var chunk = ToLowerRes(q, r, _chunkRadius);

		var verts = new Vector2[6];
		for (int i = 0; i < 6; i++)
		{
			float angle = Mathf.DegToRad(30f + 60f * i);
			verts[i] = center + new Vector2(
				_outerRadius * Mathf.Cos(angle),
				_outerRadius * Mathf.Sin(angle));
		}

		bool inBounds = _chunkCols <= 0 ||
			(chunk.X >= 0 && chunk.X < _chunkCols && chunk.Y >= 0 && chunk.Y < _chunkRows);
		int colorIdx = ((chunk.X - chunk.Y) % 3 + 3) % 3;
		DrawPolygon(verts, [inBounds ? ChunkColors[colorIdx] : OutOfBoundsColor]);

		// var testverts = new Vector2[4];
		// testverts[0] = new Vector2(0,0);
		// testverts[1] = new Vector2(0,64);
		// testverts[2] = new Vector2(64,64);
		// testverts[3] = new Vector2(64,0);
		// DrawPolygon(testverts, [OutOfBoundsColor]);

		var outline = new Vector2[7];
		Array.Copy(verts, outline, 6);
		outline[6] = verts[0];
		DrawPolyline(outline, LineColor, LineWidth, true);

		if (_font == null) return;

		long cidx = HexSpiralIndex(chunk.X, chunk.Y);
		string hexStr = $"{q},{r}";
		string chunkStr = $"{chunk.X},{chunk.Y}";
		string cidxStr = cidx.ToString();

		float line = FontSize + 1f;
		float hw = _font.GetStringSize(hexStr, HorizontalAlignment.Left, -1, FontSize).X;
		float cw = _font.GetStringSize(chunkStr, HorizontalAlignment.Left, -1, FontSize).X;
		float iw = _font.GetStringSize(cidxStr, HorizontalAlignment.Left, -1, FontSize).X;

		DrawString(_font, new Vector2(center.X - hw * 0.5f, center.Y - line), hexStr, HorizontalAlignment.Left, -1, FontSize, LabelColor);
		DrawString(_font, new Vector2(center.X - cw * 0.5f, center.Y + 2f), chunkStr, HorizontalAlignment.Left, -1, FontSize, LabelColor);
		DrawString(_font, new Vector2(center.X - iw * 0.5f, center.Y + line + 3f), cidxStr, HorizontalAlignment.Left, -1, FontSize, LabelColor);
	}

	// ── Hex math (mirrors server methods.rs / hex_grid_overlay.gd) ──────────────

	private Vector2 HexToWorld(int q, int r)
	{
		float R = _outerRadius;
		return new Vector2(R * (Sqrt3 * q + Sqrt3 * 0.5f * r), R * 1.5f * r);
	}

	private Vector2I WorldToHex(float wx, float wy)
	{
		float R = _outerRadius;
		float q = (wx * Sqrt3 / 3f - wy / 3f) / R;
		float r = wy * 2f / 3f / R;
		float s = -q - r;
		float rq = MathF.Round(q);
		float rr = MathF.Round(r);
		float rs = MathF.Round(s);
		if (MathF.Abs(rq - q) > MathF.Abs(rr - r) && MathF.Abs(rq - q) > MathF.Abs(rs - s))
			rq = -rr - rs;
		else if (MathF.Abs(rr - r) > MathF.Abs(rs - s))
			rr = -rq - rs;
		return new Vector2I((int)rq, (int)rr);
	}

	// Bijection Z² → ℕ using hex spiral order. Mirrors spiral_chunk_index in methods.rs.
	private static long HexSpiralIndex(int cq, int cr)
	{
		int x = cq, z = cr, y = -cq - cr;
		int rho = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
		if (rho == 0) return 0;
		long b = 3L * rho * (rho - 1) + 1;
		long arm, step;
		if (z == -rho && x > 0) { arm = 0; step = y; }
		else if (y == rho && x > -rho) { arm = 1; step = -x; }
		else if (x == -rho && z < rho) { arm = 2; step = z; }
		else if (z == rho && x < 0) { arm = 3; step = x + rho; }
		else if (y == -rho && x >= 0 && x < rho) { arm = 4; step = x; }
		else { arm = 5; step = y + rho; }
		return b + arm * rho + step;
	}

	// Port of hexx Hex::to_lower_res. Mirrors _to_lower_res in hex_grid_overlay.gd.
	private static Vector2I ToLowerRes(int q, int r, int radius)
	{
		int s = -q - r;
		float area = 3f * radius * (radius + 1) + 1f;
		int shift = 3 * radius + 2;
		int a = Mathf.FloorToInt((r + shift * q) / area);
		int b = Mathf.FloorToInt((s + shift * r) / area);
		int c = Mathf.FloorToInt((q + shift * s) / area);
		return new Vector2I(
			Mathf.FloorToInt((1f + a - b) / 3f),
			Mathf.FloorToInt((1f + b - c) / 3f));
	}
}
