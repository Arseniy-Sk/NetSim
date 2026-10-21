using NetSim.Core.Model;
using NetSim.Core.Serialization;

namespace NetSim.App.Services;

/// <summary>Отмена действий через снимки проекта (JSON). Просто и надёжно для учебных проектов.</summary>
public sealed class UndoStack
{
    private const int Capacity = 60;
    private readonly LinkedList<string> _undo = new();
    private readonly Stack<string> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(Project project)
    {
        _undo.AddLast(ProjectSerializer.ToJson(project));
        if (_undo.Count > Capacity) _undo.RemoveFirst();
        _redo.Clear();
    }

    public Project? Undo(Project current)
    {
        if (_undo.Count == 0) return null;
        _redo.Push(ProjectSerializer.ToJson(current));
        var json = _undo.Last!.Value;
        _undo.RemoveLast();
        return ProjectSerializer.FromJson(json);
    }

    public Project? Redo(Project current)
    {
        if (_redo.Count == 0) return null;
        _undo.AddLast(ProjectSerializer.ToJson(current));
        return ProjectSerializer.FromJson(_redo.Pop());
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
