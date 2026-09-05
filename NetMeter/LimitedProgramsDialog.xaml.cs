using System.Windows;

namespace NetMeter;

public sealed class LimitRow
{
    public string Name { get; }
    public long DownKb { get; }
    public long UpKb { get; }
    public int Tcp { get; }
    public int Udp { get; }
    public string Status { get; }
    public LimitsConfig Cfg { get; }

    public LimitRow(string name, LimitsConfig cfg, bool isRunning)
    {
        Name = name;
        DownKb = cfg.DownBytesPerSecond / 1024;
        UpKb = cfg.UpBytesPerSecond / 1024;
        Tcp = cfg.MaxTcpConnections;
        Udp = cfg.MaxUdpFlows;
        Status = isRunning ? L.Tr("lp.Running") : L.Tr("lp.NotRunning");
        Cfg = cfg;
    }
}

public partial class LimitedProgramsDialog : Window
{
    private readonly WinDivertEngine _engine;
    private readonly Action _onChanged;
    private readonly Func<string, bool> _isProcessRunning;

    public LimitedProgramsDialog(WinDivertEngine engine, Action onChanged, Func<string, bool> isProcessRunning)
    {
        InitializeComponent();
        _engine = engine;
        _onChanged = onChanged;
        _isProcessRunning = isProcessRunning;
        Title = L.Tr("lp.Title");
        HintText.Text = L.Tr("lp.Hint");
        ColName.Header = L.Tr("col.Process");
        ColStatus.Header = L.Tr("lp.Status");
        ColDown.Header = L.Tr("lp.DownKb");
        ColUp.Header = L.Tr("lp.UpKb");
        ColTcp.Header = L.Tr("col.TcpLimit");
        ColUdp.Header = L.Tr("col.UdpLimit");
        EditButton.Content = L.Tr("lp.Adjust");
        DeleteButton.Content = L.Tr("lp.Delete");
        CloseButton.Content = L.Tr("lp.Close");
        RefreshGrid();
    }

    private void RefreshGrid() =>
        Grid.ItemsSource = _engine.Limits
            .Select(kvp => new LimitRow(kvp.Key, kvp.Value, _isProcessRunning(kvp.Key)))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not LimitRow row)
            return;

        var dialog = new LimitDialog(_engine, row.Name, row.Cfg, null, _onChanged) { Owner = this };
        if (dialog.ShowDialog() == true)
            RefreshGrid();
    }

    private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is LimitRow)
            Edit_Click(sender, e);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not LimitRow row)
            return;

        _engine.ClearLimits(row.Name);
        _onChanged();
        RefreshGrid();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}