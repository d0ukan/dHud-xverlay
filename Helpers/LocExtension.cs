using System.Windows.Markup;
using DHud.Services;

namespace DHud.Helpers;

/// <summary>XAML'de çevrilmiş metin: <c>Text="{h:Loc S.Close}"</c>. Pencere açılırken seçili dile göre çözülür.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
