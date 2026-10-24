using System.Text;
using System.Text.Json;

namespace MultiAgentHub.Core;

public sealed partial class ChatGptPlan
{
    public async Task<HarnessTurn> HarnessTurn(AgentConfig agent, HarnessInput input, Action<string> text, CancellationToken token)
    {
        Validate(agent); string access = await AccessToken(agent.PlanAccountId, token);
        var items = new List<object>();
        foreach (var item in JsonSerializer.SerializeToElement(ChatAttachments.ResponsesInput(input.Prompt, input.Files)).EnumerateArray()) items.Add(item.Clone());
        foreach (var exchange in input.History) {
            foreach (var item in exchange.Turn.Native.EnumerateArray()) items.Add(item.Clone());
            items.AddRange(exchange.Results.Select(r => (object)new { type = "function_call_output", call_id = r.CallId, output = r.Output }));
        }
        using var request = Authorized(HttpMethod.Post, "/responses", access);
        request.Content = new StringContent(JsonSerializer.Serialize(new { model = agent.Model, instructions = input.Instructions, input = items, store = false, stream = true,
            include = new[] { "reasoning.encrypted_content" }, tools = input.Tools.Select(t => new { type = "function", name = t.Name, description = t.Description, parameters = t.Parameters, strict = false }).ToArray() }), Encoding.UTF8, "application/json");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) throw ChatGptPlanException.From(response, await ReadBounded(response.Content, token));
        if (response.Content.Headers.ContentType?.MediaType != "text/event-stream") throw new InvalidDataException("ChatGPT API không trả SSE.");
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
        var output = await ChatGptPlanStream.ReadTurn(reader, text, token);
        return ProviderHarnessModel.Responses(output.Text, output.Output);
    }
}
