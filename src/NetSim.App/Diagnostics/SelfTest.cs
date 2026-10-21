using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NetSim.App.ViewModels;
using NetSim.App.Views;
using NetSim.Core.Model;

namespace NetSim.App.Diagnostics;

/// <summary>
/// Самопроверка интерфейса: <c>NetSim.App.exe --selftest &lt;папка&gt;</c>.
/// Открывает примеры, прогоняет моделирование, открывает основные окна и сохраняет их снимки в PNG.
/// Удобно для быстрой проверки сборки без ручного «прокликивания».
/// </summary>
public static class SelfTest
{
    public static async void Run(MainWindow window, MainViewModel vm, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var log = new List<string>();
        try
        {
            await Settle();
            Snap(window, outDir, "01-empty");

            vm.OpenSampleCommand.Execute("techno");
            vm.RunCommand.Execute(null);
            vm.TrafficSim!.RunUntil(30);
            vm.PauseCommand.Execute(null);
            vm.RequestRedraw();
            await Settle();
            Snap(window, outDir, "02-techno-running");
            log.Add($"techno: events={vm.TrafficSim.ProcessedEvents}, diagnostics={vm.TrafficSim.Diagnostics.Count}");

            var server = vm.Project.AllDevices().First(d => d.Name == "Data server");
            vm.Selected = server;
            await Settle();
            Snap(window, outDir, "03-techno-selected");

            await Dialog(new StatisticsReportWindow(vm), outDir, "04-report");
            await Dialog(new DataFlowWindow(vm), outDir, "05-dataflow");
            await Dialog(new DeviceConfigWindow(vm, server), outDir, "06-device-config");
            var pc = vm.Project.AllDevices().First(d => d.Name == "Sales PC 1");
            await Dialog(new ProfilesWindow(vm, pc, server), outDir, "07-profiles");
            var sw = vm.Project.AllDevices().First(d => d.Name == "Sales switch");
            await Dialog(new LinkAssistantWindow(vm, sw, pc, null), outDir, "08-link-assistant");
            await Dialog(new CalculatorsWindow(vm), outDir, "09-calculators");
            await Dialog(new HelpWindow(), outDir, "10-help");

            vm.IsDirty = false;
            vm.OpenSampleCommand.Execute("hier");
            await Settle();
            Snap(window, outDir, "11-hier-root");
            vm.OpenContainer(vm.Project.Root.Devices.First(d => d.Name == "California"));
            await Settle();
            Snap(window, outDir, "12-hier-california");

            vm.IsDirty = false;
            vm.OpenSampleCommand.Execute("ip-demo");
            await Settle();
            var pc1 = vm.Project.AllDevices().First(d => d.Name == "PC 1");
            var pc3 = vm.Project.AllDevices().First(d => d.Name == "PC 3");
            vm.SendPdu(pc1, pc3);
            vm.PauseCommand.Execute(null);
            vm.ProtoSim!.Engine.RunUntil(vm.ProtoSim.Engine.Now + 0.9);
            vm.RequestRedraw();
            await Settle();
            Snap(window, outDir, "13-ip-pdu");

            var router = vm.Project.AllDevices().First(d => d.Name == "R1");
            var console = new ConsoleWindow(vm, router) { Owner = window };
            console.Show();
            await Settle();
            foreach (var cmd in new[] { "en", "sh ip int br", "ping 192.168.2.10", "show ip route" })
                Type(console, cmd);
            await Settle();
            Snap(console, outDir, "14-console");
            console.Close();

            vm.IsDirty = false;
            vm.Mode = SimMode.Traffic;
            vm.OpenSampleCommand.Execute("wifi-office");
            await Settle();
            Snap(window, outDir, "15-wifi-plan");
            await Dialog(new CoverageWindow(vm), outDir, "16-coverage");

            vm.IsDirty = false;
            vm.OpenSampleCommand.Execute("techno");
            vm.RunCommand.Execute(null);
            vm.TrafficSim!.RunUntil(20);
            vm.PauseCommand.Execute(null);
            vm.Project.RememberCalculation("ПР8: нагрузка ЛВС", NetSim.Calculators.LanLoadCalculator.Calculate(new NetSim.Calculators.LanLoadInput()).Report.ToText());
            await Settle();
            new ReportWindow(vm, window.RenderLevel).Build(Path.Combine(outDir, "report-techno.docx"));
            vm.IsDirty = false;
            vm.OpenSampleCommand.Execute("hier");
            await Settle();
            new ReportWindow(vm, window.RenderLevel).Build(Path.Combine(outDir, "report-hier.docx"));
            log.Add("reports ok");
            log.Add("ok");
        }
        catch (Exception ex)
        {
            log.Add("FAIL: " + ex);
        }
        File.WriteAllLines(Path.Combine(outDir, "selftest.log"), log);
        vm.IsDirty = false;
        Application.Current.Shutdown();
    }

    private static void Type(ConsoleWindow console, string cmd)
    {
        var input = (System.Windows.Controls.TextBox)((System.Windows.Controls.DockPanel)((System.Windows.Controls.DockPanel)console.Content).Children[0]).Children[1];
        input.Text = cmd;
        input.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(input)!, 0, System.Windows.Input.Key.Enter)
        {
            RoutedEvent = UIElement.PreviewKeyDownEvent,
        });
    }

    internal static async Task Dialog(Window w, string dir, string name)
    {
        w.Owner = Application.Current.MainWindow;
        w.Show();
        await Settle();
        Snap(w, dir, name);
        w.Close();
    }

    internal static async Task Settle()
    {
        for (int i = 0; i < 3; i++)
        {
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(150);
        }
    }

    internal static void Snap(Window w, string dir, string name)
    {
        if (w.Content is FrameworkElement root) SnapElement(root, dir, name);
    }

    /// <summary>Снимок отдельного элемента окна (например, одного редактора закона распределения).</summary>
    internal static void SnapElement(FrameworkElement root, string dir, string name)
    {
        var dpi = VisualTreeHelper.GetDpi(root);
        var bmp = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        }
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(dir, name + ".png"));
        enc.Save(fs);
    }
}
