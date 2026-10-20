using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal static class BrowserChatSetup
{
    public static string Install()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultiAgentHub", "browser-bridge");
        string extension = Path.Combine(root, "extension");
        foreach (string folder in new[] { root, extension }) {
            if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("Thư mục bridge không được là liên kết.");
            Directory.CreateDirectory(folder);
        }
        var assembly = Assembly.GetExecutingAssembly();
        foreach (string resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("BrowserExtension.", StringComparison.Ordinal))) {
            string name = resource["BrowserExtension.".Length..];
            if (Path.GetFileName(name) != name) throw new InvalidDataException("Tên tài nguyên extension không hợp lệ.");
            string target = Path.Combine(extension, name);
            if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("File extension không được là liên kết.");
            using var input = assembly.GetManifestResourceStream(resource)!; using var output = File.Create(target); input.CopyTo(output);
        }
        if (!File.Exists(Path.Combine(extension, "manifest.json"))) throw new InvalidDataException("Bản build chưa kèm extension trình duyệt.");
        string hostFile = Path.Combine(root, "native-host.json");
        if (File.Exists(hostFile) && (File.GetAttributes(hostFile) & FileAttributes.ReparsePoint) != 0) throw new IOException("Native host manifest không được là liên kết.");
        File.WriteAllText(hostFile, JsonSerializer.Serialize(new { name = BrowserChatProtocol.HostName,
            description = "Multi-Agent Hub browser chatbot bridge", path = Application.ExecutablePath, type = "stdio",
            allowed_origins = new[] { "chrome-extension://" + BrowserChatProtocol.ExtensionId + "/" } }, Json.Options));
        foreach (string browser in new[] { @"Software\Google\Chrome", @"Software\Microsoft\Edge" }) {
            using var key = Registry.CurrentUser.CreateSubKey(browser + @"\NativeMessagingHosts\" + BrowserChatProtocol.HostName);
            key.SetValue("", hostFile, RegistryValueKind.String);
        }
        return extension;
    }
    public static void Explore(string path) => Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = false, ArgumentList = { path } })?.Dispose();
}

internal sealed class BrowserChatDialog : Form
{
    private readonly BrowserChatBridge bridge;
    private readonly ListView tabs = new() { Dock = DockStyle.Fill, CheckBoxes = true, View = View.Details, FullRowSelect = true, HideSelection = false, AccessibleName = "Các tab chatbot bạn chủ động kết nối" };
    private readonly Label status = Ui.Label("");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 750 };
    private string stamp = "";
    public List<BrowserChatBinding> SelectedBindings { get; private set; } = [];
    public BrowserChatDialog(BrowserChatBridge bridge)
    {
        this.bridge = bridge; Text = "Bàn luận giữa các chatbot • bridge trình duyệt thử nghiệm"; Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent; Icon = Ui.AppIcon; ShowInTaskbar = false;
        var root = Ui.Table(); root.Padding = new Padding(18); root.RowCount = 5;
        for (int i = 0; i < 3; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
        var help = Ui.Label("1. Chuẩn bị bridge; mở chrome://extensions hoặc edge://extensions → Developer mode → Load unpacked, chọn thư mục vừa mở.\n2. Mở chat mới trống trên website chatbot bạn dùng, đăng nhập và chọn model. Bấm extension tại tab → Kết nối. Nhãn để trống để tự nhận diện; website khác Gemini/ChatGPT dùng adapter chung thử nghiệm, có thể cần CSS.\n3. Hub chỉ liệt kê tab bạn đã liên kết, không tạo sẵn agent/model. Model lấy từ giao diện/phản hồi nếu website công khai; không cần project trong Hub Chat. Giữ tab mở, không dùng API key.");
        help.MaximumSize = new Size(820, 0); root.Controls.Add(help, 0, 0);
        var actions = Ui.Flow(); actions.Controls.Add(Ui.Button("Chuẩn bị bridge & mở thư mục…", (_, _) => {
            try { string folder = BrowserChatSetup.Install(); BrowserChatSetup.Explore(folder); status.Text = "Đã đăng ký native host cho tài khoản Windows này. Load unpacked thư mục extension vừa mở; không có cài extension tự động."; }
            catch (Exception error) { status.Text = error.Message; }
        })); actions.Controls.Add(Ui.Button("Dò lại tab", (_, _) => { stamp = ""; RefreshBindings(); })); root.Controls.Add(actions, 0, 1);
        status.MaximumSize = new Size(820, 0); root.Controls.Add(status, 0, 2);
        tabs.Columns.Add("Website", 155); tabs.Columns.Add("Model nhận diện được", 300); tabs.Columns.Add("Tab / trạng thái", 305); root.Controls.Add(tabs, 0, 3);
        var footer = Ui.Flow(); footer.Controls.Add(Ui.Button("Dùng các chatbot đã chọn", (_, _) => {
            var selected = tabs.CheckedItems.Cast<ListViewItem>().Select(i => (BrowserChatBinding)i.Tag!).ToList();
            if (selected.Count == 0 || selected.Any(b => !b.Ready)) { status.Text = "Chọn tab chatbot đã sẵn sàng: một để tự nghiên cứu, hai hoặc nhiều hơn để trao đổi qua lại."; return; }
            SelectedBindings = selected; DialogResult = DialogResult.OK;
        }, true)); footer.Controls.Add(Ui.Button("Đóng", (_, _) => DialogResult = DialogResult.Cancel)); root.Controls.Add(footer, 0, 4);
        timer.Tick += (_, _) => RefreshBindings(); Shown += (_, _) => { RefreshBindings(); timer.Start(); }; FormClosed += (_, _) => { timer.Stop(); timer.Dispose(); };
        float scale = DeviceDpi / 96f; root.Scale(new SizeF(scale, scale)); Ui.Theme(this); AutoScaleMode = AutoScaleMode.Dpi;
        var area = Screen.FromControl(this).WorkingArea; Size = new Size(Math.Min((int)(900 * scale), area.Width - 40), Math.Min((int)(700 * scale), area.Height - 40));
        MinimumSize = new Size((int)(680 * scale), (int)(500 * scale));
    }
    private void RefreshBindings()
    {
        var bindings = bridge.Bindings; string next = JsonSerializer.Serialize(bindings, Json.Options) + bridge.LastError;
        if (stamp == next) return; stamp = next;
        var selected = tabs.CheckedItems.Cast<ListViewItem>().Select(i => ((BrowserChatBinding)i.Tag!).Id).ToHashSet();
        tabs.BeginUpdate(); tabs.Items.Clear();
        foreach (var binding in bindings) {
            var item = new ListViewItem(binding.DisplaySite) { Tag = binding, Checked = selected.Contains(binding.Id), ForeColor = binding.Ready ? Ui.Ink : Ui.Muted };
            item.SubItems.Add(binding.DisplayModel); item.SubItems.Add(binding.Title + (binding.Ready ? "" : " • cần xử lý trên web")); tabs.Items.Add(item);
        }
        tabs.EndUpdate(); status.Text = bindings.Count == 0 ? "Chưa có tab kết nối. " + bridge.LastError : $"{bindings.Count} tab đã kết nối. Model lấy từ phản hồi/giao diện khi website công khai; không xác minh backend. Chọn model tại website.";
    }
}

internal sealed partial class MainWindow
{
    private void ConfigureBrowserChatbots() => Protect(() => {
        if (runner.Busy) return;
        using var dialog = new BrowserChatDialog(browserChat); if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var next = CopyConfig(); next.ChatAgentIds = [];
        foreach (var binding in dialog.SelectedBindings) {
            var agent = next.Agents.FirstOrDefault(a => Catalog.IsBrowserChat(a.Connection) && a.BrowserBindingId == binding.Id);
            if (agent == null) {
                if (next.Agents.Count >= Catalog.MaxAgents) throw new ArgumentException($"Tối đa {Catalog.MaxAgents} agent; xóa kết nối cũ trước khi thêm.");
                agent = new() { Provider = "browser", Connection = "browser-web",
                    Name = (binding.DisplaySite + " chatbot")[..Math.Min(80, binding.DisplaySite.Length + 8)], Permission = "default", Enabled = false }; next.Agents.Add(agent);
            }
            agent.BrowserBindingId = binding.Id; agent.Model = binding.Model; next.ChatAgentIds.Add(agent.Id);
        }
        if (config.Surface == "work") next.WorkMode = config.Mode;
        next.Surface = "chat"; next.Mode = next.ChatMode = "discussion";
        next.Discussion = new() { MaxTurns = 0, DurationMinutes = 0, TurnTimeoutMinutes = 0, PauseSeconds = 0 };
        Commit(next); updating = true;
        try { surface.SelectedItem = surface.Items.Cast<Choice>().First(c => c.Id == "chat"); ConfigureModes("discussion"); }
        finally { updating = false; }
        NewConversation(); status.Text = "Đã chọn chatbot cho Trò chuyện, không cần project. Để hỗ trợ công việc qua văn bản, chuyển sang Làm việc và chủ động chọn chatbot trong danh sách agent.";
    });
}
