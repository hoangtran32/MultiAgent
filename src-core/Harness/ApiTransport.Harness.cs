using System.Text;
using System.Text.Json;

namespace MultiAgentHub.Core;

public sealed partial class ApiTransport
{
    public async Task<HarnessTurn> HarnessTurn(AgentConfig agent, HarnessInput input, Action<string> text, CancellationToken token)
    {
        Validate(agent); ChatAttachments.ValidateFor(agent, input.Files);
        object payload; string route;
        if (agent.Connection == "anthropic-api") {
            var messages = new List<object> { new { role = "user", content = ChatAttachments.ApiParts(agent.Connection, input.Prompt, input.Files) } };
            foreach (var exchange in input.History) {
                messages.Add(new { role = "assistant", content = exchange.Turn.Native });
                messages.Add(new { role = "user", content = exchange.Results.Select(r => new { type = "tool_result", tool_use_id = r.CallId, content = r.Output, is_error = r.Error }).ToArray() });
            }
            route = "messages"; payload = new { model = agent.Model, max_tokens = 8192, system = input.Instructions, messages,
                tools = input.Tools.Select(t => new { name = t.Name, description = t.Description, input_schema = t.Parameters }).ToArray() };
        } else if (agent.Connection == "gemini-api") {
            var contents = new List<object> { new { role = "user", parts = ChatAttachments.ApiParts(agent.Connection, input.Prompt, input.Files) } };
            foreach (var exchange in input.History) {
                contents.Add(exchange.Turn.Native);
                contents.Add(new { role = "user", parts = exchange.Results.Select(r => {
                    var response = new Dictionary<string, object> { ["name"] = r.Name, ["response"] = new { result = r.Output, error = r.Error } };
                    if (!r.CallId.StartsWith("hub_", StringComparison.Ordinal)) response["id"] = r.CallId;
                    return new { functionResponse = response };
                }).ToArray() });
            }
            route = "models/" + Uri.EscapeDataString(agent.Model.Replace("models/", "")) + ":generateContent";
            payload = new { systemInstruction = new { parts = new[] { new { text = input.Instructions } } }, contents,
                tools = new[] { new { functionDeclarations = input.Tools.Select(t => new { name = t.Name, description = t.Description,
                    parameters = new { type = "object", properties = t.Parameters.GetProperty("properties"), required = t.Parameters.GetProperty("required") } }).ToArray() } } };
        } else {
            bool media = input.Files.Any(f => f.Image || f.Pdf);
            var messages = new List<object> { new { role = "system", content = input.Instructions }, new { role = "user", content = media ? (object)ChatAttachments.ApiParts(agent.Connection, input.Prompt, input.Files) : input.Prompt } };
            foreach (var exchange in input.History) {
                messages.Add(exchange.Turn.Native);
                messages.AddRange(exchange.Results.Select(r => (object)new { role = "tool", tool_call_id = r.CallId, content = r.Output }));
            }
            route = "chat/completions"; payload = new { model = agent.Model, messages, stream = false,
                tools = input.Tools.Select(t => new { type = "function", function = new { name = t.Name, description = t.Description, parameters = t.Parameters } }).ToArray() };
        }
        using var request = Request(agent, HttpMethod.Post, route); request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var doc = await Send(agent, request, token);
        var turn = ParseHarness(agent.Connection, doc.RootElement); if (turn.Text.Length > 0) text(turn.Text); return turn;
    }
    internal static HarnessTurn ParseHarness(string connection, JsonElement root)
    {
        try {
            var calls = new List<HarnessCall>(); string text; JsonElement native;
            if (connection == "anthropic-api") {
                if (root.Str("stop_reason") is not ("end_turn" or "tool_use" or "stop_sequence")) throw new InvalidDataException("Anthropic chưa kết thúc lượt thành công; không thực thi công cụ từ đầu ra chưa đủ.");
                native = root.GetProperty("content").Clone(); text = string.Join("\n", native.EnumerateArray().Where(p => p.Str("type") == "text").Select(p => p.Str("text")));
                foreach (var part in native.EnumerateArray().Where(p => p.Str("type") == "tool_use")) calls.Add(new(part.Str("id"), part.Str("name"), part.GetProperty("input").Clone()));
                if ((calls.Count > 0) != (root.Str("stop_reason") == "tool_use")) throw new InvalidDataException("Anthropic trả trạng thái tool_use không khớp nội dung.");
            } else if (connection == "gemini-api") {
                var candidate = root.GetProperty("candidates")[0];
                if (candidate.Str("finishReason") != "STOP") throw new InvalidDataException("Gemini chưa kết thúc lượt thành công; nội dung/công cụ chưa được thực thi.");
                native = candidate.GetProperty("content").Clone(); var parts = native.GetProperty("parts").EnumerateArray().ToArray();
                text = string.Join("\n", parts.Where(p => !p.TryGetProperty("thought", out var thought) || thought.ValueKind != JsonValueKind.True).Select(p => p.Str("text")).Where(s => s.Length > 0));
                foreach (var part in parts) if (part.TryGetProperty("functionCall", out var call)) calls.Add(new(call.Str("id").Length > 0 ? call.Str("id") : "hub_" + Guid.NewGuid().ToString("N"), call.Str("name"), call.TryGetProperty("args", out var args) ? args.Clone() : JsonSerializer.SerializeToElement(new { })));
            } else {
                var choice = root.GetProperty("choices")[0];
                if (choice.Str("finish_reason") is not ("stop" or "tool_calls")) throw new InvalidDataException("API chưa kết thúc lượt thành công; không chạy công cụ từ đầu ra bị cắt.");
                native = choice.GetProperty("message").Clone(); text = native.Str("content");
                if (native.TryGetProperty("tool_calls", out var items)) foreach (var call in items.EnumerateArray()) {
                    if (call.Str("type") != "function") throw new InvalidDataException("Loại công cụ API chưa hỗ trợ.");
                    var function = call.GetProperty("function"); calls.Add(new(call.Str("id"), function.Str("name"), ProviderHarnessModel.Arguments(function.Str("arguments"))));
                }
                if ((calls.Count > 0) != (choice.Str("finish_reason") == "tool_calls")) throw new InvalidDataException("API trả trạng thái tool_calls không khớp nội dung.");
            }
            return new(text, calls, native);
        } catch (Exception error) when (error is KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException) { throw new InvalidDataException("API không trả tool-call/câu trả lời hợp lệ cho harness."); }
    }
}
