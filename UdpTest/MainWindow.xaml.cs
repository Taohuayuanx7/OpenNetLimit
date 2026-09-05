using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;

namespace UdpTest;

public partial class MainWindow : Window
{
    private const int BasePort = 40000;
    private const int EchoPort = 41000;
    private static readonly byte[] Ping = new byte[4];
    private static readonly byte[] Data = new byte[1400];

    private CancellationTokenSource? _cts;
    private Task? _task;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (FlowsBox is null || ParamLabel is null) return;

        bool rateMode = ModeSend.IsChecked == true || ModeRecv.IsChecked == true;
        FlowsBox.IsEnabled = rateMode;
        ParamLabel.Text = rateMode ? "速率(KB/s):" : "连接数量:";
    }

    private static int Parse(string text, int fallback) =>
        int.TryParse(text, out var value) && value > 0 ? value : fallback;

    private void Log(string message) =>
        Dispatcher.Invoke(() => LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n"));

    private void SetRate(string text) =>
        Dispatcher.Invoke(() => RateText.Text = text);

    private void Finish() =>
        Dispatcher.Invoke(() =>
        {
            if (_cts is null)
            {
                StartButton.Content = "开始";
            }
        });

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is not null)
        {
            _cts.Cancel();
            Log("正在停止...");
            return;
        }

        int param = Parse(ParamBox.Text, 0);
        int seconds = Parse(SecondsBox.Text, 0);
        int flows = Parse(FlowsBox.Text, 4);
        if (param <= 0 || seconds <= 0 || flows <= 0)
        {
            MessageBox.Show(this, "请输入有效的数字参数。", "UDP 测试工具", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _cts = new CancellationTokenSource();
        StartButton.Content = "停止";
        RateText.Text = "0.0 KB/s";

        var token = _cts.Token;
        if (ModeFlows.IsChecked == true)
        {
            Log($"开始连接数测试: {param} 个连接, {seconds} 秒");
            _task = Task.Run(() => RunFlows(param, seconds, token));
        }
        else
        {
            bool upload = ModeSend.IsChecked == true;
            Log($"开始{(upload ? "上行" : "下行")}限速测试: {param} KB/s, {flows} 个并发, {seconds} 秒");
            _task = Task.Run(() => RunRate(param, seconds, flows, upload, token));
        }
    }

    private void RunFlows(int count, int seconds, CancellationToken ct)
    {
        var echo = StartEcho(count);
        var clients = new List<UdpClient>();
        long[] echoed = new long[count];

        var threads = new List<Thread>();
        for (int i = 0; i < count; i++)
        {
            int index = i;
            var thread = new Thread(() =>
            {
                var buffer = new byte[65536];
                while (true)
                {
                    try
                    {
                        var n = clients[index].Client.Receive(buffer);
                        Interlocked.Add(ref echoed[index], n);
                    }
                    catch
                    {
                        break;
                    }
                }
            })
            { IsBackground = true };
            threads.Add(thread);
        }

        try
        {
            for (int i = 0; i < count; i++)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var c = new UdpClient();
                    c.Connect(IPAddress.Loopback, EchoPort + i);
                    c.Send(Ping, Ping.Length);
                    clients.Add(c);

                    Log($"已建立第 {i + 1} 个 UDP 连接 (127.0.0.1:{EchoPort + i} 回声)");
                }
                catch (SocketException ex)
                {
                    Log($"第 {i + 1} 个连接失败: {ex.Message}");
                }

                Thread.Sleep(200);
            }

            foreach (var t in threads) t.Start();

            Log($"共建立 {clients.Count} 个连接, 保活 {seconds} 秒");
            Log("提示: 在 NetMeter 给 udptest 设置 UDP 上限, 超限的新连接会被拦截(状态栏拦截计数增加)");

            var sw = Stopwatch.StartNew();
            int lastKeep = 0;
            bool verified = false;
            while (sw.Elapsed.TotalSeconds < seconds && !ct.IsCancellationRequested)
            {
                if ((int)sw.Elapsed.TotalSeconds - lastKeep >= 2)
                {
                    lastKeep = (int)sw.Elapsed.TotalSeconds;
                    foreach (var c in clients)
                    {
                        try { c.Send(Ping, Ping.Length); } catch { }
                    }
                }

                // After a few keepalive rounds, report how many connections
                // actually deliver data. A blocked flow may leak one or two
                // early datagrams before the owner snapshot catches up, so
                // sustained delivery (>= 3 datagrams) is required.
                if (!verified && sw.Elapsed.TotalSeconds >= 8 && clients.Count > 0)
                {
                    verified = true;
                    int ok = 0;
                    for (int i = 0; i < clients.Count; i++)
                    {
                        bool alive = Interlocked.Read(ref echoed[i]) >= 3;
                        if (alive) ok++;
                        Log($"  连接 {i + 1} (端口 {EchoPort + i}): {(alive ? "数据正常到达" : "数据被拦截/未到达")}");
                    }
                    Log($"验证结果: {ok}/{clients.Count} 个连接数据正常送达");
                }

                Thread.Sleep(50);
            }

            Log(ct.IsCancellationRequested ? "已停止" : "测试结束");
        }
        catch (Exception ex)
        {
            Log($"错误: {ex.Message}");
        }
        finally
        {
            foreach (var c in clients) c.Dispose();
            KillEcho(echo);
            _cts = null;
            Finish();
        }
    }

    private static Process? StartEcho(int count)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "udpecho.exe");
            if (!File.Exists(path)) return null;

            return Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = $"{EchoPort} {count} 600",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch
        {
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

    private void RunRate(int kbps, int seconds, int flows, bool upload, CancellationToken ct)
    {
        var receivers = CreateReceivers(flows);
        var senders = CreateSenders(flows);
        long received = 0;

        var threads = receivers.Select(r => new Thread(() =>
        {
            var buffer = new byte[65536];
            while (true)
            {
                try
                {
                    Interlocked.Add(ref received, r.Client.Receive(buffer));
                }
                catch
                {
                    break;
                }
            }
        })
        { IsBackground = true }).ToArray();
        foreach (var t in threads) t.Start();

        try
        {
            long target = (long)kbps * 1024;
            var sw = Stopwatch.StartNew();
            long sent = 0;
            long lastCount = 0;
            double lastReport = 0;

            while (sw.Elapsed.TotalSeconds < seconds && !ct.IsCancellationRequested)
            {
                long tickTarget = (long)(target * sw.Elapsed.TotalSeconds);
                long burst = Math.Min(tickTarget - sent, 64 * 1024);
                int idx = 0;
                while (burst >= Data.Length && sw.Elapsed.TotalSeconds < seconds && !ct.IsCancellationRequested)
                {
                    senders[idx % flows].Send(Data, Data.Length);
                    sent += Data.Length;
                    burst -= Data.Length;
                    idx++;
                }

                double t = sw.Elapsed.TotalSeconds;
                if (t - lastReport >= 1)
                {
                    long count = upload ? sent : Interlocked.Read(ref received);
                    double rate = (count - lastCount) / ((t - lastReport) * 1024);
                    Log($"实际{(upload ? "发送" : "接收")} {rate:0.0} KB/s (目标 {kbps} KB/s)");
                    SetRate($"{rate:0.0} KB/s");
                    lastCount = count;
                    lastReport = t;
                }

                Thread.Sleep(5);
            }

            Log(ct.IsCancellationRequested ? "已停止" : "测试结束");
        }
        catch (Exception ex)
        {
            Log($"错误: {ex.Message}");
        }
        finally
        {
            foreach (var s in senders) s.Dispose();
            foreach (var r in receivers) r.Dispose();
            _cts = null;
            Finish();
        }
    }

    private static List<UdpClient> CreateReceivers(int flows)
    {
        var list = new List<UdpClient>();
        for (int i = 0; i < flows; i++)
        {
            var r = new UdpClient();
            r.Client.Bind(new IPEndPoint(IPAddress.Loopback, BasePort + i));
            list.Add(r);
        }
        return list;
    }

    private static List<UdpClient> CreateSenders(int flows)
    {
        var list = new List<UdpClient>();
        for (int i = 0; i < flows; i++)
        {
            var s = new UdpClient();
            s.Connect(IPAddress.Loopback, BasePort + i);
            list.Add(s);
        }
        return list;
    }
}