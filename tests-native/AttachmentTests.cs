using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MultiAgentHub.Core;

internal static class AttachmentTests
{
    private static void Assert(bool value) { if (!value) throw new Exception("Attachment assertion failed."); }
    private static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidDataException) { return; } throw new Exception("Expected attachment rejection."); }
    private static byte[] Pdf()
    {
        using var data = new MemoryStream(); var offsets = new List<long> { 0 };
        void Write(string text) { byte[] bytes = Encoding.ASCII.GetBytes(text); data.Write(bytes); }
        Write("%PDF-1.4\n"); string content = "BT /F1 12 Tf 40 100 Td (Attached report) Tj ET";
        string[] objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"];
        for (int i = 0; i < objects.Length; i++) { offsets.Add(data.Position); Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        long xref = data.Position; Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n"); foreach (long offset in offsets.Skip(1)) Write($"{offset:0000000000} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"); return data.ToArray();
    }
    public static async Task Run(Func<string, Func<Task>, Task> test, string root)
    {
        await test("PDF and DOCX attachments extract text while preserving immutable original bytes", () => {
            var pdf = ChatAttachments.FromBytes("report.pdf", Pdf()); Assert(pdf.Text.Contains("Attached report") && pdf.Pdf && pdf.Info.Size == pdf.Data.Length);
            using var data = new MemoryStream();
            using (var zip = new ZipArchive(data, ZipArchiveMode.Create, true)) {
                using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
                writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Xin chào Việt Nam</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Table cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>");
            }
            var word = ChatAttachments.FromBytes("notes.docx", data.ToArray()); Assert(word.Text.Contains("Xin chào Việt Nam") && word.Text.Contains("Table cell"));
            return Task.CompletedTask;
        });
        await test("attachment snapshots reopen after source removal and detect tampering without storing binary data in history", () => {
            var store = new Storage(Path.Combine(root, "attachment-store")); var file = ChatAttachments.FromBytes("source.txt", Encoding.UTF8.GetBytes("Original private contents"));
            store.SaveAttachments([file]); var reopened = store.LoadAttachments([file.Info]).Single(); Assert(reopened.Text == file.Text && reopened.Data.SequenceEqual(file.Data));
            var agent = new AgentConfig { RuntimeAttachments = [file] }; Assert(agent.Copy().RuntimeAttachments.Count == 1 && !JsonSerializer.Serialize(agent, Json.Options).Contains("Original private contents"));
            string json = JsonSerializer.Serialize(new JobRecord { Attachments = [file.Info] }, Json.Options); Assert(!json.Contains("Original private contents"));
            File.WriteAllText(Path.Combine(store.DataDir, "attachments", file.Info.Id + ".bin"), "Tampered contents"); Reject(() => store.LoadAttachments([file.Info])); return Task.CompletedTask;
        });
        await test("native image and PDF payloads use real data parts and unsupported peers fail before any inference", () => {
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jB9kAAAAASUVORK5CYII=");
            var image = ChatAttachments.FromBytes("image.png", png); var pdf = ChatAttachments.FromBytes("report.pdf", Pdf());
            using var payload = JsonDocument.Parse(JsonSerializer.Serialize(ChatAttachments.ResponsesInput("Read these files", [image, pdf])));
            var parts = payload.RootElement[0].GetProperty("content").EnumerateArray().ToArray();
            Assert(parts.Single(p => p.Str("type") == "input_image").Str("image_url") == image.DataUrl);
            Assert(parts.Single(p => p.Str("type") == "input_file").Str("file_data") == pdf.DataUrl);
            foreach (string connection in new[] { "chat-api", "gemini-api", "anthropic-api" }) {
                using var body = JsonDocument.Parse(JsonSerializer.Serialize(ChatAttachments.ApiParts(connection, "Read attachments", [image, pdf])));
                Assert(body.RootElement.GetArrayLength() == 5 && body.RootElement.GetRawText().Contains(Convert.ToBase64String(png)));
            }
            var fixture = new Executor(); var runner = new JobRunner(fixture, new Storage(Path.Combine(root, "attachment-preflight")));
            var config = new HubConfig { Surface = "chat", Agents = [new() { Provider = "chatgpt-plan", Connection = "chatgpt-plan" }, new() { Provider = "custom", Connection = "custom-cli" }] };
            Reject(() => runner.Start(config, "Read this image", attachments: [image])); Assert(!runner.Busy && fixture.Calls == 0); return Task.CompletedTask;
        });
        await test("attachment limits invalid images binary files and legacy Word are rejected before sending", () => {
            Reject(() => ChatAttachments.FromBytes("wrong.png", Encoding.UTF8.GetBytes("not an image")));
            Reject(() => ChatAttachments.FromBytes("old.doc", [1,2,3])); Reject(() => ChatAttachments.FromBytes("binary.exe", [0,1,2]));
            var file = ChatAttachments.FromBytes("file.txt", Encoding.UTF8.GetBytes("File body")); Reject(() => ChatAttachments.ValidateSet(Enumerable.Repeat(file, 11).ToArray()));
            Reject(() => ChatAttachments.FromBytes("large.txt", new byte[ChatAttachments.MaxFileBytes + 1]));
            Assert(ChatAttachments.WithText("User question", new AgentConfig { RuntimeAttachments = [file] }).Contains("File body")); return Task.CompletedTask;
        });
        await test("completion audit rejects optional invented tasks and gaps absent from direct user scope", () => {
            const string scope = "Fix the requested login error.";
            TeamProtocol.ValidateCompletion(new() { Message = "Done", Finished = true }, scope);
            TeamProtocol.ValidateCompletion(new() { Message = "Missing", ScopeGaps = [new("login error", "The original error still reproduces.")] }, scope);
            Reject(() => TeamProtocol.ValidateCompletion(new() { Message = "More", ScopeGaps = [new("Improve every feature", "Optional optimization")] }, scope));
            Reject(() => TeamProtocol.ValidateCompletion(new() { Message = "More" }, scope));
            Reject(() => TeamProtocol.ValidateCompletion(new() { Message = "Done", Finished = true, Tasks = [new() { Id = "new-task" }] }, scope)); return Task.CompletedTask;
        });
    }
    private sealed class Executor : IAgentExecutor
    {
        public int Calls;
        public Task<string> Execute(AgentConfig agent, string project, string prompt, Action<string> text, Action<string> log, CancellationToken token) { Calls++; return Task.FromResult("unused"); }
    }
}
