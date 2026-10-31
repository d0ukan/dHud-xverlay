using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DHud.Services;

namespace DHud;

/// <summary>
/// Sistem mesaj kutusunun yerine geçen, uygulamanın koyu temasıyla ve seçili dilde gösterilen pencere.
/// <see cref="DarkMessageBox"/> üzerinden kullanılır.
/// </summary>
public partial class DarkMessageWindow : Window
{
    public MessageBoxResult Result { get; private set; }

    private readonly MessageBoxResult _cancelResult;

    public DarkMessageWindow(string text, string title, MessageBoxButton buttons, MessageBoxImage icon)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = text;

        (IconText.Text, IconText.Foreground) = icon switch
        {
            MessageBoxImage.Warning => ("⚠", (Brush)FindResource("WarnBrush")),
            MessageBoxImage.Error => ("✖", (Brush)FindResource("HotBrush")),
            MessageBoxImage.Question => ("?", (Brush)FindResource("AccentBrush")),
            _ => ("ℹ", (Brush)FindResource("AccentBrush"))
        };

        switch (buttons)
        {
            case MessageBoxButton.YesNo:
                // Uyarı (riskli işlem) onaylarında varsayılan (Enter) "Hayır": yanlışlıkla Enter ile onaylanmasın.
                var safeNo = icon == MessageBoxImage.Warning;
                AddButton(Loc.T("S.Yes"), MessageBoxResult.Yes, isDefault: !safeNo, isCancel: false);
                AddButton(Loc.T("S.No"), MessageBoxResult.No, isDefault: safeNo, isCancel: true);
                _cancelResult = MessageBoxResult.No;
                Result = MessageBoxResult.No;
                break;

            default:
                AddButton(Loc.T("S.Ok"), MessageBoxResult.OK, isDefault: true, isCancel: true);
                _cancelResult = MessageBoxResult.OK;
                Result = MessageBoxResult.OK;
                break;
        }
    }

    private void AddButton(string label, MessageBoxResult result, bool isDefault, bool isCancel)
    {
        var button = new Button
        {
            Content = label,
            Style = (Style)FindResource("DarkButton"),
            MinWidth = 84,
            Margin = new Thickness(ButtonPanel.Children.Count == 0 ? 0 : 8, 0, 0, 0),
            IsDefault = isDefault,
            IsCancel = isCancel
        };
        button.Click += (_, _) =>
        {
            Result = result;
            DialogResult = true;
        };
        ButtonPanel.Children.Add(button);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Result = _cancelResult;
        DialogResult = true;
    }
}

/// <summary><see cref="System.Windows.MessageBox"/> ile aynı kullanım; koyu tema ve uygulama dili ile.</summary>
public static class DarkMessageBox
{
    public static MessageBoxResult Show(string text, string title, MessageBoxButton buttons, MessageBoxImage icon) =>
        Show(null, text, title, buttons, icon);

    public static MessageBoxResult Show(Window? owner, string text, string title, MessageBoxButton buttons, MessageBoxImage icon)
    {
        var window = new DarkMessageWindow(text, title, buttons, icon);
        if (owner is { IsLoaded: true, IsVisible: true })
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        window.ShowDialog();
        return window.Result;
    }
}
