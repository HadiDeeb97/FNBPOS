using System.Windows.Markup;
using ClothingStore.Core.Localization;

namespace ClothingStore.Desktop.Localization;

/// <summary>
/// <c>{l:T Register.Title}</c>: translated text for the current language. Windows are rebuilt when the
/// language changes, so the value is read once when the view loads.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
