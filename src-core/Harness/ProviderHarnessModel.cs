using System.Text.Json;

namespace MultiAgentHub.Core;

public sealed class ProviderHarnessModel(ApiTransport api, ChatGptPlan? plan) : IHarnessModel
{
    public static bool Supported(string connection) => Catalog.IsTextApi(connection);
    public Task<HarnessTurn> Turn(AgentConfig agent, HarnessInput input, Action<string> text, CancellationToken token) =>
        agent.Connection == "chatgpt-plan" ? (plan ?? throw new InvalidOperationException("Chưa mở API ChatGPT.")).HarnessTurn(agent, input, text, token) : api.HarnessTurn(agent, input, text, token);
    internal static JsonElement Arguments(string text)
    {
        if (text.Length > 1100000) throw new InvalidDataException("Đối số công cụ vượt giới hạn.");
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Đối số công cụ cần object JSON.");
        return doc.RootElement.Clone();
    }
    internal static HarnessTurn Responses(string text, JsonElement output)
    {
        var calls = output.EnumerateArray().Where(i => i.Str("type") == "function_call").Select(i => new HarnessCall(i.Str("call_id"), i.Str("name"), Arguments(i.Str("arguments")))).ToArray();
        if (output.EnumerateArray().Any(i => i.Str("type").EndsWith("_call", StringComparison.Ordinal) && i.Str("type") != "function_call")) throw new InvalidDataException("API yêu cầu loại công cụ chưa được cấp; không tự thực thi.");
        return new(text, calls, output.Clone());
    }
}
