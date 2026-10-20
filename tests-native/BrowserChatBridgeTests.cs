using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using MultiAgentHub.Core;

internal static class BrowserChatBridgeTests
{
    private static void Assert(bool value) { if (!value) throw new Exception("Browser chat framing assertion failed."); }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException) { return; }
        throw new Exception("Invalid browser frame was accepted.");
    }
    public static async Task Run(Func<string, Func<Task>, Task> test, string root)
    {
        await test("browser native frames round-trip fragmented Unicode messages with exact little-endian lengths", async () => {
            byte[] first = BrowserChatProtocol.Serialize(new { type = "request", prompt = "Nghiên cứu chủ đề: nhân quả 🌏", id = "first" });
            byte[] second = BrowserChatProtocol.Serialize(new { type = "result", text = "Kết quả tiếng Việt", id = "second" });
            using var stream = new MemoryStream();
            await BrowserChatProtocol.WriteFrame(stream, first); await BrowserChatProtocol.WriteFrame(stream, second);
            Assert(BinaryPrimitives.ReadUInt32LittleEndian(stream.ToArray()) == first.Length);
            using var chunks = new FragmentedStream(stream.ToArray());
            var restored = await BrowserChatProtocol.ReadFrame(chunks); Assert(restored!.SequenceEqual(first));
            using var json = BrowserChatProtocol.Parse(restored); Assert(json.RootElement.GetProperty("prompt").GetString()!.Contains("nhân quả 🌏"));
            Assert((await BrowserChatProtocol.ReadFrame(chunks))!.SequenceEqual(second) && await BrowserChatProtocol.ReadFrame(chunks) == null);
        });
        await test("browser frames reject empty oversized and truncated payloads before allocation or partial acceptance", async () => {
            using var empty = new MemoryStream(); Assert(await BrowserChatProtocol.ReadFrame(empty) == null);
            foreach (uint length in new uint[] { 0, BrowserChatProtocol.MaxFrameBytes + 1, uint.MaxValue }) {
                var header = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(header, length);
                using var bad = new MemoryStream(header); await Reject(async () => { await BrowserChatProtocol.ReadFrame(bad); });
            }
            using var shortHeader = new MemoryStream([1, 0]); await Reject(async () => { await BrowserChatProtocol.ReadFrame(shortHeader); });
            using var shortBody = new MemoryStream([3, 0, 0, 0, 123]); await Reject(async () => { await BrowserChatProtocol.ReadFrame(shortBody); });
            await Reject(() => BrowserChatProtocol.WriteFrame(empty, Array.Empty<byte>()));
            await Reject(() => BrowserChatProtocol.WriteFrame(empty, new byte[BrowserChatProtocol.MaxFrameBytes + 1]));
        });
        await test("native host origin matches only the bundled extension and malformed UTF-8 cannot become a prompt", async () => {
            Assert(BrowserChatProtocol.IsExtensionOrigin($"chrome-extension://{BrowserChatProtocol.ExtensionId}/"));
            foreach (string origin in new[] { "", "chrome-extension://other/", $"chrome-extension://{BrowserChatProtocol.ExtensionId}.evil/", $"chrome-extension://{BrowserChatProtocol.ExtensionId}/path", $"https://{BrowserChatProtocol.ExtensionId}/" })
                Assert(!BrowserChatProtocol.IsExtensionOrigin(origin));
            await Reject(() => { using var _ = BrowserChatProtocol.Parse(new byte[] { 0xff }); return Task.CompletedTask; });
        });
        await test("browser bridge binds exact provider and model, correlates answers, cancels without replay and fails on disconnect", async () => {
            // CI-only IPC fixture: no browser, credentials, user app data, network or model inference.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var bridge = new BrowserChatBridge(); bridge.Start();
            using var client = new NamedPipeClientStream(".", BrowserChatProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(deadline.Token);
            string a = Guid.NewGuid().ToString("N"), b = Guid.NewGuid().ToString("N");
            Task Send(object message) => BrowserChatProtocol.WriteFrame(client, BrowserChatProtocol.Serialize(message), deadline.Token);
            async Task<System.Text.Json.JsonDocument> Read() => BrowserChatProtocol.Parse((await BrowserChatProtocol.ReadFrame(client, deadline.Token)) ?? throw new Exception("Fixture pipe closed unexpectedly."));
            async Task Until(Func<bool> condition) { while (!condition()) await Task.Delay(10, deadline.Token); }
            async Task Fails<T>(Func<Task> action) where T : Exception {
                try { await action(); } catch (T) { return; }
                throw new Exception("Expected browser request failure: " + typeof(T).Name);
            }
            using var hello = await Read();
            Assert(hello.RootElement.GetProperty("type").GetString() == "hello" && hello.RootElement.GetProperty("protocol").GetInt32() == 1);
            await Send(new { type = "state", bindings = new[] {
                new { id = a, provider = "gemini-web", tabId = 1, url = "https://gemini.google.com/app/fixture", title = "Gemini fixture", model = "Argon High", ready = true },
                new { id = b, provider = "chatgpt-web", tabId = 2, url = "https://chatgpt.com/c/fixture", title = "ChatGPT fixture", model = "Confirmed Chat Model", ready = true }
            } });
            await Until(() => bridge.Bindings.Count == 2);
            bridge.Validate("gemini-web", a, "ARGON HIGH");
            await Fails<InvalidOperationException>(() => { bridge.Validate("chatgpt-web", a, "Argon High"); return Task.CompletedTask; });
            await Fails<InvalidOperationException>(() => { bridge.Validate("gemini-web", a, "Other model"); return Task.CompletedTask; });
            await Fails<ArgumentException>(() => { bridge.Validate("gemini-web", "", "Argon High"); return Task.CompletedTask; });
            using (var otherProfile = new BrowserChatBridge()) {
                otherProfile.Start(); await Until(() => otherProfile.LastError.Length > 0);
                Assert(!otherProfile.IsConnected && otherProfile.Bindings.Count == 0 && bridge.IsConnected);
            }
            string observed = "";
            var answer = bridge.Execute("gemini-web", a, "Argon High", "Research a topic", value => observed = value, _ => { }, deadline.Token);
            using var request = await Read(); string id = request.RootElement.GetProperty("id").GetString()!;
            Assert(request.RootElement.GetProperty("bindingId").GetString() == a && request.RootElement.GetProperty("expectedModel").GetString() == "Argon High");
            await Fails<InvalidOperationException>(() => bridge.Execute("gemini-web", a, "Argon High", "Concurrent request", _ => { }, _ => { }, deadline.Token));
            await Send(new { type = "result", id = "unknown-request", text = "Must be ignored" });
            await Send(new { type = "delta", id, text = "Full partial response" });
            await Until(() => observed == "Full partial response"); Assert(!answer.IsCompleted);
            await Send(new { type = "result", id, text = "Final research answer" }); Assert(await answer == "Final research answer");
            using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            var cancelledAnswer = bridge.Execute("gemini-web", a, "Argon High", "Cancel this prompt", _ => { }, _ => { }, cancelled.Token);
            using var cancelledRequest = await Read(); string cancelledId = cancelledRequest.RootElement.GetProperty("id").GetString()!;
            cancelled.Cancel(); await Fails<OperationCanceledException>(() => cancelledAnswer);
            using var cancel = await Read(); Assert(cancel.RootElement.GetProperty("type").GetString() == "cancel" && cancel.RootElement.GetProperty("id").GetString() == cancelledId);
            var otherAnswer = bridge.Execute("chatgpt-web", b, "Confirmed Chat Model", "Respond to the research", _ => { }, _ => { }, deadline.Token);
            using var otherRequest = await Read(); string otherId = otherRequest.RootElement.GetProperty("id").GetString()!;
            Assert(otherRequest.RootElement.GetProperty("type").GetString() == "request" && otherRequest.RootElement.GetProperty("bindingId").GetString() == b);
            await Send(new { type = "result", id = cancelledId, text = "Late cancelled answer" });
            await Send(new { type = "result", id = otherId, text = "Peer answer" }); Assert(await otherAnswer == "Peer answer");
            var invalidAnswer = bridge.Execute("gemini-web", a, "Argon High", "Check invalid output handling", _ => { }, _ => { }, deadline.Token);
            using var invalidRequest = await Read(); string invalidId = invalidRequest.RootElement.GetProperty("id").GetString()!;
            await Send(new { type = "result", id = invalidId, text = new string('x', BrowserChatProtocol.MaxTextCharacters + 1) });
            await Fails<InvalidDataException>(() => invalidAnswer);
            using var invalidCancel = await Read(); Assert(invalidCancel.RootElement.GetProperty("type").GetString() == "cancel");
            Assert(bridge.IsConnected && bridge.Bindings.Count == 2);
            var disconnected = bridge.Execute("gemini-web", a, "Argon High", "Disconnect during this prompt", _ => { }, _ => { }, deadline.Token);
            using var disconnectedRequest = await Read(); client.Dispose();
            await Fails<IOException>(() => disconnected);
            await Until(() => bridge.Bindings.Count == 0);
        });
    }
    private sealed class FragmentedStream(byte[] contents) : MemoryStream(contents)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => base.ReadAsync(buffer[..Math.Min(2, buffer.Length)], token);
    }
}
