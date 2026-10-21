using System.Security.Cryptography;
using System.Text.Json;

namespace MultiAgentHub.Core;

/// <summary>Aliases and explicit browser bindings only. Website credentials never enter Hub.</summary>
public sealed class GatewayAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Tài khoản web";
    public string BindingId { get; set; } = "";
    public string Origin { get; set; } = "";
    public GatewayAccount Copy() => (GatewayAccount)MemberwiseClone();
    public void Validate()
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(Id, "^[a-zA-Z0-9-]{1,80}$") || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Name.Any(char.IsControl)
            || string.IsNullOrWhiteSpace(BindingId) || BindingId.Length > 128 || BindingId.Any(char.IsControl)) throw new ArgumentException("Liên kết tài khoản gateway không hợp lệ.");
        if (!Uri.TryCreate(Origin, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0
            || uri.GetLeftPart(UriPartial.Authority) != Origin || Origin.Any(char.IsControl)) throw new ArgumentException("Website gateway cần đúng HTTPS origin của tab đã liên kết.");
    }
}
public sealed class GatewaySettings
{
    public int Port { get; set; } = 20129;
    public List<GatewayAccount> Accounts { get; set; } = [];
}
public sealed class GatewayAccounts
{
    private readonly object gate = new();
    private readonly Storage storage;
    private readonly ISecretStore secrets;
    private GatewaySettings settings;
    public GatewayAccounts(Storage storage, ISecretStore secrets)
    {
        this.storage = storage; this.secrets = secrets;
        string path = Path.Combine(storage.DataDir, "gateway-accounts.json");
        settings = File.Exists(path) ? JsonSerializer.Deserialize<GatewaySettings>(File.ReadAllText(path), Json.Options) ?? new() : new();
        if (settings.Accounts.Count > 64 || settings.Port is < 1024 or > 65535 || settings.Accounts.Select(a => a.Id).Distinct().Count() != settings.Accounts.Count) throw new InvalidDataException("Cấu hình gateway không hợp lệ.");
        foreach (var account in settings.Accounts) account.Validate();
    }
    public IReadOnlyList<GatewayAccount> List { get { lock (gate) return settings.Accounts.Select(a => a.Copy()).ToArray(); } }
    public int Port { get { lock (gate) return settings.Port; } }
    public GatewayAccount Get(string id) => List.SingleOrDefault(a => a.Id == id) ?? throw new ArgumentException("Tài khoản gateway chưa được liên kết.");
    public void Save(GatewayAccount account)
    {
        account.Validate();
        lock (gate) {
            if (settings.Accounts.Count >= 64 && settings.Accounts.All(a => a.Id != account.Id)) throw new ArgumentException("Tối đa 64 tài khoản gateway.");
            Persist(new() { Port = settings.Port, Accounts = settings.Accounts.Where(a => a.Id != account.Id).Append(account.Copy()).ToList() });
        }
    }
    public void Remove(string id) { lock (gate) Persist(new() { Port = settings.Port, Accounts = settings.Accounts.Where(a => a.Id != id).ToList() }); }
    public void SetPort(int port)
    {
        if (port is < 1024 or > 65535) throw new ArgumentException("Cổng gateway cần nằm trong 1024–65535.");
        lock (gate) Persist(new() { Port = port, Accounts = settings.Accounts });
    }
    private void Persist(GatewaySettings next) { storage.Write("gateway-accounts", next); settings = next; }
    public string LocalKey()
    {
        lock (gate) {
            string key = secrets.Read("gateway/local-key");
            if (key.Length > 0) return key;
            key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(); secrets.Write("gateway/local-key", key); return key;
        }
    }
}
