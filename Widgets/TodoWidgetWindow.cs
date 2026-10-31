using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>Yapılacaklar listesi: madde ekle (Enter), tik at, sil, tamamlananları temizle. widgets.json'da saklanır.</summary>
public sealed class TodoWidgetWindow : WidgetWindowBase
{
    private readonly WidgetService _widgets = App.Current.Widgets;
    private readonly StackPanel _list = new();
    private readonly TextBox _input;
    private readonly TextBlock _footer;

    protected override WidgetOptions Options => App.Current.Settings.Current.Extra(WidgetIds.Todo);

    public TodoWidgetWindow()
    {
        Card.Width = 280;
        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };
        root.Children.Add(TitleBar(Loc.T("W.Todo"), WidgetIds.Todo));

        _input = Field(200);
        _input.Margin = new Thickness(0, 8, 0, 6);
        _input.ToolTip = Loc.T("W.TodoAdd");
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Add();
                e.Handled = true;
            }
        };
        root.Children.Add(_input);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 300, Content = _list };
        root.Children.Add(scroll);

        var bottom = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _footer = Subtle("");
        _footer.VerticalAlignment = VerticalAlignment.Center;
        var clear = SmallButton(Loc.T("W.TodoClear"), ClearDone);
        clear.Margin = new Thickness(0);
        Grid.SetColumn(clear, 1);
        bottom.Children.Add(_footer);
        bottom.Children.Add(clear);
        root.Children.Add(bottom);
        Card.Child = root;

        RestorePosition(WidgetIds.Todo, 5, 296);
        Rebuild();
        ApplyOptions();
    }

    private void Add()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (_widgets.Data.Todos.Count >= WidgetService.MaxTodos)
        {
            _footer.Text = Loc.T("W.TodoFull", WidgetService.MaxTodos);
            return;
        }

        _widgets.Data.Todos.Add(new TodoItem { Text = text });
        _widgets.ScheduleSave();
        _input.Clear();
        Rebuild();
    }

    private void ClearDone()
    {
        if (_widgets.Data.Todos.RemoveAll(t => t.Done) > 0)
        {
            _widgets.ScheduleSave();
            Rebuild();
        }
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        var todos = _widgets.Data.Todos;
        foreach (var item in todos.OrderBy(t => t.Done).ToList())
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = item.Text,
                TextWrapping = TextWrapping.Wrap,
                TextDecorations = item.Done ? TextDecorations.Strikethrough : null,
                Opacity = item.Done ? 0.5 : 1
            };
            var check = new CheckBox { Content = label, IsChecked = item.Done };
            check.SetResourceReference(StyleProperty, "DarkCheckBox");
            var current = item;
            check.Click += (_, _) =>
            {
                current.Done = check.IsChecked == true;
                _widgets.ScheduleSave();
                Rebuild();
            };

            var remove = IconButton("✕", Loc.T("W.Cancel"), (_, _) =>
            {
                _widgets.Data.Todos.Remove(current);
                _widgets.ScheduleSave();
                Rebuild();
            });
            Grid.SetColumn(remove, 1);
            row.Children.Add(check);
            row.Children.Add(remove);
            _list.Children.Add(row);
        }

        if (todos.Count == 0)
        {
            _list.Children.Add(Subtle(Loc.T("W.TodoEmpty")));
        }

        _footer.Text = todos.Count == 0 ? string.Empty : Loc.T("W.TodoLeft", todos.Count(t => !t.Done));
    }
}
