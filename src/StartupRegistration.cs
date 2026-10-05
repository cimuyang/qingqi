using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace OrbitLauncher
{
    public sealed class StartupRegistration
    {
        const string ValueName = "OrbitLauncher";
        readonly string runPath;
        readonly string approvalPath;
        readonly string command;

        public sealed class State
        {
            internal object Value;
            internal RegistryValueKind Kind;
            public bool Registered { get { return Value != null; } }
            public bool CurrentPath { get; internal set; }
            public bool Disabled { get; internal set; }
            public string Description
            {
                get
                {
                    if (!Registered) return "尚未开启；勾选后点击“保存设置”生效。";
                    if (Disabled) return "Windows 已禁用此启动项，请在系统“启动应用”中启用轻启。";
                    if (!CurrentPath) return "程序位置已变化；点击“保存设置”更新启动路径。";
                    return "已登记当前程序位置，登录后由 Windows 打开轻启；可能稍有延迟。";
                }
            }
        }

        public StartupRegistration() : this(@"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", Process.GetCurrentProcess().MainModule.FileName) { }
        // Verification uses an isolated registry branch, never the real login startup keys.
        internal StartupRegistration(string runPath, string approvalPath, string executable)
        {
            this.runPath = runPath; this.approvalPath = approvalPath;
            command = "\"" + Path.GetFullPath(executable) + "\"";
        }
        public State Read()
        {
            var state = new State();
            using (var key = Registry.CurrentUser.OpenSubKey(runPath))
            {
                if (key != null && key.GetValueNames().Contains(ValueName, StringComparer.OrdinalIgnoreCase))
                {
                    state.Value = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    state.Kind = key.GetValueKind(ValueName);
                }
            }
            state.CurrentPath = String.Equals(state.Value as string, command, StringComparison.OrdinalIgnoreCase);
            using (var key = Registry.CurrentUser.OpenSubKey(approvalPath))
            {
                var bytes = key == null ? null : key.GetValue(ValueName) as byte[];
                state.Disabled = bytes != null && bytes.Length >= 12 && (bytes[0] == 3 || bytes[0] == 7);
            }
            return state;
        }
        public void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(runPath))
            {
                if (enabled) key.SetValue(ValueName, command, RegistryValueKind.String);
                else key.DeleteValue(ValueName, false);
            }
            var state = Read();
            if (enabled ? !state.CurrentPath : state.Registered) throw new IOException("开机启动项校验失败，请重试。");
        }
        public void Restore(State previous)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(runPath))
            {
                if (previous.Registered) key.SetValue(ValueName, previous.Value, previous.Kind);
                else key.DeleteValue(ValueName, false);
            }
        }
    }
}
