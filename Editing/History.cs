using MuWorldEditor.Formats;

namespace MuWorldEditor.Editing;

public interface IUndoable
{
    string Name { get; }
    void Undo();
    void Redo();
}

/// <summary>
/// Cambio sobre un arreglo del mapa (altura, atributos, tiles, alfa). Guarda
/// el valor anterior y el nuevo de cada celda tocada, asi el deshacer es exacto
/// y no depende de recalcular nada.
/// </summary>
public sealed class ArrayStroke<T> : IUndoable where T : struct
{
    private readonly T[] _target;
    private readonly Dictionary<int, T> _old = new();
    private readonly Dictionary<int, T> _new = new();

    public string Name { get; }
    public int Count => _old.Count;
    public IEnumerable<int> Indices => _old.Keys;

    public ArrayStroke(string name, T[] target) { Name = name; _target = target; }

    public void Record(int index, T oldValue, T newValue)
    {
        if (!_old.ContainsKey(index)) _old[index] = oldValue;
        _new[index] = newValue;
    }

    public void Undo() { foreach (var kv in _old) _target[kv.Key] = kv.Value; }
    public void Redo() { foreach (var kv in _new) _target[kv.Key] = kv.Value; }
}

public sealed class AddObjects : IUndoable
{
    private readonly List<MapFiles.MapObject> _list;
    private readonly List<MapFiles.MapObject> _added;
    public string Name => _added.Count == 1 ? "Agregar objeto" : "Agregar " + _added.Count + " objetos";

    public AddObjects(List<MapFiles.MapObject> list, List<MapFiles.MapObject> added)
    { _list = list; _added = added; }

    public void Undo() { foreach (var o in _added) _list.Remove(o); }
    public void Redo() { foreach (var o in _added) if (!_list.Contains(o)) _list.Add(o); }
}

public sealed class RemoveObjects : IUndoable
{
    private readonly List<MapFiles.MapObject> _list;
    private readonly List<(int index, MapFiles.MapObject obj)> _removed;
    public string Name => _removed.Count == 1 ? "Borrar objeto" : "Borrar " + _removed.Count + " objetos";

    public RemoveObjects(List<MapFiles.MapObject> list, List<(int, MapFiles.MapObject)> removed)
    { _list = list; _removed = removed; }

    public void Undo()
    {
        foreach (var (i, o) in _removed.OrderBy(r => r.index))
            _list.Insert(Math.Min(i, _list.Count), o);
    }

    public void Redo() { foreach (var (_, o) in _removed) _list.Remove(o); }
}

/// <summary>Mover, rotar o escalar uno o varios objetos.</summary>
public sealed class TransformObjects : IUndoable
{
    public readonly struct State
    {
        public readonly float X, Y, Z, AX, AY, AZ, Scale;
        public State(MapFiles.MapObject o)
        { X = o.X; Y = o.Y; Z = o.Z; AX = o.AngleX; AY = o.AngleY; AZ = o.AngleZ; Scale = o.Scale; }

        public void ApplyTo(MapFiles.MapObject o)
        { o.X = X; o.Y = Y; o.Z = Z; o.AngleX = AX; o.AngleY = AY; o.AngleZ = AZ; o.Scale = Scale; }
    }

    private readonly List<(MapFiles.MapObject obj, State before, State after)> _items;
    public string Name { get; }

    public TransformObjects(string name, List<(MapFiles.MapObject, State, State)> items)
    { Name = name; _items = items; }

    public void Undo() { foreach (var (o, b, _) in _items) b.ApplyTo(o); }
    public void Redo() { foreach (var (o, _, a) in _items) a.ApplyTo(o); }
}

public sealed class CompositeAction : IUndoable
{
    private readonly List<IUndoable> _parts;
    public string Name { get; }
    public CompositeAction(string name, List<IUndoable> parts) { Name = name; _parts = parts; }
    public void Undo() { for (int i = _parts.Count - 1; i >= 0; i--) _parts[i].Undo(); }
    public void Redo() { foreach (var p in _parts) p.Redo(); }
}

/// <summary>Pila de deshacer sin limite de pasos.</summary>
public sealed class History
{
    private readonly List<IUndoable> _undo = new();
    private readonly List<IUndoable> _redo = new();

    public event Action Changed;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string UndoName => CanUndo ? _undo[^1].Name : null;
    public string RedoName => CanRedo ? _redo[^1].Name : null;
    public int Depth => _undo.Count;

    public void Push(IUndoable action)
    {
        _undo.Add(action);
        _redo.Clear();
        Changed?.Invoke();
    }

    public IUndoable Undo()
    {
        if (!CanUndo) return null;
        var a = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        a.Undo();
        _redo.Add(a);
        Changed?.Invoke();
        return a;
    }

    public IUndoable Redo()
    {
        if (!CanRedo) return null;
        var a = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        a.Redo();
        _undo.Add(a);
        Changed?.Invoke();
        return a;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }
}
