using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DHud.Services;
using Microsoft.Win32;

namespace DHud.Widgets;

/// <summary>
/// Hızlı başlatıcı: en sık kullandığın uygulama/dosya/klasörler. Öğeler Windows Gezgini aracılığıyla açılır; böylece dHud yönetici
/// olarak çalışsa bile uygulamalar normal (yükseltilmemiş) yetkiyle başlar.
/// </summary>
public sealed class LauncherWidgetWindow : WidgetWindowBase
{
    private readonly WidgetService _widgets = App.Current.Widgets;
    private readonly WrapPanel _tiles = new() { MaxWidth = 300 };
    private readonly TextBlock _status;

    protected override WidgetOptions Options => App.Current.Settings.Current.Extra(WidgetIds.Launcher);

    public LauncherWidgetWindow()
    {
        var add = IconButton("＋", Loc.T("W.LauncherAdd"), (_, _) => AddItem());
        var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12), MinWidth = 150 };
        root.Children.Add(TitleBar(Loc.T("W.Launcher"), WidgetIds.Launcher, add));
        _tiles.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(_tiles);
        _status = Subtle("");
        _status.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(_status);
        Card.Child = root;

        RestorePosition(WidgetIds.Launcher, 10, 200);
        Rebuild();
        ApplyOptions();
    }

    private void AddItem()
    {
        if (_widgets.Data.Launchers.Count >= WidgetService.MaxLaunchers)
        {
            _status.Text = Loc.T("W.TooMany");
            return;
        }

        var dialog = new OpenFileDialog { Title = Loc.T("W.LauncherAdd"), CheckFileExists = true, Filter = Loc.T("W.AllFiles") + "|*.*" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _widgets.Data.Launchers.Add(new LauncherItem { Name = Path.GetFileNameWithoutExtension(dialog.FileName), Path = dialog.FileName });
        _widgets.ScheduleSave();
        Rebuild();
    }

    private void Rebuild()
    {
        _tiles.Children.Clear();
        _status.Text = _widgets.Data.Launchers.Count == 0 ? Loc.T("W.LauncherEmpty") : string.Empty;

        foreach (var item in _widgets.Data.Launchers.ToList())
        {
            var stack = new StackPanel { Width = 64, Margin = new Thickness(0, 0, 4, 4) };
            stack.Children.Add(new Image { Source = IconOf(item.Path), Width = 28, Height = 28, Margin = new Thickness(0, 2, 0, 2) });
            stack.Children.Add(new TextBlock
            {
                Text = item.Name,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center
            });

            var button = new Button { Content = stack, ToolTip = item.Path, Padding = new Thickness(2, 4, 2, 4) };
            button.SetResourceReference(StyleProperty, "DarkButton");
            var current = item;
            button.Click += (_, _) => Launch(current);

            var menu = new ContextMenu();
            var remove = new MenuItem { Header = Loc.T("W.LauncherRemove") };
            remove.Click += (_, _) =>
            {
                _widgets.Data.Launchers.Remove(current);
                _widgets.ScheduleSave();
                Rebuild();
            };
            menu.Items.Add(remove);
            button.ContextMenu = menu;
            _tiles.Children.Add(button);
        }
    }

    private void Launch(LauncherItem item)
    {
        if (!File.Exists(item.Path) && !Directory.Exists(item.Path))
        {
            _status.Text = Loc.T("W.LauncherMissing", item.Name);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.Path}\"") { UseShellExecute = false });
            _status.Text = string.Empty;
        }
        catch (Exception)
        {
            _status.Text = Loc.T("W.LauncherFail", item.Name);
        }
    }

    private static ImageSource? IconOf(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception)
        {
            return null; // simge okunamadı (ör. klasör): boş simge gösterilir
        }
    }
}
