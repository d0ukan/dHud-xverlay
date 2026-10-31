using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DHud.Helpers;
using DHud.Services;

namespace DHud;

/// <summary>Ayarlar penceresi: ayarlarda arama ve yardım (tanı bilgisi) düğmeleri.</summary>
public partial class SettingsWindow
{
    private sealed record Hit(string Text, RadioButton Tab, FrameworkElement Target);

    private List<Hit> _hits = [];

    // ---- Arama ----

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text.Trim();
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchResults.Children.Clear();
        if (query.Length < 2)
        {
            SearchPopup.IsOpen = false;
            return;
        }

        _hits = FindSettings(query).Take(9).ToList();
        if (_hits.Count == 0)
        {
            SearchResults.Children.Add(new TextBlock { Text = Loc.T("S.SearchNone"), Margin = new Thickness(10, 6, 10, 6), Foreground = (Brush)FindResource("SubtleTextBrush") });
        }

        foreach (var hit in _hits)
        {
            var current = hit;
            var button = new Button
            {
                Content = new TextBlock { Text = $"{current.Tab.Content}  ›  {current.Text}", TextTrimming = TextTrimming.CharacterEllipsis },
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 1, 0, 1)
            };
            button.SetResourceReference(StyleProperty, "DarkButton");
            button.Click += (_, _) => GoTo(current);
            SearchResults.Children.Add(button);
        }

        SearchPopup.IsOpen = true;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Clear();
            SearchPopup.IsOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _hits.Count > 0)
        {
            GoTo(_hits[0]);
            e.Handled = true;
        }
    }

    /// <summary>Tüm sekmelerdeki ayar adlarını (onay kutuları, seçenekler, düğmeler, bölüm ve widget başlıkları) tarar.</summary>
    private IEnumerable<Hit> FindSettings(string query)
    {
        if (TabScroll.Content is not Grid container)
        {
            yield break;
        }

        // XAML'deki içerik sırası = sekme sırası.
        RadioButton[] tabs = [AppearanceTab, MetricsTab, BehaviorTab, WidgetsTab, ProfilesTab, AlertsTab];
        var sectionHeader = TryFindResource("SectionHeader") as Style;
        var seen = new HashSet<string>();

        for (var i = 0; i < container.Children.Count && i < tabs.Length; i++)
        {
            foreach (var element in Descendants(container.Children[i]))
            {
                var text = element switch
                {
                    CheckBox { Content: string s } => s,
                    RadioButton { Content: string s } => s,
                    Button { Content: string s } when s.Length > 2 => s,
                    TextBlock { Tag: "card" } t => t.Text,
                    TextBlock t when sectionHeader is not null && ReferenceEquals(t.Style, sectionHeader) => t.Text,
                    _ => null
                };
                if (string.IsNullOrWhiteSpace(text) || !text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                if (seen.Add($"{i}|{text}"))
                {
                    yield return new Hit(text, tabs[i], (FrameworkElement)element);
                }
            }
        }
    }

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not FrameworkElement element)
            {
                continue;
            }

            yield return element;
            foreach (var inner in Descendants(element))
            {
                yield return inner;
            }
        }
    }

    private void GoTo(Hit hit)
    {
        SearchPopup.IsOpen = false;
        hit.Tab.IsChecked = true;

        // Kapalı bir widget ayrıntı panelinin içindeyse paneli aç.
        for (DependencyObject? parent = hit.Target; parent is not null; parent = LogicalTreeHelper.GetParent(parent))
        {
            if (parent is StackPanel { Tag: Action refresh } panel && panel.Visibility != Visibility.Visible)
            {
                panel.Visibility = Visibility.Visible;
                refresh();
            }
        }

        SearchBox.Clear();
        Dispatcher.BeginInvoke(() =>
        {
            hit.Target.BringIntoView();
            hit.Target.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(260))
            {
                RepeatBehavior = new RepeatBehavior(3),
                FillBehavior = FillBehavior.Stop
            });
        }, DispatcherPriority.Loaded);
    }

    // ---- Yardım ----

    private void CopyDiagButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(App.Current.BuildDiagnostics());
            DiagStatusText.Text = Loc.T("S.DiagCopied");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            DiagStatusText.Text = Loc.T("W.ClipBusy");
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppPaths.DataDir);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.DataDir}\"") { UseShellExecute = false });
        }
        catch (Exception)
        {
            DiagStatusText.Text = Loc.T("P.ErrWrite");
        }
    }
}
