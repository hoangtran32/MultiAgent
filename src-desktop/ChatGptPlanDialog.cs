using System.Diagnostics;
using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal sealed class ChatGptPlanDialog : Form
{
    private readonly ChatGptPlan plan;
    private readonly AgentConfig? editing;
    private readonly ComboBox accounts = Ui.Combo(610), models = Ui.Combo(610);
    private readonly TextBox label = Ui.Text();
    private readonly Label status = Ui.Label("");
    private readonly List<Button> actions = [];
    private readonly CancellationTokenSource lifetime = new();
    private int generation;
    private bool updating, busy;
    private string modelsAccount = "";
    public AgentConfig? SelectedAgent { get; private set; }
    public ChatGptPlanDialog(ChatGptPlan plan, AgentConfig? editing = null)
    {
        this.plan = plan; this.editing = editing;
        Text = "Tài khoản ChatGPT • API"; Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false; Icon = Ui.AppIcon; MinimumSize = new Size(720, 550); Size = new Size(800, 680);
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(20) }; Controls.Add(root);
        void Help(string value) { var text = Ui.Label(value); text.MaximumSize = new Size(710, 0); root.Controls.Add(text); }
        Help("Using ChatGPT plan • API chính thức, chạy ngầm sau khi đăng nhập. Lần đầu cần cho phép Multi-Agent Hub dùng gói ChatGPT. Các yêu cầu hợp lệ dùng hạn mức gói của bạn; quản lý quyền, giới hạn và tùy chọn dùng credits tại ChatGPT Settings → Usage.");
        Help("Model được lấy từ tài khoản sau đăng nhập. Tính năng và model được phép qua API có thể khác giao diện ChatGPT. Hub không đọc lịch sử chat, phiên đăng nhập Codex hay hồ sơ trình duyệt.");
        root.Controls.Add(accounts); label.Width = 610; label.MaxLength = 80; label.PlaceholderText = "Tên để phân biệt tài khoản / workspace"; root.Controls.Add(label);
        var row = Ui.Flow(); root.Controls.Add(row);
        void Button(string name, Func<Task> action) { var button = Ui.Button(name, async (_, _) => await Run(action)); actions.Add(button); row.Controls.Add(button); }
        Button("Continue with ChatGPT", async () => {
            string? accountId = Ui.Value(accounts).Length == 0 ? null : Ui.Value(accounts);
            var result = await plan.SignIn(accountId, label.Text, uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose(), lifetime.Token);
            RefreshAccounts(result.Id);
            if (!result.PlanEnabled) { status.Text = "Đã đăng nhập nhưng chưa cho phép dùng gói ChatGPT. Bấm Continue with ChatGPT để cấp quyền."; return; }
            await LoadModels();
        });
        Button("Tải lại model", LoadModels);
        Button("Đăng xuất tài khoản", async () => {
            string id = Ui.Value(accounts); if (id.Length == 0) throw new ArgumentException("Chọn tài khoản cần đăng xuất.");
            bool revoked = await plan.SignOut(id, lifetime.Token); RefreshAccounts(id); status.Text = revoked ? "Đã đăng xuất và thu hồi phiên Hub. Đăng ký tài khoản được giữ để đăng nhập lại." : "Đã xóa token tại máy. Chưa xác nhận thu hồi từ máy chủ; có thể ngắt quyền Hub tại ChatGPT Settings → Usage.";
        });
        Help("Model được máy chủ cho phép cho tài khoản đã chọn:"); root.Controls.Add(models);
        var use = Ui.Button(editing == null ? "Thêm model vào Hub" : "Dùng tài khoản / model này", (_, _) => {
            try {
                string accountId = Ui.Value(accounts), modelId = Ui.Value(models);
                if (modelsAccount != accountId || modelId.Length == 0) throw new ArgumentException("Chọn tài khoản, tải danh sách rồi chọn model.");
                var account = plan.Store.Accounts.Single(a => a.Id == accountId);
                var agent = editing?.Copy() ?? new AgentConfig { Name = "ChatGPT • " + models.Text, Enabled = false };
                agent.Provider = agent.Connection = "chatgpt-plan"; agent.PlanAccountId = account.Id; agent.Model = modelId; agent.Permission = "default";
                agent.Command = agent.Endpoint = agent.KeyEnvironment = agent.BrowserBindingId = agent.GatewayAccountId = agent.Reasoning = ""; agent.Arguments = [];
                if (agent.Name.Length > 80) agent.Name = agent.Name[..80]; plan.Validate(agent); SelectedAgent = agent; DialogResult = DialogResult.OK;
            } catch (Exception error) { status.Text = error.Message; }
        }, true); actions.Add(use); root.Controls.Add(use);
        root.Controls.Add(Ui.Button("ChatGPT Settings → Usage", (_, _) => Process.Start(new ProcessStartInfo("https://chatgpt.com/settings/usage") { UseShellExecute = true })?.Dispose()));
        Help("Gemini desktop: chưa có kết nối API được xác minh dùng đúng quota/model của app. Gemini Developer API dùng cơ chế cấp quyền và hạn mức riêng; Hub không tự thay kết nối đó vào tài khoản này.");
        status.MaximumSize = new Size(710, 0); root.Controls.Add(status);
        accounts.SelectedIndexChanged += async (_, _) => { if (updating || busy) return; string id = Ui.Value(accounts); label.Text = plan.Store.Accounts.FirstOrDefault(a => a.Id == id)?.Label ?? ""; await Run(LoadModels); };
        Shown += async (_, _) => { RefreshAccounts(editing?.PlanAccountId); if (Ui.Value(accounts).Length > 0) await Run(LoadModels); };
        FormClosed += (_, _) => { lifetime.Cancel(); }; Ui.Theme(this); AutoScaleMode = AutoScaleMode.Dpi;
    }
    private void RefreshAccounts(string? selected = null)
    {
        updating = true;
        try { Ui.Choices(accounts, new[] { new Choice("", "Thêm tài khoản / workspace khác…") }.Concat(plan.Store.Accounts.Select(a => new Choice(a.Id, a.Label + " · " + a.ClientId + (a.SignedIn ? "" : " · đã đăng xuất")))), selected ?? ""); }
        finally { updating = false; }
        models.Items.Clear(); modelsAccount = "";
    }
    private async Task LoadModels()
    {
        int version = ++generation; string id = Ui.Value(accounts); models.Items.Clear(); modelsAccount = "";
        if (id.Length == 0) { status.Text = "Bấm Continue with ChatGPT để đăng nhập và cấp quyền cho Hub."; return; }
        status.Text = "Đang lấy danh sách model từ tài khoản…";
        var choices = await plan.Models(id, lifetime.Token);
        if (IsDisposed || version != generation || id != Ui.Value(accounts)) return;
        modelsAccount = id; Ui.Choices(models, new[] { new Choice("", "Chọn model…") }.Concat(choices), editing?.PlanAccountId == id ? editing.Model : "");
        status.Text = $"{choices.Count} model do API của tài khoản trả về. Chưa gửi yêu cầu suy luận.";
    }
    private async Task Run(Func<Task> action)
    {
        if (busy) return; busy = true; accounts.Enabled = false; label.Enabled = false; models.Enabled = false; actions.ForEach(b => b.Enabled = false);
        try { await action(); }
        catch (OperationCanceledException) { if (!IsDisposed) status.Text = "Đăng nhập/tải model đã hủy hoặc quá thời gian. Có thể thử lại."; }
        catch (Exception error) { if (!IsDisposed) status.Text = error is HttpRequestException ? "Chưa kết nối được dịch vụ ChatGPT. Kiểm tra mạng và thử lại." : error.Message; }
        finally { busy = false; if (!IsDisposed) { accounts.Enabled = label.Enabled = models.Enabled = true; actions.ForEach(b => b.Enabled = true); } }
    }
}
