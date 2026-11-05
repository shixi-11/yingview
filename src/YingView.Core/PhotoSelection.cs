namespace YingView.Core;

/// <summary>Transient selection in an ordered contact sheet, independent from saved favorites.</summary>
public sealed class PhotoSelection
{
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private string? _anchor;
    public IReadOnlySet<string> Selected => _selected;
    public void Select(string path, IReadOnlyList<string> ordered, bool toggle = false, bool range = false)
    {
        var index = Index(ordered, path);
        if (index < 0) return;
        var anchorIndex = _anchor is null ? -1 : Index(ordered, _anchor);
        if (range && anchorIndex >= 0)
        {
            if (!toggle) _selected.Clear();
            for (var i = Math.Min(index, anchorIndex); i <= Math.Max(index, anchorIndex); i++) _selected.Add(ordered[i]);
        }
        else
        {
            if (!toggle) _selected.Clear();
            if (toggle && _selected.Contains(path)) _selected.Remove(path); else _selected.Add(path);
            _anchor = path;
        }
    }
    public void Replace(IEnumerable<string> paths) { _selected.Clear(); _selected.UnionWith(paths); _anchor = _selected.FirstOrDefault(); }
    public void Retain(IEnumerable<string> paths) { _selected.IntersectWith(paths); if (_anchor is not null && !paths.Contains(_anchor, StringComparer.OrdinalIgnoreCase)) _anchor = null; }
    public string[] Ordered(IReadOnlyList<string> files) => files.Where(_selected.Contains).ToArray();
    private static int Index(IReadOnlyList<string> ordered, string path)
    {
        for (var i = 0; i < ordered.Count; i++) if (StringComparer.OrdinalIgnoreCase.Equals(ordered[i], path)) return i;
        return -1;
    }
}
