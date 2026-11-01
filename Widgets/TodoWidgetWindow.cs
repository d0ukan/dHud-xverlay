using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DHud.Services;

namespace DHud.Widgets;

/// <summary>
/// Yapılacaklar listesi: madde ekle (Enter), alt görev ekle (＋), tik at, sil, tamamlananları temizle.
/// Sağ tık: alt görevleriyle çoğalt / metin olarak panoya kopyala. widgets.json'da saklanır.
/// </summary>
public sealed class TodoWidgetWindow : WidgetWindowBase
{
    private readonly WidgetService _widgets = App.Current.Widgets;
    private readonly StackPanel _list = new();
    private readonly TextBox _input;
    private readonly TextBlock _footer;
    private string? _addingSubTo;   // alt görev kutusu açık olan görevin kimliği

    protected override WidgetOptions Options => App.Current.Settings.Current.Extra(WidgetIds.Todo);

    protected override bool ZoomByWidth => true;   // liste: genişlikle büyür, fazla yükseklik daha çok satır gösterir

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

    private void AddSubtask(TodoItem parent, string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (parent.Subtasks.Count >= WidgetService.MaxSubtasks)
        {
            _footer.Text = Loc.T("W.TodoSubFull", WidgetService.MaxSubtasks);
            return;
        }

        parent.Subtasks.Add(new TodoItem { Text = text });
        parent.Done = false;            // yeni yapılmamış iş eklendi: ana görev tamam sayılmaz
        _widgets.ScheduleSave();
        Rebuild();
    }

    private void Duplicate(TodoItem item)
    {
        if (_widgets.Data.Todos.Count >= WidgetService.MaxTodos)
        {
            _footer.Text = Loc.T("W.TodoFull", WidgetService.MaxTodos);
            return;
        }

        var index = _widgets.Data.Todos.IndexOf(item);
        _widgets.Data.Todos.Insert(index < 0 ? _widgets.Data.Todos.Count : index + 1, item.Duplicate());
        _widgets.ScheduleSave();
        Rebuild();
    }

    private void CopyAsText(TodoItem item)
    {
        try
        {
            Clipboard.SetText(ToText(item));
            _footer.Text = Loc.T("W.TodoCopied");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            _footer.Text = Loc.T("W.ClipBusy"); // pano başka uygulamada meşgul
        }
    }

    /// <summary>"- [ ] Görev" + girintili "- [x] alt görev" satırları.</summary>
    public static string ToText(TodoItem item)
    {
        var sb = new StringBuilder();
        sb.Append("- [").Append(item.Done ? 'x' : ' ').Append("] ").AppendLine(item.Text);
        foreach (var sub in item.Subtasks)
        {
            sb.Append("  - [").Append(sub.Done ? 'x' : ' ').Append("] ").AppendLine(sub.Text);
        }

        return sb.ToString().TrimEnd();
    }

    private void ClearDone()
    {
        var removed = _widgets.Data.Todos.RemoveAll(t => t.Done);
        foreach (var todo in _widgets.Data.Todos)
        {
            removed += todo.Subtasks.RemoveAll(s => s.Done);   // ana görev açıkken biten alt görevler de temizlenir
        }

        if (removed > 0)
        {
            _widgets.ScheduleSave();
            Rebuild();
        }
    }

    private CheckBox CheckRow(TodoItem item, Action<bool> onToggle)
    {
        var label = new TextBlock
        {
            Text = item.Text,
            TextWrapping = TextWrapping.Wrap,
            TextDecorations = item.Done ? TextDecorations.Strikethrough : null,
            Opacity = item.Done ? 0.5 : 1
        };
        var check = new CheckBox { Content = label, IsChecked = item.Done };
        check.SetResourceReference(StyleProperty, "DarkCheckBox");
        check.Click += (_, _) =>
        {
            onToggle(check.IsChecked == true);
            _widgets.ScheduleSave();
            Rebuild();
        };
        return check;
    }

    private Grid Row(UIElement check, params Button[] buttons)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(check);
        foreach (var button in buttons)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(button, row.ColumnDefinitions.Count - 1);
            row.Children.Add(button);
        }

        return row;
    }

    private void Rebuild()
    {
        _list.Children.Clear();
        var todos = _widgets.Data.Todos;
        foreach (var item in todos.OrderBy(t => t.Done).ToList())
        {
            var current = item;
            var check = CheckRow(current, done =>
            {
                current.Done = done;
                foreach (var sub in current.Subtasks)
                {
                    sub.Done = done;            // ana görevi işaretlemek alt görevleri de işaretler
                }
            });

            var add = IconButton("＋", Loc.T("W.TodoSubAdd"), (_, _) =>
            {
                _addingSubTo = _addingSubTo == current.Id ? null : current.Id;
                Rebuild();
            });
            var remove = IconButton("✕", Loc.T("W.Cancel"), (_, _) =>
            {
                todos.Remove(current);
                _widgets.ScheduleSave();
                Rebuild();
            });

            var main = Row(check, add, remove);
            main.Margin = new Thickness(0, 2, 0, 2);
            main.ContextMenu = BuildMenu(current);
            _list.Children.Add(main);

            if (current.Subtasks.Count > 0)
            {
                var progress = Subtle($"{current.Subtasks.Count(s => s.Done)}/{current.Subtasks.Count}", 11);
                progress.Margin = new Thickness(26, -2, 0, 2);
                _list.Children.Add(progress);
            }

            foreach (var sub in current.Subtasks)
            {
                var subItem = sub;
                var subCheck = CheckRow(subItem, done => subItem.Done = done);
                var subRemove = IconButton("✕", Loc.T("W.Cancel"), (_, _) =>
                {
                    current.Subtasks.Remove(subItem);
                    _widgets.ScheduleSave();
                    Rebuild();
                });
                var subRow = Row(subCheck, subRemove);
                subRow.Margin = new Thickness(22, 0, 0, 0);
                subRow.ContextMenu = BuildMenu(current);
                _list.Children.Add(subRow);
            }

            if (_addingSubTo == current.Id)
            {
                var box = Field(200);
                box.Margin = new Thickness(22, 2, 0, 4);
                box.ToolTip = Loc.T("W.TodoSubHint");
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        AddSubtask(current, box.Text);   // kutu açık kalır: arka arkaya eklenebilir
                        e.Handled = true;
                    }
                    else if (e.Key == Key.Escape)
                    {
                        _addingSubTo = null;
                        Rebuild();
                        e.Handled = true;
                    }
                };
                _list.Children.Add(box);
                box.Dispatcher.BeginInvoke(() => box.Focus(), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        if (todos.Count == 0)
        {
            _list.Children.Add(Subtle(Loc.T("W.TodoEmpty")));
        }

        _footer.Text = todos.Count == 0 ? string.Empty : Loc.T("W.TodoLeft", todos.Count(t => !t.Done));
    }

    private ContextMenu BuildMenu(TodoItem item)
    {
        var menu = new ContextMenu();
        var sub = new MenuItem { Header = Loc.T("W.TodoSubAdd") };
        sub.Click += (_, _) =>
        {
            _addingSubTo = item.Id;
            Rebuild();
        };
        var duplicate = new MenuItem { Header = Loc.T("W.TodoDuplicate") };
        duplicate.Click += (_, _) => Duplicate(item);
        var copy = new MenuItem { Header = Loc.T("W.TodoCopy") };
        copy.Click += (_, _) => CopyAsText(item);
        menu.Items.Add(sub);
        menu.Items.Add(duplicate);
        menu.Items.Add(copy);
        return menu;
    }
}
