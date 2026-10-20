/* Runs in MAIN world, dormant except the one turn armed by this extension.
   Only normalized text/model/completion leaves the page. No request replay or credential export. */
(() => {
  "use strict";
  if (globalThis.__multiAgentHubNetwork) return;
  globalThis.__multiAgentHubNetwork = true;
  const protocol = globalThis.MultiAgentHubNetworkProtocol;
  let active = null;
  const readers = new Set();
  function stop() { active = null; for (const reader of readers) void reader.cancel().catch(() => {}); readers.clear(); }
  function emit(turn, state) {
    if (active !== turn) return;
    window.postMessage({source: "multi-agent-hub-network", channel: turn.channel, id: turn.id,
      text: state.text || "", model: state.model || "", protocol: state.protocol || "", done: state.done === true,
      error: typeof state.error === "string" ? state.error.slice(0, 500) : ""}, location.origin);
  }
  window.addEventListener("message", event => {
    const value = event.data;
    if (event.source !== window || event.origin !== location.origin || value?.source !== "multi-agent-hub-control") return;
    if (typeof value.channel !== "string" || value.channel.length > 128 || typeof value.id !== "string" || value.id.length > 128) return;
    if (value.type === "disarm" && active?.channel === value.channel && active.id === value.id) { stop(); return; }
    if (value.type !== "arm" || typeof value.prompt !== "string" || !value.prompt.trim() || value.prompt.length > 200000) return;
    stop(); active = {channel: value.channel, id: value.id, prompt: value.prompt, expires: Date.now() + 20000};
    emit(active, {protocol: "armed"});
  });
  function eligible(turn, method, url) {
    if (!turn || turn !== active || turn.claimed || Date.now() > turn.expires || method.toUpperCase() !== "POST") return false;
    try { return new URL(url, location.href).origin === location.origin; } catch { return false; }
  }
  async function observe(response, turn, body) {
    if (!eligible(turn, "POST", response.url || location.href) || !protocol.matchesPrompt(body, turn.prompt)) return;
    const kind = response.headers.get("content-type") || "";
    if (!response.ok || !/json|event-stream|text\/plain/i.test(kind) || !response.body) return;
    let reader;
    try {
      // Bounded consumer reads continuously; cancelling releases the cloned stream on disarm/errors.
      reader = response.clone().body.getReader(); readers.add(reader);
      const owner = {};
      const decode = protocol.decoder(state => {
        if (state.text && !turn.owner) turn.owner = owner;
        if (!turn.owner || turn.owner === owner) emit(turn, state);
      }), utf8 = new TextDecoder();
      let bytes = 0;
      while (active === turn) {
        const item = await reader.read();
        if (item.done) { decode.push(utf8.decode()); decode.end(); break; }
        bytes += item.value.byteLength;
        if (bytes > protocol.MAX_BODY) throw new Error("Luồng website vượt 4 MiB.");
        decode.push(utf8.decode(item.value, {stream: true}));
        const state = decode.snapshot();
        if (state.text) turn.claimed = true;
        if (state.done) break;
      }
    } catch { /* Unrecognized/interrupted transport falls back to the page's explicit UI state. */ }
    finally { if (reader) { readers.delete(reader); void reader.cancel().catch(() => {}); } }
  }
  const originalFetch = window.fetch;
  window.fetch = function(input, init) {
    const turn = active, request = input instanceof Request ? input : null;
    const method = init?.method || request?.method || "GET", url = request?.url || String(input);
    let body = null;
    if (eligible(turn, method, url)) {
      if (typeof init?.body === "string") body = Promise.resolve(init.body);
      else if (init?.body instanceof URLSearchParams) body = Promise.resolve(init.body.toString());
      else if (request && !init?.body) {
        // Do not read arbitrary Request streams (could be uploads); cloned string bodies only via size/type bounds.
        const size = Number(request.headers.get("content-length"));
        if (size > 0 && size <= protocol.MAX_BODY) body = request.clone().text();
      }
    }
    const result = Reflect.apply(originalFetch, this, arguments);
    if (body) void result.then(response => body.then(value => observe(response, turn, value))).catch(() => {});
    return result;
  };
  const open = XMLHttpRequest.prototype.open, send = XMLHttpRequest.prototype.send;
  const states = new WeakMap();
  XMLHttpRequest.prototype.open = function(method, url) {
    const result = Reflect.apply(open, this, arguments); states.set(this, {method: String(method), url: String(url)}); return result;
  };
  XMLHttpRequest.prototype.send = function(body) {
    const turn = active, info = states.get(this);
    if (info && eligible(turn, info.method, info.url) && protocol.matchesPrompt(typeof body === "string" ? body : "", turn.prompt)) {
      const owner = {};
      const decode = protocol.decoder(state => {
        if (state.text && !turn.owner) turn.owner = owner;
        if (!turn.owner || turn.owner === owner) emit(turn, state);
      });
      let offset = 0, failed = false;
      const read = final => {
        if (active !== turn || failed || this.status < 200 || this.status >= 300 || this.responseType && this.responseType !== "text") return;
        try {
          if (!/json|event-stream|text\/plain/i.test(this.getResponseHeader("content-type") || "")) return;
          const value = this.responseText;
          if (value.length > protocol.MAX_BODY) { failed = true; return; }
          decode.push(value.slice(offset)); offset = value.length;
          if (final) decode.end();
          if (decode.snapshot().text) turn.claimed = true;
        } catch { failed = true; }
      };
      const progress = () => read(false);
      this.addEventListener("progress", progress);
      this.addEventListener("loadend", () => { read(true); this.removeEventListener("progress", progress); }, {once: true});
    }
    return Reflect.apply(send, this, arguments);
  };
})();
