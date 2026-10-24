using System.Text;
using System.Text.Json;
using System.Diagnostics;

namespace MultiAgentHub.Core;

public static class ChatGptPlanStream
{
    public static async Task<string> Read(TextReader reader, Action<string> text, CancellationToken token)
    {
        var turn = await ReadTurn(reader, text, token);
        if (string.IsNullOrWhiteSpace(turn.Text)) throw new InvalidDataException("ChatGPT hoàn tất nhưng không có câu trả lời văn bản.");
        return turn.Text;
    }
    public static async Task<(string Text, JsonElement Output)> ReadTurn(TextReader reader, Action<string> text, CancellationToken token)
    {
        var data = new StringBuilder(); var output = new StringBuilder(); bool completed = false;
        JsonElement native = JsonSerializer.SerializeToElement(Array.Empty<object>());
        long lastFlush = 0; int sentLength = 0;
        void Emit(bool force = false) {
            if (output.Length != sentLength && (force || lastFlush == 0 || Stopwatch.GetElapsedTime(lastFlush).TotalMilliseconds >= 100)) {
                text(output.ToString()); sentLength = output.Length; lastFlush = Stopwatch.GetTimestamp();
            }
        }
        // Read character blocks rather than unbounded ReadLineAsync on untrusted SSE.
        var line = new StringBuilder(); var buffer = new char[4096]; int count; long received = 0;
        void Dispatch()
        {
            if (data.Length == 0) return;
            string json = data.ToString().TrimEnd('\n'); data.Clear();
            if (json == "[DONE]") return; // A transport terminator is not proof of completed inference.
            using var doc = JsonDocument.Parse(json); var item = doc.RootElement; string type = item.Str("type");
            if (type == "response.output_text.delta") {
                string delta = item.Str("delta"); if (output.Length + delta.Length > 1000000) throw new InvalidDataException("Câu trả lời ChatGPT vượt dung lượng của Hub."); output.Append(delta); Emit();
            }
            else if (type is "response.failed" or "error") {
                var error = item.TryGetProperty("response", out var response) && response.TryGetProperty("error", out var nested) ? nested : item.TryGetProperty("error", out var direct) ? direct : item;
                string code = error.Str("code"); if (code.Length == 0 || code.Length > 100 || code.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')) code = "stream_error";
                throw new ChatGptPlanException(code);
            }
            else if (type == "response.incomplete") throw new InvalidDataException("ChatGPT dừng với response.incomplete. Nội dung đã nhận được giữ lại; lượt chưa hoàn tất.");
            else if (type == "response.completed") {
                if (item.TryGetProperty("response", out var response) && response.TryGetProperty("output", out var items)) {
                    if (items.ValueKind != JsonValueKind.Array) throw new InvalidDataException("ChatGPT output không hợp lệ.");
                    native = items.Clone();
                    if (output.Length == 0) foreach (var message in items.EnumerateArray().Where(m => m.Str("type") == "message"))
                        if (message.TryGetProperty("content", out var content)) foreach (var part in content.EnumerateArray()) if (part.Str("type") == "output_text") output.Append(part.Str("text"));
                }
                completed = true;
            }
        }
        void Line()
        {
            string value = line.ToString().TrimEnd('\r'); line.Clear();
            if (value.Length == 0) Dispatch();
            else if (value.StartsWith("data:", StringComparison.Ordinal)) { data.Append(value.AsSpan(5).TrimStart(' ')); data.Append('\n'); if (data.Length > 2 * 1024 * 1024) throw new InvalidDataException("Sự kiện SSE ChatGPT quá lớn."); }
        }
        try {
            while (!completed && (count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0) {
                received += count; if (received > 16 * 1024 * 1024) throw new InvalidDataException("Luồng ChatGPT vượt dung lượng của Hub.");
                for (int index = 0; index < count && !completed; index++) { char c = buffer[index]; if (c == '\n') Line(); else { line.Append(c); if (line.Length > 2 * 1024 * 1024) throw new InvalidDataException("Dòng SSE ChatGPT quá lớn."); } }
            }
            if (!completed) { if (line.Length > 0) Line(); Dispatch(); }
        }
        finally { Emit(true); }
        if (!completed) throw new InvalidDataException("Luồng ChatGPT bị ngắt trước response.completed. Nội dung đã nhận được giữ lại; không tự gửi lại prompt.");
        return (output.ToString(), native);
    }
}
