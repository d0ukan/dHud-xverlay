using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DHud.Helpers;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>
/// Basit görev yöneticisi: hangi uygulama ne kadar CPU, RAM, disk ve (yönetici yetkisiyle) internet kullanıyor.
/// Aynı adlı işlemler tek satırda toplanır. Sütun başlığına tıklayınca o sütuna göre sıralanır; başlıkların altında sistem toplamı görünür.
/// Yalnızca görünürken 2 saniyede bir örnekler.
/// </summary>
public sealed class TaskManWidgetWindow : WidgetWindowBase
{
    private const int RowCount = 8;
    private const double CpuWidth = 50, GpuWidth = 50, RamWidth = 62, DiskWidth = 68, NetWidth = 68;
    private const double CardWidth = 440;

    private readonly ProcessMonitor _monitor = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Grid _header = new();
    private readonly Dictionary<ProcSort, (TextBlock Label, TextBlock Total)> _columns = [];
    private static readonly Dictionary<ProcSort, string> ColumnNames = new() { [ProcSort.Cpu] = "CPU", [ProcSort.Gpu] = "GPU", [ProcSort.Memory] = "RAM", [ProcSort.Disk] = "DISK", [ProcSort.Network] = "NET" };
    private readonly StackPanel _rows = new();
    private readonly TextBlock _gpu;
    private readonly TextBlock _hint;
    private ProcSort _sort = ProcSort.Cpu;
    private bool _descending = true;   // sayı sütunlarında önce en çok kaynak yiyen; aynı başlığa tekrar tıklayınca ters çevrilir
    private TextBlock? _nameHeader;
    private List<ProcRow> _all = [];
    private bool _busy;
    private bool _demo;

    private static readonly double[] ColumnWidths = [CpuWidth, GpuWidth, RamWidth, DiskWidth, NetWidth];

    private bool NetworkShown => _demo || _monitor.NetworkAvailable;

    private bool GpuShown => _demo || _monitor.GpuAvailable;

    protected override WidgetOptions Options => App.Current.Settings.Current.Extra(WidgetIds.TaskMan);

    protected override bool ZoomByWidth => true;

    public TaskManWidgetWindow()
    {
        Card.Width = CardWidth;
        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        root.Children.Add(TitleBar(Loc.T("W.TaskMan"), WidgetIds.TaskMan));

        _gpu = Subtle("");
        _gpu.Margin = new Thickness(0, 4, 0, 0);
        root.Children.Add(_gpu);

        BuildHeader();
        _header.Margin = new Thickness(0, 8, 0, 4);
        root.Children.Add(_header);
        root.Children.Add(_rows);

        _hint = Subtle("");
        _hint.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(_hint);
        Card.Child = root;

        RestorePosition(WidgetIds.TaskMan, 13, CardWidth);
        Render();

        _timer.Tick += async (_, _) => await RefreshAsync();
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible)
            {
                _monitor.Reset();
                _monitor.StartNetwork();
                _timer.Start();
                await RefreshAsync();
            }
            else
            {
                _timer.Stop();
                _monitor.StopNetwork();
            }
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _monitor.Dispose();
        };
        ApplyOptions();
    }

    /// <summary>Ekran görüntüsü üretimi için: örnek satırları çizer (ağ sütunu açıkmış gibi).</summary>
    internal void ShowDemo(List<ProcRow> rows)
    {
        _demo = true;
        _all = rows;
        Render();
    }

    private void BuildHeader()
    {
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 80 });
        foreach (var width in ColumnWidths)
        {
            _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        }

        _nameHeader = Subtle(Loc.T("W.TaskName"));
        _nameHeader.VerticalAlignment = VerticalAlignment.Top;
        _nameHeader.Cursor = Cursors.Hand;
        _nameHeader.ToolTip = Loc.T("W.TaskSort");
        _nameHeader.Background = System.Windows.Media.Brushes.Transparent;
        _nameHeader.MouseLeftButtonUp += (_, e) =>
        {
            SortBy(ProcSort.Name);
            e.Handled = true;
        };
        _header.Children.Add(_nameHeader);

        var sorts = new[] { (ProcSort.Cpu, "CPU"), (ProcSort.Gpu, "GPU"), (ProcSort.Memory, "RAM"), (ProcSort.Disk, "DISK"), (ProcSort.Network, "NET") };
        for (var i = 0; i < sorts.Length; i++)
        {
            var (sort, text) = sorts[i];
            var label = new TextBlock { Text = text, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Right };
            var total = new TextBlock { FontSize = 10.5, TextAlignment = TextAlignment.Right };
            total.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
            var cell = new StackPanel { Cursor = Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent, ToolTip = Loc.T("W.TaskSort") };
            cell.Children.Add(label);
            cell.Children.Add(total);
            cell.MouseLeftButtonUp += (_, e) =>
            {
                SortBy(sort);
                e.Handled = true;
            };
            Grid.SetColumn(cell, i + 1);
            _header.Children.Add(cell);
            _columns[sort] = (label, total);
        }
    }

    /// <summary>Başlığa tıklandı: yeni sütunda varsayılan yön (sayılar: çoktan aza, ad: A→Z); aynı sütunda yön tersine döner.</summary>
    private void SortBy(ProcSort sort)
    {
        if (sort == _sort)
        {
            _descending = !_descending;
        }
        else
        {
            _sort = sort;
            _descending = sort != ProcSort.Name;
        }

        Render();
    }

    /// <summary>Başlık metni: etkin sütunda yön oku (▼ çoktan aza, ▲ azdan çoğa; adda ▲ A→Z).</summary>
    private string Arrow(ProcSort sort) => sort != _sort ? string.Empty : _descending ? " ▼" : " ▲";

    private async Task RefreshAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            _all = await Task.Run(_monitor.Sample);
            Render();
        }
        catch (Exception)
        {
            // Örnekleme o an yapılamadı (işlem listesi okunurken değişti); sonraki turda yeniden denenir.
        }
        finally
        {
            _busy = false;
        }
    }

    private void Render()
    {
        var settings = App.Current.Settings.Current;
        var snapshot = App.Current.Monitor.Latest;
        var bits = settings.NetworkInBits;

        // Başlık altı toplamlar: CPU/RAM/ağ sistem ölçümünden, disk işlem toplamından.
        var cpuTotal = snapshot?.Cpu?.Load is float load ? Format.Load(load) : $"{_all.Sum(r => r.CpuPercent):0}%";
        var ramTotal = snapshot?.Memory is { MemoryUsedGb: float used } ? $"{used:0.0}G" : FormatBytes(_all.Sum(r => r.MemoryBytes));
        var diskTotal = Format.Speed(_all.Sum(r => r.DiskBytesPerSecond), bits, true);
        var netTotal = NetworkShown ? Format.Speed(_all.Sum(r => r.NetBytesPerSecond), bits, true) : "--";
        var gpuTotal = snapshot?.Gpu?.Load is float gpuLoad ? Format.Load(gpuLoad) : GpuShown ? $"{_all.Sum(r => r.GpuPercent):0}%" : "--";
        _columns[ProcSort.Cpu].Total.Text = cpuTotal;
        _columns[ProcSort.Gpu].Total.Text = gpuTotal;
        _columns[ProcSort.Memory].Total.Text = ramTotal;
        _columns[ProcSort.Disk].Total.Text = diskTotal;
        _columns[ProcSort.Network].Total.Text = netTotal;
        foreach (var (sort, (label, _)) in _columns)
        {
            label.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, sort == _sort ? "AccentBrush" : "TextBrush");
            label.Text = ColumnNames[sort] + Arrow(sort);
        }

        if (_nameHeader is not null)
        {
            _nameHeader.Text = Loc.T("W.TaskName") + Arrow(ProcSort.Name);
            _nameHeader.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, _sort == ProcSort.Name ? "AccentBrush" : "SubtleTextBrush");
        }

        if (snapshot?.Gpu is { } gpu)
        {
            _gpu.Text = $"GPU {Format.Load(gpu.Load)}" + (gpu.MemoryUsedGb is float vu && gpu.MemoryTotalGb is float vt ? $" · VRAM {vu:0.0}/{vt:0} GB" : string.Empty);
            _gpu.Visibility = Visibility.Visible;
        }
        else
        {
            _gpu.Visibility = Visibility.Collapsed;
        }

        _rows.Children.Clear();
        var top = ProcessTable.Top(_all, _sort, RowCount, _descending);
        if (top.Count == 0)
        {
            _rows.Children.Add(Subtle(Loc.T("W.TaskLoading")));
        }

        foreach (var row in top)
        {
            _rows.Children.Add(Row(row, bits));
        }

        _hint.Text = NetworkShown ? string.Empty : Loc.T("W.TaskNetAdmin");
        _hint.Visibility = NetworkShown ? Visibility.Collapsed : Visibility.Visible;
    }

    private Grid Row(ProcRow row, bool bits)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 80 });
        foreach (var width in ColumnWidths)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        }

        var label = row.Count > 1 ? $"{row.Name} ×{row.Count}" : row.Name;
        grid.Children.Add(new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = label });

        Cell(grid, 1, row.CpuPercent >= 0.05 ? $"{row.CpuPercent:0.0}%" : "0%", _sort == ProcSort.Cpu, row.CpuPercent >= 50);
        Cell(grid, 2, !GpuShown ? "--" : row.GpuPercent >= 0.05 ? $"{row.GpuPercent:0.0}%" : "0%", _sort == ProcSort.Gpu, row.GpuPercent >= 50);
        Cell(grid, 3, FormatBytes(row.MemoryBytes), _sort == ProcSort.Memory, false);
        Cell(grid, 4, row.DiskBytesPerSecond >= 1 ? Format.Speed(row.DiskBytesPerSecond, bits, true) : "0", _sort == ProcSort.Disk, false);
        Cell(grid, 5, !NetworkShown ? "--" : row.NetBytesPerSecond >= 1 ? Format.Speed(row.NetBytesPerSecond, bits, true) : "0", _sort == ProcSort.Network, false);
        return grid;
    }

    private static void Cell(Grid grid, int column, string text, bool sorted, bool hot)
    {
        var block = new TextBlock { Text = text, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontWeight = sorted ? FontWeights.SemiBold : FontWeights.Normal };
        block.SetResourceReference(TextBlock.ForegroundProperty, hot ? "HotBrush" : sorted ? "TextBrush" : "SubtleTextBrush");
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    /// <summary>Bayt sayısını "812M" / "1.4G" gibi kısa yazar (ondalık ayraç: 1000'lik, Görev Yöneticisi'ne yakın).</summary>
    internal static string FormatBytes(long bytes)
    {
        double value = Math.Max(bytes, 0);
        string[] units = ["B", "K", "M", "G", "T"];
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value < 10 && unit >= 3 ? $"{value:0.0}{units[unit]}" : $"{value:0}{units[unit]}";
    }
}
