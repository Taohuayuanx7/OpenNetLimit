using System.Collections.Concurrent;
using System.Net;

namespace NetMeter;

public enum TransportProtocol
{
    Tcp,
    Udp
}

public readonly record struct FlowKey(
    TransportProtocol Protocol,
    IPAddress LocalAddress,
    ushort LocalPort,
    IPAddress RemoteAddress,
    ushort RemotePort)
{
    public FlowKey Reversed() => new(Protocol, RemoteAddress, RemotePort, LocalAddress, LocalPort);
}

public sealed class LimitsConfig
{
    public long DownBytesPerSecond { get; set; }
    public long UpBytesPerSecond { get; set; }
    public int MaxTcpConnections { get; set; }
    public int MaxUdpFlows { get; set; }

    public bool HasRateLimit => DownBytesPerSecond > 0 || UpBytesPerSecond > 0;
    public bool HasConnectionLimit => MaxTcpConnections > 0 || MaxUdpFlows > 0;
}

public sealed class ProcessState
{
    public required uint ProcessId { get; init; }
    public required string ProcessName { get; set; }

    private long _downBytes;
    private long _upBytes;
    private long _downRate;
    private long _upRate;
    private long _lastActivityTicks = DateTime.UtcNow.Ticks;

    public long DownRate => Interlocked.Read(ref _downRate);
    public long UpRate => Interlocked.Read(ref _upRate);

    public long LastActivityTicks => Interlocked.Read(ref _lastActivityTicks);

    public void Touch() => Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

    public void AddDownBytes(long count) => Interlocked.Add(ref _downBytes, count);
    public void AddUpBytes(long count) => Interlocked.Add(ref _upBytes, count);

    public void TickRates(double elapsedSeconds)
    {
        double seconds = elapsedSeconds > 0.1 ? elapsedSeconds : 1.0;
        Interlocked.Exchange(ref _downRate, (long)(Interlocked.Exchange(ref _downBytes, 0) / seconds));
        Interlocked.Exchange(ref _upRate, (long)(Interlocked.Exchange(ref _upBytes, 0) / seconds));
    }

    public int TcpFlowCount => TcpFlows.Count;
    public int UdpFlowCount => UdpFlows.Count;

    public ConcurrentDictionary<FlowKey, byte> TcpFlows { get; } = new();
    public ConcurrentDictionary<FlowKey, byte> UdpFlows { get; } = new();

    public TokenBucket? DownBucket { get; set; }
    public TokenBucket? UpBucket { get; set; }
}

/// <summary>
/// Lock-free token bucket using Interlocked fixed-point arithmetic
/// (tokens stored scaled by 1000). Safe for concurrent callers.
/// </summary>
public sealed class TokenBucket
{
    private long _tokens;      // tokens * 1000
    private long _lastRefill;  // UtcNow ticks
    private double _rate;      // tokens per second

    public TokenBucket(double rate)
    {
        _rate = rate;
        _tokens = (long)(Capacity(rate) * 1000.0);
        _lastRefill = DateTime.UtcNow.Ticks;
    }

    public double Rate
    {
        get => System.Threading.Volatile.Read(ref _rate);
        set => System.Threading.Volatile.Write(ref _rate, value);
    }

    // Small burst budget: ~50ms of tokens, at least 16 KB
    private static double Capacity(double rate) => Math.Max(rate * 0.05, 16 * 1024);

    /// <summary>Returns true if the packet may pass; false if it must be dropped.</summary>
    public bool TryConsume(int bytes)
    {
        long now = DateTime.UtcNow.Ticks;
        double rate = System.Threading.Volatile.Read(ref _rate);
        double capacity = Capacity(rate);

        while (true)
        {
            long tokens = Interlocked.Read(ref _tokens);
            long lastRefill = Interlocked.Read(ref _lastRefill);
            double elapsed = (now - lastRefill) / 10_000_000.0;
            double refilled = Math.Min(capacity, tokens / 1000.0 + elapsed * rate);

            // Even a fully-refilled bucket cannot fit this packet: drop it.
            // (Safe without CAS because at capacity the value is a ceiling.)
            if (refilled < bytes) return false;

            long target = (long)((refilled - bytes) * 1000.0);
            if (Interlocked.CompareExchange(ref _tokens, target, tokens) == tokens)
            {
                Interlocked.Exchange(ref _lastRefill, now);
                return true;
            }
            // Lost the race; recompute against the winning value
        }
    }
}