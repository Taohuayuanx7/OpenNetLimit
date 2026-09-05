using System.Net;
using System.Net.Sockets;

// Usage: udpecho <startPort> <count> [timeoutSeconds]
var startPort = int.Parse(args[0]);
var count = int.Parse(args[1]);
var timeoutSec = args.Length > 2 ? int.Parse(args[2]) : 600;

var sockets = new List<UdpClient>();
for (int i = 0; i < count; i++)
{
    var u = new UdpClient();
    u.Client.Bind(new IPEndPoint(IPAddress.Loopback, startPort + i));
    sockets.Add(u);
}

var threads = sockets.Select(u => new Thread(() =>
{
    var buffer = new byte[65536];
    while (true)
    {
        try
        {
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            var n = u.Client.ReceiveFrom(buffer, ref remote);
            u.Client.SendTo(buffer, 0, n, SocketFlags.None, remote);
        }
        catch
        {
            break;
        }
    }
})
{ IsBackground = true }).ToArray();

foreach (var t in threads) t.Start();
Thread.Sleep(timeoutSec * 1000);
foreach (var u in sockets) u.Dispose();