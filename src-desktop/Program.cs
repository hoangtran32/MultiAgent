namespace MultiAgentHubApp;
internal static class Program
{
    [STAThread] private static void Main(string[] args)
    {
        if (BrowserNativeHost.IsInvocation(args)) { Environment.ExitCode = BrowserNativeHost.Run(args).GetAwaiter().GetResult(); return; }
        ApplicationConfiguration.Initialize();
        string? dataDir = args.Length == 2 && args[0] == "--data-dir" ? Path.GetFullPath(args[1]) : null;
        string instance = dataDir == null ? "Default" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dataDir.ToUpperInvariant())))[..16];
        using var mutex = new Mutex(true, @"Local\MultiAgentHub.Desktop.Native3." + instance, out bool first);
        if (!first) { MessageBox.Show("Multi-Agent Hub native đang mở."); return; }
        try { Application.Run(new MainWindow(dataDir)); }
        catch (Exception e) { MessageBox.Show(e.Message, "Không thể mở Multi-Agent Hub", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
