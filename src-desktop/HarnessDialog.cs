using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal sealed class HarnessDialog : Form
{
    public HarnessOptions Result { get; private set; }
    public HarnessDialog(HarnessOptions options)
    {
        Result = options.Copy(); SuspendLayout(); Text = "Runtime & công cụ"; Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; Icon = Ui.AppIcon;
        var root = Ui.Table(2); root.Padding = new Padding(18); root.AutoScroll = true;
        root.ColumnStyles.Add(new(SizeType.Percent, 65)); root.ColumnStyles.Add(new(SizeType.Percent, 35)); Controls.Add(root);
        var enabled = new CheckBox { Text = "Dùng harness của Hub cho các kết nối API", Checked = options.Enabled, AutoSize = true };
        var web = new CheckBox { Text = "Cho phép đọc website HTTPS công khai", Checked = options.AllowWeb, AutoSize = true };
        var commands = new CheckBox { Text = "Cho phép công cụ lệnh trong Hub Code khi agent có toàn quyền", Checked = options.AllowCommands, AutoSize = true };
        int row = 0;
        void Wide(Control control) { root.RowStyles.Add(new(SizeType.AutoSize)); root.Controls.Add(control, 0, row++); root.SetColumnSpan(control, 2); control.Margin = new Padding(0, 4, 0, 8); }
        Wide(enabled); Wide(web); Wide(commands);
        NumericUpDown Number(string title, int value, int min, int max) {
            var number = new NumericUpDown { Minimum = min, Maximum = max, Value = value, Dock = DockStyle.Fill, ThousandsSeparator = true };
            root.RowStyles.Add(new(SizeType.Absolute, 42)); root.Controls.Add(Ui.Label(title), 0, row); root.Controls.Add(number, 1, row++); return number;
        }
        var calls = Number("Lần gọi model tối đa (0 = không giới hạn)", options.MaxModelCalls, 0, 10000);
        var tools = Number("Lần dùng công cụ tối đa (0 = không giới hạn)", options.MaxToolCalls, 0, 10000);
        var context = Number("Ngân sách ngữ cảnh (ký tự)", options.ContextChars, 32768, 800000);
        var output = Number("Ký tự đầu ra công cụ đưa vào ngữ cảnh", options.ToolOutputChars, 2000, 64000);
        var timeout = Number("Thời gian chạy một lệnh (giây)", options.CommandTimeoutSeconds, 5, 3600);
        Wide(Ui.Label("URL công cụ tìm kiếm (tùy chọn, HTTPS, chứa {query}; để trống thì chỉ có đọc URL):"));
        var search = Ui.Text(); search.Text = options.SearchUrlTemplate; Wide(search);
        var note = Ui.Label("Chat dùng tệp đính kèm, bộ nhớ riêng hội thoại và công cụ web. Code có thêm đọc/tìm file; sửa file cần quyền ghi workspace hoặc toàn quyền. Chạy lệnh cần toàn quyền, dùng executable + đối số và không có sandbox hệ điều hành. Bước lập kế hoạch/review chỉ đọc.\n\nCLI tiếp tục dùng harness của chính CLI. Extension/gateway vẫn theo khả năng website, không tự nhận quyền công cụ máy. Model/API phải hỗ trợ function calling; lỗi không tự đổi model hoặc tài khoản.\n\nNgữ cảnh giữ nguyên yêu cầu và các cặp call/result gần nhất; công cụ cũ có nhật ký để đọc lại. Giới hạn nhóm vẫn tính cả lần gọi model bên trong harness. Không tự phát lại thao tác sau gián đoạn.");
        note.MaximumSize = new Size(720, 0); Wide(note);
        var buttons = Ui.Flow(); buttons.Controls.Add(Ui.Button("Lưu", (_, _) => {
            try {
                Result = new() { Enabled = enabled.Checked, AllowWeb = web.Checked, AllowCommands = commands.Checked, MaxModelCalls = (int)calls.Value, MaxToolCalls = (int)tools.Value,
                    ContextChars = (int)context.Value, ToolOutputChars = (int)output.Value, CommandTimeoutSeconds = (int)timeout.Value, SearchUrlTemplate = search.Text.Trim() };
                Result.Validate(); DialogResult = DialogResult.OK;
            } catch (ArgumentException error) { MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }, true)); buttons.Controls.Add(Ui.Button("Hủy", (_, _) => DialogResult = DialogResult.Cancel)); Wide(buttons);
        Ui.Theme(this); float scale = DeviceDpi / 96f; root.Scale(new SizeF(scale, scale)); AutoScaleDimensions = new(DeviceDpi, DeviceDpi); AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(true); var area = Screen.FromControl(this).WorkingArea; Size = new(Math.Min((int)(820 * scale), area.Width - 40), Math.Min((int)(820 * scale), area.Height - 40));
    }
}
