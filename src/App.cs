using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

[assembly: AssemblyTitle("轻启 · 一键启动")]
[assembly: AssemblyDescription("把常用应用放在一起，一个分组，一次开启。")]
[assembly: AssemblyCompany("轻启")]
[assembly: AssemblyProduct("轻启")]
[assembly: AssemblyVersion("1.2.2.0")]
[assembly: AssemblyFileVersion("1.2.2.0")]
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace OrbitLauncher
{
    public static class Entry
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // Keep the DPI preferences in the executable so no companion config is needed.
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
            AppContext.SetSwitch("Switch.System.Windows.DoNotUsePresentationDpiCapabilityTier2OrGreater", false);
            if (args.Length == 2 && args[0] == "--probe") { File.WriteAllText(args[1], "orbit-probe-ok", Encoding.UTF8); return 0; }
            if (args.Length == 2 && args[0] == "--self-test") return Tests.Run(Path.GetFullPath(args[1]));
            if (args.Length == 2 && args[0] == "--preview") return Preview.Run(Path.GetFullPath(args[1]));
            if (args.Length == 2 && args[0] == "--icon-test") return IconVerification.Run(Path.GetFullPath(args[1]));
            if (args.Length == 2 && args[0] == "--ui-test") return UiVerification.Run(Path.GetFullPath(args[1]));
            if (args.Length == 2 && args[0] == "--responsiveness-test") return ResponsivenessVerification.Run(Path.GetFullPath(args[1]));
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OrbitLauncher");
            string groupId = null;
            for (int n = 0; n < args.Length; n++)
            {
                if (args[n] == "--data-dir" && n + 1 < args.Length) folder = Path.GetFullPath(args[++n]);
                else if (args[n] == "--group" && n + 1 < args.Length) groupId = args[++n];
                else { Standalone("无法识别启动参数", "请直接打开轻启，或使用分组菜单创建的快捷方式。"); return 2; }
            }
            string instanceKey = Hash(folder.ToUpperInvariant());
            bool created;
            using (var mutex = new Mutex(true, "Local\\OrbitLauncher-" + instanceKey, out created))
            {
                if (!created) { if (InstanceBroker.Send(instanceKey, groupId)) return 0; Standalone("暂时无法唤起轻启", "已有窗口可能正在启动或退出，请稍后重试。"); return 1; }
                try
                {
                    var app = CreateApplication();
                    app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                    {
                        e.Handled = true;
                        Ui.Error(app.MainWindow, "操作未能完成：\n" + e.Exception.Message);
                    };
                    var store = new ConfigStore(folder); var config = store.Load();
                    var main = new MainWindow(store, config);
                    if (groupId != null)
                    {
                        var group = config.Groups.FirstOrDefault(g => g.Id == groupId);
                        if (group == null) main.Loaded += delegate { Ui.Info(main, "分组已不存在", "此快捷方式对应的分组已经不存在，请重新创建快捷方式。"); };
                        else main.Loaded += delegate { main.Dispatcher.BeginInvoke(new Action(delegate { main.RunGroup(group); }), DispatcherPriority.ApplicationIdle); };
                    }
                    using (var broker = new InstanceBroker(instanceKey, delegate(string command) { main.Dispatcher.BeginInvoke(new Action(delegate { main.Receive(command); })); }))
                    { app.ShutdownMode = ShutdownMode.OnMainWindowClose; return app.Run(main); }
                }
                catch (Exception e)
                {
                    try { Standalone("轻启无法启动", e.Message); }
                    catch { MessageBox.Show(e.Message, "轻启无法启动", MessageBoxButton.OK, MessageBoxImage.Error); }
                    return 1;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
        public static Application CreateApplication()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Orbit.Theme")) app.Resources = (ResourceDictionary)XamlReader.Load(stream);
            return app;
        }
        static void Standalone(string title, string message)
        {
            bool ownsApplication = Application.Current == null;
            var app = Application.Current ?? CreateApplication(); Ui.Info(null, title, message); if (ownsApplication) app.Shutdown();
        }
        static string Hash(string s)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-", "").Substring(0, 24);
        }
    }
}
