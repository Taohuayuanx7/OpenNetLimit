using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace UdpTest;

/// <summary>Console self-test mode: udptest --headless [flows] [seconds].</summary>
public static class Headless
{
    private const int BasePort = 40000;
    private const int EchoPort = 41000;

    public static int Run(string[] args)
    {
        int count = args.Length > 1 && int.TryParse(args[1], out var c) ? c : 10;
        int seconds = args.Length > 2 && int.TryParse(args[2], out var sec) ? sec : 9;

        Console.WriteLine($"[self-test] flows={count} seconds={seconds} pid={Environment.ProcessId}");
        var limitFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetMeter", "limits.json");
        Console.WriteLine($"[self-test] limit file exists: {File.Exists(limitFile)}");
        if (File.Exists(limitFile))
            Console.WriteLine("[self-test] " + File.ReadAllText(limitFile).Replace("\n", " "));

        var echo = StartEcho(count);
        if (echo is not null) Thread.Sleep(300);
        var clients = new List<UdpClient>();
        long[] echoed = new long[count];
        var threads = new List<Thread>();

        for (int i = 0; i < count; i++)
        {
            int index = i;
            threads.Add(new Thread(() =>
            {
                var buffer = new byte[65536];
                while (true)
                {
                    try
                    {
                        Interlocked.Add(ref echoed[index], clients[index].Client.Receive(buffer));
                    }
                    catch
                    {
                        break;
                    }
                }
            })
            { IsBackground = true });
        }

        try
        {
            for (int i = 0; i < count; i++)
            {
                var client = new UdpClient();
                client.Connect(IPAddress.Loopback, EchoPort + i);
                client.Send(new byte[4], 4);
                clients.Add(client);
                Thread.Sleep(200);
            }

            foreach (var t in threads) t.Start();
            Console.WriteLine($"[self-test] established {count} flows, self pid = {Environment.ProcessId}");

            DumpUdpOwners();

            var sw = Stopwatch.StartNew();
            int lastKeep = 0;
            while (sw.Elapsed.TotalSeconds < seconds)
            {
                if ((int)sw.Elapsed.TotalSeconds - lastKeep >= 2)
                {
                    lastKeep = (int)sw.Elapsed.TotalSeconds;
                    foreach (var cl in clients)
                    {
                        try { cl.Send(new byte[4], 4); } catch { }
                    }
                }

                if (sw.Elapsed.TotalSeconds >= 8)
                {
                    int ok = 0;
                    for (int i = 0; i < clients.Count; i++)
                    {
                        bool alive = Interlocked.Read(ref echoed[i]) >= 3;
                        if (alive) ok++;
                        Console.WriteLine($"  conn {i + 1} port {EchoPort + i}: {(alive ? "DELIVERED" : "BLOCKED")} (received {Interlocked.Read(ref echoed[i])})");
                    }
                    Console.WriteLine($"[self-test] RESULT: {ok}/{clients.Count} delivered");
                    break;
                }

                Thread.Sleep(50);
            }
        }
        finally
        {
            foreach (var cl in clients) cl.Dispose();
            KillEcho(echo);
        }

        return 0;
    }

    public static int Rate(string[] args)
    {
        int kbps = args.Length > 1 && int.TryParse(args[1], out var k) ? k : 2000;
        int seconds = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 12;
        int flows = args.Length > 3 && int.TryParse(args[3], out var f) ? f : 4;
        var data = new byte[1400];

        Console.WriteLine($"[rate-test] target={kbps} KB/s seconds={seconds} flows={flows} pid={Environment.ProcessId}");

        var echo = StartEcho(flows);
        if (echo is not null) Thread.Sleep(300);
        var clients = new List<UdpClient>();
        long received = 0;
        var threads = new List<Thread>();

        for (int i = 0; i < flows; i++)
        {
            int index = i;
            threads.Add(new Thread(() =>
            {
                var buffer = new byte[65536];
                while (true)
                {
                    try
                    {
                        Interlocked.Add(ref received, clients[index].Client.Receive(buffer));
                    }
                    catch
                    {
                        break;
                    }
                }
            })
            { IsBackground = true });
        }

        try
        {
            for (int i = 0; i < flows; i++)
            {
                var client = new UdpClient();
                client.Connect(IPAddress.Loopback, EchoPort + i);
                client.Send(new byte[4], 4);
                clients.Add(client);
            }

            foreach (var t in threads) t.Start();

            // Wait until the echo is confirmed ready: probe once and wait for
            // its reply (with timeout). Avoids losing the first seconds of
            // traffic to the echo process still starting up.
            var probe = clients[0];
            probe.Send(new byte[4], 4);
            var probeWait = Stopwatch.StartNew();
            var probeBuf = new byte[65536];
            bool echoReady = false;
            while (probeWait.Elapsed.TotalSeconds < 3 && !echoReady)
            {
                if (probe.Available > 0)
                {
                    probe.Client.Receive(probeBuf);
                    echoReady = true;
                }
                Thread.Sleep(20);
            }
            Console.WriteLine($"[rate-test] echo ready: {echoReady}");

            long target = (long)kbps * 1024;
            var sw = Stopwatch.StartNew();
            long sent = 0;
            long lastSent = 0;
            long lastRecv = 0;
            double lastReport = 0;

            while (sw.Elapsed.TotalSeconds < seconds)
            {
                long tickTarget = (long)(target * sw.Elapsed.TotalSeconds);
                long burst = Math.Min(tickTarget - sent, 256 * 1024);
                int idx = 0;
                while (burst >= data.Length)
                {
                    clients[idx % flows].Send(data, data.Length);
                    sent += data.Length;
                    burst -= data.Length;
                    idx++;
                }

                double t = sw.Elapsed.TotalSeconds;
                if (t - lastReport >= 2)
                {
                    long recv = Interlocked.Read(ref received);
                    Console.WriteLine($"  t={t:0.0}s actual sent {(sent - lastSent) / ((t - lastReport) * 1024):0.0} KB/s, recv {(recv - lastRecv) / ((t - lastReport) * 1024):0.0} KB/s");
                    lastSent = sent;
                    lastRecv = recv;
                    lastReport = t;
                }

                Thread.Sleep(5);
            }

            Console.WriteLine($"[rate-test] TOTAL sent={sent / 1024.0:0.0} KB, recv={Interlocked.Read(ref received) / 1024.0:0.0} KB");
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
            KillEcho(echo);
        }

        return 0;
    }

    private static Process? StartEcho(int count)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "udpecho.exe");
            if (!File.Exists(path))
            {
                Console.WriteLine("[self-test] WARNING: udpecho.exe not found, no delivery verification possible");
                return null;
            }

            return Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = $"{EchoPort} {count} 600",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[self-test] echo start failed: {ex.Message}");
            return null;
        }
    }

    private static void KillEcho(Process? echo)
    {
        if (echo is null) return;
        try
        {
            if (!echo.HasExited) echo.Kill();
        }
        catch
        {
        }
    }

    // ---- IP Helper UDP owner table dump (same layout as NetMeter.OwnerResolver) ----
    private const uint NoError = 0;
    private const uint ErrorInsufficientBuffer = 122;
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const uint UdpTableOwnerPid = 1;

    private static void DumpUdpOwners()
    {
        Console.WriteLine("[self-test] IP Helper UDP owner table entries for test ports:");
        DumpTable(AfInet, isV6: false);
        DumpTable(AfInet6, isV6: true);
    }

    private static unsafe void DumpTable(int family, bool isV6)
    {
        int size = 0;
        if (GetExtendedUdpTable(IntPtr.Zero, ref size, false, family, UdpTableOwnerPid, 0) != ErrorInsufficientBuffer || size <= 0)
            return;

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

                if ((localPort >= BasePort && localPort < BasePort + 32) ||
                    (localPort >= EchoPort && localPort < EchoPort + 32))
                {
                    string tag = localPort >= BasePort && localPort <= BasePort + 9 ? "client" :
                                 localPort >= EchoPort && localPort <= EchoPort + 9 ? "echo" : "other";
                    Console.WriteLine($"  {(isV6 ? "v6" : "v4")} {local}:{localPort} -> pid {pid} ({tag})");
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
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
    private static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int pdwSize,
        bool bOrder, int ulAf, uint TableClass, uint Reserved);
}