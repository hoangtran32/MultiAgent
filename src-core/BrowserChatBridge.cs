using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MultiAgentHub.Core;

/// <summary>Explicit browser tab bindings; requests are never retried or moved to another tab.</summary>
public sealed class BrowserChatBridge : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, Client> clients = [];
    private readonly Dictionary<string, Pending> pending = [];
    private NamedPipeServerStream? listening;
    private Task? accepting;
    private bool disposed;
    public event Action? Changed;
    public string LastError { get; private set; } = "";
    public bool IsConnected { get { lock (gate) return clients.Count > 0; } }
    public IReadOnlyList<BrowserChatBinding> Bindings { get { lock (gate) return clients.Values.SelectMany(c => c.Bindings).OrderBy(b => b.Provider).ThenBy(b => b.Title).ToArray(); } }

    public void Start()
    {
        lock (gate) {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (accepting == null || accepting.IsCompleted) accepting = Task.Run(OwnAndAccept);
        }
    }

    private void OwnAndAccept()
    {
        string user = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..16];
        using var ownership = new Mutex(false, @"Local\MultiAgentHub.BrowserChat.Owner.v1." + user);
        bool owned;
        try { owned = ownership.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        if (!owned) {
            lock (gate) if (!disposed) LastError = "Một cửa sổ Hub khác đang giữ kết nối chatbot trình duyệt. Đóng cửa sổ đó rồi mở lại hoặc kết nối lại ở Hub này.";
            Notify(); return;
        }
        // Mutex ownership stays on this worker thread across asynchronous pipe operations.
        try { AcceptClients().GetAwaiter().GetResult(); }
        finally { ownership.ReleaseMutex(); }
    }

    public void Validate(string connection, string bindingId, string expectedModel)
    {
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); Select(connection, bindingId, expectedModel); }
    }

    public async Task<string> Execute(string connection, string bindingId, string expectedModel, string prompt,
        Action<string> text, Action<string> log, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > BrowserChatProtocol.MaxPromptCharacters) throw new ArgumentException("Yêu cầu chatbot cần có 1–200.000 ký tự.");
        Pending request;
        lock (gate) {
            ObjectDisposedException.ThrowIf(disposed, this);
            var selected = Select(connection, bindingId, expectedModel);
            if (pending.Values.Any(p => p.BindingId == bindingId)) throw new InvalidOperationException("Tab chatbot này đang xử lý một yêu cầu khác. Chờ hoàn tất hoặc dừng yêu cầu đang chạy.");
            request = new(Guid.NewGuid().ToString("N"), selected.Binding.Provider, bindingId, selected.Binding.Model, selected.Client, text);
            pending.Add(request.Id, request);
        }
        try {
            // Complete frame writes use the bridge lifetime, so cancelling a request cannot corrupt the shared stream.
            _ = Dispatch(request, prompt);
            using var registration = token.Register(() => {
                if (!request.Answer.TrySetCanceled(token)) return;
                try { log("\nĐã yêu cầu dừng tab chatbot. Prompt đang gửi hoặc đã gửi có thể vẫn được dịch vụ xử lý.\n"); } catch { }
                _ = CancelRequest(request);
            });
            string answer = await request.Answer.Task;
            BrowserChatBinding? observed;
            lock (gate) observed = request.Client.Bindings.FirstOrDefault(b => b.Id == bindingId);
            if (observed != null) log($"\nChatbot: {observed.DisplaySite} · {observed.DisplayModel} · {observed.Transport}\n");
            return answer;
        }
        finally { lock (gate) pending.Remove(request.Id); }
    }

    private async Task Dispatch(Pending request, string prompt)
    {
        try {
            await request.Client.Send(new { type = "request", id = request.Id, bindingId = request.BindingId, prompt, expectedModel = request.ExpectedModel }, lifetime.Token);
            request.Sent.TrySetResult(true);
        }
        catch (Exception e) { request.Sent.TrySetResult(false); request.Answer.TrySetException(e); }
    }

    private (Client Client, BrowserChatBinding Binding) Select(string connection, string bindingId, string expectedModel)
    {
        if (!Catalog.IsBrowserChat(connection)) throw new ArgumentException("Kết nối chatbot trình duyệt không hợp lệ.");
        if (string.IsNullOrWhiteSpace(bindingId)) throw new ArgumentException("Chọn đúng tab chatbot đã liên kết trong thiết lập model.");
        if (expectedModel.Length > 120 || expectedModel.Any(char.IsControl)) throw new ArgumentException("Nhãn model không hợp lệ.");
        var found = clients.Values.SelectMany(c => c.Bindings.Select(b => (Client: c, Binding: b))).Where(item => item.Binding.Id == bindingId).ToArray();
        if (found.Length != 1) throw new InvalidOperationException(clients.Values.Any(c => c.Bindings.Length > 0)
            ? "Hub đã nhận tab từ trình duyệt, nhưng agent này còn gắn mã tab cũ. Chọn đúng tab đang kết nối trong Thiết lập / model; không cần đăng nhập lại."
            : "Hub chưa nhận danh sách tab. Mở extension và bấm Đồng bộ lại với Hub; nếu tab đã tải lại, liên kết lại tab đó. Không cần đăng nhập lại khi website vẫn giữ tài khoản.");
        var selected = found[0];
        if (connection != "browser-web" && selected.Binding.Provider != connection) throw new InvalidOperationException("Tab đã liên kết thuộc website khác. Chọn kết nối Chatbot từ extension để dùng tab này.");
        if (!selected.Binding.Ready) throw new InvalidOperationException("Tab chatbot chưa sẵn sàng. Kiểm tra đăng nhập, model và ô nhập trong tab đã chọn.");
        if (expectedModel.Length > 0 && !string.Equals(expectedModel.Trim(), selected.Binding.Model.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Model đã xác nhận trong tab là ‘{selected.Binding.Model}’, khác ‘{expectedModel}’. Chọn và xác nhận đúng model trong tiện ích.");
        return selected;
    }

    private async Task AcceptClients()
    {
        while (!lifetime.IsCancellationRequested) {
            NamedPipeServerStream? server = null;
            try {
                server = new(BrowserChatProtocol.PipeName, PipeDirection.InOut, 8, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                lock (gate) { if (disposed) { server.Dispose(); return; } listening = server; }
                await server.WaitForConnectionAsync(lifetime.Token);
                var client = new Client(server); server = null;
                lock (gate) { listening = null; if (disposed) { client.Close(); return; } clients.Add(client.Id, client); LastError = ""; }
                _ = ReadClient(client);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException or ObjectDisposedException) {
                server?.Dispose();
                lock (gate) { listening = null; if (!disposed) LastError = "Không mở được kết nối tiện ích trình duyệt: " + e.Message; }
                if (lifetime.IsCancellationRequested) return;
                Notify();
                try { await Task.Delay(1000, lifetime.Token); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task ReadClient(Client client)
    {
        string reason = "Tiện ích trình duyệt đã ngắt kết nối. Kết quả đang nhận được giữ lại; Hub không tự gửi lại prompt.";
        try {
            // Chrome's native port can exist before its process reaches Hub; acknowledge the actual pipe connection.
            await client.Send(new { type = "hello", protocol = 1 }, lifetime.Token);
            Notify();
            while (!lifetime.IsCancellationRequested) {
                var payload = await BrowserChatProtocol.ReadFrame(client.Stream, lifetime.Token);
                if (payload == null) break;
                using var document = BrowserChatProtocol.Parse(payload);
                Receive(client, document.RootElement);
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or OperationCanceledException) {
            if (!lifetime.IsCancellationRequested) reason = "Kết nối tiện ích trình duyệt bị gián đoạn: " + e.Message;
        }
        finally {
            lock (gate) {
                clients.Remove(client.Id);
                foreach (var request in pending.Values.Where(p => ReferenceEquals(p.Client, client))) request.Answer.TrySetException(new IOException(reason));
                if (!disposed) LastError = reason;
            }
            client.Close(); Notify();
        }
    }

    private void Receive(Client client, JsonElement frame)
    {
        string type = String(frame, "type", 32);
        if (type == "state") {
            if (!frame.TryGetProperty("bindings", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 128) throw new InvalidDataException("Danh sách tab chatbot không hợp lệ.");
            var bindings = items.EnumerateArray().Select(ReadBinding).ToArray();
            if (bindings.Select(b => b.Id).Distinct(StringComparer.Ordinal).Count() != bindings.Length) throw new InvalidDataException("Liên kết tab chatbot bị trùng ID.");
            lock (gate) {
                var otherIds = clients.Values.Where(c => c != client).SelectMany(c => c.Bindings).Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
                if (bindings.Any(b => otherIds.Contains(b.Id))) throw new InvalidDataException("Liên kết tab chatbot bị trùng giữa các trình duyệt.");
                client.Bindings = bindings;
                foreach (var request in pending.Values.Where(p => ReferenceEquals(p.Client, client))) {
                    var binding = bindings.FirstOrDefault(b => b.Id == request.BindingId);
                    if (binding == null) request.Answer.TrySetException(new IOException("Tab chatbot đã đóng hoặc bỏ liên kết; Hub không tự gửi lại prompt."));
                    else if (binding.Provider != request.Provider || request.ExpectedModel.Length > 0 && !string.Equals(request.ExpectedModel.Trim(), binding.Model.Trim(), StringComparison.OrdinalIgnoreCase)) {
                        if (request.Answer.TrySetException(new InvalidOperationException("Model xác nhận trong tab đã thay đổi giữa lượt. Dừng lượt này để tránh dùng nhầm model."))) _ = CancelRequest(request);
                    }
                }
            }
            Notify(); return;
        }
        if (type is not ("delta" or "result" or "error")) return;
        string id = frame.Str("id");
        if (id.Length is 0 or > 128) return;
        Pending? pendingRequest;
        lock (gate) pendingRequest = pending.GetValueOrDefault(id);
        if (pendingRequest == null || !ReferenceEquals(pendingRequest.Client, client) || pendingRequest.Answer.Task.IsCompleted) return;
        // An invalid individual response fails only that request, never other tabs sharing the pipe.
        try {
            if (type == "error") {
                string message = String(frame, "message", 2000);
                pendingRequest.Answer.TrySetException(Quota.Exhausted(message) ? new AgentQuotaException() : new InvalidOperationException(message)); return;
            }
            string content = String(frame, "text", BrowserChatProtocol.MaxTextCharacters, allowEmpty: type == "delta");
            pendingRequest.Text(content);
            if (type == "result") pendingRequest.Answer.TrySetResult(content);
        }
        catch (Exception e) {
            if (pendingRequest.Answer.TrySetException(e)) _ = CancelRequest(pendingRequest);
        }
    }

    private static BrowserChatBinding ReadBinding(JsonElement item)
    {
        string id = String(item, "id", 128), provider = String(item, "provider", 32), url = String(item, "url", 4096);
        string title = String(item, "title", 500, true), model = String(item, "model", 120, true);
        if (id.Any(char.IsControl) || model.Any(char.IsControl) || !item.TryGetProperty("tabId", out var tab) || !tab.TryGetInt32(out int tabId) || tabId < 0 ||
            !item.TryGetProperty("ready", out var ready) || ready.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("Thông tin tab chatbot không hợp lệ.");
        if (!Catalog.IsBrowserChat(provider) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || url.Any(char.IsControl)
            || provider == "gemini-web" && uri.Host != "gemini.google.com" || provider == "chatgpt-web" && uri.Host != "chatgpt.com")
            throw new InvalidDataException("Tab chatbot cần là trang HTTPS được người dùng liên kết từ extension.");
        string Optional(string name, int max, string fallback = "") => item.TryGetProperty(name, out _) ? String(item, name, max, true) : fallback;
        string detected = Optional("detectedModel", 120), source = Optional("modelSource", 16, "unknown"), transport = Optional("transport", 32, "dom"), site = Optional("siteName", 200, uri.Host);
        if (detected.Any(char.IsControl) || site.Any(char.IsControl) || source is not ("unknown" or "page" or "response")
            || transport is not ("dom" or "chat-completions" or "responses" or "messages" or "chatgpt-message" or "generate-content" or "gemini-rpc"))
            throw new InvalidDataException("Thông tin model/luồng website không hợp lệ.");
        return new(id, provider, tabId, url, title, model, ready.GetBoolean()) { SiteName = site, DetectedModel = detected, ModelSource = source, Transport = transport };
    }

    private static string String(JsonElement item, string name, int max, bool allowEmpty = false)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Thông điệp chatbot thiếu ‘" + name + "’.");
        string result = value.GetString()!;
        if (result.Length > max || !allowEmpty && string.IsNullOrWhiteSpace(result)) throw new InvalidDataException("Trường ‘" + name + "’ của chatbot rỗng hoặc vượt giới hạn.");
        return result;
    }

    private async Task CancelRequest(Pending request)
    {
        try { if (await request.Sent.Task) await request.Client.Send(new { type = "cancel", id = request.Id }, lifetime.Token); }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
    }
    private void Notify() { try { Changed?.Invoke(); } catch { /* A closed UI must not take down the pipe service. */ } }
    public void Dispose()
    {
        lock (gate) {
            if (disposed) return; disposed = true; lifetime.Cancel(); listening?.Dispose(); listening = null;
            foreach (var client in clients.Values) client.Close(); clients.Clear();
            foreach (var request in pending.Values) request.Answer.TrySetException(new ObjectDisposedException(nameof(BrowserChatBridge)));
            pending.Clear();
        }
        Notify();
    }

    private sealed class Client(NamedPipeServerStream stream)
    {
        private readonly SemaphoreSlim write = new(1, 1);
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public NamedPipeServerStream Stream { get; } = stream;
        public BrowserChatBinding[] Bindings { get; set; } = [];
        public async Task Send<T>(T message, CancellationToken token)
        {
            var payload = BrowserChatProtocol.Serialize(message);
            using var delivery = CancellationTokenSource.CreateLinkedTokenSource(token);
            delivery.CancelAfter(TimeSpan.FromSeconds(15));
            bool acquired = false;
            try {
                await write.WaitAsync(delivery.Token); acquired = true;
                await BrowserChatProtocol.WriteFrame(Stream, payload, delivery.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                // This bounds only local frame delivery, never the chatbot's reasoning or answer duration.
                Close(); throw new IOException("Tiện ích trình duyệt không nhận khung kết nối trong 15 giây; Hub không tự gửi lại prompt.");
            }
            catch (IOException) { Close(); throw; }
            finally { if (acquired) write.Release(); }
        }
        public void Close() => Stream.Dispose();
    }
    private sealed record Pending(string Id, string Provider, string BindingId, string ExpectedModel, Client Client, Action<string> Text)
    {
        public TaskCompletionSource<string> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Sent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
