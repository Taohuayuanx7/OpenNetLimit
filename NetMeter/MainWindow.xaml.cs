using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SharpDivert;

namespace NetMeter;

public sealed class ProcessRow : INotifyPropertyChanged
{
    public uint Pid { get; }
    public string Name { get; }

    private int _tcpCount;
    private int _udpCount;
    private double _downRate;
    private double _upRate;
    private long _downLimitKb;
    private long _upLimitKb;
    private int _tcpLimit;
    private int _udpLimit;

    public int TcpCount { get => _tcpCount; set { if (_tcpCount != value) { _tcpCount = value; OnPropertyChanged(nameof(TcpCount)); } } }
    public int UdpCount { get => _udpCount; set { if (_udpCount != value) { _udpCount = value; OnPropertyChanged(nameof(UdpCount)); } } }
    public double DownRate { get => _downRate; set { if (_downRate != value) { _downRate = value; OnPropertyChanged(nameof(DownRate)); } } }
    public double UpRate { get => _upRate; set { if (_upRate != value) { _upRate = value; OnPropertyChanged(nameof(UpRate)); } } }
    public long DownLimitKb { get => _downLimitKb; set { if (_downLimitKb != value) { _downLimitKb = value; OnPropertyChanged(nameof(DownLimitKb)); } } }
    public long UpLimitKb { get => _upLimitKb; set { if (_upLimitKb != value) { _upLimitKb = value; OnPropertyChanged(nameof(UpLimitKb)); } } }
    public int TcpLimit { get => _tcpLimit; set { if (_tcpLimit != value) { _tcpLimit = value; OnPropertyChanged(nameof(TcpLimit)); } } }
    public int UdpLimit { get => _udpLimit; set { if (_udpLimit != value) { _udpLimit = value; OnPropertyChanged(nameof(UdpLimit)); } } }

    public ProcessRow(ProcessState state, LimitsConfig? cfg)
    {
        Pid = state.ProcessId;
        Name = state.ProcessName;
        Update(state, cfg);
    }

    public void Update(ProcessState state, LimitsConfig? cfg)
    {
        TcpCount = state.TcpFlowCount;
        UdpCount = state.UdpFlowCount;
        DownRate = state.DownRate / 1024.0;
        UpRate = state.UpRate / 1024.0;
        DownLimitKb = cfg?.DownBytesPerSecond / 1024 ?? 0;
        UpLimitKb = cfg?.UpBytesPerSecond / 1024 ?? 0;
        TcpLimit = cfg?.MaxTcpConnections ?? 0;
        UdpLimit = cfg?.MaxUdpFlows ?? 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class MainWindow : Window
{
    private readonly FlowTable _flowTable = new();
    private readonly WinDivertEngine _engine;
    private readonly LimitsStore _store = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _filterDebounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly ObservableCollection<ProcessRow> _rows = new();
    private DateTime _lastTick = DateTime.UtcNow;
    private bool _running;
    private bool _suppressLangEvent;
    private string? _statusKey;
    private string? _sortMemberPath;
    private ListSortDirection _sortDirection;

    public MainWindow()
    {
        // XAML's SelectedIndex="0" must not overwrite the persisted language
        // before the ctor has selected the item matching L.Lang.
        _suppressLangEvent = true;
        InitializeComponent();
        Grid.ItemsSource = _rows;
        _engine = new WinDivertEngine(_flowTable);
        _engine.Stopped += OnEngineStopped;
        foreach (var (name, cfg) in _store.Load())
            _engine.SetLimits(name, cfg);
        _timer.Tick += (_, _) =>
        {
            try
            {
                Refresh();
            }
            catch
            {
                // UI refresh must never take the app down
            }
        };
        _filterDebounce.Tick += (_, _) =>
        {
            _filterDebounce.Stop();
            if (_running) Refresh();
        };
        RefreshCombo.SelectionChanged += (_, _) =>
        {
            if (RefreshCombo.SelectedItem is ComboBoxItem item && double.TryParse((string)item.Tag, out var seconds))
            {
                _timer.Interval = TimeSpan.FromSeconds(seconds);
                _lastTick = DateTime.UtcNow;
            }
        };
        foreach (ComboBoxItem item in LangCombo.Items)
        {
            if ((string)item.Tag == L.Lang)
            {
                LangCombo.SelectedItem = item;
                break;
            }
        }
        _suppressLangEvent = false;
        L.Changed += ApplyLanguage;
        ApplyLanguage();
        Loaded += (_, _) => StartEngine();
        Closed += (_, _) =>
        {
            _timer.Stop();
            PersistLimits();
            _engine.Dispose();
            try
            {
                StoragePaths.SafeAppend(StoragePaths.DiagPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] WINDOW CLOSED\n");
            }
            catch
            {
            }
        };
    }

    private void OnEngineStopped()
    {
        // Fired from a loop thread; marshal back to the UI thread
        Dispatcher.BeginInvoke(() =>
        {
            if (!_running) return;
            _running = false;
            _timer.Stop();
            _statusKey = "main.Stopped";
            ApplyLanguage();
        });
    }

    private void ApplyLanguage()
    {
        Title = L.Tr("app.Title");
        ColPid.Header = L.Tr("col.Pid");
        ColProcess.Header = L.Tr("col.Process");
        ColTcp.Header = L.Tr("col.Tcp");
        ColUdp.Header = L.Tr("col.Udp");
        ColDown.Header = L.Tr("col.Down");
        ColUp.Header = L.Tr("col.Up");
        ColDownLimit.Header = L.Tr("col.DownLimit");
        ColUpLimit.Header = L.Tr("col.UpLimit");
        ColTcpLimit.Header = L.Tr("col.TcpLimit");
        ColUdpLimit.Header = L.Tr("col.UdpLimit");
        ToggleButton.Content = _running ? L.Tr("main.ToggleStop") : L.Tr("main.ToggleStart");
        LimitedProgramsButton.Content = L.Tr("main.LimitedPrograms");
        FilterLabel.Text = L.Tr("main.Filter");
        RefreshLabel.Text = L.Tr("main.Refresh");
        LangLabel.Text = L.Tr("main.Language");
        BlockedLabel.Text = L.Tr("main.Blocked");
        DroppedLabel.Text = L.Tr("main.Dropped");
        StatusText.Text = _statusKey is null ? L.Tr("main.NotStarted") : L.Tr(_statusKey);
        if (_statusKey is not null)
            Title = $"{L.Tr("app.Title")} [{L.Tr(_statusKey)}]";
    }

    private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLangEvent) return;
        if (LangCombo.SelectedItem is ComboBoxItem item && (string)item.Tag is { } code)
            L.Set(code);
    }

    private void PersistLimits() => _store.Save(_engine.Limits);

    private void StartEngine()
    {
        try
        {
            _engine.Start();
            _running = true;
            _timer.Start();
            _statusKey = "main.Running";
            ApplyLanguage();
        }
        catch (Exception ex)
        {
            _running = false;
            _statusKey = "main.StartFailed";
            ApplyLanguage();
            var detail = ex is WinDivertException wd && wd.WinDivertNativeMethod is { } fn
                ? $"{ex.Message}{L.Tr("main.WinDivertFunc", fn)}"
                : ex.Message;
            MessageBox.Show(this,
                L.Tr("main.StartError", detail),
                L.Tr("app.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            _engine.Stop();
            _timer.Stop();
            _running = false;
            _statusKey = "main.Stopped";
            ApplyLanguage();
        }
        else
        {
            StartEngine();
        }
    }

    private void LimitedPrograms_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LimitedProgramsDialog(_engine, PersistLimits, IsProcessRunning) { Owner = this };
        dialog.ShowDialog();
    }

    private bool IsProcessRunning(string name) => _flowTable.TrackedNames.Contains(name);

    private void Refresh()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastTick).TotalSeconds;
        _lastTick = now;

        // Housekeeping: purge stale flow attribution (bounded per call) and
        // retire long-idle processes so tables cannot grow without bound
        _flowTable.PurgeStale(TimeSpan.FromMinutes(5));
        var retireCutoff = now.AddMinutes(-10).Ticks;
        foreach (var pid in _flowTable.States.Keys.ToList())
        {
            if (_flowTable.States.TryGetValue(pid, out var st) &&
                st.LastActivityTicks < retireCutoff &&
                st.TcpFlowCount == 0 && st.UdpFlowCount == 0)
            {
                _flowTable.RetirePid(pid);
            }
        }

        var filter = FilterBox.Text.Trim();
        var cutoff = now.AddSeconds(-60).Ticks;
        _activePids.Clear();
        WriteDiag();
        foreach (var state in _flowTable.GetTrackedStates())
        {
            state.TickRates(elapsed);
            if (state.LastActivityTicks < cutoff)
                continue;
            if (filter.Length > 0 &&
                !state.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            _engine.Limits.TryGetValue(state.ProcessName, out var cfg);
            ApplyRow(state, cfg);
        }

        ReconcileRows();
        BlockedText.Text = _engine.TotalBlocked.ToString();
        DroppedText.Text = _engine.TotalDropped.ToString();
    }

    private readonly HashSet<uint> _activePids = new();
    private readonly Dictionary<uint, ProcessRow> _rowByPid = new();

    private void ApplyRow(ProcessState state, LimitsConfig? cfg)
    {
        _activePids.Add(state.ProcessId);
        if (_rowByPid.TryGetValue(state.ProcessId, out var row))
        {
            row.Update(state, cfg);
            return;
        }
        var created = new ProcessRow(state, cfg);
        _rowByPid[state.ProcessId] = created;
        _rows.Add(created);
    }

    /// <summary>
    /// Keeps the grid consistent with the current sort without rebuilding
    /// ItemsSource: rows that disappeared this cycle are removed, rows that
    /// moved are relocated, and existing row instances (and thus
    /// selection/scroll) are preserved.
    /// </summary>
    private void ReconcileRows()
    {
        var desired = SortRows(_rows.Where(r => _activePids.Contains(r.Pid)).ToList());

        var desiredPids = new HashSet<uint>(desired.Select(r => r.Pid));
        for (int i = _rows.Count - 1; i >= 0; i--)
        {
            if (!desiredPids.Contains(_rows[i].Pid))
            {
                _rowByPid.Remove(_rows[i].Pid);
                _rows.RemoveAt(i);
            }
        }

        for (int i = 0; i < desired.Count; i++)
        {
            if (_rows[i] == desired[i]) continue;
            int current = _rows.IndexOf(desired[i]);
            if (current >= 0 && current != i)
                _rows.Move(current, i);
        }
    }

    private List<ProcessRow> SortRows(List<ProcessRow> rows)
    {
        if (_sortMemberPath is null)
            return rows.OrderByDescending(r => r.DownRate + r.UpRate).ToList();

        var property = typeof(ProcessRow).GetProperty(_sortMemberPath);
        if (property is null)
            return rows.OrderByDescending(r => r.DownRate + r.UpRate).ToList();

        return _sortDirection == ListSortDirection.Ascending
            ? rows.OrderBy(r => property.GetValue(r)).ToList()
            : rows.OrderByDescending(r => property.GetValue(r)).ToList();
    }

    private void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;

        _sortDirection = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        e.Column.SortDirection = _sortDirection;
        _sortMemberPath = e.Column.SortMemberPath;

        Refresh();
    }

    private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Grid.SelectedItem is not ProcessRow row)
            return;

        _engine.Limits.TryGetValue(row.Name, out var cfg);
        var dialog = new LimitDialog(_engine, row.Name, cfg, row, PersistLimits) { Owner = this };
        if (dialog.ShowDialog() == true)
            StatusText.Text = L.Tr("main.AppliedLimit", row.Name);
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _filterDebounce.Stop();
        _filterDebounce.Start();
    }

    private void WriteDiag()
    {
        try
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {_engine.GetDiag()}";
            _ = Task.Run(() =>
            {
                try
                {
                    var info = new System.IO.FileInfo(StoragePaths.DiagPath);
                    if (info.Exists && info.Length > 300_000)
                        StoragePaths.SafeWrite(StoragePaths.DiagPath, line + Environment.NewLine);
                    else
                        StoragePaths.SafeAppend(StoragePaths.DiagPath, line + Environment.NewLine);
                }
                catch
                {
                }
            });
        }
        catch
        {
        }
    }
}