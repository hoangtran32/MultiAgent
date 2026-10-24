using System.Security.Cryptography;
using System.Text;

namespace MultiAgentHub.Core;

public sealed record WorkspaceDocument(string Path, string Text, string Hash, int CodePage, bool Bom);
public static class WorkspaceDocuments
{
    public static string Resolve(string project, string relative)
    {
        if (string.IsNullOrWhiteSpace(project) || Path.IsPathFullyQualified(relative) || relative.Contains(':') || relative.Any(char.IsControl)) throw new ArgumentException("Chọn đường dẫn tương đối trong project.");
        string root = System.IO.Path.GetFullPath(project).TrimEnd(System.IO.Path.DirectorySeparatorChar), path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
        if (path != root && !path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Đường dẫn ngoài project.");
        if (relative.Split('/', '\\').Any(p => p.Equals(".git", StringComparison.OrdinalIgnoreCase) || p is not ("" or "." or "..") && (p.EndsWith('.') || p.EndsWith(' ')))) throw new ArgumentException("Đường dẫn nội bộ Git hoặc mơ hồ không được mở bằng editor.");
        HarnessJournal.PlainPath(path); return path;
    }
    public static WorkspaceDocument Read(string project, string relative)
    {
        string path = Resolve(project, relative); byte[] data;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            if (stream.Length > 1024 * 1024) throw new IOException("Editor hỗ trợ tệp văn bản tối đa 1 MB.");
            data = new byte[checked((int)stream.Length)]; stream.ReadExactly(data);
        }
        using var reader = new StreamReader(new MemoryStream(data), new UTF8Encoding(false, true), true);
        string text; try { text = reader.ReadToEnd(); } catch (DecoderFallbackException) { throw new IOException("Chưa hỗ trợ file nhị phân hoặc mã hóa này trong editor."); }
        if (text.Contains('\0')) throw new IOException("Chưa hỗ trợ file nhị phân trong editor.");
        bool bom = data.AsSpan().StartsWith(reader.CurrentEncoding.GetPreamble()) && reader.CurrentEncoding.GetPreamble().Length > 0;
        return new(relative, text, Convert.ToHexStringLower(SHA256.HashData(data)), reader.CurrentEncoding.CodePage, bom);
    }
    public static WorkspaceDocument Save(string project, WorkspaceDocument document, string text)
    {
        string path = Resolve(project, document.Path); var current = Read(project, document.Path);
        if (current.Hash != document.Hash) throw new IOException("File đã được agent hoặc công cụ khác sửa. Giữ bản nháp; tải lại file trước khi lưu.");
        Encoding encoding = document.CodePage switch { 1200 => new UnicodeEncoding(false, document.Bom, true), 1201 => new UnicodeEncoding(true, document.Bom, true), 12000 => new UTF32Encoding(false, document.Bom, true), 12001 => new UTF32Encoding(true, document.Bom, true), _ => new UTF8Encoding(document.Bom, true) };
        byte[] data = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray(); if (data.Length > 1024 * 1024) throw new IOException("File vượt 1 MB; bản nháp chưa được ghi.");
        string temporary = path + ".hub-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, data); if (Read(project, document.Path).Hash != document.Hash) throw new IOException("File thay đổi khi chuẩn bị ghi; chưa thay file."); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return Read(project, document.Path);
    }
    public static async Task<string> Diff(string project, string relative, CancellationToken token)
    {
        Resolve(project, relative);
        string git = ProcessTransport.OnPath("git") ?? throw new IOException("Chưa tìm thấy Git.");
        var output = new StringBuilder(); object gate = new();
        void Append(string part) { lock (gate) { output.Append(part); if (output.Length > 120000) output.Remove(0, output.Length - 120000); } }
        await ProcessTransport.Run(new(git, ["--no-optional-locks", "-c", "core.fsmonitor=false", "-C", project, "diff", "--no-ext-diff", "--no-textconv", "--no-color", "HEAD", "--", relative], ""), project, Append, Append, token);
        return output.Length == 0 ? "Không có diff so với HEAD. File chưa được Git theo dõi hoặc nằm ngoài repository có thể chưa xuất hiện ở đây." : output.ToString();
    }
}
