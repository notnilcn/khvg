#nullable enable
using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// Binds one subscribed SpacetimeDB table (TableName — picked from a dropdown of
/// TableSubscriber's subscription waves) and re-exposes its row events as Godot signals
/// (RowInserted/RowUpdated/RowDeleted) so the parent can wire handlers in the editor.
///
/// Rows are plain C# classes and can't cross the signal boundary, so signals carry no
/// arguments: handlers read LastRow (inserted / new row), LastOldRow (update only) or
/// LastDeletedRow and cast to the row type. With ReplayExistingRows, RowInserted fires once
/// per already-cached row right after binding, replacing manual Iter() replay loops.
///
/// Place as a child of the consuming component (one binder per table); it finds the
/// connection through the DatabaseConnector autoload (<see cref="DatabaseConnector.Instance"/>),
/// so it works under any entity in any scene. [Tool] only for the inspector
/// dropdown — no connection work happens in the editor.
/// </summary>
[Tool]
public partial class TableBinderComponent : Component
{
    /// Fired after a row is inserted; read <see cref="LastRow"/> in the handler.
    [Signal] public delegate void RowInsertedEventHandler();
    /// Fired after a row is updated; read <see cref="LastRow"/> (new) and <see cref="LastOldRow"/>.
    [Signal] public delegate void RowUpdatedEventHandler();
    /// Fired after a row is deleted; read <see cref="LastDeletedRow"/> in the handler.
    [Signal] public delegate void RowDeletedEventHandler();

    private static readonly StringName TableNameProperty = "TableName";

    /// PascalCase RemoteTables field name of the table to bind (dropdown in the inspector;
    /// declared via _GetPropertyList so the options can come from TableSubscriber).
    public string TableName { get; set; } = "";

    /// When true, RowInserted fires once per row already in the client cache when the binder
    /// connects (late bind or rows delivered before the parent's signals were wired).
    [Export] public bool ReplayExistingRows { get; set; }

    /// The inserted row, or the new row after an update. Cast to the bound table's row type.
    public object? LastRow { get; private set; }
    /// The previous row after an update (update events only).
    public object? LastOldRow { get; private set; }
    /// The deleted row. Cast to the bound table's row type.
    public object? LastDeletedRow { get; private set; }

    private object? _handle;
    private readonly List<(EventInfo Event, Delegate Handler)> _bindings = new();

    public override void _Ready()
{
        // [Tool] runs this in the editor too, where no IEntity ancestor exists (the entity's
        // script isn't tool-enabled) — registration is runtime-only, so skip it there.
        if (!Engine.IsEditorHint())
            base._Ready();
    }

    protected override void OnEntityReady()
    {
        if (Engine.IsEditorHint()) return;

        var connector = DatabaseConnector.Instance;
        if (connector == null)
    {
            GD.PrintErr($"[TableBinderComponent] {GetPath()}: DatabaseConnector autoload not ready");
            return;
        }

        if (connector.Conn?.IsActive == true)
            Bind(connector.Conn);
        else
            connector.Connected += OnConnected;
    }

    private void OnConnected()
    {
        var conn = DatabaseConnector.Instance?.Conn;
        if (conn != null)
            Bind(conn);
    }

    private void Bind(DbConnection conn)
    {
        if (_handle != null) return;

        if (string.IsNullOrEmpty(TableName))
    {
            GD.PrintErr($"[TableBinderComponent] {GetPath()}: TableName is not set");
            return;
        }
        if (!TableSubscriber.AllSubscribedTables.Contains(TableName))
            GD.PushWarning($"[TableBinderComponent] {GetPath()}: '{TableName}' is not in TableSubscriber's subscription waves — it will never receive rows");

        var field = typeof(RemoteTables).GetField(TableName);
        if (field == null)
    {
            GD.PrintErr($"[TableBinderComponent] {GetPath()}: no table '{TableName}' in the generated module bindings");
            return;
        }

        _handle = field.GetValue(conn.Db)!;
        Hook("OnInsert", nameof(HandleInsert));
        Hook("OnUpdate", nameof(HandleUpdate));
        Hook("OnDelete", nameof(HandleDelete));
        GD.Print($"[TableBinderComponent] DEBUG bound {TableName} at {GetPath()}, hooked {_bindings.Count} events");

        if (ReplayExistingRows && _handle.GetType().GetMethod("Iter")?.Invoke(_handle, null) is IEnumerable rows)
            foreach (var row in rows)
    {
                LastRow = row;
                EmitSignal(SignalName.RowInserted);
            }
    }

    /// Subscribes the named table event to the matching generic Handle* method, closing it
    /// over the row type recovered from the event's own delegate signature.
    private void Hook(string eventName, string handlerMethod)
    {
        var eventInfo = _handle!.GetType().GetEvent(eventName);
        if (eventInfo == null) return; // event tables (e.g. BulletPatternEvent) lack OnUpdate/OnDelete

        var handlerType = eventInfo.EventHandlerType!;
        var paramTypes = handlerType.GetMethod("Invoke")!.GetParameters();
        var rowType = paramTypes[^1].ParameterType;

        var closed = GetType()
            .GetMethod(handlerMethod, BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(rowType);
        var handler = Delegate.CreateDelegate(handlerType, this, closed);
        eventInfo.AddEventHandler(_handle, handler);
        _bindings.Add((eventInfo, handler));
    }

    private void HandleInsert<TRow>(EventContext ctx, TRow row)
    {
        LastRow = row;
        if (!_firstInsertLogged) { _firstInsertLogged = true; GD.Print($"[TableBinderComponent] DEBUG first insert for {TableName}"); }
        EmitSignal(SignalName.RowInserted);
    }

    private bool _firstInsertLogged;

    private void HandleUpdate<TRow>(EventContext ctx, TRow oldRow, TRow newRow)
    {
        LastOldRow = oldRow;
        LastRow = newRow;
        EmitSignal(SignalName.RowUpdated);
    }

    private void HandleDelete<TRow>(EventContext ctx, TRow row)
    {
        LastDeletedRow = row;
        EmitSignal(SignalName.RowDeleted);
    }

    public override void _ExitTree()
{
        if (_handle != null)
            foreach (var (tableEvent, handler) in _bindings)
                tableEvent.RemoveEventHandler(_handle, handler);
        _bindings.Clear();
        _handle = null;
        base._ExitTree();
    }

    public override Godot.Collections.Array<Godot.Collections.Dictionary> _GetPropertyList()
{
        return new Godot.Collections.Array<Godot.Collections.Dictionary>
    {
            new()
    {
                { "name", TableNameProperty },
    { "type", (int)Variant.Type.String },
{ "hint", (int)PropertyHint.Enum },
    { "hint_string", string.Join(",", TableSubscriber.AllSubscribedTables) },
{ "usage", (int)PropertyUsageFlags.Default },
            },
        };
    }

    public override Variant _Get(StringName property)
{
        if (property == TableNameProperty)
            return TableName;
        return base._Get(property);
    }

    public override bool _Set(StringName property, Variant value)
{
        if (property == TableNameProperty)
    {
            TableName = value.AsString();
            UpdateConfigurationWarnings();
            return true;
        }
        return base._Set(property, value);
    }

    public override string[] _GetConfigurationWarnings()
{
        var warnings = new List<string>(base._GetConfigurationWarnings() ?? Array.Empty<string>());
        if (string.IsNullOrEmpty(TableName))
            warnings.Add("TableName is not set — pick a table to bind.");
        else if (!TableSubscriber.AllSubscribedTables.Contains(TableName))
            warnings.Add($"'{TableName}' is not in TableSubscriber's subscription waves — it will never receive rows.");
        return warnings.ToArray();
    }
}

