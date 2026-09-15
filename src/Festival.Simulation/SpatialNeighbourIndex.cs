namespace Festival.Simulation;

/// <summary>Stable spatial hash used by bounded local separation and diagnostics.</summary>
public sealed class SpatialNeighbourIndex
{
    public const int BucketSizeMillimetres = 1_000;
    private readonly SortedDictionary<(int X, int Z), List<(EntityId Id, int X, int Z)>> _buckets = [];

    public SpatialNeighbourIndex() { }

    public SpatialNeighbourIndex(IEnumerable<NavigationAgentSnapshot> agents)
    {
        foreach (var agent in agents.OrderBy(item => item.Id))
        {
            Add(agent.Id, agent.XMillimetres, agent.ZMillimetres);
        }
    }

    public void Add(EntityId id, int xMillimetres, int zMillimetres)
    {
        var key = Bucket(xMillimetres, zMillimetres);
        if (!_buckets.TryGetValue(key, out var bucket)) _buckets.Add(key, bucket = []);
        bucket.Add((id, xMillimetres, zMillimetres));
        bucket.Sort((left, right) => left.Id.CompareTo(right.Id));
    }

    public IReadOnlyList<EntityId> Query(int xMillimetres, int zMillimetres, int radiusMillimetres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radiusMillimetres);
        var centre = Bucket(xMillimetres, zMillimetres);
        var span = radiusMillimetres / BucketSizeMillimetres + 1;
        var radiusSquared = (long)radiusMillimetres * radiusMillimetres;
        var result = new List<EntityId>();
        for (var z = centre.Z - span; z <= centre.Z + span; z++)
        for (var x = centre.X - span; x <= centre.X + span; x++)
            if (_buckets.TryGetValue((x, z), out var bucket))
                foreach (var item in bucket)
                    if ((long)(item.X - xMillimetres) * (item.X - xMillimetres) + (long)(item.Z - zMillimetres) * (item.Z - zMillimetres) <= radiusSquared)
                        result.Add(item.Id);
        result.Sort();
        return result;
    }

    private static (int X, int Z) Bucket(int x, int z) => (FloorDiv(x, BucketSizeMillimetres), FloorDiv(z, BucketSizeMillimetres));
    private static int FloorDiv(int value, int divisor) => value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
}
