using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NetSim.App.Localization;
using NetSim.App.ViewModels;
using NetSim.Core.Model;
using NetSim.Protocols.Cli;

namespace NetSim.App.Views;

/// <summary>Консоль устройства: Cisco IOS CLI для маршрутизаторов/коммутаторов, командная строка для ПК.</summary>
public sealed class ConsoleWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly CommandShell _shell;
    private readonly TextBox _output = new()
    {
        IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 13, Background = Brushes.Black, Foreground = Brushes.Gainsboro,
        BorderThickness = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(6),
    };
    private readonly TextBlock _prompt = new() { FontFamily = new FontFamily("Consolas"), FontSize = 13, Foreground = Brushes.LightGreen, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) };
    private readonly TextBox _input = new() { FontFamily = new FontFamily("Consolas"), FontSize = 13, Background = Brushes.Black, Foreground = Brushes.White, BorderThickness = new Thickness(0), CaretBrush = Brushes.White };
    private readonly List<string> _history = new();
    private int _historyIndex;

    public ConsoleWindow(MainViewModel vm, Device device)
    {
        _vm = vm;
        _shell = CommandShell.For(device, vm.ProtoSim!);
        Title = Loc.F("Console_Title", device.Name);
        Width = 760;
        Height = 520;
        Background = Brushes.Black;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var dock = new DockPanel();
        var inputRow = new DockPanel { Background = Brushes.Black, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(_prompt, Dock.Left);
        inputRow.Children.Add(_prompt);
        inputRow.Children.Add(_input);
        DockPanel.SetDock(inputRow, Dock.Bottom);
        dock.Children.Add(inputRow);
        dock.Children.Add(_output);
        Content = dock;

        foreach (var l in _shell.Banner()) Append(l);
        Append(Loc.T("Console_Hint"));
        Append("");
        UpdatePrompt();
        _input.PreviewKeyDown += OnKey;
        Loaded += (_, _) => _input.Focus();
    }

    private void Append(string line)
    {
        _output.AppendText(line + Environment.NewLine);
        _output.ScrollToEnd();
    }

    private void UpdatePrompt() => _prompt.Text = _shell.Prompt;

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                var line = _input.Text;
                Append(_shell.Prompt + line);
                _input.Clear();
                if (line.Trim().Equals("cls", StringComparison.OrdinalIgnoreCase) || line.Trim().Equals("clear", StringComparison.OrdinalIgnoreCase))
                    _output.Clear();
                else
                    foreach (var l in _shell.Execute(line)) Append(l);
                if (!string.IsNullOrWhiteSpace(line)) _history.Add(line);
                _historyIndex = _history.Count;
                UpdatePrompt();
                _vm.IsDirty = true;
                _vm.RequestRedraw();
                e.Handled = true;
                break;
            case Key.Up:
                if (_history.Count > 0) { _historyIndex = Math.Max(0, _historyIndex - 1); _input.Text = _history[_historyIndex]; _input.CaretIndex = _input.Text.Length; }
                e.Handled = true;
                break;
            case Key.Down:
                if (_history.Count > 0)
                {
                    _historyIndex = Math.Min(_history.Count, _historyIndex + 1);
                    _input.Text = _historyIndex < _history.Count ? _history[_historyIndex] : "";
                    _input.CaretIndex = _input.Text.Length;
                }
                e.Handled = true;
                break;
        }
    }
}
