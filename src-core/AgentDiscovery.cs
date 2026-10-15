namespace MultiAgentHub.Core;

public sealed record ModelInventory(IReadOnlyList<string> Models, string Source, string Detail);
public sealed record DiscoveredAgent(AgentConfig Agent, string Executable, ModelInventory Inventory, bool Existing);

/// <summary>Discovery returns choices; it never creates/enables agents or sends inference.</summary>
public static class AgentDiscovery
{
    public static async Task<ModelInventory> Models(AgentConfig agent, ISecretStore secrets, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Catalog.IsApi(agent.Connection))
            return new(await new ApiTransport(secrets).Models(agent, token), "API /models", "Danh sách do endpoint đã cấu hình trả về; chưa kiểm tra quota suy luận.");
        if (agent.Connection == "agy-cli")
            return new(await AntigravityModels.Load(agent, token), "Antigravity CLI: agy models", "Danh sách do CLI trả về; chưa kiểm tra quota suy luận/quyền công cụ.");
        if (agent.Connection is "codex-cli" or "claude-cli") {
            var models = await Task.Run(() => LocalModels.Read(agent), token);
            return new(models, agent.Connection == "codex-cli" ? "Cache model Codex trên máy" : "Cấu hình Claude trên máy",
                models.Count > 0 ? "Model đọc từ dữ liệu cục bộ, không xác nhận quyền truy cập/quota hiện tại." : "Công cụ chưa cung cấp danh sách model cục bộ. Để trống để dùng model mặc định của CLI, hoặc nhập model ID của bạn.");
        }
        return new([], "Cấu hình thủ công", "Kết nối này chưa có giao thức liệt kê model; chọn trong ứng dụng ngoài hoặc nhập model ID.");
    }

    public static async Task<List<DiscoveredAgent>> Scan(IEnumerable<AgentConfig> configured, ISecretStore secrets, CancellationToken token)
    {
        var existing = configured.Select(a => a.Copy()).ToList();
        var choices = new List<(AgentConfig Agent, string Executable, bool Existing)>();
        // Catalog describes adapter protocols, not a prepopulated personal agent list.
        foreach (var provider in Catalog.Providers) foreach (var connection in Catalog.Connections(provider.Id)) {
            if (connection.Id is not ("codex-cli" or "claude-cli" or "agy-cli")) continue;
            token.ThrowIfCancellationRequested();
            var seed = new AgentConfig { Provider = provider.Id, Connection = connection.Id, Name = Catalog.ProviderName(provider.Id), Enabled = false,
                Permission = connection.Id == "agy-cli" ? "default" : "read-only" };
            string? binary = TryFind(seed); if (binary == null) continue;
            var match = existing.FirstOrDefault(a => a.Connection == connection.Id && string.Equals(TryFind(a), binary, StringComparison.OrdinalIgnoreCase));
            var selected = match?.Copy() ?? seed; selected.Command = binary;
            choices.Add((selected, binary, match != null));
        }
        foreach (var agent in existing.Where(a => !Catalog.IsManual(a.Connection))) {
            string? binary = Catalog.IsApi(agent.Connection) ? "" : TryFind(agent);
            if (binary != null && (Catalog.IsApi(agent.Connection) || !choices.Any(c => c.Agent.Connection == agent.Connection && string.Equals(c.Executable, binary, StringComparison.OrdinalIgnoreCase))))
                choices.Add((agent, binary, true));
        }
        using var limit = new SemaphoreSlim(3);
        var results = await Task.WhenAll(choices.Select(async choice => {
            await limit.WaitAsync(token);
            try {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                ModelInventory inventory;
                try { inventory = await Models(choice.Agent, secrets, timeout.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { inventory = new([], "Chưa lấy được model", "Dò model quá 30 giây. Có thể cấu hình công cụ rồi dò lại."); }
                catch (Exception e) when (e is not OperationCanceledException) { inventory = new([], "Chưa lấy được model", e.Message); }
                return new DiscoveredAgent(choice.Agent, choice.Executable, inventory, choice.Existing);
            } finally { limit.Release(); }
        }));
        return results.OrderBy(r => r.Agent.Name).ToList();
    }

    public static string? FindExecutable(AgentConfig agent) => TryFind(agent);
    private static string? TryFind(AgentConfig agent)
    {
        try { return ProcessTransport.Find(agent); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }
}
