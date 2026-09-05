using System.Windows;

namespace NetMeter;

public partial class LimitDialog : Window
{
    private readonly WinDivertEngine _engine;
    private readonly string _processName;
    private readonly Action _onChanged;

    public LimitDialog(WinDivertEngine engine, string processName, LimitsConfig? current, ProcessRow? row, Action onChanged)
    {
        InitializeComponent();

        _engine = engine;
        _processName = processName;
        _onChanged = onChanged;
        Title = L.Tr("ld.TitleWithName", processName);
        ProcessText.Text = processName;
        CurrentRateText.Text = row is null
            ? L.Tr("ld.CurrentRateNone")
            : L.Tr("ld.CurrentRate", row.DownRate.ToString("0.0"), row.UpRate.ToString("0.0"));
        DownLabel.Text = L.Tr("ld.Down");
        UpLabel.Text = L.Tr("ld.Up");
        TcpLabel.Text = L.Tr("ld.Tcp");
        UdpLabel.Text = L.Tr("ld.Udp");
        ZeroHint.Text = L.Tr("ld.ZeroHint");
        ClearButton.Content = L.Tr("ld.Clear");
        CancelButton.Content = L.Tr("ld.Cancel");
        ApplyButton.Content = L.Tr("ld.Apply");

        DownBox.Text = (current?.DownBytesPerSecond / 1024 ?? 0).ToString();
        UpBox.Text = (current?.UpBytesPerSecond / 1024 ?? 0).ToString();
        TcpBox.Text = (current?.MaxTcpConnections ?? 0).ToString();
        UdpBox.Text = (current?.MaxUdpFlows ?? 0).ToString();

        Loaded += (_, _) => DownBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(DownBox.Text, out var downKb) || downKb < 0)
        {
            ShowError(L.Tr("err.Down"));
            return;
        }
        if (!long.TryParse(UpBox.Text, out var upKb) || upKb < 0)
        {
            ShowError(L.Tr("err.Up"));
            return;
        }
        if (!int.TryParse(TcpBox.Text, out var maxTcp) || maxTcp < 0)
        {
            ShowError(L.Tr("err.Tcp"));
            return;
        }
        if (!int.TryParse(UdpBox.Text, out var maxUdp) || maxUdp < 0)
        {
            ShowError(L.Tr("err.Udp"));
            return;
        }

        _engine.SetLimits(_processName, new LimitsConfig
        {
            DownBytesPerSecond = downKb * 1024,
            UpBytesPerSecond = upKb * 1024,
            MaxTcpConnections = maxTcp,
            MaxUdpFlows = maxUdp
        });

        _onChanged();
        DialogResult = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _engine.ClearLimits(_processName);
        _onChanged();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string message) =>
        MessageBox.Show(this, message, L.Tr("app.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
}