using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace NetMeter;

public sealed class FlowTable
{
    private readonly ConcurrentDictionary<FlowKey, uint> _owner = new();
    private readonly ConcurrentDictionary<FlowKey, DateTime> _lastSeen = new();
    private readonly ConcurrentDictionary<uint, ProcessState> _states = new();
    private readonly ConcurrentDictionary<uint, string> _pidNames = new();
    private readonly ConcurrentDictionary<uint, DateTime> _pidProbedAt = new();
    private readonly ConcurrentDictionary<string, byte> _trackedNames = new(StringComparer.OrdinalIgnoreCase);

    private const double ProbeIntervalSeconds = 5;

    public ConcurrentDictionary<uint, ProcessState> States => _states;
    public ICollection<string> TrackedNames => _trackedNames.Keys;

    public ProcessState? GetState(uint processId) =>
        _states.TryGetValue(processId, out var state) ? state : null;

    public ProcessState GetOrAdd(uint processId, string processName)
    {
        if (_states.TryGetValue(processId, out var state))
        {
            if (!state.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                state.ProcessName = processName;
            return state;
        }

        var created = new ProcessState { ProcessId = processId, ProcessName = processName };
        var winner = _states.GetOrAdd(processId, created);
        _trackedNames[winner.ProcessName] = 0;
        return winner;
    }

    public uint? LookupProcessId(FlowKey flowKey)
    {
        if (_owner.TryGetValue(flowKey, out var pid)) return pid;
        if (_owner.TryGetValue(flowKey.Reversed(), out pid)) return pid;
        return null;
    }

    public void RegisterFlow(FlowKey flowKey, uint processId, string processName)
    {
        _owner[flowKey] = processId;
        var state = GetOrAdd(processId, processName);
        var set = flowKey.Protocol == TransportProtocol.Tcp ? state.TcpFlows : state.UdpFlows;
        set[flowKey] = 0;
        _lastSeen[flowKey] = DateTime.UtcNow;
    }

    public void UnregisterFlow(FlowKey flowKey)
    {
        _owner.TryRemove(flowKey, out _);
        _lastSeen.TryRemove(flowKey, out _);

        foreach (var state in _states.Values)
        {
            var set = flowKey.Protocol == TransportProtocol.Tcp ? state.TcpFlows : state.UdpFlows;
            set.TryRemove(flowKey, out _);
            set.TryRemove(flowKey.Reversed(), out _);
        }
    }

    public void TouchLastSeen(FlowKey flowKey)
    {
        _lastSeen[flowKey] = DateTime.UtcNow;
    }

    public IReadOnlyList<ProcessState> GetTrackedStates() => _states.Values.ToList();

    /// <summary>
    /// Removes flow-attribution entries idle for longer than maxAge. Bounded
    /// to 500 entries per call so it can safely run on the UI thread each
    /// second; leftovers are picked up on the next pass.
    /// </summary>
    public void PurgeStale(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        int removed = 0;
        foreach (var kvp in _lastSeen)
        {
            if (removed >= 500) break;
            if (kvp.Value >= cutoff) continue;

            _owner.TryRemove(kvp.Key, out _);
            _lastSeen.TryRemove(kvp.Key, out _);
            foreach (var state in _states.Values)
            {
                var set = kvp.Key.Protocol == TransportProtocol.Tcp ? state.TcpFlows : state.UdpFlows;
                set.TryRemove(kvp.Key, out _);
            }
            removed++;
        }
    }

    /// <summary>
    /// Fully removes a process from all tables: flows it owns, its state and
    /// its name cache (a recycled PID must re-resolve from scratch).
    /// </summary>
    public void RetirePid(uint processId)
    {
        foreach (var kvp in _owner)
        {
            if (kvp.Value == processId)
            {
                _owner.TryRemove(kvp.Key, out _);
                _lastSeen.TryRemove(kvp.Key, out _);
            }
        }
        if (_states.TryRemove(processId, out var state))
        {
            // Keep the name tracked if another live process still uses it
            // (e.g. two instances of the same executable).
            bool stillActive = false;
            foreach (var other in _states.Values)
            {
                if (other.ProcessName.Equals(state.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    stillActive = true;
                    break;
                }
            }
            if (!stillActive)
                _trackedNames.TryRemove(state.ProcessName, out _);
        }
        _pidNames.TryRemove(processId, out _);
        _pidProbedAt.TryRemove(processId, out _);
    }

    public string ResolveProcessName(uint processId)
    {
        // Cached name + recent liveness probe: reuse without touching the OS
        if (_pidNames.TryGetValue(processId, out var cached) &&
            _pidProbedAt.TryGetValue(processId, out var probedAt) &&
            (DateTime.UtcNow - probedAt).TotalSeconds < ProbeIntervalSeconds)
        {
            return cached;
        }

        // 1. Managed lookup via Process.GetProcessById. Success also proves
        //    liveness: if the PID was recycled, this returns the NEW name,
        //    so stale cache entries self-correct.
        try
        {
            using var process = Process.GetProcessById((int)processId);
            _pidNames[processId] = process.ProcessName;
            _pidProbedAt[processId] = DateTime.UtcNow;
            return process.ProcessName;
        }
        catch
        {
            // 2. Fallback: open the process directly and query its image file name
            //    (works even when the managed Process wrapper fails)
            var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
            if (handle != IntPtr.Zero)
            {
                try
                {
                    var buffer = new StringBuilder(32768);
                    int size = buffer.Capacity;
                    if (QueryFullProcessImageNameW(handle, 0, buffer, ref size) && size > 0)
                    {
                        var name = Path.GetFileNameWithoutExtension(buffer.ToString(0, size));
                        if (!string.IsNullOrEmpty(name))
                        {
                            _pidNames[processId] = name;
                            _pidProbedAt[processId] = DateTime.UtcNow;
                            return name;
                        }
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }
        }

        // Process is gone: drop any stale cache entry so a recycled PID starts fresh
        _pidNames.TryRemove(processId, out _);
        _pidProbedAt.TryRemove(processId, out _);
        return $"pid-{processId}";
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags,
        System.Text.StringBuilder exeName, ref int size);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public static IPAddress ParseIPv6Addr(SharpDivert.IPv6Addr addr)
    {
        Span<byte> raw = stackalloc byte[16];
        unsafe
        {
            var ptr = (byte*)&addr;
            for (int i = 0; i < 16; i++)
                raw[i] = ptr[i];
        }

        // WinDivert stores IPv4 addresses in the 16-byte flow address as:
        // [rev4][FF FF 00 00][0 x8] (see windivert_get_ipv4_addr in the driver)
        bool isIpv4 = raw[4] == 0xFF && raw[5] == 0xFF && raw[6] == 0x00 && raw[7] == 0x00;
        for (int i = 8; i < 16 && isIpv4; i++)
        {
            if (raw[i] != 0) isIpv4 = false;
        }

        if (isIpv4)
        {
            Span<byte> v4 = stackalloc byte[4];
            for (int i = 0; i < 4; i++)
                v4[i] = raw[3 - i];
            return new IPAddress(v4);
        }

        // IPv6 addresses are stored byte-reversed (windivert_get_ipv6_addr)
        Span<byte> v6 = stackalloc byte[16];
        for (int i = 0; i < 16; i++)
            v6[i] = raw[15 - i];
        return new IPAddress(v6);
    }
}