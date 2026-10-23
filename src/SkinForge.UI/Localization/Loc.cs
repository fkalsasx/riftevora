using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using SkinForge.Core.Localization;

namespace SkinForge.UI.Localization;

/// <summary>Live-switchable UI text. Bind with <c>{loc:T key}</c>; code uses <see cref="T"/>.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    private IReadOnlyDictionary<string, string> _active = UiStrings.Tr;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public string Language { get; private set; } = "tr";
    public CultureInfo Culture { get; private set; } = new("tr-TR");

    public string this[string key] => Lookup(key);

    private string Lookup(string key)
    {
        if (_active.TryGetValue(key, out var value)) return value;
        if (UiStrings.En.TryGetValue(key, out value)) return value;
        return "⟦" + key + "⟧"; // a visible gap is better than a silent blank: the parity test also catches it
    }

    public void SetLanguage(string code)
    {
        code = UiStrings.Supported.Contains(code) ? code : "tr";
        if (code == Language) return;
        Language = code;
        _active = code == "en" ? UiStrings.En : UiStrings.Tr;
        Culture = new CultureInfo(code == "en" ? "en-US" : "tr-TR");
        CultureInfo.CurrentUICulture = Culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public static string T(string key) => Instance.Lookup(key);
    public static string T(string key, params object?[] args) => string.Format(Instance.Culture, Instance.Lookup(key), args);
    public static string Upper(string text) => text.ToUpper(Instance.Culture);

    /// <summary>"Çok" becomes "1 şampiyon" / "2 champions": one place for count phrasing.</summary>
    public static string Count(string key, int count)
    {
        var forms = Instance.Lookup(key).Split('|');
        var form = forms.Length > 1 && count != 1 ? forms[1] : forms[0];
        return string.Format(Instance.Culture, form, count.ToString("N0", Instance.Culture));
    }
}

/// <summary>Inserts hair spaces between letters: WPF text has no letter-spacing, this gives uppercase labels their tracking.</summary>
public sealed class TrackingConverter : IValueConverter
{
    public static readonly TrackingConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = Loc.Upper(value as string ?? "");
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) elements.Add((string)enumerator.Current);
        return string.Join(' ', elements);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class UpperConverter : IValueConverter
{
    public static readonly UpperConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Loc.Upper(value as string ?? "");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) { Key = key; }
    [ConstructorArgument("key")] public string Key { get; set; } = "";
    /// <summary>Uppercase in the active language (Turkish İ handled correctly).</summary>
    public bool Upper { get; set; }
    /// <summary>Uppercase plus letter-spacing, for small section labels.</summary>
    public bool Track { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
        if (Track) binding.Converter = TrackingConverter.Instance;
        else if (Upper) binding.Converter = UpperConverter.Instance;
        return binding.ProvideValue(serviceProvider);
    }
}
