using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace NetMeter;

/// <summary>
/// Falls back to the Windows IP Helper TCP/UDP owner tables to attribute
/// packets to a process. The WinDivert FLOW layer cannot report flows that
/// existed before the handle was opened, so pre-existing connections would
/// otherwise never be counted (and never limited).
///
/// Tables are rebuilt on a background thread every second into an immutable
/// snapshot; the packet path only reads the current snapshot, so the
/// AllocHGlobal table queries never run on the network hot path.
/// </summary>
public sealed class OwnerResolver
{
    private const uint NoError = 0;
    private const uint ErrorInsufficientBuffer = 122;
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const uint TcpTableOwnerPidAll = 5;
    private const uint UdpTableOwnerPid = 1;

    private sealed class Snapshot
    {
        public readonly Dictionary<(TransportProtocol Protocol, IPAddress Address, ushort Port), uint> Owners;
        public readonly Dictionary<(TransportProtocol Protocol, ushort Port), uint> Wildcards;

        public Snapshot(
            Dictionary<(TransportProtocol Protocol, IPAddress Address, ushort Port), uint> owners,
            Dictionary<(TransportProtocol Protocol, ushort Port), uint> wildcards)
        {
            Owners = owners;
            Wildcards = wildcards;
        }
    }

    private volatile Snapshot _current = new(new(), new());
    private int _consecutiveFailures;

    public long ResolveCalls;
    public long ExactHits;
    public long WildcardHits;
    public long Misses;

    public OwnerResolver()
    {
        System.Threading.Tasks.Task.Run(RefreshLoop);
    }

    private async System.Threading.Tasks.Task RefreshLoop()
    {
        while (true)
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(1000);
                Refresh();
            }
            catch
            {
                // loop must never die
            }
        }
    }

    private void Refresh()
    {
        try
        {
            var owners = new Dictionary<(TransportProtocol Protocol, IPAddress Address, ushort Port), uint>();
            var wildcards = new Dictionary<(TransportProtocol Protocol, ushort Port), uint>();

            ReadTcpTable(AfInet, isV6: false, owners);
            ReadTcpTable(AfInet6, isV6: true, owners);
            ReadUdpTable(AfInet, isV6: false, owners, wildcards);
            ReadUdpTable(AfInet6, isV6: true, owners, wildcards);

            _current = new Snapshot(owners, wildcards);
            Interlocked.Exchange(ref _consecutiveFailures, 0);
        }
        catch (Exception ex)
        {
            if (Interlocked.Increment(ref _consecutiveFailures) == 3)
            {
                DiagLog.Error($"OwnerResolver refresh failing, keeping last good snapshot: {ex.Message}");
                Interlocked.Exchange(ref _consecutiveFailures, 0);
            }
        }
    }

    public uint? Resolve(FlowKey flowKey)
    {
        Interlocked.Increment(ref ResolveCalls);
        var snapshot = _current;

        // Local endpoint first: for an outbound packet the local socket is
        // the true owner. Most UDP apps bind 0.0.0.0, so fall back to a
        // port-only match on the local side before consulting the remote
        // endpoint (which is usually a different process).
        if (snapshot.Owners.TryGetValue((flowKey.Protocol, flowKey.LocalAddress, flowKey.LocalPort), out var pid))
        {
            Interlocked.Increment(ref ExactHits);
            return pid;
        }
        if (flowKey.Protocol == TransportProtocol.Udp &&
            snapshot.Wildcards.TryGetValue((TransportProtocol.Udp, flowKey.LocalPort), out pid))
        {
            Interlocked.Increment(ref WildcardHits);
            return pid;
        }
        if (snapshot.Owners.TryGetValue((flowKey.Protocol, flowKey.RemoteAddress, flowKey.RemotePort), out pid))
        {
            Interlocked.Increment(ref ExactHits);
            return pid;
        }
        if (flowKey.Protocol == TransportProtocol.Udp &&
            snapshot.Wildcards.TryGetValue((TransportProtocol.Udp, flowKey.RemotePort), out pid))
        {
            Interlocked.Increment(ref WildcardHits);
            return pid;
        }

        Interlocked.Increment(ref Misses);
        return null;
    }

    public string GetStats() =>
        $"resolver calls={ResolveCalls} exact={ExactHits} wildcard={WildcardHits} miss={Misses} owners={_current.Owners.Count}+{_current.Wildcards.Count}";

    private unsafe void ReadTcpTable(int family, bool isV6,
        Dictionary<(TransportProtocol Protocol, IPAddress Address, ushort Port), uint> owners)
    {
        int size = 0;
        if (GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TcpTableOwnerPidAll, 0) != ErrorInsufficientBuffer ||
            size <= 0)
        {
            return;
        }

        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(ptr, ref size, false, family, TcpTableOwnerPidAll, 0) != NoError)
                return;

            byte* p = (byte*)ptr.ToPointer();
            int count = *(int*)p;
            int stride = isV6 ? Marshal.SizeOf<MibTcp6RowOwnerPid>() : Marshal.SizeOf<MibTcpRowOwnerPid>();
            byte* row = p + 4;

            for (int i = 0; i < count; i++, row += stride)
            {
                IPAddress local, remote;
                ushort localPort, remotePort;
                uint pid;

                if (isV6)
                {
                    local = new IPAddress(ReadBytes(row, 16));
                    remote = new IPAddress(ReadBytes(row + 24, 16));
                    localPort = ReadNetworkPort(row + 20);
                    remotePort = ReadNetworkPort(row + 44);
                    pid = *(uint*)(row + 52);
                }
                else
                {
                    local = new IPAddress(ReadBytes(row + 4, 4));
                    remote = new IPAddress(ReadBytes(row + 12, 4));
                    localPort = ReadNetworkPort(row + 8);
                    remotePort = ReadNetworkPort(row + 16);
                    pid = *(uint*)(row + 20);
                }

                AddOwner(owners, null, TransportProtocol.Tcp, local, localPort, pid);
                AddOwner(owners, null, TransportProtocol.Tcp, remote, remotePort, pid);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private unsafe void ReadUdpTable(int family, bool isV6,
        Dictionary<(TransportProtocol Protocol, IPAddress Address, ushort Port), uint> owners,
        Dictionary<(TransportProtocol Protocol, ushort Port), uint> wildcards)
    {
        int size = 0;
        if (GetExtendedUdpTable(IntPtr.Zero, ref size, false, family, UdpTableOwnerPid, 0) != ErrorInsufficientBuffer ||
            size <= 0)
        {
            return;
        }

        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedUdpTable(ptr, ref size, false, family, UdpTableOwnerPid, 0) != NoError)
                return;

            byte* p = (byte*)ptr.ToPointer();
            int count = *(int*)p;
            int stride = isV6 ? Marshal.SizeOf<MibUdp6RowOwnerPid>() : Marshal.SizeOf<MibUdpRowOwnerPid>();
            byte* row = p + 4;

            for (int i = 0; i < count; i++, row += stride)
            {
                IPAddress local;
                ushort localPort;
                uint pid;

                if (isV6)
                {
                    local = new IPAddress(ReadBytes(row, 16));
                    localPort = ReadNetworkPort(row + 20);
                    pid = *(uint*)(row + 24);
                }
                else
                {
                    local = new IPAddress(ReadBytes(row, 4));
                    localPort = ReadNetworkPort(row + 4);
                    pid = *(uint*)(row + 8);
                }

                AddOwner(owners, wildcards, TransportProtocol.Udp, local, localPort, pid);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static void AddOwner(
        Dictionary<(TransportProtocol Protocol, IPAddress Address, ushort Port), uint> owners,
        Dictionary<(TransportProtocol Protocol, ushort Port), uint>? wildcards,
        TransportProtocol protocol, IPAddress address, ushort port, uint pid)
    {
        if (port == 0 || pid == 0 || address is null) return;

        if (protocol == TransportProtocol.Udp && IsWildcard(address))
            wildcards![(protocol, port)] = pid;
        else
            owners[(protocol, address, port)] = pid;
    }

    private static bool IsWildcard(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return address.Equals(IPAddress.Any);

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6Any)) return true;
            // IPv4-mapped wildcard ::ffff:0.0.0.0
            var bytes = address.GetAddressBytes();
            bool zero = true;
            for (int i = 0; i < 12; i++)
            {
                if (bytes[i] != 0) { zero = false; break; }
            }
            return zero && bytes[12] == 0xFF && bytes[13] == 0xFF && bytes[14] == 0 && bytes[15] == 0;
        }

        return false;
    }

    private static unsafe byte[] ReadBytes(byte* p, int length)
    {
        var result = new byte[length];
        for (int i = 0; i < length; i++)
            result[i] = p[i];
        return result;
    }

    private static unsafe ushort ReadNetworkPort(byte* p) => (ushort)((p[0] << 8) | p[1]);

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdpRowOwnerPid
    {
        public uint LocalAddr;
        public uint LocalPort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibUdp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize,
        bool bOrder, int ulAf, uint TableClass, uint Reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int pdwSize,
        bool bOrder, int ulAf, uint TableClass, uint Reserved);
}