using System.Collections.Concurrent;

namespace JitenMPV.Core.Cache;

internal sealed class BoundedCache<TKey, TValue>(int maxEntries) where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> _cache = new();
    private readonly ConcurrentQueue<TKey> _order = new();

    public TValue? GetOrDefault(TKey key)
        => _cache.GetValueOrDefault(key);

    public bool TryGetValue(TKey key, out TValue? value)
        => _cache.TryGetValue(key, out value);

    public bool TryAdd(TKey key, TValue value)
    {
        if (!_cache.TryAdd(key, value)) return false;
        _order.Enqueue(key);
        while (_cache.Count > maxEntries && _order.TryDequeue(out var oldest))
            _cache.TryRemove(oldest, out _);
        return true;
    }

    public void Clear()
    {
        _cache.Clear();
        while (_order.TryDequeue(out _)) { }
    }

    public ICollection<TValue> Values => _cache.Values;
}
