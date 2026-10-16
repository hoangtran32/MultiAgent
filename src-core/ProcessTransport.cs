using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace MultiAgentHub.Core;

public sealed record Invocation(string Executable, IReadOnlyList<string> Arguments, string Input);

public static class ProcessTransport
{
    public static string? OnPath(string name)
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(folder.Trim('"'), name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
    public static string? Find(AgentConfig agent)
    {
        if (agent.Command.Length > 0) return File.Exists(agent.Command) ? Path.GetFullPath(agent.Command) : OnPath(agent.Command);
        string variable = agent.Connection switch {
            "codex-cli" => "CODEX_BINARY", "claude-cli" => "CLAUDE_BINARY", "agy-cli" => "AGY_BINARY",
            "antigravity-ide" => "ANTIGRAVITY_IDE_BINARY", "antigravity-desktop" => "ANTIGRAVITY_BINARY", _ => "" };
        string? env = variable.Length > 0 ? Environment.GetEnvironmentVariable(variable) : null;
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (agent.Connection == "codex-cli")
        {
            string dir = Path.Combine(local, "OpenAI", "Codex", "bin");
            if (Directory.Exists(dir))
            {
                var candidates = Directory.GetDirectories(dir).Select(p => Path.Combine(p, "codex.exe")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc);
                if (candidates.FirstOrDefault() is { } binary) return binary;
            }
        }
        if (agent.Connection is "antigravity-ide" or "antigravity-desktop")
        {
            string app = agent.Connection == "antigravity-ide" ? "Antigravity IDE" : "Antigravity";
            foreach (string candidate in new[] {
                Path.Combine(local, "Programs", app, app + ".exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), app, app + ".exe")
            })
            {
                if (File.Exists(candidate)) return candidate;
            }
            return OnPath(app);
        }
        string name = agent.Connection switch { "codex-cli" => "codex", "claude-cli" => "claude", "agy-cli" => "agy", _ => "" };
        if (name.Length == 0) return null;
        string userBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", name + ".exe");
        if (File.Exists(userBin)) return userBin;
        if (agent.Connection == "agy-cli")
        {
            string installed = Path.Combine(local, "agy", "bin", "agy.exe");
            if (File.Exists(installed)) return installed;
            string geminiBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "bin", "agy.exe");
            if (File.Exists(geminiBin)) return geminiBin;
            string progBin = Path.Combine(local, "Programs", "Antigravity", "bin", "agy.exe");
            if (File.Exists(progBin)) return progBin;
        }
        return OnPath(name);
    }
    public static Invocation Build(AgentConfig agent, string project, string prompt, string executable)
    {
        Catalog.Validate(agent);
        var args = new List<string>(); string input = prompt;
        switch (agent.Connection)
        {
            case "codex-cli":
                args.AddRange(["exec", "--json", "--color", "never", "--skip-git-repo-check"]);
                if (agent.Permission == "full") args.Add("--dangerously-bypass-approvals-and-sandbox");
                else args.AddRange(["--sandbox", agent.Permission == "workspace-write" ? "workspace-write" : "read-only", "-c", "approval_policy=\"never\""]);
                if (agent.Model.Length > 0) args.AddRange(["--model", agent.Model]);
                if (agent.Reasoning.Length > 0) args.AddRange(["-c", $"model_reasoning_effort=\"{agent.Reasoning}\""]);
                args.Add("-"); break;
            case "claude-cli":
                args.AddRange(["-p", "--output-format", "stream-json", "--verbose"]);
                if (agent.Permission == "full") args.Add("--dangerously-skip-permissions");
                else if (agent.Permission == "workspace-write") args.AddRange(["--permission-mode", "acceptEdits"]);
                else args.AddRange(["--permission-mode", "plan"]);
                if (agent.Model.Length > 0) args.AddRange(["--model", agent.Model]);
                if (agent.Reasoning.Length > 0) args.AddRange(["--effort", agent.Reasoning]);
                break;
            case "agy-cli":
                args.AddRange(["--input-format", "stream-json", "--output-format", "stream-json"]);
                if (agent.Permission == "full") args.Add("--dangerously-skip-permissions");
                else if (agent.Permission == "read-only") args.AddRange(["--mode", "plan"]);
                else if (agent.Permission == "workspace-write") args.AddRange(["--mode", "accept-edits"]);
                if (agent.Model.Length > 0) args.AddRange(["--model", agent.Model]);
                if (agent.Reasoning.Length > 0) args.AddRange(["--effort", agent.Reasoning]);
                input = JsonSerializer.Serialize(new { @event = "user", message = new { content = prompt } }) + "\n"; break;
            case "custom-cli":
                args.AddRange(agent.Arguments.Select(a => a.Replace("{model}", agent.Model).Replace("{project}", project))); break;
            default: throw new ArgumentException("Kết nối này không chạy qua CLI.");
        }
        return new(executable, args, input);
    }
    public static async Task Run(Invocation invocation, string project, Action<string> stdout, Action<string> stderr, CancellationToken token, Task? completion = null)
    {
        if (Path.GetExtension(invocation.Executable).ToLowerInvariant() is ".cmd" or ".bat") throw new InvalidOperationException("Chọn executable .exe; Hub không chạy qua shell. Với script, chọn node.exe/python.exe và cấu hình đối số.");
        var info = new ProcessStartInfo(invocation.Executable) { WorkingDirectory = project, UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false) };
        foreach (string argument in invocation.Arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult();
        token.ThrowIfCancellationRequested(); process.Start();
        using var ioStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        int protocolComplete = 0;
        bool stoppedAfterCompletion = false;
        void Terminate(bool entireTree = true)
        {
            try { if (!process.HasExited) process.Kill(entireTree); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException) {
                try { if (!process.HasExited) process.Kill(); }
                catch (Exception fallback) when (fallback is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        using var registration = token.Register(() => Terminate(Volatile.Read(ref protocolComplete) == 0));
        var callbackGate = new object();
        async Task Pump(StreamReader reader, Action<string> callback)
        {
            try
            {
                char[] buffer = new char[4096]; int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory(), ioStop.Token).ConfigureAwait(false)) > 0) {
                    lock (callbackGate) {
                        if (ioStop.IsCancellationRequested) return;
                        callback(new string(buffer, 0, count));
                    }
                }
            }
            catch (OperationCanceledException) when (ioStop.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (ioStop.IsCancellationRequested) { }
            catch { Terminate(); throw; }
        }
        var output = Pump(process.StandardOutput, stdout); var error = Pump(process.StandardError, stderr);
        async Task Input()
        {
            try { await process.StandardInput.WriteAsync(invocation.Input.AsMemory(), ioStop.Token).ConfigureAwait(false); await process.StandardInput.FlushAsync(ioStop.Token).ConfigureAwait(false); }
            catch (IOException) { /* An early process failure is reported by its exit code. */ }
            catch (OperationCanceledException) when (ioStop.IsCancellationRequested) { }
            catch { Terminate(); throw; }
            finally { process.StandardInput.Close(); }
        }
        Task[] ioTasks = [output, error, Input()];
        var streams = Task.WhenAll(ioTasks);
        // A descendant (for example an ADB server) can inherit the pipe handles and outlive
        // the CLI. EOF is not a reliable signal for the root process's completion.
        try {
            // Root exit and the provider's terminal protocol event are independent of pipe EOF.
            // Final prose alone is not completion: agents can send intermediate messages before tools.
            if (completion != null) {
                var first = await Task.WhenAny(exited.Task, completion).WaitAsync(token).ConfigureAwait(false);
                if (first == completion) {
                    await completion.ConfigureAwait(false);
                    Interlocked.Exchange(ref protocolComplete, 1);
                    try { await exited.Task.WaitAsync(TimeSpan.FromSeconds(2), token).ConfigureAwait(false); }
                    catch (TimeoutException) {
                        token.ThrowIfCancellationRequested();
                        stoppedAfterCompletion = true;
                        // A finished CLI may hang in shutdown/hooks. Stop only that root;
                        // servers launched for the user's project must remain alive.
                        Terminate(entireTree: false);
                        await exited.Task.WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
                        lock (callbackGate) stderr("\n[Hub] CLI đã phát tín hiệu hoàn tất nhưng chưa thoát; đã giữ kết quả và kết thúc riêng CLI, giữ nguyên tiến trình nền của project.\n");
                    }
                }
            }
            await exited.Task.WaitAsync(token).ConfigureAwait(false);
            try { await streams.WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false); }
            catch (TimeoutException) {
                ioStop.Cancel();
                // Some anonymous-pipe reads cannot be interrupted promptly. Do not await
                // them indefinitely or terminate independently running background tools.
                try { await streams.WaitAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false); }
                catch (TimeoutException) { }
                foreach (var task in ioTasks.Where(t => t.IsFaulted)) task.GetAwaiter().GetResult();
                lock (callbackGate) stderr("\n[Hub] CLI đã thoát; hết thời gian thu nốt stdout/stderr. Đã giữ output nhận được và ngừng chờ luồng do tiến trình nền có thể còn giữ mở.\n");
            }
            token.ThrowIfCancellationRequested();
        }
        catch {
            ioStop.Cancel(); Terminate(Volatile.Read(ref protocolComplete) == 0);
            try { await exited.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
            catch (Exception e) when (e is InvalidOperationException or TimeoutException) { }
            token.ThrowIfCancellationRequested(); throw;
        }
        finally {
            ioStop.Cancel();
            // Observe faults even if a handle inherited by a descendant delays completion.
            _ = streams.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (!streams.IsCompleted) {
                // Close only Hub's ends of the pipes. The descendant's process is untouched.
                foreach (var reader in new[] { process.StandardOutput, process.StandardError }) {
                    try { reader.Dispose(); } catch (IOException) { } catch (ObjectDisposedException) { }
                }
            }
        }
        if (!stoppedAfterCompletion && process.ExitCode != 0) throw new InvalidOperationException($"CLI kết thúc với mã {process.ExitCode}. Xem nhật ký để biết chi tiết.");
    }
}

public sealed class CliParser(string connection, Action<string> text, Action<string> log)
{
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Completion => completion.Task;
    private string buffer = "", answer = "", failure = "";
    private bool quota;
    public void Feed(string chunk)
    {
        if (connection == "custom-cli") { answer = Limit(answer + chunk); text(answer); return; }
        buffer += chunk;
        int index;
        while ((index = buffer.IndexOf('\n')) >= 0) { Consume(buffer[..index]); buffer = buffer[(index + 1)..]; }
        if (buffer.Length > 4 * 1024 * 1024) throw new InvalidDataException("CLI trả một dòng JSON quá lớn.");
    }
    public string Finish()
    {
        if (buffer.Length > 0) { string remaining = buffer; buffer = ""; Consume(remaining); }
        ThrowIfFailed();
        if (string.IsNullOrWhiteSpace(answer)) throw new InvalidDataException("CLI không trả câu trả lời. Xem nhật ký để xác định lỗi quyền công cụ, model hoặc kết nối.");
        return answer.Trim();
    }
    public void ThrowIfFailed()
    {
        ThrowIfQuota();
        if (failure.Length > 0) throw new InvalidOperationException(failure);
    }
    public void ThrowIfQuota() { if (quota) throw new AgentQuotaException(); }
    private void Failed(JsonElement error, string message)
    {
        quota |= Quota.Error(error) || Quota.Exhausted(message);
        failure = message;
    }
    public static string Limit(string value) => value.Length > 1000000 ? value[^1000000..] : value;
    private void Consume(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); } catch (JsonException) { log(line + "\n"); return; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { log(line + "\n"); return; }
            string type = root.Str("type");
            bool terminal = false;
            if (connection == "codex-cli")
            {
                if (root.TryGetProperty("item", out var item))
                {
                    if (type == "item.completed" && item.Str("type") == "agent_message") answer = item.Str("text");
                    if (item.Str("type") == "command_execution") log(item.Str("command") + "\n" + item.Str("aggregated_output") + "\n");
                }
                if (type is "error" or "turn.failed") {
                    var error = root.TryGetProperty("error", out var e) ? e : root;
                    Failed(error, root.Str("message").Length > 0 ? root.Str("message") : error.Str("message").Length > 0 ? error.Str("message") : "Codex không hoàn thành.");
                }
                terminal = type == "turn.completed";
            }
            else if (connection == "claude-cli")
            {
                if (type == "assistant" && root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var blocks))
                {
                    string content = string.Join("", blocks.EnumerateArray().Where(b => b.Str("type") == "text").Select(b => b.Str("text")));
                    if (root.Str("error").Length > 0) Failed(root, content); else answer = Limit(answer + content);
                }
                if (type == "result")
                {
                    terminal = true;
                    if (root.TryGetProperty("is_error", out var err) && err.ValueKind == JsonValueKind.True) {
                        string errors = root.TryGetProperty("errors", out var entries) && entries.ValueKind == JsonValueKind.Array ? string.Join("\n", entries.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.Str("message"))) : "";
                        Failed(root, root.Str("result").Length > 0 ? root.Str("result") : errors.Length > 0 ? errors : "Claude kết thúc với lỗi.");
                    }
                    else if (root.Str("result").Length > 0) answer = root.Str("result");
                }
            }
            else if (connection == "agy-cli")
            {
                if (root.Str("event") == "step_update" && root.TryGetProperty("step_update", out var step) && step.Str("step_type") == "agent_response") answer = Limit(answer + step.Str("text_delta"));
                if (root.Str("event") == "result" && root.TryGetProperty("result", out var result))
                {
                    terminal = true;
                    if (result.Str("status") != "SUCCESS") {
                        var error = result.TryGetProperty("error", out var detail) ? detail : result;
                        Failed(error, result.Str("error").Length > 0 ? result.Str("error") : error.Str("message").Length > 0 ? error.Str("message") : "Antigravity: " + result.Str("status"));
                    }
                    else answer = result.Str("response");
                }
            }
            text(Limit(answer));
            if (terminal && !quota && failure.Length == 0 && !string.IsNullOrWhiteSpace(answer)) completion.TrySetResult();
        }
    }
}
