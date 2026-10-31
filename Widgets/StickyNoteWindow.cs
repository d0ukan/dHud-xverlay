using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>
/// Yapışkan not: yazdıkça otomatik kaydedilir, renk seçilir, boyutlandırılır. Varsayılan "yığın" modunda tüm notlar bir klasör gibi
/// üst üste durur: her notun başlık şeridi (ilk satırı başlık olur) bir klasör sekmesi gibi görünür; sekmeye tıklayınca o not öne gelir,
/// sekmeyi sürüklemek tüm yığını taşır.
/// </summary>
public sealed class StickyNoteWindow : WidgetWindowBase
{
    /// <summary>Yığında iki not arasındaki dikey mesafe: başlık şeridinin yüksekliği.</summary>
    public const double TabStep = 34;

    private const double Pad = 8; // kart etrafındaki gölge payı

    // Koyu temaya uyumlu 6 ton: (kart zemini, başlık vurgusu)
    private static readonly (Color Card, Color Accent)[] Palette =
    [
        (Color.FromArgb(0xF2, 0x24, 0x2B, 0x3D), Color.FromRgb(0x60, 0xA5, 0xFA)),
        (Color.FromArgb(0xF2, 0x3A, 0x31, 0x1C), Color.FromRgb(0xFA, 0xCC, 0x15)),
        (Color.FromArgb(0xF2, 0x1F, 0x37, 0x2A), Color.FromRgb(0x4A, 0xDE, 0x80)),
        (Color.FromArgb(0xF2, 0x3B, 0x24, 0x33), Color.FromRgb(0xF4, 0x72, 0xB6)),
        (Color.FromArgb(0xF2, 0x2F, 0x28, 0x44), Color.FromRgb(0xA7, 0x8B, 0xFA)),
        (Color.FromArgb(0xF2, 0x40, 0x25, 0x25), Color.FromRgb(0xF8, 0x71, 0x71))
    ];

    private readonly StickyNote _note;
    private readonly WidgetService _widgets = App.Current.Widgets;
    private readonly TextBox _text;
    private readonly Border _header;
    private readonly TextBlock _title;
    private readonly Button _pin;

    public StickyNote Note => _note;

    protected override WidgetOptions Options => App.Current.Settings.Current.NotesOptions;

    private static bool Stacked => App.Current.Settings.Current.NotesStacked;

    // Yığında z sırası sekme sırasını belirler; bu yüzden hepsi aynı katmanda (grup ayarı) durur. Serbest modda her notun kendi iğnesi vardır.
    protected override bool WantsTopmost => Stacked ? Options.AlwaysOnTop : _note.Pinned;

    public StickyNoteWindow(StickyNote note)
    {
        _note = note;
        SizeToContent = SizeToContent.Manual;
        ResizeMode = ResizeMode.NoResize; // boyutlandırma sağ alttaki tutamaçtan
        ShowActivated = true;             // yazı yazılabilsin
        MinWidth = 176;
        MinHeight = 116;
        ApplySize();

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Başlık şeridi (klasör sekmesi): solda not başlığı, sağda düğmeler. Sürükleme ve "öne getir" alanı.
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 4, 2) };
        _pin = IconButton("📌", Loc.T("W.Pin"), (_, _) => TogglePin());
        buttons.Children.Add(IconButton("＋", Loc.T("W.NewNote"), (_, _) => App.Current.WidgetManager.NewNote()));
        buttons.Children.Add(IconButton("🎨", Loc.T("W.NoteColor"), (_, _) => CycleColor()));
        buttons.Children.Add(_pin);
        buttons.Children.Add(IconButton("✕", Loc.T("W.DeleteNote"), (_, _) => Delete()));

        _title = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 8, 0)
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(buttons, 1);
        headerGrid.Children.Add(_title);
        headerGrid.Children.Add(buttons);
        _header = new Border { Child = headerGrid, Padding = new Thickness(8, 0, 0, 0), CornerRadius = new CornerRadius(11, 11, 0, 0), Background = Brushes.Transparent, Height = TabStep };
        Grid.SetRow(_header, 0);
        root.Children.Add(_header);

        _text = new TextBox
        {
            Text = note.Text,
            AcceptsReturn = true,
            AcceptsTab = false,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 4, 12, 12),
            FontSize = 14,
            MaxLength = WidgetService.MaxNoteLength,
            CaretBrush = Brushes.White
        };
        _text.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        _text.TextChanged += (_, _) =>
        {
            _note.Text = _text.Text;
            UpdateTitle();
            _widgets.ScheduleSave();
        };
        _text.LostKeyboardFocus += (_, _) => _widgets.Flush();
        Closed += (_, _) => _widgets.Flush();
        Grid.SetRow(_text, 1);
        root.Children.Add(_text);

        // Boyutlandırma tutamacı (sağ alt köşe)
        var grip = new Thumb
        {
            Width = 16,
            Height = 16,
            Cursor = Cursors.SizeNWSE,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Opacity = 0.5,
            Template = GripTemplate()
        };
        grip.DragDelta += (_, e) =>
        {
            Width = Math.Max(MinWidth, Width + e.HorizontalChange);
            Height = Math.Max(MinHeight, Height + e.VerticalChange);
        };
        grip.DragCompleted += (_, _) =>
        {
            if (Stacked)
            {
                // Yığındaki tüm notlar aynı boyutu paylaşır.
                _widgets.Data.StackWidth = Width - 2 * Pad;
                _widgets.Data.StackHeight = Height - 2 * Pad;
                _widgets.ScheduleSave();
                App.Current.WidgetManager.LayoutNotes();
                App.Current.WidgetManager.ResolveStack();
                return;
            }

            _note.Width = Width - 2 * Pad;
            _note.Height = Height - 2 * Pad;
            _widgets.ScheduleSave();
            App.Current.WidgetManager.Resolve(this);
        };
        Grid.SetRow(grip, 1);
        root.Children.Add(grip);

        Card.Child = root;
        ApplyColor();
        UpdateTitle();
        UpdatePinVisual();
        ApplyOptions();

        if (Stacked)
        {
            WindowStartupLocation = WindowStartupLocation.Manual; // konumu WidgetManager.LayoutNotes verir
        }
        else
        {
            var def = DefaultSpot(Math.Max(0, _widgets.Data.Notes.IndexOf(note)) + 1, note.Width);
            Place(note.Left, note.Top, def.Left, def.Top);
        }

        Moved += p =>
        {
            if (!Stacked)
            {
                _note.Left = p.X;
                _note.Top = p.Y;
                _widgets.ScheduleSave();
            }
        };

        ContextMenu = BuildMenu();
        Loaded += (_, _) =>
        {
            if (Stacked)
            {
                App.Current.WidgetManager.ResolveStack(); // pencere artık ölçülü: yığın boş hücreye otursun
            }

            // Yeni (boş) not hemen yazmaya hazır açılır.
            if (_note.Text.Length == 0)
            {
                Activate();
                _text.Focus();
            }
        };
    }

    /// <summary>Yığında bu notun sekmesine tıklandı mı (sürükleme olmadan) / yığın sürüklendi mi? Taban sınıfın yerleştirmesini devralır.</summary>
    protected override bool OnDragCompleted(Point before)
    {
        if (!Stacked)
        {
            return false;
        }

        var moved = Math.Abs(Left - before.X) >= 3 || Math.Abs(Top - before.Y) >= 3;
        if (!moved)
        {
            Left = before.X;
            Top = before.Y;
            App.Current.WidgetManager.BringNoteToFront(_note);
        }
        else
        {
            App.Current.WidgetManager.MoveStackBy(new Point(Left - before.X, Top - before.Y));
        }

        return true;
    }

    /// <summary>Yığın düzeninde bu notun yeri: kartın sol üst köşesi ve ortak boyut.</summary>
    public void ApplyStack(double cardLeft, double cardTop, double width, double height)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = cardLeft - Pad;
        Top = cardTop - Pad;
        Width = width + 2 * Pad;
        Height = height + 2 * Pad;
    }

    /// <summary>Ayar değişti (yığın açık/kapalı, iğne): başlık, iğne ve boyutu yeniler.</summary>
    public void Refresh()
    {
        UpdateTitle();
        UpdatePinVisual();
        if (!Stacked)
        {
            ApplySize();
        }
    }

    private void ApplySize()
    {
        var d = _widgets.Data;
        Width = (Stacked ? d.StackWidth : _note.Width) + 2 * Pad;
        Height = (Stacked ? d.StackHeight : _note.Height) + 2 * Pad;
    }

    /// <summary>Sekme başlığı: notun ilk dolu satırı (kısaltılmış); boşsa "Yeni not".</summary>
    private void UpdateTitle()
    {
        var first = _note.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        _title.Text = first is null ? Loc.T("W.UntitledNote") : first.Length > 28 ? first[..28] + "…" : first;
        _title.Opacity = first is null ? 0.55 : 1;
    }

    private static ControlTemplate GripTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        factory.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M12,4 L4,12 M12,8 L8,12"));
        factory.SetValue(System.Windows.Shapes.Shape.StrokeProperty, new SolidColorBrush(Color.FromRgb(0xB4, 0xBA, 0xC6)));
        factory.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 1.5);
        factory.SetValue(System.Windows.Shapes.Shape.StrokeStartLineCapProperty, PenLineCap.Round);
        factory.SetValue(System.Windows.Shapes.Shape.StrokeEndLineCapProperty, PenLineCap.Round);
        return new ControlTemplate(typeof(Thumb)) { VisualTree = factory };
    }

    private void ApplyColor()
    {
        var (card, accent) = Palette[Math.Clamp(_note.Color, 0, Palette.Length - 1)];
        SetCardColor(card);
        _header.BorderBrush = new SolidColorBrush(accent);
        _header.BorderThickness = new Thickness(0, 3, 0, 0);
    }

    private void CycleColor()
    {
        _note.Color = (_note.Color + 1) % Palette.Length;
        ApplyColor();
        _widgets.ScheduleSave();
    }

    private void TogglePin()
    {
        if (Stacked)
        {
            // Yığında iğne tüm notlar için ortaktır (z sırası bozulmasın).
            var settings = App.Current.Settings;
            settings.Current.NotesOptions.AlwaysOnTop = !Options.AlwaysOnTop;
            App.Current.WidgetManager.PinAllNotes(settings.Current.NotesOptions.AlwaysOnTop);
            settings.NotifyChanged();
            return;
        }

        _note.Pinned = !_note.Pinned;
        ApplyOptions();
        UpdatePinVisual();
        _widgets.ScheduleSave();
    }

    private void UpdatePinVisual() => _pin.Opacity = (Stacked ? Options.AlwaysOnTop : _note.Pinned) ? 1 : 0.4;

    private void Delete()
    {
        // Boş not sormadan silinir; içinde yazı varsa onay istenir.
        if (_note.Text.Trim().Length > 0
            && DarkMessageBox.Show(this, Loc.T("W.DeleteNoteConfirm"), Loc.T("W.NoteTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        _widgets.RemoveNote(_note);
        App.Current.WidgetManager.Apply();
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        var newNote = new MenuItem { Header = Loc.T("W.NewNote") };
        newNote.Click += (_, _) => App.Current.WidgetManager.NewNote();
        var hideAll = new MenuItem { Header = Loc.T("W.HideNotes") };
        hideAll.Click += (_, _) =>
        {
            App.Current.Settings.Current.NotesVisible = false;
            App.Current.Settings.NotifyChanged();
        };
        menu.Items.Add(newNote);
        menu.Items.Add(hideAll);
        return menu;
    }
}
