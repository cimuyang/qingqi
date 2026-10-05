using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace OrbitLauncher
{
    public sealed class GroupPages
    {
        public const int PageSize = 4;
        readonly Dictionary<string, int> positions = new Dictionary<string, int>();
        public static int Count(int items) { return Math.Max(1, (items + PageSize - 1) / PageSize); }
        public int Get(string id, int items)
        {
            int value; if (!positions.TryGetValue(id, out value)) value = 0;
            return Set(id, items, value);
        }
        public int Set(string id, int items, int page) { int value = Math.Max(0, Math.Min(Count(items) - 1, page)); positions[id] = value; return value; }
        public void Retain(IEnumerable<string> ids) { var valid = new HashSet<string>(ids); foreach (var id in positions.Keys.Where(id => !valid.Contains(id)).ToList()) positions.Remove(id); }
    }
    public sealed class LaunchItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Target { get; set; }
        public string Arguments { get; set; }
        public string WorkingDirectory { get; set; }
        public LaunchItem() { Id = Guid.NewGuid().ToString("N"); Arguments = ""; WorkingDirectory = ""; }
    }
    public sealed class LaunchGroup
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Color { get; set; }
        public List<LaunchItem> Items { get; set; }
        public LaunchGroup() { Id = Guid.NewGuid().ToString("N"); Name = "新分组"; Color = "blue"; Items = new List<LaunchItem>(); }
    }
    public sealed class Preferences
    {
        public bool ExitAfterLaunch { get; set; }
        public int DelayMs { get; set; }
        public Preferences() { ExitAfterLaunch = true; DelayMs = 300; }
    }
    public sealed class Configuration
    {
        public int Version { get; set; }
        public List<LaunchGroup> Groups { get; set; }
        public Preferences Settings { get; set; }
        public Configuration() { Version = 1; Groups = new List<LaunchGroup>(); Settings = new Preferences(); }
        public static Configuration Initial()
        {
            var c = new Configuration();
            c.Groups.Add(new LaunchGroup { Name = "开始工作", Color = "blue" });
            c.Groups.Add(new LaunchGroup { Name = "保持联系", Color = "green" });
            c.Groups.Add(new LaunchGroup { Name = "专注时刻", Color = "purple" });
            return c;
        }
    }
    public static class ConfigCodec
    {
        static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024, RecursionLimit = 32 }; }
        public static string Encode(Configuration config) { Validate(config); return Serializer().Serialize(config); }
        public static Configuration Decode(string json)
        {
            var serializer = Serializer();
            var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("Version") || !root.ContainsKey("Groups") || !root.ContainsKey("Settings")) throw new InvalidDataException("配置缺少版本、分组或设置字段。");
            var c = serializer.Deserialize<Configuration>(json);
            Validate(c); return c;
        }
        public static Configuration Clone(Configuration c) { return Decode(Encode(c)); }
        public static void Validate(Configuration c)
        {
            if (c == null || c.Version != 1 || c.Groups == null || c.Settings == null) throw new InvalidDataException("配置格式无效或来自不支持的版本。");
            if (c.Groups.Count > 100 || c.Settings.DelayMs < 0 || c.Settings.DelayMs > 10000) throw new InvalidDataException("分组数量或启动间隔超出允许范围。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var g in c.Groups)
            {
                if (g == null || g.Items == null || g.Items.Count > 200) throw new InvalidDataException("分组内容无效；每组最多 200 项。");
                CheckId(g.Id, ids); CheckText(g.Name, 40, "分组名称");
                if (!new[] { "blue", "green", "purple", "orange", "pink", "slate" }.Contains(g.Color)) throw new InvalidDataException("分组颜色无效。");
                foreach (var i in g.Items)
                {
                    if (i == null) throw new InvalidDataException("应用项目无效。");
                    CheckId(i.Id, ids); CheckText(i.Name, 80, "应用名称"); CheckText(i.Target, 2048, "打开位置");
                    if ((i.Arguments ?? "").Length > 4096 || (i.WorkingDirectory ?? "").Length > 2048) throw new InvalidDataException("高级选项过长。");
                }
            }
        }
        static void CheckText(string value, int max, string label)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(ch => Char.IsControl(ch))) throw new InvalidDataException(label + "为空、过长或包含控制字符。");
        }
        static void CheckId(string id, HashSet<string> ids)
        {
            Guid parsed;
            if (!Guid.TryParse(id, out parsed) || !ids.Add(id)) throw new InvalidDataException("配置包含无效或重复的项目标识。");
        }
    }
    public sealed class ConfigStore
    {
        readonly SemaphoreSlim writes = new SemaphoreSlim(1);
        public string Folder { get; private set; }
        public string FilePath { get { return Path.Combine(Folder, "config.json"); } }
        public string RecoveryMessage { get; private set; }
        public ConfigStore(string folder) { Folder = Path.GetFullPath(folder); }
        public Configuration Load()
        {
            if (!File.Exists(FilePath)) return Configuration.Initial();
            try { return Read(FilePath); }
            catch (Exception primary)
            {
                try
                {
                    var recovered = Read(FilePath + ".bak");
                    File.Copy(FilePath, FilePath + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                    RecoveryMessage = "配置读取失败，已恢复上一份备份。原文件已保留。";
                    Save(recovered); File.Copy(FilePath, FilePath + ".bak", true); return recovered;
                }
                catch (Exception backup) { throw new InvalidDataException("无法读取配置及备份。原文件已保留，请检查：" + FilePath + "\n" + primary.Message + "\n" + backup.Message); }
            }
        }
        public static Configuration Read(string path)
        {
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("配置文件不能超过 2 MB。");
            return ConfigCodec.Decode(File.ReadAllText(path, Encoding.UTF8));
        }
        public void Save(Configuration c)
        {
            writes.Wait(); try { SaveCore(c); } finally { writes.Release(); }
        }
        public async Task SaveAsync(Configuration c)
        {
            await writes.WaitAsync().ConfigureAwait(false);
            try { await Task.Run(delegate { SaveCore(c); }).ConfigureAwait(false); }
            finally { writes.Release(); }
        }
        void SaveCore(Configuration c)
        {
            string json = ConfigCodec.Encode(c);
            Directory.CreateDirectory(Folder);
            string temp = FilePath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) { writer.Write(json); writer.Flush(); stream.Flush(true); }
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak", true);
                else File.Move(temp, FilePath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
    public static class Targets
    {
        public static string Clean(string target)
        {
            string s = (target ?? "").Trim();
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2);
            return Environment.ExpandEnvironmentVariables(s);
        }
        public static bool IsWeb(string target)
        {
            Uri uri; return Uri.TryCreate(target, UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http") && !String.IsNullOrEmpty(uri.Host);
        }
        public static string Error(LaunchItem item)
        {
            if (item == null) return "项目不存在。";
            string s = Clean(item.Target);
            if (String.IsNullOrWhiteSpace(s)) return "请填写打开位置。";
            try
            {
                if (!IsWeb(s))
                {
                    if (!Path.IsPathRooted(s)) return "请使用完整文件路径，或 http / https 网站地址。";
                    if (!File.Exists(s) && !Directory.Exists(s)) return "位置不存在，文件可能已被移动或删除。";
                }
                if (!String.IsNullOrWhiteSpace(item.WorkingDirectory) && !Directory.Exists(Clean(item.WorkingDirectory))) return "工作目录不存在。";
                if (IsWeb(s) && !String.IsNullOrWhiteSpace(item.Arguments)) return "网站地址不支持启动参数。";
                return null;
            }
            catch (Exception e) { return "无法读取位置：" + e.Message; }
        }
        public static string GuessName(string target)
        {
            var s = Clean(target);
            if (IsWeb(s)) return new Uri(s).Host;
            try { return Path.GetFileNameWithoutExtension(s.TrimEnd(Path.DirectorySeparatorChar)); }
            catch { return "新应用"; }
        }
        public static string Key(LaunchItem i) { return Clean(i.Target).ToUpperInvariant() + "\n" + (i.Arguments ?? "") + "\n" + Clean(i.WorkingDirectory).ToUpperInvariant(); }
    }
    public sealed class LaunchOutcome
    {
        public LaunchItem Item { get; set; }
        public string Error { get; set; }
        public bool Success { get { return Error == null; } }
    }
    public sealed class Launcher
    {
        int busy;
        public bool IsBusy { get { return busy != 0; } }
        public async Task<List<LaunchOutcome>> RunAsync(IEnumerable<LaunchItem> source, int delay, Action<int, int, LaunchItem> progress, CancellationToken token)
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) throw new InvalidOperationException("启动正在进行，请等待完成。");
            var results = new List<LaunchOutcome>();
            try
            {
                var items = source.ToList();
                for (int n = 0; n < items.Count; n++)
                {
                    if (token.IsCancellationRequested) break;
                    if (progress != null) progress(n + 1, items.Count, items[n]);
                    results.Add(await OpenAsync(items[n]));
                    if (n < items.Count - 1 && !token.IsCancellationRequested)
                    {
                        try { await Task.Delay(delay, token); } catch (OperationCanceledException) { break; }
                    }
                }
                return results;
            }
            finally { Interlocked.Exchange(ref busy, 0); }
        }
        public static Task<LaunchOutcome> OpenAsync(LaunchItem item)
        {
            var task = new TaskCompletionSource<LaunchOutcome>();
            var thread = new Thread(delegate()
            {
                var result = new LaunchOutcome { Item = item, Error = Targets.Error(item) };
                if (result.Error == null)
                {
                    try
                    {
                        string path = Targets.Clean(item.Target);
                        var info = new ProcessStartInfo(path) { UseShellExecute = true, Arguments = item.Arguments ?? "" };
                        if (!String.IsNullOrWhiteSpace(item.WorkingDirectory)) info.WorkingDirectory = Targets.Clean(item.WorkingDirectory);
                        else if (File.Exists(path)) info.WorkingDirectory = Path.GetDirectoryName(path);
                        using (var process = Process.Start(info)) { }
                    }
                    catch (Exception e) { result.Error = e.Message; }
                }
                task.SetResult(result);
            });
            thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA); thread.Start();
            return task.Task;
        }
    }
}
