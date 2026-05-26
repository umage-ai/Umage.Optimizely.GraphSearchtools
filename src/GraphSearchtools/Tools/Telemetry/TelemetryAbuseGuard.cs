using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using UmageAI.Optimizely.GraphSearchTools.Configuration;

namespace UmageAI.Optimizely.GraphSearchTools.Tools.Telemetry;

/// <summary>
/// Self-contained, in-process rate limiter for the public ingest endpoint. Two
/// fixed-second windows: per-IP and global. Open-ingest exposure means the
/// limiter is a load-bearing defense, not a nice-to-have; we own it here so
/// the addon doesn't depend on the host wiring <c>app.UseRateLimiter()</c>.
/// </summary>
/// <remarks>
/// Memory is bounded by periodic eviction of per-IP entries that haven't
/// admitted a request in the last 5 minutes. An attacker rotating IPs faster
/// than the eviction window can grow the dictionary; the global limiter still
/// caps the cost they impose on the channel + flusher.
/// </remarks>
internal sealed class TelemetryAbuseGuard
{
    private readonly LocalTelemetryOptions _options;
    private readonly ConcurrentDictionary<string, FixedWindow> _perIp = new();
    private readonly FixedWindow _global;
    private long _lastEvictionTicks;

    private const int EvictionIntervalSeconds = 60;
    private const int EvictionStalenessSeconds = 300;

    public TelemetryAbuseGuard(IOptions<GraphSearchtoolsOptions> options)
    {
        _options = options.Value.Telemetry;
        _global = new FixedWindow();
        _lastEvictionTicks = Environment.TickCount64;
    }

    /// <summary>
    /// Returns <c>true</c> when the request is within both per-IP and global
    /// caps, <c>false</c> when either has been exceeded for the current second.
    /// </summary>
    public bool TryAdmit(string? clientIp)
    {
        var nowSecond = NowSecond();
        if (!_global.TryAdmit(nowSecond, _options.GlobalEventsPerSecond)) return false;

        var ipKey = string.IsNullOrEmpty(clientIp) ? "unknown" : clientIp;
        var window = _perIp.GetOrAdd(ipKey, _ => new FixedWindow());
        if (!window.TryAdmit(nowSecond, _options.PerIpEventsPerSecond)) return false;

        MaybeEvict(nowSecond);
        return true;
    }

    private void MaybeEvict(long nowSecond)
    {
        var nowTicks = Environment.TickCount64;
        if (nowTicks - _lastEvictionTicks < EvictionIntervalSeconds * 1000) return;
        if (Interlocked.Exchange(ref _lastEvictionTicks, nowTicks) == nowTicks) return;

        foreach (var (ip, window) in _perIp)
        {
            if (nowSecond - window.LastSecond > EvictionStalenessSeconds)
            {
                _perIp.TryRemove(ip, out _);
            }
        }
    }

    private static long NowSecond() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private sealed class FixedWindow
    {
        private long _second;
        private int _count;
        private readonly object _gate = new();

        public long LastSecond => Volatile.Read(ref _second);

        public bool TryAdmit(long nowSecond, int permitLimit)
        {
            lock (_gate)
            {
                if (nowSecond != _second)
                {
                    _second = nowSecond;
                    _count = 0;
                }
                if (_count >= permitLimit) return false;
                _count++;
                return true;
            }
        }
    }
}
