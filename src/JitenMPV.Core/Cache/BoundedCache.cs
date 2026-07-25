namespace JitenMPV.Core.Cache;

internal sealed class BoundedCache<TKey, TValue>(int maxEntries) where TKey : notnull
{
    private readonly Lock _lock = new();
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _cache = [];
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();

    public TValue? GetOrDefault(TKey key)
        => TryGetValue(key, out var value) ? value : default;

    public bool TryGetValue(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (!_cache.TryGetValue(key, out var node))
            {
                value = default;
                return false;
            }

            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public bool TryAdd(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_cache.ContainsKey(key)) return false;

            _cache[key] = _order.AddFirst((key, value));

            while (_cache.Count > maxEntries && _order.Last is { } evicted)
            {
                _order.RemoveLast();
                _cache.Remove(evicted.Value.Key);
            }

            return true;
        }
    }

    public void ForEachValue(Action<TValue> action)
    {
        lock (_lock)
        {
            foreach (var entry in _order)
                action(entry.Value);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _cache.Clear();
            _order.Clear();
        }
    }
}
