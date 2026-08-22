#nullable enable
using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

/// <summary>
/// Renders Spell ability casts (P5) as code-created Polygon2D flashes, one per
/// AbilityVisualEvent row: Circle = ring at the target, Line = rectangle from the cast
/// origin to the far end, Cone = wedge from the origin, Meteor = big circle plus a few
/// shrapnel streaks (VISUAL-ONLY deviation — the damage is the single circle), Converge =
/// circle at the target plus streaks converging inward (VISUAL-ONLY deviation). Every
/// flash fades out over the event's duration and frees itself; damage is entirely
/// server-side — this component draws, nothing more.
///
/// The caster renders OPTIMISTICALLY from LocalPlayerInventoryComponent.TryActivateAbility
/// (and the charge-release path) via the same Render method, so the echo handler skips its
/// own cast_by exactly like BulletControllerComponent.
///
/// Fed by the child AbilityVisualEventBinder, which MUST keep ReplayExistingRows = FALSE:
/// AbilityVisualEvent is an append-only event table with no last-seen-id tracking, so one
/// flipped flag would re-fire every spell visual in the table's history on scene entry
/// (the BulletControlEvent rule — the opposite of the persistent GroundZone table).
/// </summary>
public partial class SpellVisualComponent : Component
{
    /// Mirror of SPELL_VISUAL_SECONDS in main/global.rs (the server stamps it as the
    /// event's duration; the optimistic path needs it before the echo) — change together.
    public const float DefaultDuration = 0.5f;

    private const int CirclePoints = 32;
    private const float RingThickness = 6f;

    private static readonly Color CircleColor = new(0.5f, 0.9f, 1f, 0.8f);    // arcane ice
    private static readonly Color LineColor = new(0.6f, 0.95f, 1f, 0.7f);     // beam
    private static readonly Color ConeColor = new(1f, 0.7f, 0.3f, 0.5f);      // spray
    private static readonly Color MeteorColor = new(1f, 0.45f, 0.15f, 0.55f); // impact
    private static readonly Color ConvergeColor = new(0.8f, 0.5f, 1f, 0.7f);  // collapse

    private TableBinderComponent visualBinder = null!;

    public override void _Ready()
    {
        base._Ready();
        visualBinder = GetNode<TableBinderComponent>("AbilityVisualEventBinder");
    }

    /// AbilityVisualEventBinder RowInserted handler (wired in game.tscn). Replays another
    /// player's cast; the caster's own echo is skipped (already rendered optimistically).
    private void OnAbilityVisualEventRow()
    {
        var row = (AbilityVisualEvent)visualBinder.LastRow!;
        if (GameManager.IsLocal(row.CastBy)) return;
        Render(row.Kind, new Vector2(row.X, row.Y), new Vector2(row.TargetX, row.TargetY), row.Radius, row.Duration);
    }

    /// Draws one spell flash: origin is the caster's position, target the effect center
    /// (Circle kinds) or the far end (Line/Cone), radius the circle radius / line width /
    /// cone spread in radians (the AbilityVisualEvent field encoding). Shared by the echo
    /// handler and the caster's optimistic path so both draw the identical flash.
    public void Render(AbilityVisualKind kind, Vector2 origin, Vector2 target, float radius, float duration)
    {
        switch (kind)
        {
            case AbilityVisualKind.Circle:
                Flash([MakeRing(target, radius, CircleColor)], duration);
                break;
            case AbilityVisualKind.Line:
                Flash([MakeRect(origin, target, radius, LineColor)], duration);
                break;
            case AbilityVisualKind.Cone:
                Flash([MakeCone(origin, target, radius, ConeColor)], duration);
                break;
            case AbilityVisualKind.Meteor:
            {
                // VISUAL-ONLY deviation: the damage is the plain circle — the streaks sell it.
                var parts = new List<Polygon2D> { MakeDisc(target, radius, MeteorColor) };
                for (int i = 0; i < 6; i++)
                {
                    float angle = Mathf.Tau * i / 6f + GD.Randf() * 0.5f;
                    float length = radius * (0.6f + GD.Randf() * 0.6f);
                    var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    parts.Add(MakeRect(target + dir * radius * 0.2f, target + dir * length, 5f, MeteorColor));
                }
                Flash(parts, duration);
                break;
            }
            case AbilityVisualKind.Converge:
            {
                // VISUAL-ONLY deviation: the damage is the plain circle.
                var parts = new List<Polygon2D> { MakeRing(target, radius, ConvergeColor) };
                for (int i = 0; i < 6; i++)
                {
                    float angle = Mathf.Tau * i / 6f + GD.Randf() * 0.5f;
                    var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    parts.Add(MakeRect(target + dir * radius * 1.6f, target + dir * radius * 0.5f, 4f, ConvergeColor));
                }
                Flash(parts, duration);
                break;
            }
        }
    }

    /// Parents the flash's polygons under one code-created container (data-driven count,
    /// like the bullet controller's zone visuals), fades it out over duration and frees it.
    /// ZIndex 2 keeps spell flashes above the entities (0) and bullets (1).
    private void Flash(List<Polygon2D> parts, float duration)
    {
        var container = new Node2D { ZIndex = 2 };
        GetParent().AddChild(container);
        foreach (var part in parts) container.AddChild(part);
        var tween = container.CreateTween();
        tween.TweenProperty(container, "modulate:a", 0f, Mathf.Max(duration, 0.05f));
        tween.TweenCallback(Callable.From(container.QueueFree));
    }

    /// A ring band (outer arc + inner arc reversed — the holed-polygon trick) centered on
    /// `center`, in world coordinates.
    private static Polygon2D MakeRing(Vector2 center, float radius, Color color)
    {
        var polygon = new Vector2[CirclePoints * 2];
        float inner = Mathf.Max(radius - RingThickness, 1f);
        for (int i = 0; i < CirclePoints; i++)
        {
            float angle = Mathf.Tau * i / CirclePoints;
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            polygon[i] = center + dir * radius;
            polygon[CirclePoints * 2 - 1 - i] = center + dir * inner;
        }
        return new Polygon2D { Polygon = polygon, Color = color };
    }

    /// A filled disc — Meteor's impact circle.
    private static Polygon2D MakeDisc(Vector2 center, float radius, Color color)
    {
        var polygon = new Vector2[CirclePoints];
        for (int i = 0; i < CirclePoints; i++)
        {
            float angle = Mathf.Tau * i / CirclePoints;
            polygon[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }
        return new Polygon2D { Polygon = polygon, Color = color };
    }

    /// A full-width rectangle from origin to end (the beam/laser and the Meteor/Converge
    /// streaks). A degenerate (zero-length) rectangle collapses to a dot at the origin.
    private static Polygon2D MakeRect(Vector2 origin, Vector2 end, float width, Color color)
    {
        var axis = end - origin;
        if (axis.LengthSquared() < 0.000001f) axis = Vector2.Right;
        var offset = axis.Orthogonal().Normalized() * (width * 0.5f);
        return new Polygon2D
        {
            Polygon = [origin - offset, origin + offset, end + offset, end - offset],
            Color = color,
        };
    }

    /// A wedge from origin toward `end` (the far end — its distance is the cone length)
    /// with full-angle `spread` radians.
    private static Polygon2D MakeCone(Vector2 origin, Vector2 end, float spread, Color color)
    {
        var axis = end - origin;
        if (axis.LengthSquared() < 0.000001f) axis = Vector2.Right;
        float baseAngle = axis.Angle();
        float length = axis.Length();
        var points = new List<Vector2> { origin };
        for (int i = 0; i <= CirclePoints; i++)
        {
            float angle = baseAngle - spread * 0.5f + spread * i / CirclePoints;
            points.Add(origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length);
        }
        return new Polygon2D { Polygon = points.ToArray(), Color = color };
    }
}
