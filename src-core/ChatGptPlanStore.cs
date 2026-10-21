using System.Security.Cryptography;
using System.Text.Json;

namespace MultiAgentHub.Core;

public sealed record ChatGptPlanAccount(string Id, string Label, string ClientId, bool SignedIn, bool PlanEnabled);

// The complete rotating token set is encrypted for this Windows user, then replaced atomically.
// This store is independent of Codex, browser profiles, history and config exports.
public sealed class ChatGptPlanStore
{
    private readonly string directory;
    internal string HostId { get; }
    internal sealed class Session
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Label { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string Subject { get; set; } = "";
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public string IdToken { get; set; } = "";
        public string[] Scopes { get; set; } = [];
        public DateTimeOffset ExpiresAt { get; set; }
        public bool PlanEnabled => Scopes.Contains("chatgpt.tokens.use.direct");
        public ChatGptPlanAccount Info => new(Id, Label, ClientId, AccessToken.Length > 0, PlanEnabled);
    }
    public ChatGptPlanStore(string dataDirectory)
    {
        directory = Path.Combine(dataDirectory, "chatgpt-plan");
        Directory.CreateDirectory(directory);
        string hostFile = Path.Combine(directory, "host-id");
        // Keep one host identity even if another instance starts at the same time.
        try { using var file = new FileStream(hostFile, FileMode.CreateNew, FileAccess.Write, FileShare.None); using var writer = new StreamWriter(file); writer.Write("urn:uuid:" + Guid.NewGuid()); }
        catch (IOException) when (File.Exists(hostFile)) { }
        HostId = File.ReadAllText(hostFile).Trim();
        if (!HostId.StartsWith("urn:uuid:") || !Guid.TryParse(HostId[9..], out _)) throw new InvalidDataException("Host ID ChatGPT không hợp lệ.");
    }
    public IReadOnlyList<ChatGptPlanAccount> Accounts => Directory.EnumerateFiles(directory, "*.protected").Select(p => Load(Path.GetFileNameWithoutExtension(p)).Info).OrderBy(a => a.Label).ToArray();
    private string FileName(string id) => Guid.TryParseExact(id, "N", out _) ? Path.Combine(directory, id + ".protected") : throw new ArgumentException("Tài khoản ChatGPT không hợp lệ.");
    internal Session Load(string id)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Đăng nhập ChatGPT dùng kho bảo vệ Windows.");
        byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(FileName(id)), null, DataProtectionScope.CurrentUser);
        try { var session = JsonSerializer.Deserialize<Session>(plain, Json.Options) ?? throw new InvalidDataException("Không đọc được phiên ChatGPT."); if (session.Id != id) throw new InvalidDataException("Sai tài khoản ChatGPT."); return session; }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    internal void Save(Session session)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(session, Json.Options);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        string path = FileName(session.Id), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, encrypted); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal void ClearTokens(Session session)
    {
        session.AccessToken = session.RefreshToken = session.IdToken = ""; session.Scopes = []; session.ExpiresAt = default; Save(session);
    }
    // Cross-process exclusion prevents reuse of rotating refresh tokens and sign-out races.
    internal async Task<FileStream> Lock(string id, CancellationToken token)
    {
        string path = FileName(id) + ".lock";
        while (true) { token.ThrowIfCancellationRequested(); try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); } catch (IOException) { await Task.Delay(150, token); } }
    }
}
