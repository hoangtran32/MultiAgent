using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace MultiAgentHub.Core;

public sealed class HubIdeManifest
{
    public string Version { get; set; } = "";
    public string SourceCommit { get; set; } = "";
    public string EditorCommit { get; set; } = "";
    public long ArchiveBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string ExecutableSha256 { get; set; } = "";
}

public static class HubIdeDistribution
{
    public const string ExecutableName = "Hub Code.exe";
    public static void Extract(Stream archive, string destination, HubIdeManifest manifest, CancellationToken token)
    {
        bool Hex(string value, int length) => value.Length == length && value.All(Uri.IsHexDigit);
        if (!Hex(manifest.SourceCommit, 40) || !Hex(manifest.EditorCommit, 40) || !Hex(manifest.Sha256, 64) || !Hex(manifest.ExecutableSha256, 64) || manifest.ArchiveBytes <= 0) throw new InvalidDataException("Manifest Hub IDE không hợp lệ.");
        if (archive.Length != manifest.ArchiveBytes || Convert.ToHexStringLower(SHA256.HashData(archive)) != manifest.Sha256) throw new InvalidDataException("Checksum editor Hub không khớp bundle.");
        archive.Position = 0;
        PlainPath(destination);
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        if (Directory.Exists(destination)) throw new IOException("Thư mục giải nén phải mới.");
        Directory.CreateDirectory(destination); long total = 0;
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, true);
        foreach (var entry in zip.Entries) {
            token.ThrowIfCancellationRequested();
            string normalized = entry.FullName.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);
            if (normalized.Contains(':') || normalized.Split(Path.DirectorySeparatorChar).Any(part => part is ".." or ".") || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Đường dẫn ZIP Hub không hợp lệ.");
            string target = Path.GetFullPath(Path.Combine(destination, normalized));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ZIP thoát thư mục cài đặt.");
            total += entry.Length; if (total > 3_000_000_000 || entry.Length > 800_000_000) throw new InvalidDataException("ZIP Hub quá lớn.");
            if (entry.FullName.EndsWith('/')) Directory.CreateDirectory(target);
            else { Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target, false); }
        }
        ValidateInstalled(destination, manifest);
        File.WriteAllText(Path.Combine(destination, ".hub-sha256"), manifest.Sha256);
    }
    public static void ValidateInstalled(string editor, HubIdeManifest manifest)
    {
        PlainPath(editor);
        string exe = Path.Combine(editor, ExecutableName), product = Path.Combine(editor, "resources", "app", "product.json");
        PlainPath(exe); PlainPath(product);
        using var input = File.OpenRead(exe);
        if (Convert.ToHexStringLower(SHA256.HashData(input)) != manifest.ExecutableSha256) throw new InvalidDataException("Executable Hub Code đã thay đổi.");
        using var doc = JsonDocument.Parse(File.ReadAllText(product));
        if (doc.RootElement.Str("nameShort") != "Hub Code" || doc.RootElement.Str("hubVersion") != manifest.Version || doc.RootElement.Str("hubSourceCommit") != manifest.SourceCommit) throw new InvalidDataException("Bundle không phải editor Hub cùng phiên bản.");
        string agent = Path.Combine(editor, "resources", "app", "extensions", "multiagent-hub", "package.json"); PlainPath(agent);
        using var extension = JsonDocument.Parse(File.ReadAllText(agent));
        if (extension.RootElement.Str("publisher") != "multiagent-hub" || extension.RootElement.Str("version") != manifest.Version) throw new InvalidDataException("Bundle thiếu Agent tích hợp đúng phiên bản.");
    }
    public static void PlainPath(string path)
    {
        for (string? part = Path.GetFullPath(path); part != null; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new IOException("Thư mục IDE không được là liên kết: " + part);
    }
}
