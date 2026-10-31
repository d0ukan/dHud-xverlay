using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>Yapışkan not: yazdıkça otomatik kaydedilir, renk seçilir, boyutlandırılır, istenirse diğer pencerelerin üstünde durur.</summary>
public sealed class StickyNoteWindow : WidgetWindowBase
{
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
    private readonly Button _pin;

    public StickyNote Note => _note;

    public StickyNoteWindow(StickyNote note)
    {
        _note = note;
        SizeToContent = SizeToContent.Manual;
        ResizeMode = ResizeMode.NoResize; // boyutlandırma sağ alttaki tutamaçtan
        ShowActivated = true;             // yazı yazılabilsin
        Width = note.Width + 16;
        Height = note.Height + 16;
        MinWidth = 176;
        MinHeight = 116;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Başlık: sürükleme alanı + düğmeler
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 4, 2) };
        _pin = IconButton("📌", Loc.T("W.Pin"), (_, _) => TogglePin());
        buttons.Children.Add(IconButton("＋", Loc.T("W.NewNote"), (_, _) => App.Current.WidgetManager.NewNote()));
        buttons.Children.Add(IconButton("🎨", Loc.T("W.NoteColor"), (_, _) => CycleColor()));
        buttons.Children.Add(_pin);
        buttons.Children.Add(IconButton("✕", Loc.T("W.DeleteNote"), (_, _) => Delete()));
        _header = new Border { Child = buttons, Padding = new Thickness(6, 0, 0, 0), CornerRadius = new CornerRadius(11, 11, 0, 0), Background = Brushes.Transparent };
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
            _widgets.ScheduleSave();
        };
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
            _note.Width = Width - 16;
            _note.Height = Height - 16;
            _widgets.ScheduleSave();
        };
        Grid.SetRow(grip, 1);
        root.Children.Add(grip);

        Card.Child = root;
        ApplyColor();
        Topmost = note.Pinned;
        UpdatePinVisual();

        var def = DefaultSpot(Math.Max(0, _widgets.Data.Notes.IndexOf(note)) + 1, note.Width);
        Place(note.Left, note.Top, def.Left, def.Top);
        Moved += p =>
        {
            _note.Left = p.X;
            _note.Top = p.Y;
            _widgets.ScheduleSave();
        };

        ContextMenu = BuildMenu();
        Loaded += (_, _) =>
        {
            // Yeni (boş) not hemen yazmaya hazır açılır.
            if (_note.Text.Length == 0)
            {
                Activate();
                _text.Focus();
            }
        };
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
        Card.Background = new SolidColorBrush(card);
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
        _note.Pinned = !_note.Pinned;
        Topmost = _note.Pinned;
        UpdatePinVisual();
        _widgets.ScheduleSave();
    }

    private void UpdatePinVisual() => _pin.Opacity = _note.Pinned ? 1 : 0.4;

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
