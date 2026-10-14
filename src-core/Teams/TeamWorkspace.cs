using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace MultiAgentHub.Core;

/// <summary>One workflow writer at a time. Fingerprints exclude generated folders, not source files.</summary>
public static class TeamWorkspace
{
    public static FileStream AcquireWriter(string project)
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultiAgentHub", "workspace-locks");
        Directory.CreateDirectory(folder);
        if (new DirectoryInfo(folder).LinkTarget != null) throw new IOException("Thư mục khóa workspace không được là liên kết.");
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(project).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())));
        try { return new FileStream(Path.Combine(folder, key + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) { throw new IOException("Workspace đang được một phiên nhóm khác sử dụng hoặc chưa mở được khóa ghi. Đóng phiên đó trước khi tiếp tục.", e); }
    }
    private static readonly HashSet<string> Generated = new(StringComparer.OrdinalIgnoreCase) {
        ".git", "bin", "obj", "node_modules", ".build", "app-bin", "release", "dist", "build", ".vs", ".idea", ".next", "coverage", "__pycache__", ".venv", "venv" };

    public static string Fingerprint(string project, CancellationToken token)
    {
        string root = Path.GetFullPath(project);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0; long bytes = 0;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out string? folder)) {
            token.ThrowIfCancellationRequested();
            foreach (string entry in Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.Ordinal)) {
                token.ThrowIfCancellationRequested(); var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0 && Generated.Contains(Path.GetFileName(entry))) continue;
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Workspace có liên kết filesystem; chưa thể xác minh fingerprint an toàn.");
                if ((attributes & FileAttributes.Directory) != 0) { pending.Push(entry); continue; }
                var info = new FileInfo(entry);
                if (++count > 12000 || (bytes += info.Length) > 256L * 1024 * 1024) throw new IOException("Workspace vượt giới hạn fingerprint (12.000 file / 256 MB ngoài thư mục sinh tự động).");
                hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, entry).Replace('\\', '/') + "\0"));
                using var stream = File.OpenRead(entry); byte[] buffer = new byte[65536]; int length;
                while ((length = stream.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); hash.AppendData(buffer.AsSpan(0, length)); }
                hash.AppendData([0]);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static string ReadFiles(string project, IEnumerable<string> files)
    {
        var result = new StringBuilder();
        foreach (string file in files.Take(12)) {
            string name = Path.GetFileName(file);
            if (name.StartsWith(".env", StringComparison.OrdinalIgnoreCase) || name.Contains("secret", StringComparison.OrdinalIgnoreCase) || name.Contains("credential", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".key", StringComparison.OrdinalIgnoreCase)) continue;
            try {
                string text = ProjectContext.Read(project, Path.Combine(project, file));
                result.AppendLine(text[..Math.Min(6000, text.Length)]);
                if (result.Length > 24000) break;
            } catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { result.AppendLine($"[Không đọc được file được khai báo: {file}]"); }
        }
        return result.ToString();
    }

    public static async Task RunCheck(VerificationCommand command, string project, VerificationEvidence evidence, Action<Action> change, CancellationToken token)
    {
        command.Validate();
        string executable = File.Exists(command.Executable) ? Path.GetFullPath(command.Executable) : ProcessTransport.OnPath(command.Executable) ?? throw new FileNotFoundException("Chưa tìm thấy executable kiểm tra: " + command.Executable);
        var info = new ProcessStartInfo(executable) { WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (string argument in command.Arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(command.TimeoutSeconds));
        token.ThrowIfCancellationRequested(); process.Start();
        void Kill() {
            try { if (!process.HasExited) process.Kill(true); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException) {
                try { if (!process.HasExited) process.Kill(); } catch (Exception fallback) when (fallback is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        using var registration = timeout.Token.Register(Kill);
        async Task Pump(StreamReader reader) {
            char[] buffer = new char[4096]; int n;
            while ((n = await reader.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false)) > 0) {
                string part = new(buffer, 0, n);
                change(() => { string log = evidence.Log + part; evidence.Log = log.Length > 64000 ? log[^64000..] : log; });
            }
        }
        try {
            await Task.WhenAll(Pump(process.StandardOutput), Pump(process.StandardError), process.WaitForExitAsync(timeout.Token)).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            change(() => { evidence.ExitCode = process.ExitCode; evidence.State = process.ExitCode == 0 ? "passed" : "failed"; });
        } catch (OperationCanceledException) {
            change(() => evidence.State = token.IsCancellationRequested ? "cancelled" : "timeout");
            token.ThrowIfCancellationRequested();
        } catch { Kill(); throw; }
    }
}
