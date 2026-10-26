using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal static class IdeLauncher
{
    private static Assembly Assembly => typeof(IdeLauncher).Assembly;
    public static bool HasEmbeddedEditor => Assembly.GetManifestResourceInfo("HubIde.Archive") != null;
    public static async Task<string> Prepare(Storage profile, Action<string> progress, CancellationToken token)
    {
        using var metadata = Assembly.GetManifestResourceStream("HubIde.Manifest") ?? throw new InvalidOperationException("Bản build này chưa chứa Hub Code IDE. Dùng EXE standalone từ trang Release.");
        using var reader = new StreamReader(metadata);
        var manifest = JsonSerializer.Deserialize<HubIdeManifest>(await reader.ReadToEndAsync(token), Json.Options) ?? throw new InvalidDataException("Thiếu manifest Hub IDE.");
        string ownVersion = Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        if (ownVersion != manifest.Version + "+" + manifest.SourceCommit) throw new InvalidDataException("Editor không cùng phiên bản/source với Hub.");
        string baseDir = Path.Combine(profile.DataDir, "hub-code");
        string editor = Path.Combine(baseDir, manifest.Version + "-" + manifest.SourceCommit[..12]);
        HubIdeDistribution.PlainPath(baseDir); Directory.CreateDirectory(baseDir);
        using var ownership = new FileStream(Path.Combine(baseDir, "install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (!Directory.Exists(editor)) {
            progress("Đang cài editor và Agent của Hub từ bộ EXE…");
            string stage = Path.Combine(baseDir, "extract-" + Guid.NewGuid().ToString("N"));
            try {
                using var archive = Assembly.GetManifestResourceStream("HubIde.Archive") ?? throw new InvalidDataException("Thiếu bundle Hub.");
                await Task.Run(() => HubIdeDistribution.Extract(archive, stage, manifest, token), token);
                Directory.Move(stage, editor);
            } finally { if (Directory.Exists(stage)) { HubIdeDistribution.PlainPath(stage); Directory.Delete(stage, true); } }
        }
        await Task.Run(() => HubIdeDistribution.ValidateInstalled(editor, manifest), token);
        string userData = Path.Combine(baseDir, "user-data"), settings = Path.Combine(userData, "User", "settings.json"); HubIdeDistribution.PlainPath(settings);
        if (!File.Exists(settings)) {
            Directory.CreateDirectory(Path.GetDirectoryName(settings)!);
            File.WriteAllText(settings, JsonSerializer.Serialize(new Dictionary<string, object> {
                ["workbench.colorTheme"] = "Default Dark Modern", ["workbench.startupEditor"] = "none", ["window.title"] = "${rootName}${separator}Hub Code",
                ["telemetry.telemetryLevel"] = "off", ["editor.minimap.enabled"] = false, ["workbench.editor.enablePreview"] = false,
                ["chat.disableAIFeatures"] = true, ["update.mode"] = "none"
            }));
        }
        return editor;
    }
    public static void Launch(string editor, Storage profile, string project)
    {
        if (!Directory.Exists(project)) throw new DirectoryNotFoundException("Chọn project trước khi mở Hub Code.");
        string root = Path.GetDirectoryName(editor)!;
        var start = new ProcessStartInfo(Path.Combine(editor, HubIdeDistribution.ExecutableName)) { UseShellExecute = false, WorkingDirectory = project, WindowStyle = ProcessWindowStyle.Normal };
        foreach (string arg in new[] { "--new-window", "--user-data-dir", Path.Combine(root, "user-data"), project }) start.ArgumentList.Add(arg);
        start.Environment["MULTI_AGENT_HUB_EXE"] = Environment.ProcessPath!;
        start.Environment["MULTI_AGENT_HUB_PROFILE"] = profile.DataDir;
        start.Environment["MULTI_AGENT_HUB_IDE"] = "1";
        Process.Start(start)?.Dispose();
    }
}

internal sealed class IdeLaunchWindow : Form
{
    public bool Launched { get; private set; }
    private readonly Label status = Ui.Label("Chuẩn bị Hub Code…", true);
    private readonly CancellationTokenSource closing = new();
    public IdeLaunchWindow(Storage profile, string project)
    {
        Text = "Hub Code"; Icon = Ui.AppIcon;
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
