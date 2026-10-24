using WebhookEngine.Core.Hashing;

namespace WebhookEngine.Tests;

public class ConsistentHashRingTests
{
    private static IEnumerable<string> Keys(int n)
        => Enumerable.Range(0, n).Select(i => $"key-{i}");

    [Fact]
    public void GetNode_EmptyRing_ReturnsNull()
        => Assert.Null(new ConsistentHashRing().GetNode("k"));

    [Fact]
    public void GetNode_SameKey_AlwaysSameNode()
    {
        var ring = new ConsistentHashRing();
        ring.AddNode("a"); ring.AddNode("b"); ring.AddNode("c");
        Assert.Equal(ring.GetNode("webhook-42"), ring.GetNode("webhook-42"));
    }

    [Fact]
    public void AddNode_RelocatesRoughlyOneOverN()
    {
        var ring = new ConsistentHashRing();
        foreach (var n in new[] { "a", "b", "c" }) ring.AddNode(n);

        var keys = Keys(10_000).ToList();
        var before = keys.ToDictionary(k => k, k => ring.GetNode(k));

        ring.AddNode("d");
        var moved = keys.Count(k => before[k] != ring.GetNode(k));
        var fraction = (double)moved / keys.Count;

        // 3→4 узла: новый забирает ~25%. Допуск широкий из-за дискретности колец.
        Assert.InRange(fraction, 0.10, 0.40);
    }

    [Fact]
    public void RemoveNode_KeysMigrateToSurvivors()
    {
        var ring = new ConsistentHashRing();
        foreach (var n in new[] { "a", "b", "c" }) ring.AddNode(n);
        ring.RemoveNode("a");

        var keys = Keys(1_000);
        Assert.All(keys, k => Assert.NotEqual("a", ring.GetNode(k)));
    }
}
