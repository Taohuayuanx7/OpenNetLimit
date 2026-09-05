using System.Collections.Concurrent;
using System.Net;
using SharpDivert;

namespace NetMeter;

public sealed class WinDivertEngine : IDisposable
{
    private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "services", "lsass", "csrss", "wininit", "smss",
        "dns", "dhcp", "dnscache", "System", "ntoskrnl"
    };

    private readonly FlowTable _flowTable;
    private readonly OwnerResolver _ownerResolver = new();
    private readonly ConcurrentDictionary<string, LimitsConfig> _limits = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<FlowKey, DateTime> _blockedFlows = new();
    private long _flowEventCount;

    private WinDivert? _flowHandle;
    private WinDivert? _networkHandle;
    private CancellationTokenSource? _cts;
    private Task? _flowTask;
    private Task? _networkTask;
    private volatile bool _isRunning;
    private int _stopAnnounced;

    public bool IsRunning => _isRunning;
    public event Action? Stopped;
    public FlowTable FlowTable => _flowTable;
    public long TotalBlocked;
    public long TotalDropped;
    public long TotalPackets;
    public long TotalMatched;
    public long TotalUnmatched;
    public long TotalFlowEvents;

    public IReadOnlyDictionary<string, LimitsConfig> Limits => _limits;

    public WinDivertEngine(FlowTable flowTable)
    {
        _flowTable = flowTable;
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        // Release the previous (cancelled) CTS before creating a new one,
        // otherwise every Start/Stop cycle leaks a kernel event handle
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            OpenHandles();
        }
        catch (WinDivertException wd) when (wd.NativeErrorCode == 1058)
        {
            // Driver service exists but is disabled (e.g. stale install).
            // Delete it so SharpDivert's auto-install can recreate it, then retry once.
            _flowHandle?.Dispose();
            _networkHandle?.Dispose();
            _flowHandle = null;
            _networkHandle = null;
            DeleteDriverService();
            try
            {
                OpenHandles();
            }
            catch
            {
                _isRunning = false;
                throw;
            }
        }
        catch
        {
            _isRunning = false;
            _flowHandle?.Dispose();
            _networkHandle?.Dispose();
            _flowHandle = null;
            _networkHandle = null;
            throw;
        }

        _flowTask = Task.Factory.StartNew(() => FlowLoop(_cts.Token), _cts.Token,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        _networkTask = Task.Factory.StartNew(() => NetworkLoop(_cts.Token), _cts.Token,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private void OpenHandles()
    {
        // The flow layer requires BOTH the Sniff and RecvOnly flags
        // (see WinDivert sys/windivert.c IOCTL_WINDIVERT_INITIALIZE).
        _flowHandle = new WinDivert("true", WinDivert.Layer.Flow, 0,
            WinDivert.Flag.Sniff | WinDivert.Flag.RecvOnly);
        _networkHandle = new WinDivert("true", WinDivert.Layer.Network, 0, default);

        // Larger queues reduce packet loss (and thus under-counting) under
        // heavy traffic. WinDivert drops packets it cannot queue.
        try
        {
            _networkHandle.QueueLength = 16384;
            _networkHandle.QueueTime = 4096;
        }
        catch (WinDivertException)
        {
            // Non-fatal; defaults still work
        }
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint desiredAccess);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(IntPtr service);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    private static void DeleteDriverService()
    {
        const uint scManagerAllAccess = 0x000F003F;
        const uint serviceAllAccess = 0x000F01FF;

        var manager = OpenSCManager(null, null, scManagerAllAccess);
        if (manager == IntPtr.Zero) return;

        try
        {
            var service = OpenService(manager, "WinDivert", serviceAllAccess);
            if (service != IntPtr.Zero)
            {
                DeleteService(service);
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    public void Stop()
    {
        // Must not early-return on !_isRunning: after a self-stop the sibling
        // loop and handles are still alive and only Stop() can release them.
        _isRunning = false;
        _cts?.Cancel();
        if (_networkTask is not null && !_networkTask.Wait(TimeSpan.FromSeconds(3)))
            DiagLog.Warn("NetworkLoop exceeded 3s stop timeout");
        if (_flowTask is not null && !_flowTask.Wait(TimeSpan.FromSeconds(3)))
            DiagLog.Warn("FlowLoop exceeded 3s stop timeout");
        _networkHandle?.Dispose();
        _flowHandle?.Dispose();
        _networkHandle = null;
        _flowHandle = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void SetLimits(string processName, LimitsConfig config) =>
        _limits[processName] = config;

    public void ClearLimits(string processName) =>
        _limits.TryRemove(processName, out _);

    private void FlowLoop(CancellationToken ct)
    {
        var buffer = new Memory<byte>(new byte[65535]);
        var addrBuffer = new Memory<WinDivertAddress>(new WinDivertAddress[1]);
        int failures = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _flowHandle!.RecvEx(buffer.Span, addrBuffer.Span);
                failures = 0;
                ref var addr = ref addrBuffer.Span[0];
                var flow = addr.Flow;

                var protocol = flow.Protocol == 6 ? TransportProtocol.Tcp :
                               flow.Protocol == 17 ? TransportProtocol.Udp :
                               (TransportProtocol?)null;
                if (protocol is null) continue;

                var localAddr = FlowTable.ParseIPv6Addr(flow.LocalAddr);
                var remoteAddr = FlowTable.ParseIPv6Addr(flow.RemoteAddr);
                var flowKey = new FlowKey(protocol.Value, localAddr, flow.LocalPort, remoteAddr, flow.RemotePort);

                if (addr.Event == WinDivert.Event.FlowEstablished)
                {
                    if (protocol.Value == TransportProtocol.Udp)
                    {
                        // The driver cannot attribute loopback UDP flows to a
                        // process (it reports System/pid 0). Skip the event
                        // path for UDP: NetworkLoop backfills attribution via
                        // IP Helper, which resolves the true owner.
                        Interlocked.Increment(ref TotalFlowEvents);
                        continue;
                    }

                    var processName = _flowTable.ResolveProcessName(flow.ProcessId);
                    var state = _flowTable.GetOrAdd(flow.ProcessId, processName);

                    if (ProtectedProcesses.Contains(processName))
                    {
                        _flowTable.RegisterFlow(flowKey, flow.ProcessId, processName);
                    }
                    else
                    {
                        // Connection-limit decision at flow creation; blocked
                        // flows get their packets dropped in NetworkLoop.
                        TryAcquireFlow(flowKey, flow.ProcessId, state);
                    }

                    Interlocked.Increment(ref TotalFlowEvents);
                    if (++_flowEventCount % 2000 == 0) PurgeBlockedFlows();
                }
                else if (addr.Event == WinDivert.Event.FlowDeleted)
                {
                    _flowTable.UnregisterFlow(flowKey);
                    _blockedFlows.TryRemove(flowKey, out _);
                    _blockedFlows.TryRemove(flowKey.Reversed(), out _);
                    Interlocked.Increment(ref TotalFlowEvents);
                }
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (WinDivertException wd)
            {
                DiagLog.Warn($"FlowLoop WinDivert error {wd.NativeErrorCode}: {wd.Message}");
                if (++failures > 100)
                {
                    DiagLog.Error("FlowLoop failing repeatedly; stopping engine");
                    break;
                }
                Thread.Sleep(50);
            }
            catch (Exception ex)
            {
                DiagLog.Error($"FlowLoop unhandled: {ex.GetType().Name}: {ex.Message}");
                if (++failures > 100)
                {
                    DiagLog.Error("FlowLoop failing repeatedly; stopping engine");
                    break;
                }
                Thread.Sleep(50);
            }
        }

        if (_isRunning) OnLoopExited();
    }

    private void NetworkLoop(CancellationToken ct)
    {
        var buffer = new Memory<byte>(new byte[65535]);
        var addrBuffer = new Memory<WinDivertAddress>(new WinDivertAddress[1]);
        int failures = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var (recvLen, _) = _networkHandle!.RecvEx(buffer.Span, addrBuffer.Span);
                failures = 0;
                Interlocked.Increment(ref TotalPackets);
                ref var addr = ref addrBuffer.Span[0];
                var packet = buffer[..(int)recvLen];

                var parsed = ParsePacket(packet);
                if (parsed is null)
                {
                    Interlocked.Increment(ref TotalUnmatched);
                    _networkHandle.SendEx(packet.Span, addrBuffer.Span);
                    continue;
                }
                Interlocked.Increment(ref TotalMatched);

                var (flowKey, _) = parsed.Value;
                bool isOutbound = addr.Outbound;

                // Canonical key: local endpoint always first, so flow events,
                // owner registration and per-process sets all agree regardless
                // of packet direction.
                if (!isOutbound)
                    flowKey = flowKey.Reversed();

                var processId = _flowTable.LookupProcessId(flowKey);
                if (processId is null)
                {
                    // The FLOW layer cannot report flows that existed before the
                    // handle was opened; backfill attribution from IP Helper.
                    processId = _ownerResolver.Resolve(flowKey);
                    if (processId is null)
                    {
                        _networkHandle.SendEx(packet.Span, addrBuffer.Span);
                        continue;
                    }

                    var resolvedName = _flowTable.ResolveProcessName(processId.Value);
                    var newState = _flowTable.GetOrAdd(processId.Value, resolvedName);

                    // Check the connection limit BEFORE registering the flow;
                    // otherwise the registration would make the flow look
                    // known and the limit would never engage.
                    if (!ProtectedProcesses.Contains(resolvedName) &&
                        !TryAcquireFlow(flowKey, processId.Value, newState))
                    {
                        continue;
                    }
                }

                var state = _flowTable.GetState(processId.Value) ??
                    _flowTable.GetOrAdd(processId.Value, _flowTable.ResolveProcessName(processId.Value));
                _flowTable.TouchLastSeen(flowKey);

                // 1. Drop packets of flows that were blocked by the connection limit
                if (_blockedFlows.ContainsKey(flowKey) || _blockedFlows.ContainsKey(flowKey.Reversed()))
                {
                    Interlocked.Increment(ref TotalBlocked);
                    continue;
                }

                // 2. Enforce per-process bandwidth limits
                if (!ProtectedProcesses.Contains(state.ProcessName) &&
                    !EnforceRateLimits(state, packet.Length, isOutbound))
                {
                    continue;
                }

                // 3. Count actual throughput for the speed display (full packet length
                //    including IP/transport headers, matching what Task Manager reports)
                if (isOutbound)
                    state.AddUpBytes(packet.Length);
                else
                    state.AddDownBytes(packet.Length);
                state.Touch();

                _networkHandle.SendEx(packet.Span, addrBuffer.Span);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (WinDivertException wd)
            {
                DiagLog.Warn($"NetworkLoop WinDivert error {wd.NativeErrorCode}: {wd.Message}");
                if (++failures > 100)
                {
                    DiagLog.Error("NetworkLoop failing repeatedly; stopping engine");
                    break;
                }
                Thread.Sleep(10);
            }
            catch (Exception ex)
            {
                DiagLog.Error($"NetworkLoop unhandled: {ex.GetType().Name}: {ex.Message}");
                if (++failures > 100)
                {
                    DiagLog.Error("NetworkLoop failing repeatedly; stopping engine");
                    break;
                }
                Thread.Sleep(10);
            }
        }

        if (_isRunning) OnLoopExited();
    }

    private void OnLoopExited()
    {
        // A loop broke out without a Stop() request: the engine is in an
        // unrecoverable state. Cancel the sibling loop, mark the engine
        // stopped and notify the UI instead of silently busy-spinning
        // forever with open handles.
        _cts?.Cancel();
        _isRunning = false;
        if (Interlocked.Exchange(ref _stopAnnounced, 1) == 0)
        {
            DiagLog.Error("Engine stopped due to repeated failures");
            try
            {
                Stopped?.Invoke();
            }
            catch
            {
            }
        }
    }

    /// <summary>
/// Registers a new flow with its process owner, unless the process's
/// connection limit is already reached — in which case the flow is marked
/// blocked (its packets get dropped) and never counted.
/// </summary>
private bool TryAcquireFlow(FlowKey flowKey, uint processId, ProcessState state)
{
    if (_limits.TryGetValue(state.ProcessName, out var config) && config.HasConnectionLimit)
    {
        if (flowKey.Protocol == TransportProtocol.Udp &&
            (flowKey.LocalPort == 53 || flowKey.RemotePort == 53))
        {
            // Never count/limit DNS (port 53) flows
            _flowTable.RegisterFlow(flowKey, processId, state.ProcessName);
            return true;
        }

        var set = flowKey.Protocol == TransportProtocol.Tcp ? state.TcpFlows : state.UdpFlows;
        if (set.ContainsKey(flowKey) || set.ContainsKey(flowKey.Reversed()))
        {
            _flowTable.RegisterFlow(flowKey, processId, state.ProcessName);
            return true;
        }

        int max = flowKey.Protocol == TransportProtocol.Tcp
            ? config.MaxTcpConnections
            : config.MaxUdpFlows;

        if (max > 0 && set.Count >= max)
        {
            _blockedFlows[flowKey] = DateTime.UtcNow;
            Interlocked.Increment(ref TotalBlocked);
            return false;
        }
    }

    _flowTable.RegisterFlow(flowKey, processId, state.ProcessName);
    return true;
}

    private void PurgeBlockedFlows()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-30);
        foreach (var kvp in _blockedFlows)
        {
            if (kvp.Value < cutoff)
                _blockedFlows.TryRemove(kvp.Key, out _);
        }
    }

    public string GetDiag()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"flows={TotalFlowEvents} pkt={TotalPackets} matched={TotalMatched} unmatched={TotalUnmatched} ");
        foreach (var state in _flowTable.States.Values)
        {
            sb.Append($"{state.ProcessName}:tcp={state.TcpFlows.Count}/udp={state.UdpFlows.Count}/d={state.DownRate}/u={state.UpRate} ");
        }
        sb.Append($"blocked={_blockedFlows.Count}");
        sb.Append(" " + _ownerResolver.GetStats());
        return sb.ToString();
    }

    private bool EnforceRateLimits(ProcessState state, int packetLength, bool isOutbound)
    {
        if (!_limits.TryGetValue(state.ProcessName, out var config) || !config.HasRateLimit)
            return true;

        long limit = isOutbound ? config.UpBytesPerSecond : config.DownBytesPerSecond;
        if (limit <= 0) return true;

        var bucket = isOutbound
            ? (state.UpBucket ??= new TokenBucket(limit))
            : (state.DownBucket ??= new TokenBucket(limit));
        bucket.Rate = limit;

        // Tokens are consumed using the full packet length (same accounting
        // basis as the displayed rate), so the measured rate matches the
        // configured limit instead of drifting above it.
        if (!bucket.TryConsume(packetLength))
        {
            Interlocked.Increment(ref TotalDropped);
            return false;
        }

        return true;
    }

    private static unsafe (FlowKey, int)? ParsePacket(Memory<byte> packet)
    {
        var parser = new WinDivertPacketParser(packet);
        foreach (var result in parser)
        {
            IPAddress srcAddr, dstAddr;
            if (result.IPv4Hdr != null)
            {
                Span<byte> srcBytes = stackalloc byte[4];
                Span<byte> dstBytes = stackalloc byte[4];
                new ReadOnlySpan<byte>(&result.IPv4Hdr->SrcAddr, 4).CopyTo(srcBytes);
                new ReadOnlySpan<byte>(&result.IPv4Hdr->DstAddr, 4).CopyTo(dstBytes);
                srcAddr = new IPAddress(srcBytes);
                dstAddr = new IPAddress(dstBytes);
            }
            else if (result.IPv6Hdr != null)
            {
                Span<byte> srcBytes = stackalloc byte[16];
                Span<byte> dstBytes = stackalloc byte[16];
                new ReadOnlySpan<byte>(&result.IPv6Hdr->SrcAddr, 16).CopyTo(srcBytes);
                new ReadOnlySpan<byte>(&result.IPv6Hdr->DstAddr, 16).CopyTo(dstBytes);
                srcAddr = new IPAddress(srcBytes);
                dstAddr = new IPAddress(dstBytes);
            }
            else
            {
                return null;
            }

            TransportProtocol protocol;
            ushort srcPort, dstPort;
            if (result.TCPHdr != null)
            {
                protocol = TransportProtocol.Tcp;
                srcPort = result.TCPHdr->SrcPort;
                dstPort = result.TCPHdr->DstPort;
            }
            else if (result.UDPHdr != null)
            {
                protocol = TransportProtocol.Udp;
                srcPort = result.UDPHdr->SrcPort;
                dstPort = result.UDPHdr->DstPort;
            }
            else
            {
                return null;
            }

            return (new FlowKey(protocol, srcAddr, srcPort, dstAddr, dstPort), result.Data.Length);
        }

        return null;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}