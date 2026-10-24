using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal static class IdeLauncher
{
    public static async Task<string> Prepare(Storage profile, Action<string> progress, CancellationToken token)
    {
        string baseDir = Path.Combine(profile.DataDir, "codeoss"), editor = Path.Combine(baseDir, CodeOssDistribution.Version);
        CodeOssDistribution.PlainPath(baseDir); Directory.CreateDirectory(baseDir);
        using var ownership = new FileStream(Path.Combine(baseDir, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        string archive = Path.Combine(baseDir, "VSCodium-" + CodeOssDistribution.Version + ".zip");
        string marker = Path.Combine(editor, ".hub-sha256");
        if (!File.Exists(marker) || File.ReadAllText(marker).Trim() != CodeOssDistribution.Sha256 || !File.Exists(Path.Combine(editor, "VSCodium.exe"))) {
            if (Directory.Exists(editor)) throw new IOException("Bản Code OSS cài dở hoặc đã thay đổi. Đổi tên thư mục " + editor + " rồi thử lại.");
            if (!File.Exists(archive)) {
                string partial = archive + "." + Guid.NewGuid().ToString("N") + ".download";
                try {
                    using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("MultiAgentHub/6.0.0");
                    using var response = await client.GetAsync(CodeOssDistribution.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token); response.EnsureSuccessStatusCode();
                    await using var input = await response.Content.ReadAsStreamAsync(token);
                    await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                        byte[] buffer = new byte[256 * 1024]; long size = 0; int read; long last = 0;
                        while ((read = await input.ReadAsync(buffer, token)) > 0) {
                            size += read; if (size > CodeOssDistribution.ArchiveBytes) throw new InvalidDataException("Bản tải Code OSS vượt kích thước đã xác minh.");
                            await output.WriteAsync(buffer.AsMemory(0, read), token);
                            if (size - last > 2_000_000) { last = size; progress($"Đang tải nền Code OSS: {size / 1_000_000}/{CodeOssDistribution.ArchiveBytes / 1_000_000} MB…"); }
                        }
                    }
                    await Task.Run(() => CodeOssDistribution.Verify(partial), token); File.Move(partial, archive);
                } finally { if (File.Exists(partial)) File.Delete(partial); }
            }
            progress("Đang xác minh và cài nền Code OSS…");
            await Task.Run(() => CodeOssDistribution.Verify(archive), token);
            string stage = Path.Combine(baseDir, "extract-" + Guid.NewGuid().ToString("N"));
            try { await Task.Run(() => CodeOssDistribution.Extract(archive, stage, token), token); Directory.Move(stage, editor); }
            finally { if (Directory.Exists(stage)) { CodeOssDistribution.PlainPath(stage); Directory.Delete(stage, true); } }
        }
        CodeOssDistribution.PlainPath(editor);
        string extensions = Path.Combine(baseDir, "extensions"), extension = Path.Combine(extensions, "multiagent-hub.code-6.0.0");
        CodeOssDistribution.PlainPath(extension); Directory.CreateDirectory(extension);
        var assembly = Assembly.GetExecutingAssembly();
        foreach (string resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("IdeExtension.", StringComparison.Ordinal))) {
            string name = resource["IdeExtension.".Length..];
            if (name.Contains('/') || name.Contains('\\')) throw new InvalidDataException("Extension resource name không hợp lệ.");
            string target = Path.Combine(extension, name); CodeOssDistribution.PlainPath(target);
            using var source = assembly.GetManifestResourceStream(resource)!; using var output = File.Create(target); source.CopyTo(output);
        }
        string userData = Path.Combine(baseDir, "user-data"), settings = Path.Combine(userData, "User", "settings.json"); CodeOssDistribution.PlainPath(settings);
        if (!File.Exists(settings)) {
            Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
            File.WriteAllText(settings, JsonSerializer.Serialize(new Dictionary<string, object> {
                ["workbench.colorTheme"] = "Default Dark Modern", ["workbench.startupEditor"] = "none", ["window.title"] = "${rootName}${separator}Hub Code",
                ["telemetry.telemetryLevel"] = "off", ["editor.minimap.enabled"] = false, ["workbench.editor.enablePreview"] = false,
                ["workbench.activityBar.location"] = "default", ["chat.disableAIFeatures"] = true
            }));
        }
        return editor;
    }
    public static void Launch(string editor, Storage profile, string project)
    {
        if (!Directory.Exists(project)) throw new DirectoryNotFoundException("Chọn project trước khi mở Hub Code.");
        string root = Path.GetDirectoryName(editor)!;
        var start = new ProcessStartInfo(Path.Combine(editor, "VSCodium.exe")) { UseShellExecute = false, WorkingDirectory = project, WindowStyle = ProcessWindowStyle.Normal };
        foreach (string arg in new[] { "--new-window", "--user-data-dir", Path.Combine(root, "user-data"), "--extensions-dir", Path.Combine(root, "extensions"), project }) start.ArgumentList.Add(arg);
        start.Environment["MULTI_AGENT_HUB_EXE"] = Environment.ProcessPath!;
        start.Environment["MULTI_AGENT_HUB_PROFILE"] = profile.DataDir;
        start.Environment["MULTI_AGENT_HUB_IDE"] = "1";
        Process.Start(start)?.Dispose();
    }
}

internal sealed class IdeLaunchWindow : Form
{
    public bool Launched { get; private set; }
    private readonly Storage profile;
    private readonly string project;
    private readonly Label status = Ui.Label("Chuẩn bị Hub Code…", true);
    private readonly CancellationTokenSource closing = new();
    public IdeLaunchWindow(Storage profile, string project)
    {
        this.profile = profile; this.project = project; Text = "Hub Code • Code OSS"; Icon = Ui.AppIcon;
        Size = new(650, 240); StartPosition = FormStartPosition.CenterScreen; Font = new("Segoe UI", 10);
        var layout = Ui.Table(); layout.Padding = new(22); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.AutoSize)); Controls.Add(layout);
        status.Dock = DockStyle.Fill; status.AutoSize = false; layout.Controls.Add(status, 0, 0);
        var buttons = Ui.Flow(); buttons.Controls.Add(Ui.Button("Đóng", (_, _) => Close())); layout.Controls.Add(buttons, 0, 1); Ui.Theme(this);
        Shown += async (_, _) => {
            try { if (!Directory.Exists(project)) throw new DirectoryNotFoundException("Project không tồn tại: " + project); string editor = await IdeLauncher.Prepare(profile, message => { if (!IsDisposed && IsHandleCreated) BeginInvoke(() => status.Text = message); }, closing.Token); if (!closing.IsCancellationRequested) { IdeLauncher.Launch(editor, profile, project); Launched = true; Close(); } }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!IsDisposed) status.Text = error.Message + "\nMở MultiAgentHub.exe --native để dùng Hub Chat/thiết lập."; }
        };
        FormClosing += (_, _) => closing.Cancel();
    }
}
