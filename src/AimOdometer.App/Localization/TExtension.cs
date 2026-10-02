using System.Windows.Data;
using System.Windows.Markup;

namespace AimOdometer.App.Localization;

/// <summary>
/// XAML: Text="{l:T Overview.Today}". Binds to <see cref="Loc"/> so the text follows language switches live.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
