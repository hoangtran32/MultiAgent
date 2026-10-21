/* MAIN world broker. Only explicit selected-document sessions; no cookies/token export or challenge solving. */
(() => {
  "use strict";
  if (globalThis.__multiAgentHubWebApi) return;
  globalThis.__multiAgentHubWebApi = true;
  const api = globalThis.MultiAgentHubWebApiProtocol, protocol = globalThis.MultiAgentHubNetworkProtocol;
  const fetchOriginal = window.fetch, openOriginal = XMLHttpRequest.prototype.open, sendOriginal = XMLHttpRequest.prototype.send,
    headerOriginal = XMLHttpRequest.prototype.setRequestHeader;
  let link = null, armed = null, active = null;
  const templates = new Map(), xhr = new WeakMap();
  function emit(type, fields = {}) {
    if (link) window.postMessage({source: "multi-agent-hub-web-api", channel: link.channel, type, ...fields}, location.origin);
  }
  const publicModels = () => [...templates.values()].map(t => ({id: t.id, name: t.name, protocol: t.protocol, source: t.source}));
  function arm(prompt, label) {
    if (link && typeof prompt === "string" && prompt.trim() && prompt.length <= 200000)
      armed = {prompt, label: String(label || "").slice(0, 120), expires: Date.now() + 20000};
  }
  async function capture(url, method, body, headers) {
    if (!link || !armed || Date.now() > armed.expires || String(method).toUpperCase() !== "POST") return;
    const selected = armed, kind = api.kind(url, location.origin);
    if (!kind) return;
    try {
      const shape = api.prepare(body, selected.prompt, selected.prompt, kind);
      const safe = new Headers();
      for (const [key, value] of new Headers(headers || {})) {
        // Keep only protocol headers in browser memory. Never reuse one-time anti-bot proofs.
        if (/^(content-type|accept|authorization|oai-device-id|oai-language|chatgpt-account-id|x-goog-[a-z0-9-]+)$/i.test(key)) safe.set(key, value);
      }
      emit("capture-started");
      const model = protocol.cleanModel(shape.model), label = selected.label || model;
      if (!label) { emit("notice", {message: "API đã thấy nhưng website chưa lộ model. Chọn model và đặt nhãn trong extension rồi nhận diện lại."}); return; }
      const identity = JSON.stringify([kind, model || label, [...safe].filter(([k]) => k.startsWith("x-goog-ext-"))]);
      const hash = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(identity)));
      const id = "web-" + [...hash.slice(0, 12)].map(v => v.toString(16).padStart(2, "0")).join("");
      if (armed !== selected || !link) return;
      armed = null;
      if (templates.size >= 16 && !templates.has(id)) throw new Error("Tối đa 16 model trong một phiên trình duyệt.");
      templates.set(id, {id, name: label, source: model ? "request" : "page", protocol: kind,
        url: new URL(url, location.href).href, body, prompt: selected.prompt, headers: safe});
      emit("models", {models: publicModels(), capturedAt: Date.now()});
    } catch { /* A continuation, upload, unrelated or ambiguous request is never learned. */ }
  }
  window.fetch = function(input, init) {
    const request = input instanceof Request ? input : null;
    if (link && armed) {
      const body = init?.body, url = request?.url || String(input), method = init?.method || request?.method || "GET";
      if (typeof body === "string" || body instanceof URLSearchParams) void capture(url, method, String(body), init?.headers || request?.headers);
      else if (request && !body && api.kind(url, location.origin) && /json|x-www-form-urlencoded/.test(request.headers.get("content-type") || "")) {
        // Bounded Request clone. Never buffer uploads or a general page request.
        void (async () => { const reader = request.clone().body?.getReader(); if (!reader) return;
          let raw = "", bytes = 0; const decoder = new TextDecoder();
          try { while (true) { const item = await reader.read(); if (item.done) break; bytes += item.value.length; if (bytes > 1024 * 1024) return; raw += decoder.decode(item.value, {stream: true}); }
            await capture(url, method, raw + decoder.decode(), init?.headers || request.headers);
          } finally { void reader.cancel().catch(() => {}); }
        })().catch(() => {});
      }
    }
    return Reflect.apply(fetchOriginal, this, arguments);
  };
  XMLHttpRequest.prototype.open = function(method, url) { const result = Reflect.apply(openOriginal, this, arguments); xhr.set(this, {method, url, headers: {}}); return result; };
  XMLHttpRequest.prototype.setRequestHeader = function(key, value) { const result = Reflect.apply(headerOriginal, this, arguments); const state = xhr.get(this); if (state) state.headers[key] = value; return result; };
  XMLHttpRequest.prototype.send = function(body) { const state = xhr.get(this); if (state && typeof body === "string") void capture(state.url, state.method, body, state.headers); return Reflect.apply(sendOriginal, this, arguments); };
  async function execute(message) {
    if (active) { emit("error", {id: message.id, message: "Phiên API web đang bận."}); return; }
    const template = templates.get(message.modelId);
    if (!template) { emit("error", {id: message.id, message: "Model API chưa được nhận diện trong document hiện tại. Nhận diện lại trong extension."}); return; }
    const run = {id: message.id, controller: new AbortController()}; active = run;
    try {
      const prepared = api.prepare(template.body, template.prompt, message.prompt, template.protocol);
      let body = prepared.body; const url = new URL(template.url), headers = new Headers(template.headers);
      if (template.protocol === "gemini-rpc") {
        const form = new URLSearchParams(body), csrf = window.WIZ_global_data?.SNlM0e;
        if (typeof csrf === "string") form.set("at", csrf);
        body = form.toString(); url.searchParams.set("_reqid", String(Date.now() % 1000000000));
      }
      if (template.protocol === "chatgpt-message") {
        // Get a fresh service-issued token. Challenges remain an explicit website interaction.
        const response = await Reflect.apply(fetchOriginal, window, ["/backend-api/sentinel/chat-requirements", {
          method: "POST", credentials: "include", redirect: "error", headers: new Headers([...headers].filter(([k]) => k !== "content-type")),
          body: "{}", signal: run.controller.signal}]);
        if (!response.ok) throw new Error("ChatGPT cần xác minh phiên trên website; adapter API hiện chưa đáp ứng handshake của phiên này.");
        const requirement = await response.json();
        if (requirement.turnstile?.required || requirement.proofofwork?.required || requirement.arkose?.required || requirement.so?.required || !requirement.token)
          throw new Error("ChatGPT yêu cầu xác minh tương tác trên web. Không tái sử dụng token chống bot; phiên này chưa hỗ trợ API trực tiếp.");
        headers.set("openai-sentinel-chat-requirements-token", requirement.token);
      }
      const response = await Reflect.apply(fetchOriginal, window, [url.href, {method: "POST", credentials: "include", redirect: "error", headers, body, signal: run.controller.signal}]);
      if (!response.ok) throw new Error("API website trả HTTP " + response.status + ". Kiểm tra đăng nhập, quota hoặc xác minh trên web; Hub không tự đổi model/gửi lại.");
      if (!response.body || !/json|event-stream|text\/plain/.test(response.headers.get("content-type") || "")) throw new Error("Website trả kiểu dữ liệu chưa được adapter hỗ trợ.");
      let raw = "", byteCount = 0, failure = ""; const utf8 = new TextDecoder(), reader = response.body.getReader();
      const decoder = protocol.decoder(state => { if (state.text) emit("delta", {id: run.id, text: state.text}); if (state.error) failure = state.error; });
      try {
        while (true) {
          const item = await reader.read(); if (item.done) break;
          byteCount += item.value.byteLength; if (byteCount > protocol.MAX_BODY) throw new Error("Phản hồi API vượt 4 MiB.");
          const chunk = utf8.decode(item.value, {stream: true}); raw += chunk; decoder.push(chunk);
          if (decoder.snapshot().done) break;
        }
        const last = utf8.decode(); raw += last; decoder.push(last); decoder.end();
      } finally { void reader.cancel().catch(() => {}); }
      const result = decoder.snapshot();
      if (failure) throw new Error(failure);
      if (!result.text || !(result.done || template.protocol === "gemini-rpc" && result.protocol === "gemini-rpc" && api.geminiComplete(raw)))
        throw new Error("API đã đóng nhưng chưa có phản hồi hoàn tất hợp lệ. Phần nhận được đã giữ; không tự gửi lại.");
      emit("result", {id: run.id, text: result.text});
    } catch (error) { emit("error", {id: run.id, message: error.name === "AbortError" ? "Đã hủy yêu cầu API theo lệnh Hub." : String(error.message || "Lỗi API web.").slice(0, 500)}); }
    finally { if (active === run) active = null; }
  }
  window.addEventListener("message", event => {
    const m = event.data;
    if (event.source !== window || event.origin !== location.origin || m?.source !== "multi-agent-hub-api-control" || typeof m.channel !== "string" || m.channel.length > 128) return;
    if (m.type === "link") { if (link?.channel !== m.channel) { active?.controller.abort(); templates.clear(); } link = {channel: m.channel}; emit("models", {models: publicModels()}); return; }
    if (!link || m.channel !== link.channel) return;
    if (m.type === "arm") arm(m.prompt, m.label);
    else if (m.type === "cancel" && active?.id === m.id) active.controller.abort();
    else if (m.type === "call" && typeof m.id === "string" && m.id.length <= 128 && typeof m.prompt === "string" && m.prompt.trim() && m.prompt.length <= 200000) void execute(m);
  });
})();
