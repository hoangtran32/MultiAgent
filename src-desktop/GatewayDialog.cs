using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal sealed class GatewayDialog : Form
{
    private readonly WebGateway gateway;
    private readonly BrowserChatBridge bridge;
    private readonly ComboBox binding = Ui.Combo(560), account = Ui.Combo(350), model = Ui.Combo(430);
    private readonly TextBox alias = Ui.Text();
    private readonly Label status = Ui.Label("");
    private readonly NumericUpDown port = new() { Minimum = 1024, Maximum = 65535, Width = 95 };
    private readonly Button toggle = Ui.Button("Mở gateway cục bộ");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private string stamp = "";
    private bool updating;
    public List<GatewayModel> SelectedModels { get; } = [];
    public GatewayDialog(WebGateway gateway, BrowserChatBridge bridge)
    {
        this.gateway = gateway; this.bridge = bridge;
        Text = "Gateway & tài khoản web"; Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false; Icon = Ui.AppIcon; MinimumSize = new Size(740, 600); Size = new Size(900, 740);
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(20) }; Controls.Add(root);
        void Help(string text) { var label = Ui.Label(text); label.MaximumSize = new Size(800, 0); root.Controls.Add(label); }
        Help("Dùng đúng tài khoản, model và quota website. Hub quản lý liên kết tài khoản và endpoint; đăng nhập/xác minh vẫn thực hiện trên trình duyệt. Giữ tab mở. Adapter API web thử nghiệm, chưa hỗ trợ tools, tệp hoặc mọi giao thức website.");
        var website = Ui.Text(); website.Width = 340; website.PlaceholderText = "https://website-chatbot"; root.Controls.Add(website);
        var setup = Ui.Flow(); setup.Controls.Add(Ui.Button("Cài/cập nhật extension & mở thư mục", (_, _) => Protect(() => BrowserChatSetup.Explore(BrowserChatSetup.Install()))));
        setup.Controls.Add(Ui.Button("Mở website đăng nhập…", (_, _) => {
            Protect(() => {
                if (!Uri.TryCreate(website.Text.Trim(), UriKind.Absolute, out var url) || url.Scheme != "https" || url.UserInfo.Length > 0 || !url.IsDefaultPort) throw new ArgumentException("Cần website HTTPS hợp lệ.");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            });
        })); root.Controls.Add(setup);
        Help("1. Tải lại extension và tab sau khi cập nhật. Mở chat web mới, chọn model, bấm extension → Kết nối.\n2. Gửi một tin văn bản đầu tiên trên web. Extension nhận diện request API và model đã chọn, không chuyển credential sang Hub. Nếu chưa thấy model, đặt nhãn đúng trong extension và nhận diện lại từ chat mới.\n3. Chọn phiên bên dưới, đặt tên tài khoản rồi Liên kết. Chỉ model đã nhận diện mới được chọn cho gateway.");
        root.Controls.Add(binding); alias.Width = 560; alias.PlaceholderText = "Tên riêng để phân biệt tài khoản, ví dụ: Gemini cá nhân"; root.Controls.Add(alias);
        var linking = Ui.Flow(); linking.Controls.Add(Ui.Button("Liên kết / cập nhật tài khoản", (_, _) => Protect(() => {
            var selected = bridge.Bindings.SingleOrDefault(b => b.Id == Ui.Value(binding)) ?? throw new ArgumentException("Chọn phiên từ extension.");
            if (string.IsNullOrWhiteSpace(alias.Text)) throw new ArgumentException("Nhập tên để phân biệt tài khoản.");
            var existing = gateway.Accounts.List.FirstOrDefault(a => a.Id == Ui.Value(account)) ?? new GatewayAccount();
            existing.Name = alias.Text.Trim(); existing.BindingId = selected.Id; existing.Origin = new Uri(selected.Url).GetLeftPart(UriPartial.Authority);
            gateway.Accounts.Save(existing); stamp = ""; RefreshState(existing.Id);
        })));
        linking.Controls.Add(Ui.Button("Thêm tài khoản khác", (_, _) => { account.SelectedIndex = 0; alias.Clear(); })); root.Controls.Add(linking);
        Help("Tài khoản đã liên kết / model API nhận diện:"); root.Controls.Add(account); root.Controls.Add(model);
        var actions = Ui.Flow(); actions.Controls.Add(Ui.Button("Dùng model trong Hub", (_, _) => Protect(() => {
            var selected = gateway.Models.SingleOrDefault(m => m.Id == Ui.Value(model)) ?? throw new ArgumentException("Model API chưa nhận diện hoặc phiên đã ngắt. Kiểm tra tab web.");
            SelectedModels.Add(selected); DialogResult = DialogResult.OK;
        }), true)); actions.Controls.Add(Ui.Button("Gỡ liên kết tài khoản", (_, _) => Protect(() => { gateway.Accounts.Remove(Ui.Value(account)); stamp = ""; RefreshState(); }))); root.Controls.Add(actions);
        Help("API cục bộ (tùy chọn): GET /v1/models · POST /v1/chat/completions. Hỗ trợ text, stream SSE; ngữ cảnh messages được ghép thành hội thoại văn bản. Không phải API chính thức của website. Không tự mở khi khởi động Hub.");
        port.Value = gateway.Accounts.Port;
        var server = Ui.Flow(); server.Controls.Add(port); server.Controls.Add(toggle);
        server.Controls.Add(Ui.Button("Chép endpoint", (_, _) => Protect(() => Clipboard.SetText(gateway.Endpoint))));
        server.Controls.Add(Ui.Button("Chép API key cục bộ", (_, _) => Protect(() => Clipboard.SetText(gateway.Accounts.LocalKey())))); root.Controls.Add(server);
        toggle.Click += async (_, _) => {
            toggle.Enabled = false;
            try { if (gateway.Running) await gateway.Stop(); else { gateway.Accounts.SetPort((int)port.Value); await gateway.Start(); } status.Text = gateway.Running ? "Đang mở: " + gateway.Endpoint : "Đã đóng endpoint cục bộ; Hub vẫn dùng phiên web trực tiếp."; }
            catch (Exception e) { status.Text = "Không mở được gateway: " + e.Message; }
            finally { toggle.Enabled = true; RefreshServer(); }
        };
        status.MaximumSize = new Size(800, 0); root.Controls.Add(status);
        account.SelectedIndexChanged += (_, _) => { if (!updating) { RefreshModels(); if (gateway.Accounts.List.FirstOrDefault(a => a.Id == Ui.Value(account)) is { } selected) alias.Text = selected.Name; } };
        timer.Tick += (_, _) => RefreshState(); Shown += (_, _) => { RefreshState(); timer.Start(); };
        FormClosed += (_, _) => { timer.Stop(); timer.Dispose(); };
        Ui.Theme(this); AutoScaleMode = AutoScaleMode.Dpi;
    }
    private void Protect(Action action) { try { action(); } catch (Exception e) { status.Text = e.Message; } }
    private void RefreshServer() { toggle.Text = gateway.Running ? "Đóng gateway cục bộ" : "Mở gateway cục bộ"; port.Enabled = !gateway.Running; }
    private void RefreshState(string? selected = null)
    {
        RefreshServer();
        string next = System.Text.Json.JsonSerializer.Serialize(new { Tabs = bridge.Bindings, Accounts = gateway.Accounts.List });
        if (stamp == next) return; stamp = next;
        updating = true;
        try {
            string previousBinding = Ui.Value(binding), previousAccount = selected ?? Ui.Value(account);
            Ui.Choices(binding, new[] { new Choice("", "Chọn phiên đã liên kết từ extension…") }.Concat(bridge.Bindings.Select(b => new Choice(b.Id, b.DisplaySite + " · " + b.Title + " · " + b.ApiModels.Count + " model API"))), previousBinding);
            Ui.Choices(account, new[] { new Choice("", "Thêm tài khoản / chọn tài khoản cần cập nhật…") }.Concat(gateway.Accounts.List.Select(a => new Choice(a.Id, a.Name))), previousAccount); RefreshModels();
        } finally { updating = false; }
    }
    private void RefreshModels()
    {
        string previous = Ui.Value(model);
        Ui.Choices(model, gateway.Models.Where(m => m.AccountId == Ui.Value(account)).Select(m => new Choice(m.Id, m.Name + " · " + m.Protocol)), previous);
    }
}

internal sealed partial class MainWindow
{
    private void ConfigureGateway() => Protect(() => {
        if (runner.Busy) return;
        using var dialog = new GatewayDialog(gateway, browserChat);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var next = CopyConfig();
        foreach (var selected in dialog.SelectedModels) {
            var agent = next.Agents.FirstOrDefault(a => a.Connection == "gateway-web" && a.GatewayAccountId == selected.AccountId && a.Model == selected.WebModelId);
            if (agent == null) {
                if (next.Agents.Count >= Catalog.MaxAgents) throw new ArgumentException("Đã đạt giới hạn agent.");
                string name = selected.AccountName + " · " + selected.Name;
                agent = new() { Provider = "gateway", Connection = "gateway-web", Name = name[..Math.Min(80, name.Length)], Model = selected.WebModelId, GatewayAccountId = selected.AccountId, Permission = "default", Enabled = false };
                next.Agents.Add(agent);
            }
            next.SelectAgent(agent, true);
        }
        Commit(next); status.Text = "Đã thêm model từ phiên web. Chọn thêm tài khoản/model để các chatbot cùng trò chuyện; Hub Chat không cần project.";
    });
}
