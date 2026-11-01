using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>
/// Hızlı not defteri: sekmelerle birden çok uzun not. Yazdıkça kendiliğinden kaydedilir (widgets.json).
/// Sekmeye tıklayınca açılır, çift tıklayınca adı değişir; en altta açık sekmeyi silme düğmesi vardır.
/// Yapışkan notlardan farkı: tek pencerede, uzun yazılar için.
/// </summary>
public sealed class NotepadWidgetWindow : WidgetWindowBase
{
    private readonly WidgetService _widgets = App.Current.Widgets;
    private readonly WrapPanel _tabs = new();
    private readonly TextBox _editor;
    private readonly TextBlock _footer;
    private readonly Button _delete;
    private readonly DispatcherTimer _confirmReset = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _updating;
    private bool _confirming;

    protected override WidgetOptions Options => App.Current.Settings.Current.Extra(WidgetIds.Notepad);

    protected override bool ZoomByWidth => true;

    protected override bool ContentScrolls => true;

    protected override double MinListHeight => 220;

    public NotepadWidgetWindow()
    {
        Card.Width = 340;
        EnsureTab();

        var root = new Grid { Margin = new Thickness(16, 10, 16, 12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(TitleBar(Loc.T("W.Notepad"), WidgetIds.Notepad));

        _tabs.Margin = new Thickness(0, 8, 0, 6);
        Grid.SetRow(_tabs, 1);
        root.Children.Add(_tabs);

        _editor = Field(WidgetService.MaxNotepadLength);
        _editor.AcceptsReturn = true;
        _editor.TextWrapping = TextWrapping.Wrap;
        _editor.VerticalContentAlignment = VerticalAlignment.Top;
        _editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _editor.MinHeight = 120;
        _editor.Height = 170;   // otomatik boyutta sabit yükseklik; kullanıcı boyutunda kalanı doldurur (OnCustomSizeChanged)
        _editor.TextChanged += (_, _) => OnTextChanged();
        Grid.SetRow(_editor, 2);
        root.Children.Add(_editor);

        var bottom = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _footer = Subtle("");
        _footer.VerticalAlignment = VerticalAlignment.Center;
        _delete = SmallButton(Loc.T("W.NpDelete"), DeleteTab);
        _delete.Margin = new Thickness(0);
        Grid.SetColumn(_delete, 1);
        bottom.Children.Add(_footer);
        bottom.Children.Add(_delete);
        Grid.SetRow(bottom, 3);
        root.Children.Add(bottom);
        Card.Child = root;

        _confirmReset.Tick += (_, _) => ResetConfirm();
        Closed += (_, _) =>
        {
            _confirmReset.Stop();
            _widgets.ScheduleSave();
        };

        RestorePosition(WidgetIds.Notepad, 15, 356);
        RebuildTabs();
        LoadActive();
        ApplyOptions();
    }

    protected override void OnCustomSizeChanged(bool custom)
    {
        if (custom)
        {
            _editor.ClearValue(FrameworkElement.HeightProperty);   // kart boyutlanınca metin alanı kalan yüksekliği doldurur
        }
        else
        {
            _editor.Height = 170;
        }
    }

    private List<NotepadTab> Tabs => _widgets.Data.NotepadTabs;

    private NotepadTab Active => Tabs.FirstOrDefault(t => t.Id == _widgets.Data.NotepadActive) ?? Tabs[0];

    /// <summary>Hiç sekme yoksa bir tane açar.</summary>
    private void EnsureTab()
    {
        if (Tabs.Count == 0)
        {
            Tabs.Add(new NotepadTab { Title = Loc.T("W.NpTab", 1) });
        }

        if (Tabs.All(t => t.Id != _widgets.Data.NotepadActive))
        {
            _widgets.Data.NotepadActive = Tabs[0].Id;
        }
    }

    private void LoadActive()
    {
        _updating = true;
        _editor.Text = Active.Text;
        _updating = false;
        UpdateFooter();
        ResetConfirm();
    }

    private void OnTextChanged()
    {
        if (_updating)
        {
            return;
        }

        Active.Text = _editor.Text;
        _widgets.ScheduleSave();
        UpdateFooter();
    }

    private void UpdateFooter() =>
        _footer.Text = Loc.T("W.NpChars", _editor.Text.Length.ToString("N0", System.Globalization.CultureInfo.CurrentCulture));

    private void RebuildTabs()
    {
        _tabs.Children.Clear();
        foreach (var tab in Tabs)
        {
            _tabs.Children.Add(TabChip(tab));
        }

        if (Tabs.Count < WidgetService.MaxNotepadTabs)
        {
            var add = IconButton("＋", Loc.T("W.NpNew"), (_, _) => AddTab());
            add.Margin = new Thickness(0, 0, 0, 4);
            _tabs.Children.Add(add);
        }
    }

    private FrameworkElement TabChip(NotepadTab tab)
    {
        var selected = tab.Id == Active.Id;
        var label = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(tab.Title) ? Loc.T("W.NpTab", Tabs.IndexOf(tab) + 1) : tab.Title,
            MaxWidth = 110,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, selected ? "TextBrush" : "SubtleTextBrush");
        var chip = new Border
        {
            Child = label,
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0, 0, 4, 4),
            CornerRadius = new CornerRadius(6),
            Cursor = Cursors.Hand,
            ToolTip = Loc.T("W.NpRenameHint")
        };
        chip.SetResourceReference(Border.BackgroundProperty, selected ? "FieldBackgroundBrush" : "BarTrackBrush");
        if (selected)
        {
            chip.BorderThickness = new Thickness(0, 0, 0, 2);
            chip.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        }

        chip.MouseLeftButtonUp += (_, e) =>
        {
            if (e.ClickCount >= 2)
            {
                BeginRename(tab, chip);
            }
            else if (tab.Id != _widgets.Data.NotepadActive)
            {
                _widgets.Data.NotepadActive = tab.Id;
                _widgets.ScheduleSave();
                RebuildTabs();
                LoadActive();
            }

            e.Handled = true;
        };
        return chip;
    }

    private void BeginRename(NotepadTab tab, Border chip)
    {
        var box = Field(30);
        box.Text = tab.Title;
        box.MinWidth = 90;
        box.Margin = new Thickness(0, 0, 4, 4);
        var done = false;
        void Commit()
        {
            if (done)
            {
                return;
            }

            done = true;
            tab.Title = box.Text.Trim();
            _widgets.ScheduleSave();
            RebuildTabs();
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                done = true;
                RebuildTabs();
                e.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => Commit();

        var index = _tabs.Children.IndexOf(chip);
        if (index < 0)
        {
            return;
        }

        _tabs.Children.RemoveAt(index);
        _tabs.Children.Insert(index, box);
        box.Focus();
        box.SelectAll();
    }

    private void AddTab()
    {
        if (Tabs.Count >= WidgetService.MaxNotepadTabs)
        {
            return;
        }

        var tab = new NotepadTab { Title = Loc.T("W.NpTab", NextNumber()) };
        Tabs.Add(tab);
        _widgets.Data.NotepadActive = tab.Id;
        _widgets.ScheduleSave();
        RebuildTabs();
        LoadActive();
        _editor.Focus();
    }

    /// <summary>"Not 3" gibi varsayılan adlarda henüz kullanılmayan en küçük sayı.</summary>
    private int NextNumber()
    {
        var used = Tabs.Select(t => t.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var n = Tabs.Count + 1;
        while (used.Contains(Loc.T("W.NpTab", n)))
        {
            n++;
        }

        return n;
    }

    private void DeleteTab()
    {
        // Dolu bir sekme için ikinci tık gerekir (yanlışlıkla kaybı önler); boş sekme hemen silinir.
        if (Active.Text.Length > 0 && !_confirming)
        {
            _confirming = true;
            _delete.Content = Loc.T("W.NpDeleteSure");
            _confirmReset.Stop();
            _confirmReset.Start();
            return;
        }

        var index = Tabs.IndexOf(Active);
        Tabs.RemoveAt(index);
        EnsureTab();
        _widgets.Data.NotepadActive = Tabs[Math.Min(index, Tabs.Count - 1)].Id;
        _widgets.ScheduleSave();
        RebuildTabs();
        LoadActive();
    }

    private void ResetConfirm()
    {
        _confirmReset.Stop();
        _confirming = false;
        _delete.Content = Loc.T("W.NpDelete");
    }
}
