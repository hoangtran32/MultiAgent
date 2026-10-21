/* Pure protocol helpers. Templates remain in the selected browser document, never in native messages. */
((root) => {
  "use strict";
  function kind(url, origin) {
    try {
      const u = new URL(url, origin);
      if (u.origin !== origin) return "";
      if (u.hostname === "gemini.google.com" && /^(?:\/u\/\d+)?\/_\/BardChatUi\/data\/assistant\.lamda\.BardFrontendService\/StreamGenerate$/.test(u.pathname)) return "gemini-rpc";
      if (u.hostname === "chatgpt.com" && /^\/backend-api\/(f\/)?conversation$/.test(u.pathname)) return "chatgpt-message";
      if (/\/chat\/completions$/.test(u.pathname)) return "chat-completions";
    } catch { /* Unrecognized route is not a generic fetch proxy. */ }
    return "";
  }
  function transform(value, prompt, replacement, state, depth = 0) {
    if (depth > 16) throw new Error("Cấu trúc yêu cầu web quá sâu.");
    if (typeof value === "string") {
      if (value === prompt) { state.count++; return replacement; }
      if (/^[\[{]/.test(value)) { try { return JSON.stringify(transform(JSON.parse(value), prompt, replacement, state, depth + 1)); } catch (e) { if (e instanceof SyntaxError) return value; throw e; } }
      return value;
    }
    if (Array.isArray(value)) return value.map(v => transform(v, prompt, replacement, state, depth + 1));
    if (value && typeof value === "object") return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, transform(v, prompt, replacement, state, depth + 1)]));
    return value;
  }
  function prepare(body, prompt, replacement, protocol) {
    if (typeof body !== "string" || body.length > 1024 * 1024 || !prompt || !replacement || replacement.length > 200000) throw new Error("Yêu cầu API web không hợp lệ.");
    const state = {count: 0};
    if (protocol === "gemini-rpc") {
      const form = new URLSearchParams(body), raw = form.get("f.req");
      if (!raw) throw new Error("Chưa nhận diện được f.req của Gemini.");
      // A captured continuation or upload must never become a reusable fresh-chat template.
      if (/\b(?:c|r|rc)_[\w-]+/.test(raw) || /\b(?:https:\/\/)?(?:lh3|lh7|usercontent)\./.test(raw)) throw new Error("Cần nhận diện từ tin nhắn đầu tiên trong chat mới, chỉ có văn bản.");
      form.set("f.req", JSON.stringify(transform(JSON.parse(raw), prompt, replacement, state)));
      if (state.count !== 1) throw new Error("Không xác định duy nhất phần prompt trong API Gemini.");
      return {body: form.toString(), model: ""};
    }
    const value = JSON.parse(body);
    if (protocol === "chatgpt-message") {
      if (value.conversation_id || value.action !== "next" || !Array.isArray(value.messages) || value.messages.length !== 1
          || value.messages[0]?.author?.role !== "user" || value.messages[0]?.content?.content_type !== "text"
          || value.messages[0]?.metadata?.attachments?.length) throw new Error("Cần tin nhắn văn bản đầu tiên trong chat ChatGPT mới.");
      const result = transform(value, prompt, replacement, state);
      result.parent_message_id = crypto.randomUUID(); result.messages[0].id = crypto.randomUUID();
      delete result.conversation_id;
      if (state.count !== 1) throw new Error("Không xác định duy nhất prompt trong API ChatGPT.");
      return {body: JSON.stringify(result), model: typeof value.model === "string" ? value.model : ""};
    }
    if (protocol !== "chat-completions" || !Array.isArray(value.messages) || value.messages.length !== 1
        || value.messages[0]?.role !== "user" || typeof value.messages[0]?.content !== "string" || value.tools?.length)
      throw new Error("Adapter này chỉ nhận diện chat mới bằng văn bản, chưa hỗ trợ tools/tệp.");
    const result = transform(value, prompt, replacement, state);
    if (state.count !== 1) throw new Error("Không xác định duy nhất prompt API.");
    return {body: JSON.stringify(result), model: typeof value.model === "string" ? value.model : ""};
  }
  function geminiComplete(raw) {
    // Gemini's HTTP RPC has no SSE [DONE]. Require every length-delimited JSON frame to be whole.
    let rest = raw.replace(/^\)\]\}'\s*/, ""), count = 0;
    while (rest.trim()) {
      rest = rest.trimStart();
      const header = /^(\d+)\r?\n/.exec(rest);
      if (!header) return false;
      rest = rest.slice(header[0].length);
      const size = Number(header[1]);
      if (!Number.isSafeInteger(size) || size <= 0 || size > 4 * 1024 * 1024) return false;
      // RPC lengths are UTF-8 bytes, not JavaScript UTF-16 code units.
      let bytes = 0, end = 0;
      for (const char of rest) { if (bytes >= size) break; const code = char.codePointAt(0); bytes += code <= 0x7f ? 1 : code <= 0x7ff ? 2 : code <= 0xffff ? 3 : 4; end += char.length; }
      if (bytes !== size) return false;
      try { if (!Array.isArray(JSON.parse(rest.slice(0, end)))) return false; } catch { return false; }
      rest = rest.slice(end); count++;
    }
    return count > 0;
  }
  const api = Object.freeze({kind, prepare, geminiComplete}); root.MultiAgentHubWebApiProtocol = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(globalThis);
