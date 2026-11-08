using System.Security.Cryptography;
using System.Text;

namespace WebhookEngine.Core.Hashing;

/// <summary>
/// Consistent hash ring (TASK-008): nodes and keys are points on a
/// uint32 ring. Adding/removing a node remaps ~1/n of keys, not all.
/// </summary>
public sealed class ConsistentHashRing
{
    private readonly SortedDictionary<uint, string> _points = new(); // point → node
    private readonly int _virtualNodes;
    private readonly object _gate = new();

    public ConsistentHashRing(int virtualNodes = 150)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(virtualNodes);
        _virtualNodes = virtualNodes;
    }

    public void AddNode(string node)
    {
        lock (_gate)
            for (var i = 0; i < _virtualNodes; i++)
                _points[Hash($"{node}#{i}")] = node;
    }

    public void RemoveNode(string node)
    {
        lock (_gate)
            foreach (var p in _points.Where(kv => kv.Value == node)
                                     .Select(kv => kv.Key).ToList())
                _points.Remove(p);
    }

    /// <summary>Nearest node clockwise from the key's point; null when the ring is empty.</summary>
    public string? GetNode(string key)
    {
        lock (_gate)
        {
            if (_points.Count == 0) return null;
            var h = Hash(key);
            foreach (var (point, node) in _points)
                if (point >= h) return node;
            return _points.Values.First(); // wrap around the ring
        }
    }

    private static uint Hash(string s)
        => BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}
