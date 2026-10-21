using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;

namespace NetSim.App.Localization;

/// <summary>
/// Локализация интерфейса (русский / английский) с переключением «на лету».
/// В XAML: <c>Text="{l:T Menu_File}"</c>; в коде: <c>Loc.I["Menu_File"]</c> или <c>Loc.T("Menu_File")</c>.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    private string _lang = "ru";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    /// <summary>Текущий язык: "ru" или "en".</summary>
    public string Lang
    {
        get => _lang;
        set
        {
            if (_lang == value) return;
            _lang = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lang)));
            LanguageChanged?.Invoke();
        }
    }

    public bool IsRu => _lang != "en";

    public string this[string key] =>
        Strings.Table.TryGetValue(key, out var v) ? (IsRu ? v.Ru : v.En) : $"[{key}]";

    public static string T(string key) => I[key];

    public static string F(string key, params object[] args) => string.Format(I[key], args);

    /// <summary>Выбор между двумя строками без словаря.</summary>
    public static string Pick(string ru, string en) => I.IsRu ? ru : en;
}

/// <summary>Расширение разметки: {l:T Key}.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public TExtension() { }
    public TExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.I, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
