using System.Text;

namespace MultiAgentHub.Core;

public sealed partial class Storage
{
    private string AttachmentPath(string hash, string extension)
    {
        if (hash.Length != 64 || hash.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("ID tệp đính kèm không hợp lệ.");
        string directory = Path.Combine(DataDir, "attachments"); Directory.CreateDirectory(directory); return Path.Combine(directory, hash + extension);
    }
    public void SaveAttachments(IReadOnlyList<ChatAttachment> files)
    {
        ChatAttachments.ValidateSet(files);
        void Write(string hash, string extension, byte[] data) {
            if (ChatAttachments.Hash(data) != hash) throw new InvalidDataException("Tệp đính kèm thay đổi sau khi đọc.");
            string path = AttachmentPath(hash, extension);
            if (File.Exists(path)) { if (ChatAttachments.Hash(File.ReadAllBytes(path)) != hash) throw new InvalidDataException("Bản lưu tệp bị thay đổi; chưa gửi yêu cầu."); return; }
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temp, data); File.Move(temp, path, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        foreach (var file in files) { Write(file.Info.Id, ".bin", file.Data); Write(file.Info.TextHash, ".txt", Encoding.UTF8.GetBytes(file.Text)); }
    }
    public List<ChatAttachment> LoadAttachments(IReadOnlyList<AttachmentInfo> infos)
    {
        if (infos.Count > ChatAttachments.MaxFiles || infos.Sum(f => f.Size) > ChatAttachments.MaxTotalBytes) throw new InvalidDataException("Bản lưu tệp vượt giới hạn.");
        var result = new List<ChatAttachment>();
        foreach (var info in infos) {
            string path = AttachmentPath(info.Id, ".bin"), textPath = AttachmentPath(info.TextHash, ".txt");
            if (!File.Exists(path) || !File.Exists(textPath)) throw new FileNotFoundException("Không tìm thấy bản lưu tệp " + info.Name + ". Đính kèm lại để tiếp tục.");
            if (new FileInfo(path).Length != info.Size || info.Size is <= 0 or > ChatAttachments.MaxFileBytes || new FileInfo(textPath).Length > ChatAttachments.MaxTextChars * 4L) throw new InvalidDataException("Bản lưu tệp không đúng kích thước.");
            byte[] data = File.ReadAllBytes(path), text = File.ReadAllBytes(textPath);
            if (ChatAttachments.Hash(data) != info.Id || ChatAttachments.Hash(text) != info.TextHash) throw new InvalidDataException("Bản lưu tệp " + info.Name + " đã thay đổi. Đính kèm lại trước khi gửi.");
            result.Add(new(info, data, Encoding.UTF8.GetString(text)));
        }
        ChatAttachments.ValidateSet(result); return result;
    }
}
