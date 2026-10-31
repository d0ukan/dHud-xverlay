using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DHud.Services;
using Microsoft.Win32;

namespace DHud;

/// <summary>Ayarlar → Profiller sekmesi: ayarları adlandırıp kaydetme, yedek alma ve başka bilgisayara taşıma.</summary>
public partial class SettingsWindow
{
    private const string ProfileFilter = "dHud profile (*.dhudprofile)|*.dhudprofile|JSON (*.json)|*.json";

    private StackPanel? _profileList;
    private TextBlock? _profileStatus;
    private TextBlock? _backupStatus;
    private TextBox? _profileName;
    private CheckBox? _includeWidgets;
    private CheckBox? _includeKey;

    private ProfileService Profiles => App.Current.Profiles;

    private void BuildProfilesTab()
    {
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

        // --- Kaydet ---
        root.Children.Add(Header(Loc.T("S.ProfilesSave")));
        root.Children.Add(Hint(Loc.T("S.ProfilesHint")));
        var saveCard = Card();
        var saveRow = new Grid();
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        saveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _profileName = new TextBox
        {
            MaxLength = ProfileService.MaxNameLength,
            Padding = new Thickness(6, 4, 6, 4),
            Background = new SolidColorBrush(Color.FromArgb(0x1A, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CaretBrush = Brushes.White,
            ToolTip = Loc.T("S.ProfileName")
        };
        _profileName.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        _profileName.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                SaveProfile();
                e.Handled = true;
            }
        };
        var save = Button(Loc.T("S.ProfileSave"), SaveProfile);
        save.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(save, 1);
        saveRow.Children.Add(_profileName);
        saveRow.Children.Add(save);
        _profileStatus = Hint(string.Empty);
        saveCard.Child = new StackPanel { Children = { saveRow, _profileStatus } };
        root.Children.Add(saveCard);

        // --- Profillerim ---
        root.Children.Add(Header(Loc.T("S.MyProfiles")));
        _profileList = new StackPanel();
        root.Children.Add(_profileList);

        // --- Yedek / taşıma ---
        root.Children.Add(Header(Loc.T("S.Backup")));
        root.Children.Add(Hint(Loc.T("S.BackupHint")));
        var backupCard = Card();
        var backup = new StackPanel();
        _includeWidgets = new CheckBox { Content = Loc.T("S.BackupWidgets"), IsChecked = true };
        _includeWidgets.SetResourceReference(StyleProperty, "DarkCheckBox");
        _includeKey = new CheckBox { Content = Loc.T("S.BackupKey"), ToolTip = Loc.T("S.BackupKeyTip") };
        _includeKey.SetResourceReference(StyleProperty, "DarkCheckBox");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(Button(Loc.T("S.ExportAll"), ExportAll));
        buttons.Children.Add(Button(Loc.T("S.Import"), Import));
        _backupStatus = Hint(string.Empty);
        backup.Children.Add(_includeWidgets);
        backup.Children.Add(_includeKey);
        backup.Children.Add(buttons);
        backup.Children.Add(_backupStatus);
        backupCard.Child = backup;
        root.Children.Add(backupCard);

        ProfilesTabContent.Children.Add(root);
        RefreshProfileList();
    }

    // ---- Küçük yapı taşları ----

    private TextBlock Header(string text)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(StyleProperty, "SectionHeader");
        return block;
    }

    private TextBlock Hint(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(StyleProperty, "HintText");
        return block;
    }

    private static Border Card() => new()
    {
        Margin = new Thickness(0, 4, 0, 6),
        Padding = new Thickness(12, 10, 12, 10),
        CornerRadius = new CornerRadius(10),
        Background = new SolidColorBrush(Color.FromArgb(0x1C, 255, 255, 255)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x26, 255, 255, 255)),
        BorderThickness = new Thickness(1)
    };

    private Button Button(string text, Action onClick, bool danger = false)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 4, 12, 4) };
        button.SetResourceReference(StyleProperty, "DarkButton");
        if (danger)
        {
            button.Padding = new Thickness(9, 4, 9, 4);
        }

        button.Click += (_, _) => onClick();
        return button;
    }

    private static void Say(TextBlock? target, string text, bool warn = false)
    {
        if (target is null)
        {
            return;
        }

        target.Text = text;
        target.SetResourceReference(TextBlock.ForegroundProperty, warn ? "WarnBrush" : "SubtleTextBrush");
    }

    private static string ErrorText(ProfileException ex) => Loc.T(ex.Reason switch
    {
        "newer" => "P.ErrNewer",
        "big" => "P.ErrBig",
        "name" => "P.ErrName",
        "many" => "P.ErrMany",
        _ => "P.ErrBad"
    }, ProfileService.MaxProfiles);

    private bool Confirm(string text) =>
        DarkMessageBox.Show(this, text, Loc.T("S.Tab.Profiles"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    // ---- Profiller ----

    private void RefreshProfileList()
    {
        if (_profileList is null)
        {
            return;
        }

        _profileList.Children.Clear();
        var profiles = Profiles.List();
        if (profiles.Count == 0)
        {
            _profileList.Children.Add(Hint(Loc.T("S.NoProfiles")));
        }

        foreach (var profile in profiles)
        {
            var card = Card();
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = profile.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(Hint(profile.SavedUtc.ToLocalTime().ToString("g")));

            var current = profile;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(Button(Loc.T("S.ProfileApply"), () => ApplyProfile(current)));
            buttons.Children.Add(Button(Loc.T("S.ProfileExport"), () => ExportProfile(current)));
            var delete = Button("✕", () => DeleteProfile(current), danger: true);
            delete.ToolTip = Loc.T("S.ProfileDelete");
            delete.Margin = new Thickness(0);
            buttons.Children.Add(delete);
            Grid.SetColumn(buttons, 1);

            row.Children.Add(text);
            row.Children.Add(buttons);
            card.Child = row;
            card.Margin = new Thickness(0, 2, 0, 4);
            _profileList.Children.Add(card);
        }

        App.Current.RefreshTrayProfiles();
    }

    private void SaveProfile()
    {
        var name = ProfileService.CleanName(_profileName?.Text ?? string.Empty);
        if (name.Length == 0)
        {
            Say(_profileStatus, Loc.T("P.ErrName"), warn: true);
            return;
        }

        if (Profiles.List().Any(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase))
            && !Confirm(Loc.T("P.Overwrite", name)))
        {
            return;
        }

        try
        {
            Profiles.Save(name);
            _profileName!.Clear();
            Say(_profileStatus, Loc.T("P.Saved", name));
            RefreshProfileList();
        }
        catch (ProfileException ex)
        {
            Say(_profileStatus, ErrorText(ex), warn: true);
        }
        catch (IOException)
        {
            Say(_profileStatus, Loc.T("P.ErrWrite"), warn: true);
        }
    }

    private void ApplyProfile(ProfileInfo profile)
    {
        if (!Confirm(Loc.T("P.ConfirmApply", profile.Name)))
        {
            return;
        }

        try
        {
            App.Current.ApplyProfile(ProfileService.Read(profile.Path), includeWidgetData: false, includePairingKey: false);
            Say(_profileStatus, Loc.T("P.Applied", profile.Name));
        }
        catch (ProfileException ex)
        {
            Say(_profileStatus, ErrorText(ex), warn: true);
        }
    }

    private void ExportProfile(ProfileInfo profile)
    {
        var dialog = new SaveFileDialog
        {
            Filter = ProfileFilter,
            FileName = ProfileService.SafeFileName(profile.Name) + ProfileService.Extension,
            DefaultExt = ProfileService.Extension
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.Copy(profile.Path, dialog.FileName, overwrite: true);
            Say(_profileStatus, Loc.T("P.Exported", Path.GetFileName(dialog.FileName)));
        }
        catch (IOException)
        {
            Say(_profileStatus, Loc.T("P.ErrWrite"), warn: true);
        }
        catch (UnauthorizedAccessException)
        {
            Say(_profileStatus, Loc.T("P.ErrWrite"), warn: true);
        }
    }

    private void DeleteProfile(ProfileInfo profile)
    {
        if (!Confirm(Loc.T("P.ConfirmDelete", profile.Name)))
        {
            return;
        }

        Profiles.Delete(profile);
        RefreshProfileList();
    }

    // ---- Yedek / taşıma ----

    private void ExportAll()
    {
        var includeKey = _includeKey?.IsChecked == true;
        if (includeKey && !Confirm(Loc.T("P.KeyWarn")))
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = ProfileFilter,
            FileName = $"dHud-{Environment.MachineName}-{DateTime.Now:yyyy-MM-dd}{ProfileService.Extension}",
            DefaultExt = ProfileService.Extension
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            Profiles.Export(dialog.FileName, _includeWidgets?.IsChecked == true, includeKey);
            Say(_backupStatus, Loc.T("P.Exported", Path.GetFileName(dialog.FileName)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say(_backupStatus, Loc.T("P.ErrWrite"), warn: true);
        }
    }

    private void Import()
    {
        var dialog = new OpenFileDialog { Filter = ProfileFilter, CheckFileExists = true };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ProfileFile file;
        try
        {
            file = ProfileService.Read(dialog.FileName);
        }
        catch (ProfileException ex)
        {
            Say(_backupStatus, ErrorText(ex), warn: true);
            return;
        }

        var widgetData = _includeWidgets?.IsChecked == true && file.Widgets is not null;
        var key = _includeKey?.IsChecked == true && file.HasPairingKey;
        var contents = new List<string> { Loc.T("P.ContentSettings") };
        if (widgetData)
        {
            var w = file.Widgets!;
            contents.Add(Loc.T("P.ContentWidgets", w.Notes.Count, w.Todos.Count, w.Countdowns.Count, w.Launchers.Count));
        }

        if (key)
        {
            contents.Add(Loc.T("P.ContentKey"));
        }

        var from = $"{(string.IsNullOrWhiteSpace(file.Name) ? Path.GetFileNameWithoutExtension(dialog.FileName) : file.Name)}"
                   + $" · {file.SavedUtc.ToLocalTime():g}" + (string.IsNullOrEmpty(file.AppVersion) ? string.Empty : $" · dHud {file.AppVersion}");
        if (!Confirm(Loc.T("P.ImportConfirm", from, string.Join(", ", contents))))
        {
            return;
        }

        App.Current.ApplyProfile(file, widgetData, key);
        Say(_backupStatus, Loc.T("P.Imported"));
        RefreshProfileList();
    }
}
