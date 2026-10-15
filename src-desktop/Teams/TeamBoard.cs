using System.Text.Json;
using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal sealed class TeamBoard : UserControl
{
    private readonly DataGridView tasks = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AccessibleName = "Bảng công việc chung" };
    private readonly TextBox details = Ui.Text(true), artifacts = Ui.Text(true), evidence = Ui.Text(true);
    private readonly Label phase = Ui.Label("Bàn tròn, task và đầu ra sẽ xuất hiện khi bắt đầu phiên nhóm.");
    private JobRecord? job;
    private string signature = "";
    public TeamBoard()
    {
        Dock = DockStyle.Fill;
        var root = Ui.Table(); root.Padding = new Padding(12); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); Controls.Add(root);
        var header = Ui.Flow(); header.Controls.AddRange([phase, Ui.Button("Xuất tài liệu…", (_, _) => ExportArtifacts())]); root.Controls.Add(header, 0, 0);
        foreach (var item in new[] { ("id", "ID", 45f), ("title", "Ý / task", 220f), ("state", "Trạng thái", 100f), ("agreement", "Chốt ý", 85f), ("agent", "Thực hiện", 100f), ("checks", "Kiểm tra", 100f) })
            tasks.Columns.Add(new DataGridViewTextBoxColumn { Name = item.Item1, HeaderText = item.Item2, FillWeight = item.Item3, SortMode = DataGridViewColumnSortMode.NotSortable });
        root.Controls.Add(tasks, 0, 1); tasks.SelectionChanged += (_, _) => ShowTask();
        var tabs = new TabControl { Dock = DockStyle.Fill };
        foreach (var item in new[] { ("Task & phản biện", details), ("Tài liệu & ghi nhận", artifacts), ("Bằng chứng kiểm tra", evidence) }) {
            item.Item2.ReadOnly = true; var page = new TabPage(item.Item1); page.Controls.Add(item.Item2); tabs.TabPages.Add(page);
        }
        root.Controls.Add(tabs, 0, 2); Ui.Theme(this);
    }
    public void Clear() { job = null; signature = ""; tasks.Rows.Clear(); details.Clear(); artifacts.Clear(); evidence.Clear(); phase.Text = "Bàn tròn, task và đầu ra sẽ xuất hiện khi bắt đầu phiên nhóm."; }
    public void Update(JobRecord value)
    {
        job = value; if (value.Team is not { } team) { Clear(); return; }
        string next = JsonSerializer.Serialize(team, Json.Options); if (next == signature) return; signature = next;
        string selected = tasks.CurrentRow?.Cells[0].Value?.ToString() ?? "";
        phase.Text = $"{Ui.State(team.Phase)} • {team.Tasks.Count(t => t.State == "done")}/{team.Tasks.Count} task đã review • {team.Calls}/{team.Options.MaxCalls} lần gọi{(team.ActiveTaskId.Length > 0 ? " • " + team.ActiveTaskId : "")}";
        tasks.Rows.Clear();
        foreach (var task in team.Tasks) {
            int required = value.Results.Count(r => r.State != "quota");
            string agreement = value.Mode == "consensus" ? $"v{task.ProposalVersion} · {task.AgreedBy.Count(id => value.Results.Any(r => r.AgentId == id && r.State != "quota"))}/{required}" : "—";
            int index = tasks.Rows.Add(task.Id, task.Title, Ui.State(task.State), agreement, value.Results.FirstOrDefault(r => r.AgentId == task.AssignedTo)?.Name ?? "—", Ui.State(task.Verification));
            if (task.Id == selected) tasks.CurrentCell = tasks.Rows[index].Cells[0];
        }
        ShowTask();
        artifacts.Text = "BỔ SUNG ĐANG CHỜ ÁP DỤNG\r\n" + string.Join("\r\n\r\n", team.PendingDirectives) + "\r\n\r\nBỔ SUNG ĐÃ NHẬN\r\n" + string.Join("\r\n\r\n", team.Directives) + "\r\n\r\nGHI NHẬN / BẤT ĐỒNG\r\n" + string.Join("\r\n", team.Decisions) +
            "\r\n\r\nTÀI LIỆU\r\n" + string.Join("\r\n\r\n────────────────────\r\n\r\n", team.Artifacts.Select(a => $"{a.Title} • {a.Kind} • {a.TaskId} • lượt {a.Turn}\r\n\r\n{a.Content.ReplaceLineEndings()}"));
        evidence.Text = team.Evidence.Count == 0 ? "Chưa chạy lệnh kiểm tra do Hub quản lý. Bạn có thể tự kiểm tra hoặc vào Agent & model → Phân vai & giới hạn nhóm… → Lệnh kiểm tra.\r\nLời báo đã test từ model không phải bằng chứng thực thi của Hub." :
            string.Join("\r\n\r\n────────────────────\r\n\r\n", team.Evidence.Select(e => $"{e.Name} • {e.TaskId} • {Ui.State(e.State)} • exit={e.ExitCode}\r\n{e.Executable}\r\nĐối số: {JsonSerializer.Serialize(e.Arguments)}\r\nFingerprint: {e.Fingerprint}\r\nHiệu lực: {(e.Current ? "khớp bản được ghi nhận" : "đã hết hiệu lực / chưa xác minh")}\r\n\r\n{e.Log.ReplaceLineEndings()}"));
    }
    private void ShowTask()
    {
        string id = tasks.CurrentRow?.Cells[0].Value?.ToString() ?? "";
        if (job?.Team?.Tasks.FirstOrDefault(t => t.Id == id) is not { } task) { details.Text = "Chọn một task để xem phạm vi, tiêu chí và phản biện."; return; }
        details.Text = $"{task.Id} • {task.Title}\r\n{Ui.State(task.State)} • sửa {task.Revisions}/{job.Team.Options.MaxRevisions} vòng\r\nPhụ thuộc: {string.Join(", ", task.DependsOn)}\r\n{(task.IndependentReview ? "Review chéo" : "Chưa có review độc lập")}\r\n\r\nPHẠM VI\r\n{task.Description}\r\n\r\nTIÊU CHÍ\r\n{task.Acceptance}\r\n\r\nKẾT QUẢ\r\n{task.Outcome}\r\n\r\nPHẢN BIỆN\r\n{task.Feedback}".ReplaceLineEndings();
        if (job.Mode == "consensus") details.Text = $"PHIÊN BẢN Ý: {task.ProposalVersion}\r\nĐã chốt: {string.Join(", ", task.AgreedBy.Select(id => job.Results.FirstOrDefault(r => r.AgentId == id)?.Name ?? id))}\r\n\r\n" + details.Text;
    }
    private void ExportArtifacts()
    {
        if (job?.Team == null) return;
        using var dialog = new FolderBrowserDialog { Description = "Chọn nơi xuất tài liệu; Hub tạo thư mục riêng, không ghi đè file hiện có." };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try {
            string folder = Path.Combine(dialog.SelectedPath, "Hub-" + job.Id + "-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "Phien-nhom.md"), MultiAgentHub.Core.Export.Markdown(job), new System.Text.UTF8Encoding(true));
            int index = 0;
            foreach (var artifact in job.Team.Artifacts) {
                string title = System.Text.RegularExpressions.Regex.Replace(artifact.Title, "[<>:\"/\\\\|?*\\x00-\\x1f]", " ").Trim().TrimEnd('.', ' ');
                if (title.Length == 0) title = "Tai-lieu";
                string file = $"{++index:D3}-{title[..Math.Min(title.Length, 70)]}.md";
                File.WriteAllText(Path.Combine(folder, file), $"# {artifact.Title}\n\n{artifact.Kind} • {artifact.TaskId} • lượt {artifact.Turn}\n\n{artifact.Content}", new System.Text.UTF8Encoding(true));
            }
            MessageBox.Show(this, "Đã xuất tài liệu tại:\n" + folder, "Đã lưu", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception e) { MessageBox.Show(this, e.Message, "Không xuất được tài liệu", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
