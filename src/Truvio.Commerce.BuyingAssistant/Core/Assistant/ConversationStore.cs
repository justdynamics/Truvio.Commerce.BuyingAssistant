using System.Collections.Concurrent;

namespace Truvio.Commerce.BuyingAssistant.Core.Assistant;

/// <summary>
/// Keeps conversations server-side so the browser never replays or edits model history
/// (history stays append-only, which preserved thinking requires). In-memory, per process,
/// sliding expiry; a conversation is bound to the session that started it.
/// </summary>
public sealed class ConversationStore<TMessage>
{
    private sealed class Entry
    {
        public string OwnerKey = "";
        public List<TMessage> Messages = new();
        public DateTime LastTouched = DateTime.UtcNow;
        public SemaphoreSlim Lock = new(1, 1);
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly TimeSpan _ttl;
    private readonly int _maxEntries;
    private DateTime _lastSweep = DateTime.UtcNow;

    public ConversationStore(TimeSpan? ttl = null, int maxEntries = 2000)
    {
        _ttl = ttl ?? TimeSpan.FromMinutes(45);
        _maxEntries = maxEntries;
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..20];

    /// <summary>Returns the messages for the conversation owned by <paramref name="ownerKey"/>, or an empty list for a new one.</summary>
    public List<TMessage> Get(string conversationId, string ownerKey)
    {
        Sweep();
        if (_entries.TryGetValue(conversationId, out var e) && e.OwnerKey == ownerKey)
        {
            e.LastTouched = DateTime.UtcNow;
            return e.Messages;
        }
        return new List<TMessage>();
    }

    public void Put(string conversationId, string ownerKey, List<TMessage> messages)
    {
        var e = _entries.GetOrAdd(conversationId, _ => new Entry { OwnerKey = ownerKey });
        if (e.OwnerKey != ownerKey) return;
        e.Messages = messages;
        e.LastTouched = DateTime.UtcNow;
    }

    /// <summary>One run at a time per conversation.</summary>
    public SemaphoreSlim GetLock(string conversationId, string ownerKey)
    {
        var e = _entries.GetOrAdd(conversationId, _ => new Entry { OwnerKey = ownerKey });
        return e.Lock;
    }

    public void Remove(string conversationId) => _entries.TryRemove(conversationId, out _);

    private void Sweep()
    {
        var now = DateTime.UtcNow;
        if (now - _lastSweep < TimeSpan.FromMinutes(2) && _entries.Count < _maxEntries) return;
        _lastSweep = now;
        foreach (var kv in _entries)
        {
            if (now - kv.Value.LastTouched > _ttl) _entries.TryRemove(kv.Key, out _);
        }
        if (_entries.Count > _maxEntries)
        {
            foreach (var kv in _entries.OrderBy(k => k.Value.LastTouched).Take(_entries.Count - _maxEntries))
                _entries.TryRemove(kv.Key, out _);
        }
    }
}
