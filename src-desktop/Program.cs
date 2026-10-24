namespace MultiAgentHubApp;
internal static class Program
{
    [STAThread] private static void Main(string[] args)
    {
        if (BrowserNativeHost.IsInvocation(args)) { Environment.ExitCode = BrowserNativeHost.Run(args).GetAwaiter().GetResult(); return; }
        string? dataDir = null;
        int dataIndex = Array.IndexOf(args, "--data-dir");
        if (dataIndex >= 0 && dataIndex + 1 < args.Length) dataDir = Path.GetFullPath(args[dataIndex + 1]);
        if (args.FirstOrDefault() == "--ide-backend") {
            try { if (args.Length < 2) throw new ArgumentException("Thiếu project."); Environment.ExitCode = IdeServer.Run(args[1], dataDir).GetAwaiter().GetResult(); }
            catch (Exception error) { Console.Error.WriteLine(error.Message); Environment.ExitCode = 1; }
            return;
        }
        ApplicationConfiguration.Initialize();
        if (!args.Contains("--native") && !args.Contains("--settings")) {
            try {
                var profile = new MultiAgentHub.Core.Storage(dataDir);
                bool firstProfile = !File.Exists(Path.Combine(profile.DataDir, "native-config.json"));
                var config = profile.LoadConfig();
                if (args.Contains("--code") || config.Surface == "work") {
                    int index = Array.IndexOf(args, "--code");
                    string project = index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal) ? Path.GetFullPath(args[index + 1]) : config.Project;
                    if (firstProfile) { config.Project = index >= 0 ? project : ""; profile.Write("native-config", config); }
                    if (index < 0 && (firstProfile || !Directory.Exists(project))) {
                        using var choose = new FolderBrowserDialog { Description = "Chọn thư mục project để mở Hub Code", UseDescriptionForTitle = true };
                        if (choose.ShowDialog() != DialogResult.OK) return;
                        project = choose.SelectedPath; config.Project = project; profile.Write("native-config", config);
                    }
                    Application.Run(new IdeLaunchWindow(profile, project)); return;
                }
            } catch (Exception error) { MessageBox.Show(error.Message, "Chưa mở được Hub Code", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        }
        string instance = dataDir == null ? "Default" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dataDir.ToUpperInvariant())))[..16];
        using var mutex = new Mutex(true, @"Local\MultiAgentHub.Desktop.Native3." + instance, out bool first);
        if (!first) {
            if (args.Contains("--settings") && NativeSettingsSignal.Request(instance).GetAwaiter().GetResult()) return;
            MessageBox.Show("Multi-Agent Hub native đang mở."); return;
        }
        try {
            var window = new MainWindow(dataDir);
            using var signal = new NativeSettingsSignal(instance, () => { if (!window.IsDisposed && window.IsHandleCreated) window.BeginInvoke(window.ShowAgentSettings); });
            if (args.Contains("--settings")) window.Shown += (_, _) => window.ShowAgentSettings(); Application.Run(window);
        }
        catch (Exception e) { MessageBox.Show(e.Message, "Không thể mở Multi-Agent Hub", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
