/* Pure, bounded response decoders. Input is untrusted website data, never commands. */
((root) => {
  "use strict";
  const MAX_TEXT = 64000, MAX_BODY = 4 * 1024 * 1024;
  const cleanModel = value => typeof value === "string" && value.trim().length <= 120
    && !/[\u0000-\u001f\u007f-\u009f]/.test(value) ? value.trim() : "";
  function matchesPrompt(body, prompt) {
    if (typeof body !== "string" || body.length > MAX_BODY || typeof prompt !== "string" || !prompt.trim()) return false;
    const visit = (value, depth = 0) => {
      if (depth > 12 || value == null) return false;
      if (typeof value === "string") {
        if (value === prompt || value.includes(prompt)) return true;
        try { return visit(JSON.parse(value), depth + 1); } catch { return false; }
      }
      return typeof value === "object" && Object.values(value).some(v => visit(v, depth + 1));
    };
    // Parse JSON or form fields; never inspect headers, cookies, credentials or URL query strings.
    try { return visit(JSON.parse(body)); } catch { /* Form encoded request. */ }
    if (body.includes("=")) for (const value of new URLSearchParams(body).values()) if (visit(value)) return true;
    return false;
  }
  function decoder(notify) {
    let buffer = "", raw = "", total = 0, text = "", model = "", protocol = "", recognized = false, done = false, failed = false, error = "";
    let webMessage = null;
    function webSnapshot(message) {
      if (!message || typeof message !== "object") return;
      webMessage = {author: {role: message.author?.role}, content: {content_type: message.content?.content_type,
        parts: Array.isArray(message.content?.parts) ? message.content.parts.filter(p => typeof p === "string") : []},
        status: message.status, end_turn: message.end_turn,
        metadata: {model_slug: message.metadata?.model_slug, default_model_slug: message.metadata?.default_model_slug}};
    }
    function webPatch(patch) {
      if (!patch || !["add", "replace", "append", "patch"].includes(patch.o)) return;
      if (patch.o === "patch" && Array.isArray(patch.v)) { for (const child of patch.v) webPatch(child); return; }
      if (patch.p === "" && patch.v?.message) { webSnapshot(patch.v.message); return; }
      if (patch.p === "/message" && patch.o !== "append") { webSnapshot(patch.v); return; }
      // Never apply general JSON patches, prototype keys, reasoning or tool paths from a website.
      if (!webMessage || typeof patch.p !== "string" || !/^\/message\/(?:content\/(?:parts\/[0-9]{1,2}|content_type)|author\/role|metadata\/(?:model_slug|default_model_slug)|status|end_turn)$/.test(patch.p)) return;
      const keys = patch.p.slice("/message/".length).split("/");
      let target = webMessage;
      for (const key of keys.slice(0, -1)) { target = target?.[key]; if (!target || typeof target !== "object") return; }
      const last = keys.at(-1);
      if (patch.o === "append") { if (typeof target[last] === "string" && typeof patch.v === "string") target[last] += patch.v; }
      else if (["string", "boolean"].includes(typeof patch.v)) target[last] = patch.v;
    }
    function publish() { notify({text, model, protocol, done, ...(error ? {error} : {})}); }
    function answer(value, append, kind) {
      if (typeof value !== "string" || !value) return;
      recognized = true; protocol = kind;
      const next = append ? text + value : value;
      if (next.length > MAX_TEXT) throw new Error("Phản hồi vượt 64.000 ký tự; giữ nội dung đầy đủ tại website.");
      text = next;
    }
    function object(value, depth = 0) {
      if (!value || typeof value !== "object" || depth > 8 || done) return;
      // Current ChatGPT web streams can wrap snapshots in v and follow with restricted JSON patches.
      if (!Array.isArray(value) && (value.v?.message || typeof value.o === "string" && (typeof value.p === "string" || value.o === "patch"))) {
        if (value.v?.message) webSnapshot(value.v.message); else webPatch(value);
        if (webMessage) object({message: webMessage}, depth + 1);
        return;
      }
      if (Array.isArray(value)) {
        // Gemini web RPC envelope. Candidate structure is deliberately strict; unknown arrays stay unparsed.
        if (value[0] === "wrb.fr" && typeof value[2] === "string") {
          try { object(JSON.parse(value[2]), depth + 1); } catch { /* Not a recognized chat payload. */ }
          return;
        }
        if (typeof value[0] === "string" && /^rc_[\w-]+$/.test(value[0]) && Array.isArray(value[1])
            && value[1].length && value[1].every(v => typeof v === "string")) {
          answer(value[1].join("\n"), false, "gemini-rpc"); return;
        }
        const candidates = value.filter(v => Array.isArray(v) && typeof v[0] === "string" && /^rc_[\w-]+$/.test(v[0]));
        if (candidates.length) object(candidates[0], depth + 1);
        else for (const item of value) object(item, depth + 1);
        return;
      }
      let candidateModel = "";
      const choices = value.choices;
      if (Array.isArray(choices)) {
        // Only choice 0; do not concatenate alternative answers, tools or reasoning channels.
        const choice = choices.find(c => c?.index === 0) || (choices.length === 1 ? choices[0] : null);
        answer(choice?.delta?.content, true, "chat-completions");
        answer(choice?.message?.role === "assistant" ? choice.message.content : null, false, "chat-completions");
        candidateModel = value.model;
        if (recognized && choice?.finish_reason === "stop" && !failed) done = true;
        if (choice?.finish_reason === "tool_calls" || choice?.finish_reason === "function_call") failed = true;
        if (choice?.finish_reason === "length" || choice?.finish_reason === "content_filter") {
          failed = true; error = "Website đã cắt phản hồi trước khi hoàn tất."; publish(); return;
        }
      } else if (typeof value.type === "string" && value.type.startsWith("response.")) {
        protocol = "responses";
        if (value.type === "response.output_text.delta") answer(value.delta, true, "responses");
        if (value.type === "response.output_text.done") answer(value.text, false, "responses");
        candidateModel = value.response?.model || value.model;
        if (value.type === "response.completed" && recognized && !failed) done = true;
        if (value.type === "response.failed" || value.type === "response.incomplete") {
          failed = true; error = "Website báo phản hồi lỗi hoặc chưa hoàn tất."; publish(); return;
        }
      } else if (value.type === "message_start" && value.message?.role === "assistant") {
        protocol = "messages"; candidateModel = value.message.model;
      } else if (value.type === "content_block_delta" && value.delta?.type === "text_delta") {
        answer(value.delta.text, true, "messages");
      } else if (value.type === "message_delta" && protocol === "messages" && value.delta?.stop_reason === "max_tokens") {
        failed = true; error = "Website đã cắt phản hồi trước khi hoàn tất.";
      } else if (value.type === "message_stop" && protocol === "messages" && recognized && !failed) {
        done = true;
      } else if (value.message?.author?.role === "assistant") {
        const message = value.message;
        if (message.content?.content_type === "text" && Array.isArray(message.content.parts))
          answer(message.content.parts.filter(p => typeof p === "string").join("\n"), false, "chatgpt-message");
        candidateModel = message.metadata?.model_slug || message.metadata?.default_model_slug;
        if (recognized && message.end_turn === true && message.status === "finished_successfully") done = true;
      } else if (Array.isArray(value.candidates)) {
        const candidate = value.candidates.find(c => c?.index === 0) || (value.candidates.length === 1 ? value.candidates[0] : null);
        const parts = candidate?.content?.parts;
        if (Array.isArray(parts)) answer(parts.filter(p => p && !p.thought && typeof p.text === "string").map(p => p.text).join(""), true, "generate-content");
        candidateModel = value.modelVersion;
        if (recognized && candidate?.finishReason === "STOP" && !failed) done = true;
        if (candidate?.finishReason && candidate.finishReason !== "STOP") { failed = true; error = "Website dừng phản hồi trước khi hoàn tất."; }
      }
      const nextModel = cleanModel(candidateModel);
      if (nextModel) model = nextModel;
    }
    function line(value) {
      value = value.trim();
      if (!value || value.startsWith("event:") || value.startsWith(":")) return;
      if (value.startsWith("data:")) value = value.slice(5).trim();
      if (value === "[DONE]") { if (recognized && text && !failed) { done = true; publish(); } return; }
      try { object(JSON.parse(value)); } catch (e) { if (e.message?.startsWith("Phản hồi vượt")) throw e; return; }
      if (text || model) publish();
    }
    return {
      push(chunk) {
        if (done || typeof chunk !== "string") return;
        total += chunk.length;
        if (total > MAX_BODY) throw new Error("Luồng website vượt giới hạn đọc 4 MiB; giữ nội dung tại website.");
        raw += chunk;
        buffer += chunk;
        let end;
        while (!done && (end = buffer.indexOf("\n")) >= 0) { line(buffer.slice(0, end)); buffer = buffer.slice(end + 1); }
      },
      end() { if (!done && buffer.trim()) line(buffer); if (!recognized && !done) line(raw); buffer = raw = ""; /* EOF alone never means a completed answer. */ },
      snapshot: () => ({text, model, protocol, done})
    };
  }
  const api = Object.freeze({decoder, matchesPrompt, cleanModel, MAX_BODY});
  root.MultiAgentHubNetworkProtocol = api;
  if (typeof module !== "undefined" && module.exports) module.exports = api;
})(globalThis);
