namespace UmageAI.Optimizely.GraphSearchTools.LoadTest;

/// <summary>
/// Zipfian sampler over <c>[0, n)</c>. With exponent ≈ 1.0 the most popular
/// item gets ~10× the second's weight — close to what real search-log
/// distributions look like, so the load test exercises both the head-bucket
/// hot path and the long-tail cardinality path together.
/// </summary>
internal sealed class ZipfSampler
{
    private readonly double[] _cumulative;

    public ZipfSampler(int n, double exponent, int seed)
    {
        _cumulative = new double[n];
        var sum = 0.0;
        for (var i = 0; i < n; i++)
        {
            sum += 1.0 / Math.Pow(i + 1, exponent);
            _cumulative[i] = sum;
        }
        // Normalize so the last entry is exactly 1.0.
        for (var i = 0; i < n; i++) _cumulative[i] /= sum;
        _ = seed; // reserved
    }

    public int Sample(Random rng)
    {
        var u = rng.NextDouble();
        var idx = Array.BinarySearch(_cumulative, u);
        if (idx < 0) idx = ~idx;
        if (idx >= _cumulative.Length) idx = _cumulative.Length - 1;
        return idx;
    }
}
