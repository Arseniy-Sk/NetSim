using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetSim.App.Localization;

namespace NetSim.App.Views;

/// <summary>Общая основа диалогов: область содержимого и кнопки OK/Отмена внизу.</summary>
public abstract class DialogBase : Window
{
    protected readonly DockPanel Root = new();
    protected readonly StackPanel ButtonBar = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 8, 12, 12) };
    protected Button? OkButton;

    protected DialogBase(string title, double width = 560, double height = 520)
    {
        Title = title;
        Width = width;
        Height = height;
        MinWidth = 360;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(246, 247, 249));
        DockPanel.SetDock(ButtonBar, Dock.Bottom);
        Root.Children.Add(ButtonBar);
        Content = Root;
    }

    protected void SetBody(UIElement body) => Root.Children.Add(body);

    protected Button AddButton(string text, Action onClick, bool isDefault = false, bool isCancel = false)
    {
        var b = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel, Style = (Style)FindResource("DialogButton") };
        b.Click += (_, _) => onClick();
        ButtonBar.Children.Add(b);
        return b;
    }

    /// <summary>Кнопки «OK» и «Отмена». OK вызывает <paramref name="onOk"/>; если тот вернул false — окно не закрывается.</summary>
    protected void AddOkCancel(Func<bool> onOk, string? okText = null)
    {
        OkButton = AddButton(okText ?? Loc.T("Btn_Ok"), () => { if (onOk()) DialogResult = true; }, isDefault: true);
        AddButton(Loc.T("Btn_Cancel"), () => DialogResult = false, isCancel: true);
    }

    protected void AddClose() => AddButton(Loc.T("Btn_Close"), Close, isDefault: true, isCancel: true);

    /// <summary>Сетка «подпись — поле».</summary>
    protected sealed class FormGrid : Grid
    {
        public FormGrid(double labelWidth = 170)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        public T Row<T>(string label, T control) where T : UIElement
        {
            int r = RowDefinitions.Count;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var tb = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4), TextWrapping = TextWrapping.Wrap };
            SetRow(tb, r);
            Children.Add(tb);
            if (control is FrameworkElement fe) fe.Margin = new Thickness(0, 3, 0, 3);
            SetRow(control, r);
            SetColumn(control, 1);
            Children.Add(control);
            return control;
        }

        public void Full(UIElement control)
        {
            int r = RowDefinitions.Count;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            SetRow(control, r);
            SetColumnSpan(control, 2);
            Children.Add(control);
        }
    }

    /// <summary>Активное окно приложения — владелец для вложенных диалогов.</summary>
    public static Window ActiveWindow() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow!;

    public static bool TryNumber(string text, out double value) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public static string Num(double v, string format = "0.###") => v.ToString(format, CultureInfo.CurrentCulture);
}

/// <summary>Простой запрос строки.</summary>
public sealed class InputWindow : DialogBase
{
    private readonly TextBox _box;

    private InputWindow(string title, string label, string initial) : base(title, 420, 170)
    {
        SizeToContent = SizeToContent.Height;
        var g = new FormGrid(120) { Margin = new Thickness(12) };
        _box = g.Row(label, new TextBox { Text = initial });
        SetBody(g);
        AddOkCancel(() => true);
        Loaded += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    public static string? Ask(string title, string label, string initial)
    {
        var w = new InputWindow(title, label, initial) { Owner = ActiveWindow() };
        return w.ShowDialog() == true ? w._box.Text : null;
    }
}
