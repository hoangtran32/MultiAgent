using System.IO.Compression;
using System.Security.Cryptography;

namespace MultiAgentHub.Core;

public static class CodeOssDistribution
{
    public const string Version = "1.140.06880";
    public const string Sha256 = "024ae1e88f431aee300e73c2ece92e42ebde80a7f3c484ff684f455ffa6a6a7f";
    public const long ArchiveBytes = 229054371;
    public const string DownloadUrl = "https://github.com/VSCodium/vscodium/releases/download/1.140.06880/VSCodium-win32-x64-1.140.06880.zip";
    public static void Verify(string archive)
    {
        using var input = File.OpenRead(archive);
        if (input.Length != ArchiveBytes || Convert.ToHexStringLower(SHA256.HashData(input)) != Sha256) throw new InvalidDataException("Code OSS: checksum bản tải không khớp bản phát hành VSCodium đã ghim.");
    }
    public static void Extract(string archive, string destination, CancellationToken token)
    {
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        if (Directory.Exists(destination)) throw new IOException("Thư mục giải nén phải mới.");
        Directory.CreateDirectory(destination); long total = 0;
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries) {
            token.ThrowIfCancellationRequested();
            string normalized = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (normalized.Contains(':') || normalized.Split(Path.DirectorySeparatorChar).Any(part => part is ".." or ".") || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Đường dẫn ZIP Code OSS không hợp lệ.");
            string target = Path.GetFullPath(Path.Combine(destination, normalized));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("ZIP thoát thư mục cài đặt.");
            total += entry.Length; if (total > 2_000_000_000 || entry.Length > 500_000_000) throw new InvalidDataException("ZIP Code OSS quá lớn.");
            if (entry.FullName.EndsWith('/')) Directory.CreateDirectory(target);
            else { Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target, false); }
        }
        if (!File.Exists(Path.Combine(destination, "VSCodium.exe")) || !File.Exists(Path.Combine(destination, "resources", "app", "product.json"))) throw new InvalidDataException("Thiếu executable/product Code OSS.");
        File.WriteAllText(Path.Combine(destination, ".hub-sha256"), Sha256);
    }
    public static void PlainPath(string path)
    {
        for (string? part = Path.GetFullPath(path); part != null; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new IOException("Thư mục IDE không được là liên kết: " + part);
    }
}
