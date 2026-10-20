using System.Text.Json;
using MultiAgentHub.Core;
namespace MultiAgentHubApp;

internal sealed class AgentEditor : Form
{
    private readonly AgentConfig original;
    private readonly ISecretStore secrets;
    private readonly BrowserChatBridge? browser;
    private readonly ComboBox browserBinding = Ui.Combo();
    private readonly Button refreshBrowser = Ui.Button("Dò lại tab đã kết nối");
    private readonly TextBox name = Ui.Text(), instructions = Ui.Text(true), command = Ui.Text(), arguments = Ui.Text(true), endpoint = Ui.Text(), variable = Ui.Text(), key = Ui.Text();
    private readonly ComboBox provider = Ui.Combo(), connection = Ui.Combo(), model = Ui.Combo(), effort = Ui.Combo(), permission = Ui.Combo();
    private readonly Label help = Ui.Label(""), status = Ui.Label(""), credentialStatus = Ui.Label("");
    private readonly CheckBox deleteKey = new() { Text = "Xóa key đã lưu", AutoSize = true }, showKey = new() { Text = "Hiện key vừa nhập", AutoSize = true };
    private readonly Button loadModels = Ui.Button("Tải model"), check = Ui.Button("Kiểm tra kết nối"), save = Ui.Button("Lưu agent", primary: true);
    private readonly TableLayoutPanel fields = Fields(), connectionFields = Fields();
    private readonly Dictionary<Control, (TableLayoutPanel Table, int Row, int Height)> rows = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, DraftValues> drafts = [];
    private CancellationTokenSource? inspection;
    private int inspectionVersion;
    private string activeKey = "";
    private bool initializing = true, loading;
    public AgentConfig Result { get; private set; }

    public AgentEditor(AgentConfig agent, ISecretStore secrets, ModelInventory? discovered = null, BrowserChatBridge? browser = null)
    {
        SuspendLayout(); AutoScaleMode = AutoScaleMode.None;
        original = agent.Copy(); Result = agent.Copy(); this.secrets = secrets; this.browser = browser;
        Text = "Thiết lập agent"; Font = new Font("Segoe UI", 10); BackColor = Ui.Background; ForeColor = Ui.Ink;
        var area = Screen.PrimaryScreen!.WorkingArea;
        Size = new Size(Math.Min(800, area.Width - 40), Math.Min(840, area.Height - 40)); MinimumSize = new Size(660, 600);
        StartPosition = FormStartPosition.CenterParent; MinimizeBox = MaximizeBox = false;
        if (Ui.AppIcon != null) Icon = Ui.AppIcon;
        var outer = Ui.Table(); outer.Padding = new Padding(16); outer.RowCount = 3;
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 76)); outer.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(outer);
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(16, 8) }; outer.Controls.Add(tabs, 0, 0);
        AddTab("Agent & model", fields); AddTab("Cấu hình kết nối", connectionFields);
        void AddTab(string title, TableLayoutPanel table)
        {
            var tab = new TabPage(title) { BackColor = Color.White, Padding = new Padding(12) };
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.Controls.Add(table); tab.Controls.Add(scroll); tabs.TabPages.Add(tab);
        }
        Field(fields, "Tên hiển thị", name); Field(fields, "Công cụ AI", provider); Field(fields, "Cách kết nối", connection);
        help.AutoSize = false; Field(fields, "Cách hoạt động", help, 86);
        model.DropDownStyle = ComboBoxStyle.DropDown; model.AccessibleName = "Model ID / alias";
        Field(fields, "Model ID / alias", model);
        Field(fields, "Tab chatbot", browserBinding); Field(fields, "Kết nối trình duyệt", refreshBrowser);
        var tools = Ui.Flow(); tools.Controls.AddRange([loadModels, check]); Field(fields, "Kiểm tra", tools, 46);
        Field(fields, "Mức suy luận", effort); Field(fields, "Quyền CLI", permission);
        instructions.AcceptsReturn = true; Field(fields, "Vai trò / hướng dẫn", instructions, 140);
        Field(connectionFields, "Executable (.exe)", command); Field(connectionFields, "Chọn đường dẫn", Ui.Button("Chọn executable…", (_, _) => Browse()));
        Field(connectionFields, "Đối số CLI (JSON)", arguments, 100);
        Field(connectionFields, "API base URL", endpoint); Field(connectionFields, "Biến môi trường key", variable);
        key.UseSystemPasswordChar = true; Field(connectionFields, "API key mới", key); Field(connectionFields, "", showKey);
        credentialStatus.AutoSize = false; Field(connectionFields, "Key đã lưu", credentialStatus, 68); Field(connectionFields, "", deleteKey);
        var connectionHelp = Ui.Label("Executable: để trống để Hub tự tìm công cụ đã cài.\n\nCLI tùy chỉnh: đối số là mảng JSON, ví dụ [\"agent.py\", \"--model\", \"{model}\"]. Prompt qua stdin, câu trả lời từ stdout.\n\nAPI: dùng base URL (ví dụ kết thúc bằng /v1/), không đặt key vào URL. Key mới được lưu khi bạn nhấn Lưu agent.");
        connectionHelp.AutoSize = false; Field(connectionFields, "Ghi chú", connectionHelp, 150);
        status.AutoSize = false; status.Dock = DockStyle.Fill; status.Margin = new Padding(4, 8, 4, 4); outer.Controls.Add(status, 0, 1);
        var footer = Ui.Flow(); footer.FlowDirection = FlowDirection.RightToLeft; outer.Controls.Add(footer, 0, 2);
        var cancel = Ui.Button("Hủy", (_, _) => DialogResult = DialogResult.Cancel); footer.Controls.AddRange([save, cancel]); CancelButton = cancel;
        name.Text = agent.Name; instructions.Text = agent.Instructions;
        Ui.Choices(provider, Catalog.Providers, agent.Provider); Ui.Choices(connection, Catalog.Connections(agent.Provider), agent.Connection);
        drafts[agent.Provider + "/" + agent.Connection] = new(agent.Model, agent.Reasoning, agent.Permission, agent.Command, JsonSerializer.Serialize(agent.Arguments), agent.Endpoint, agent.KeyEnvironment, "", false);
        RefreshConnection(); initializing = false;
        provider.SelectedIndexChanged += (_, _) => {
            if (initializing) return; StoreDraft(); initializing = true;
            string selectedProvider = Ui.Value(provider);
            var choices = Catalog.Connections(selectedProvider);
            string detected = choices.FirstOrDefault(c => c.Id is "codex-cli" or "claude-cli" or "agy-cli" && AgentDiscovery.FindExecutable(new() { Provider = selectedProvider, Connection = c.Id }) != null)?.Id ?? "";
            Ui.Choices(connection, new[] { new Choice("", "Chọn cách kết nối…") }.Concat(choices), detected); initializing = false; RefreshConnection();
        };
        connection.SelectedIndexChanged += (_, _) => { if (!initializing && connection.SelectedIndex >= 0) { StoreDraft(); RefreshConnection(); } };
        model.SelectedIndexChanged += (_, _) => RefreshEffort(); model.TextChanged += (_, _) => RefreshEffort();
        browserBinding.SelectedIndexChanged += (_, _) => { if (Catalog.IsBrowserChat(Ui.Value(connection)) && browser?.Bindings.FirstOrDefault(b => b.Id == Ui.Value(browserBinding)) is { } selected) model.Text = selected.Model; };
        refreshBrowser.Click += (_, _) => RefreshBrowserBindings();
        showKey.CheckedChanged += (_, _) => key.UseSystemPasswordChar = !showKey.Checked;
        loadModels.Click += async (_, _) => await Inspect(true); check.Click += async (_, _) => await Inspect(false); save.Click += (_, _) => Save();
        command.Leave += async (_, _) => await AutoModels();
        endpoint.Leave += async (_, _) => await AutoModels(); key.Leave += async (_, _) => await AutoModels(); variable.Leave += async (_, _) => await AutoModels();
        Shown += async (_, _) => { if (discovered != null) ApplyModels(discovered); else await AutoModels(); };
        FormClosed += (_, _) => { CancelInspection(); lifetime.Cancel(); lifetime.Dispose(); };
        float scale = DeviceDpi / 96f;
        outer.Scale(new SizeF(scale, scale)); MinimumSize = new Size((int)(660 * scale), (int)(600 * scale));
        Ui.Theme(this); AutoScaleDimensions = new SizeF(DeviceDpi, DeviceDpi); AutoScaleMode = AutoScaleMode.Dpi; ResumeLayout(true);
        Size = new Size(Math.Min((int)(800 * scale), area.Width - 40), Math.Min((int)(840 * scale), area.Height - 40));
    }
    private static TableLayoutPanel Fields()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 0, 14, 0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return table;
    }
    private void Field(TableLayoutPanel table, string label, Control control, int height = 44)
    {
        int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        table.Controls.Add(Ui.Label(label), 0, row); control.Dock = DockStyle.Fill; if (label.Length > 0) control.AccessibleName = label;
        table.Controls.Add(control, 1, row); rows[control] = (table, row, height);
    }
    private void ShowRow(Control control, bool visible)
    {
        var row = rows[control]; row.Table.RowStyles[row.Row].Height = visible ? row.Height * (initializing ? 1 : DeviceDpi / 96f) : 0;
        foreach (Control child in row.Table.Controls) if (row.Table.GetRow(child) == row.Row) child.Visible = visible;
        control.Enabled = visible;
    }
    private void StoreDraft()
    {
        if (activeKey.Length > 0) drafts[activeKey] = new(model.Text, Ui.Value(effort), Ui.Value(permission), command.Text, arguments.Text, endpoint.Text, variable.Text, key.Text, deleteKey.Checked);
    }
    private void RefreshConnection()
    {
        CancelInspection();
        string kind = Ui.Value(connection); activeKey = Ui.Value(provider) + "/" + kind;
        bool api = Catalog.IsApi(kind), manual = Catalog.IsManual(kind), web = Catalog.IsBrowserChat(kind), executable = kind.Length > 0 && !api && !web && kind != "manual";
        help.Text = kind switch {
            "" => "Chọn cách kết nối. CLI đã cài được ưu tiên phát hiện; IDE/app dùng luồng dán kết quả thủ công.",
            "codex-cli" => "Codex CLI chạy trên project và trả kết quả trực tiếp. Dùng tài khoản đã đăng nhập. Để model trống để dùng cấu hình CLI.",
            "claude-cli" => "Claude Code chạy trực tiếp. Nhập alias sonnet/opus/haiku hoặc model ID. Plan để phân tích, acceptEdits để sửa file theo quyền CLI.",
            "agy-cli" => "Antigravity CLI (agy) dùng tài khoản đã đăng nhập. Chạy ngầm không hiện hộp xin phép: default/Plan/Accept edits có thể chặn terminal. Đọc file bằng công cụ tích hợp; lệnh cần permissions.allow hoặc Toàn quyền.",
            "antigravity-ide" => "Mở Antigravity IDE và file prompt. Bạn chọn model trong IDE, gửi prompt rồi dán câu trả lời về Hub.",
            "antigravity-desktop" => "Mở Antigravity 2.0 desktop riêng. Bạn dán prompt sang app, chọn model rồi dán câu trả lời về Hub.",
            "manual" => "Sao chép prompt tới app / web AI của bạn. Chọn model ở đó và dán câu trả lời về đúng agent trong Hub.",
            "browser-web" or "gemini-web" or "chatgpt-web" => "Extension tự báo website và model đọc được từ giao diện/phản hồi. Để nhãn trống trong extension để theo model đang chọn trên web. Website khác dùng bộ nhận diện thử nghiệm, có thể cần CSS nâng cao. Không dùng API key.",
            "custom-cli" => "Hub chạy executable của bạn với prompt qua stdin, đọc stdout làm kết quả. Đối số JSON hỗ trợ {model} và {project}.",
            _ => "Gọi API trực tiếp. API nhận yêu cầu và file đính kèm đã chọn, trả văn bản; không tự đọc project hay sửa file." };
        ShowRow(effort, kind is "codex-cli" or "claude-cli" or "agy-cli"); ShowRow(permission, kind is "codex-cli" or "claude-cli" or "agy-cli");
        ShowRow(command, executable);
        ShowRow(browserBinding, web); ShowRow(refreshBrowser, web); model.Enabled = !web;
        var browse = connectionFields.Controls.Cast<Control>().First(c => c.AccessibleName == "Chọn đường dẫn"); ShowRow(browse, executable);
        ShowRow(arguments, kind == "custom-cli");
        foreach (var control in new Control[] { endpoint, variable, key, showKey, credentialStatus, deleteKey }) ShowRow(control, api);
        loadModels.Enabled = api || kind is "codex-cli" or "claude-cli" or "agy-cli";
        endpoint.PlaceholderText = Catalog.Endpoint(kind); command.PlaceholderText = "Tự tìm công cụ đã cài";
        variable.PlaceholderText = kind switch { "anthropic-api" => "ANTHROPIC_API_KEY", "gemini-api" => "GEMINI_API_KEY", "chat-api" => "OPENAI_API_KEY hoặc tên biến riêng", _ => "" };
        key.PlaceholderText = "Để trống để giữ key đã lưu / dùng biến môi trường";
        var values = drafts.GetValueOrDefault(activeKey) ?? new("", "", kind is "codex-cli" or "claude-cli" ? "read-only" : "default", "", "[]", "", "", "", activeKey != original.Provider + "/" + original.Connection);
        model.Items.Clear(); model.Items.AddRange(LocalModels.Read(new() { Connection = kind }).Cast<object>().ToArray()); model.Text = values.Model;
        Ui.Choices(permission, kind switch {
            "codex-cli" => [new("read-only", "Chỉ đọc"), new("workspace-write", "Ghi trong workspace"), new("full", "Toàn quyền • file và terminal")],
            "claude-cli" => [new("read-only", "Plan • phân tích"), new("workspace-write", "Accept edits • sửa file"), new("full", "Bỏ phê duyệt • toàn quyền")],
            "agy-cli" => [new("default", "Theo CLI • terminal cần cấp phép"), new("read-only", "Plan • phân tích"), new("workspace-write", "Accept edits • sửa file"), new("full", "Tự phê duyệt • toàn quyền")],
            _ => [new("default", manual ? "Chọn trong ứng dụng AI" : "Theo quyền của kết nối")] }, values.Permission);
        RefreshEffort(values.Effort);
        command.Text = values.Command; arguments.Text = values.Arguments; endpoint.Text = values.Endpoint; variable.Text = values.Variable; key.Text = values.Key; deleteKey.Checked = values.DeleteKey;
        credentialStatus.Text = secrets.Read(original.Id).Length > 0 ? "Có key trong Windows Credential Manager. Để trống API key mới để giữ nguyên." : "Chưa lưu key riêng. Có thể nhập key hoặc dùng biến môi trường.";
        status.Text = kind.Length == 0 ? "Chọn cách kết nối để cấu hình." : executable ? AgentDiscovery.FindExecutable(new() { Connection = kind, Command = command.Text.Trim() }) is { } binary ? "Đã tìm thấy: " + binary : "Chưa tìm thấy executable. Chọn file .exe hoặc cài công cụ rồi mở lại thiết lập." : "";
        if (web) RefreshBrowserBindings();
        if (Visible && !initializing) {
            string requested = activeKey;
            BeginInvoke((Action)(async () => { if (!IsDisposed && requested == activeKey) await AutoModels(); }));
        }
    }
    private void RefreshEffort(string? selected = null)
    {
        string kind = Ui.Value(connection), value = selected ?? Ui.Value(effort);
        var levels = LocalModels.Efforts(new() { Connection = kind, Model = model.Text });
        Ui.Choices(effort, new[] { new Choice("", "Theo cấu hình CLI") }.Concat(levels.Select(l => new Choice(l, l))), value);
    }
    private void RefreshBrowserBindings()
    {
        string selected = Ui.Value(browserBinding); if (selected.Length == 0) selected = original.BrowserBindingId;
        var bindings = browser?.Bindings.Where(b => Ui.Value(connection) == "browser-web" || b.Provider == Ui.Value(connection)).ToList() ?? [];
        var choices = new List<Choice> { new("", "Chọn tab đã kết nối từ extension…") };
        choices.AddRange(bindings.Select(b => new Choice(b.Id, b.DisplaySite + " · " + b.DisplayModel + (b.Ready ? "" : " (chưa sẵn sàng)"))));
        bool stale = selected.Length > 0 && !bindings.Any(b => b.Id == selected);
        if (stale) selected = "";
        Ui.Choices(browserBinding, choices, selected);
        if (bindings.FirstOrDefault(b => b.Id == Ui.Value(browserBinding)) is { } binding) model.Text = binding.Model;
        status.Text = bindings.Count == 0 ? "Hub chưa nhận tab. Mở extension và bấm Đồng bộ lại với Hub, rồi bấm Dò lại tab tại đây."
            : stale ? "Chrome đã nối tab, nhưng liên kết agent cũ không còn hợp lệ. Chọn tab đang hiển thị trong danh sách rồi Lưu; yêu cầu trong chat vẫn được giữ."
            : "Chọn đúng tab; model thực tế theo website. Nhãn nhận diện được cập nhật khi đọc được từ giao diện hoặc phản hồi.";
    }
    private void Browse()
    {
        using var dialog = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) { command.Text = dialog.FileName; _ = AutoModels(); }
    }
    private AgentConfig Draft()
    {
        var agent = original.Copy(); agent.Name = name.Text.Trim(); agent.Provider = Ui.Value(provider); agent.Connection = Ui.Value(connection);
        agent.Model = model.Text.Trim(); agent.Reasoning = effort.Enabled ? Ui.Value(effort) : ""; agent.Permission = Ui.Value(permission);
        agent.BrowserBindingId = Catalog.IsBrowserChat(agent.Connection) ? Ui.Value(browserBinding) : "";
        if (Catalog.IsBrowserChat(agent.Connection) && agent.BrowserBindingId.Length == 0) throw new ArgumentException("Chọn một tab chatbot đã kết nối từ extension.");
        if (Catalog.IsBrowserChat(agent.Connection)) (browser ?? throw new InvalidOperationException("Bridge trình duyệt chưa được mở.")).Validate(agent.Connection, agent.BrowserBindingId, agent.Model);
        agent.Instructions = instructions.Text.Trim(); agent.Command = command.Enabled ? command.Text.Trim() : "";
        agent.Arguments = agent.Connection == "custom-cli" ? JsonSerializer.Deserialize<List<string>>(arguments.Text) ?? [] : [];
        agent.Endpoint = endpoint.Enabled ? endpoint.Text.Trim() : ""; agent.KeyEnvironment = variable.Enabled ? variable.Text.Trim() : "";
        Catalog.Validate(agent); return agent;
    }
    private void CancelInspection()
    {
        inspectionVersion++; inspection?.Cancel(); inspection = null; loading = false;
        if (!IsDisposed) { check.Enabled = true; loadModels.Enabled = Catalog.IsApi(Ui.Value(connection)) || Ui.Value(connection) is "codex-cli" or "claude-cli" or "agy-cli"; }
    }
    private async Task AutoModels()
    {
        if (IsDisposed || Disposing || initializing || loading) return;
        string kind = Ui.Value(connection);
        if (kind is "codex-cli" or "claude-cli" or "agy-cli" || Catalog.IsApi(kind)) await Inspect(true);
    }
    private void ApplyModels(ModelInventory inventory)
    {
        string selected = model.Text;
        model.BeginUpdate();
        try { model.Items.Clear(); model.Items.AddRange(inventory.Models.Cast<object>().ToArray()); model.Text = selected; }
        finally { model.EndUpdate(); }
        status.Text = $"{inventory.Source} • {inventory.Models.Count} model\n{inventory.Detail}";
        Ui.Hint(status, status.Text); RefreshEffort();
    }
    private async Task Inspect(bool populate)
    {
        CancelInspection(); int version = inspectionVersion;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(30)); inspection = timeout;
        try {
            var draft = Draft(); loading = true; loadModels.Enabled = check.Enabled = false;
            if (Catalog.IsBrowserChat(draft.Connection)) { (browser ?? throw new InvalidOperationException("Bridge trình duyệt chưa được mở.")).Validate(draft.Connection, draft.BrowserBindingId, draft.Model); status.Text = "Tab chatbot sẵn sàng. Model do bạn chọn/xác nhận trên web."; return; }
            status.Text = "Đang dò công cụ/model… Bạn vẫn có thể đổi kết nối hoặc lưu cấu hình.";
            var preview = new PreviewSecrets(secrets, draft.Id, key.Text.Trim(), deleteKey.Checked);
            if (populate) {
                if (!Catalog.IsApi(draft.Connection) && !Catalog.IsManual(draft.Connection) && ProcessTransport.Find(draft) == null)
                    throw new FileNotFoundException("Chưa tìm thấy CLI. Chọn executable .exe hoặc dùng Dò công cụ & model trong Agent & model.");
                var inventory = await AgentDiscovery.Models(draft, preview, timeout.Token);
                if (!IsDisposed && version == inspectionVersion) ApplyModels(inventory);
            } else {
                var health = await AgentConnections.Inspect(draft, preview, timeout.Token);
                if (!IsDisposed && version == inspectionVersion) { status.Text = health.Detail; Ui.Hint(status, health.Detail); }
            }
        }
        catch (OperationCanceledException) { if (!IsDisposed && version == inspectionVersion) status.Text = "Dò model đã dừng hoặc quá 30 giây. Có thể Dò/Tải lại."; }
        catch (Exception e) { if (!IsDisposed && version == inspectionVersion) status.Text = e.Message; }
        finally { if (!IsDisposed && version == inspectionVersion) { inspection = null; loading = false; check.Enabled = true; loadModels.Enabled = Catalog.IsApi(Ui.Value(connection)) || Ui.Value(connection) is "codex-cli" or "claude-cli" or "agy-cli"; } }
    }
    private void Save()
    {
        try { Result = Draft(); if (Catalog.IsApi(Result.Connection) && key.Text.Trim().Length > 0) secrets.Write(Result.Id, key.Text.Trim()); else if (deleteKey.Checked) secrets.Delete(Result.Id); DialogResult = DialogResult.OK; }
        catch (Exception e) { status.Text = e.Message; }
    }
    private record DraftValues(string Model, string Effort, string Permission, string Command, string Arguments, string Endpoint, string Variable, string Key, bool DeleteKey);
    private sealed class PreviewSecrets(ISecretStore store, string id, string value, bool clear) : ISecretStore
    {
        public string Read(string requested) => requested == id && value.Length > 0 ? value : clear ? "" : store.Read(requested);
        public void Write(string id, string value) => throw new NotSupportedException(); public void Delete(string id) => throw new NotSupportedException();
    }
}
