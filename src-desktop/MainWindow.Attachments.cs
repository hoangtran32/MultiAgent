using System.Runtime.InteropServices;
using MultiAgentHub.Core;

namespace MultiAgentHubApp;

internal sealed partial class MainWindow
{
    private void ClearAttachments()
    {
        attachmentGeneration++; preparingAttachments = false; pendingAttachments.Clear(); RefreshAttachments();
    }
    private void RefreshAttachments()
    {
        attachmentTray.SuspendLayout();
        try {
            while (attachmentTray.Controls.Count > 0) { var old = attachmentTray.Controls[0]; attachmentTray.Controls.Remove(old); old.Dispose(); }
            int D(int value) => (int)Math.Round(value * DeviceDpi / 96f);
            foreach (var file in pendingAttachments) {
                var selected = file;
                var chip = new Panel { Width = D(228), Height = D(32), Margin = new Padding(D(2)), BackColor = Ui.Background };
                var name = new Label { Text = file.Info.Name, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(D(8), 0, 0, 0), Cursor = Cursors.Hand };
                var remove = Ui.IconButton("close", "Bỏ tệp " + file.Info.Name, (_, _) => { if (runner.Busy || preparingAttachments) return; pendingAttachments.Remove(selected); RefreshAttachments(); });
                remove.Dock = DockStyle.Right; remove.Width = D(30); name.Click += (_, _) => status.Text = selected.Info.Caption + " • " + selected.Info.Notice;
                Ui.Hint(name, file.Info.Caption + "\n" + file.Info.Notice); chip.Controls.Add(name); chip.Controls.Add(remove); attachmentTray.Controls.Add(chip);
            }
            attachmentTray.Visible = pendingAttachments.Count > 0;
            // This label is a layout signal; file chips occupy the actual composer row.
            attachment.Text = pendingAttachments.Count == 0 ? "Không đính kèm file" : string.Join(" | ", pendingAttachments.Select(f => f.Info.Name));
        } finally { attachmentTray.ResumeLayout(true); }
    }
    private async void AddAttachmentFiles(string[] paths)
    {
        if (runner.Busy || preparingAttachments || paths.Length == 0) return;
        int version = ++attachmentGeneration; preparingAttachments = true; status.Text = "Đang đọc tệp đính kèm…";
        try {
            if (paths.Length + pendingAttachments.Count > ChatAttachments.MaxFiles) throw new ArgumentException("Tối đa 10 tệp mỗi lần gửi.");
            if (paths.Sum(p => new FileInfo(p).Length) + pendingAttachments.Sum(f => f.Info.Size) > ChatAttachments.MaxTotalBytes) throw new ArgumentException("Tổng tệp đính kèm tối đa 20 MB.");
            var prepared = await Task.Run(() => paths.Select(ChatAttachments.Read).ToArray());
            if (IsDisposed || version != attachmentGeneration) return;
            var combined = pendingAttachments.Concat(prepared).DistinctBy(f => (f.Info.Id, f.Info.Name)).ToList(); ChatAttachments.ValidateSet(combined);
            pendingAttachments.Clear(); pendingAttachments.AddRange(combined); RefreshAttachments();
            status.Text = $"Đã đính kèm {pendingAttachments.Count} tệp. Bấm tên tệp để xem định dạng/giới hạn, dấu × để bỏ từng tệp.";
        } catch (Exception error) { if (!IsDisposed && version == attachmentGeneration) status.Text = "Chưa thêm tệp: " + error.Message; }
        finally { if (version == attachmentGeneration) preparingAttachments = false; }
    }
    private void HandleAttachmentPaste(KeyEventArgs e)
    {
        if (!e.Control || e.KeyCode != Keys.V || runner.Busy || preparingAttachments) return;
        try {
            if (Clipboard.ContainsFileDropList()) { e.SuppressKeyPress = true; AddAttachmentFiles(Clipboard.GetFileDropList().Cast<string>().ToArray()); return; }
            if (!Clipboard.ContainsImage()) return;
            e.SuppressKeyPress = true;
            using var image = Clipboard.GetImage(); if (image == null) return;
            using var bytes = new MemoryStream(); image.Save(bytes, System.Drawing.Imaging.ImageFormat.Png);
            var file = ChatAttachments.FromBytes("Ảnh-dán-" + DateTime.Now.ToString("HHmmss") + ".png", bytes.ToArray());
            var combined = pendingAttachments.Append(file).ToList(); ChatAttachments.ValidateSet(combined);
            pendingAttachments.Add(file); RefreshAttachments(); status.Text = "Đã dán ảnh vào tệp đính kèm. Nhập yêu cầu rồi gửi.";
        } catch (Exception error) when (error is ExternalException or ArgumentException) { status.Text = "Chưa dán được ảnh/tệp: " + error.Message; }
    }
}
