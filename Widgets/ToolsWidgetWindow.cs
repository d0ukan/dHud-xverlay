using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>Zamanlayıcı, saatli hatırlatıcı ve pomodoro tek kartta.</summary>
public sealed class ToolsWidgetWindow : WidgetWindowBase
{
    protected override WidgetOptions Options => App.Current.Settings.Current.ToolsOptions;

    private static readonly int[] QuickMinutes = [5, 10, 15, 25, 45];

    private readonly WidgetService _widgets = App.Current.Widgets;
    private readonly SettingsService _settings = App.Current.Settings;
    private readonly TextBox _customMinutes = Field("", 56);
    private readonly TextBox _reminderText = Field("", 0);
    private readonly TextBox _reminderTime = Field("", 64);
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _pomodoroLabel = new() { FontSize = 20, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _pomodoroSub = new();
    private readonly StackPanel _active = new();
    private readonly Dictionary<string, TextBlock> _countdowns = [];

    public ToolsWidgetWindow()
    {
        Card.Width = 320;
        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 14) };

        // Başlık
        var title = new Grid();
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        title.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        title.Children.Add(new TextBlock { Text = Loc.T("W.ToolsTitle"), FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var close = IconButton("✕", Loc.T("W.Hide"), (_, _) =>
        {
            _settings.Current.WidgetTools = false;
            _settings.NotifyChanged();
        });
        Grid.SetColumn(close, 1);
        title.Children.Add(close);
        root.Children.Add(title);

        // Zamanlayıcı
        root.Children.Add(Header(Loc.T("W.Timer")));
        var quick = new WrapPanel();
        foreach (var minutes in QuickMinutes)
        {
            var m = minutes;
            quick.Children.Add(SmallButton(Loc.T("W.Minutes", m), () => StartTimer(m)));
        }

        root.Children.Add(quick);
        var custom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        _customMinutes.ToolTip = Loc.T("W.CustomMinutes");
        _customMinutes.KeyDown += (_, e) => EnterTo(e, StartCustom);
        custom.Children.Add(_customMinutes);
        custom.Children.Add(new TextBlock { Text = Loc.T("W.MinShort"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 10, 0) });
        custom.Children.Add(SmallButton(Loc.T("W.Start"), StartCustom));
        root.Children.Add(custom);

        // Hatırlatıcı
        root.Children.Add(Header(Loc.T("W.Reminder")));
        _reminderText.ToolTip = Loc.T("W.ReminderText");
        _reminderText.MaxLength = 120;
        _reminderText.KeyDown += (_, e) => EnterTo(e, AddReminder);
        root.Children.Add(_reminderText);
        var at = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        _reminderTime.ToolTip = Loc.T("W.ReminderTime");
        _reminderTime.KeyDown += (_, e) => EnterTo(e, AddReminder);
        at.Children.Add(new TextBlock { Text = Loc.T("W.At"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        at.Children.Add(_reminderTime);
        at.Children.Add(new Border { Width = 10 });
        at.Children.Add(SmallButton(Loc.T("W.Add"), AddReminder));
        root.Children.Add(at);
        root.Children.Add(_status);

        // Pomodoro
        root.Children.Add(Header(Loc.T("W.Pomodoro")));
        _pomodoroSub.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        root.Children.Add(_pomodoroLabel);
        root.Children.Add(_pomodoroSub);
        var pomodoroButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        pomodoroButtons.Children.Add(SmallButton(Loc.T("W.Start"), () => _widgets.StartPomodoro()));
        pomodoroButtons.Children.Add(SmallButton(Loc.T("W.Skip"), () => _widgets.SkipPomodoro()));
        pomodoroButtons.Children.Add(SmallButton(Loc.T("W.Stop"), () => _widgets.StopPomodoro()));
        root.Children.Add(pomodoroButtons);

        // Çalışanlar
        root.Children.Add(Header(Loc.T("W.Running")));
        root.Children.Add(_active);

        Card.Child = root;

        var pos = _widgets.Data.ToolsPos;
        var def = DefaultSpot(2, 336);
        Place(pos?.Left ?? double.NaN, pos?.Top ?? double.NaN, def.Left, def.Top);
        Moved += p =>
        {
            _widgets.Data.ToolsPos = new WidgetPos { Left = p.X, Top = p.Y };
            _widgets.ScheduleSave();
        };

        _widgets.Ticked += OnTick;
        _widgets.RemindersChanged += RebuildActive;
        Closed += (_, _) =>
        {
            _widgets.Ticked -= OnTick;
            _widgets.RemindersChanged -= RebuildActive;
        };

        RebuildActive();
        OnTick();
        ApplyOptions();
    }

    // ---- Eylemler ----

    private void StartCustom()
    {
        if (!int.TryParse(_customMinutes.Text.Trim(), out var minutes) || minutes is < 1 or > 24 * 60)
        {
            ShowStatus(Loc.T("W.BadMinutes"));
            return;
        }

        StartTimer(minutes);
        _customMinutes.Clear();
    }

    private void StartTimer(int minutes)
    {
        if (_widgets.AddReminder(Loc.T("W.TimerName", minutes), DateTime.UtcNow.AddMinutes(minutes), "timer") is null)
        {
            ShowStatus(Loc.T("W.TooMany"));
            return;
        }

        HideStatus();
    }

    private void AddReminder()
    {
        if (!WidgetService.TryParseClock(_reminderTime.Text, DateTime.Now, out var dueLocal))
        {
            ShowStatus(Loc.T("W.BadTime"));
            return;
        }

        var text = _reminderText.Text.Trim();
        if (text.Length == 0)
        {
            text = Loc.T("W.ReminderDefault");
        }

        if (_widgets.AddReminder(text, dueLocal.ToUniversalTime(), "reminder") is null)
        {
            ShowStatus(Loc.T("W.TooMany"));
            return;
        }

        _reminderText.Clear();
        _reminderTime.Clear();
        HideStatus();
    }

    private void ShowStatus(string text)
    {
        _status.Text = text;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
        _status.Visibility = Visibility.Visible;
    }

    private void HideStatus() => _status.Visibility = Visibility.Collapsed;

    // ---- Görünüm ----

    private void OnTick()
    {
        var now = DateTime.UtcNow;
        foreach (var reminder in _widgets.Data.Reminders)
        {
            if (_countdowns.TryGetValue(reminder.Id, out var label))
            {
                label.Text = Remaining(reminder.DueUtc - now);
            }
        }

        if (_widgets.Phase == PomodoroPhase.Off)
        {
            _pomodoroLabel.Text = "--:--";
            _pomodoroSub.Text = Loc.T("W.PomodoroOff", WidgetService.PomodoroFocusMinutes, WidgetService.PomodoroShortBreakMinutes);
            return;
        }

        _pomodoroLabel.Text = Remaining(_widgets.PhaseEndUtc - now);
        _pomodoroSub.Text = Loc.T("W.Phase." + _widgets.Phase) + "  ·  "
                            + Loc.T("W.Rounds", _widgets.FocusRounds, WidgetService.PomodoroRoundsBeforeLongBreak);
    }

    private void RebuildActive()
    {
        _active.Children.Clear();
        _countdowns.Clear();
        if (_widgets.Data.Reminders.Count == 0)
        {
            var none = new TextBlock { Text = Loc.T("W.NoneRunning"), Margin = new Thickness(0, 2, 0, 0) };
            none.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
            _active.Children.Add(none);
            return;
        }

        foreach (var reminder in _widgets.Data.Reminders.OrderBy(r => r.DueUtc).ToList())
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = (reminder.Kind == "timer" ? "⏱ " : "🔔 ") + reminder.Text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = reminder.Kind == "reminder" ? reminder.DueUtc.ToLocalTime().ToString("g") : null
            };
            var left = new TextBlock { Margin = new Thickness(8, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 52, TextAlignment = TextAlignment.Right };
            left.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
            _countdowns[reminder.Id] = left;
            left.Text = Remaining(reminder.DueUtc - DateTime.UtcNow);

            var remove = IconButton("✕", Loc.T("W.Cancel"), (_, _) => _widgets.RemoveReminder(reminder));
            Grid.SetColumn(left, 1);
            Grid.SetColumn(remove, 2);
            row.Children.Add(name);
            row.Children.Add(left);
            row.Children.Add(remove);
            _active.Children.Add(row);
        }
    }

    private static string Remaining(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalHours >= 24 ? $"{(int)span.TotalDays}g {span.Hours}s"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    // ---- Yardımcılar ----

    private TextBlock Header(string text)
    {
        var block = new TextBlock { Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");
        return block;
    }

    private Button SmallButton(string text, Action onClick)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(10, 3, 10, 3) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "DarkButton");
        button.Click += (_, _) => onClick();
        return button;
    }

    private static TextBox Field(string text, double width)
    {
        var box = new TextBox
        {
            Text = text,
            Padding = new Thickness(6, 3, 6, 3),
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CaretBrush = Brushes.White
        };
        box.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        if (width > 0)
        {
            box.Width = width;
        }

        return box;
    }

    private static void EnterTo(KeyEventArgs e, Action action)
    {
        if (e.Key == Key.Enter)
        {
            action();
            e.Handled = true;
        }
    }
}
