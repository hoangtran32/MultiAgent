"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const vm = require("node:vm");
const fs = require("node:fs");
const path = require("node:path");
const {randomUUID} = require("node:crypto");
const source = fs.readFileSync(path.join(__dirname, "../browser-extension/background.js"), "utf8");
const settle = async () => { await new Promise(setImmediate); await new Promise(setImmediate); };

function browser(saved = [], options = {}) {
  let listener;
  const tab = {id: 1, title: "Gemini", url: options.url || "https://gemini.google.com/app"};
  let nonce = options.nonce || "document-one";
  const session = {bindings: structuredClone(saved)}, local = {}, ports = [], sent = [], cancels = [], navigations = [];
  const event = () => { const handlers = []; return {addListener: f => handlers.push(f), emit: (...args) => handlers.forEach(f => f(...args))}; };
  const storage = data => ({get: async key => ({[key]: structuredClone(data[key])}), set: async value => Object.assign(data, structuredClone(value))});
  const chrome = {
    storage: {session: storage(session), local: storage(local)},
    scripting: {executeScript: async () => []},
    runtime: {id: "extension-under-test", onMessage: {addListener: f => { listener = f; }}, connectNative: () => {
      const port = {onMessage: event(), onDisconnect: event(), frames: [], postMessage(value) { this.frames.push(structuredClone(value)); }, disconnect() { this.onDisconnect.emit(); }};
      ports.push(port); queueMicrotask(() => port.onMessage.emit({type: "hello", protocol: 1})); return port;
    }},
    tabs: {query: async () => [structuredClone(tab)], get: async id => { assert.equal(id, tab.id); return structuredClone(tab); },
      onRemoved: event(), onUpdated: event(), sendMessage: async (id, message) => {
        assert.equal(id, tab.id);
        if (message.type === "hub-bind") return {ready: true, nonce, url: tab.url, model: options.detectedModel || ""};
        if (message.type === "hub-api-link") return {ok: true};
        if (message.type === "hub-api-navigation") return {allowed: false, nonce, url: tab.url};
        if (message.type === "hub-ping") return {nonce, url: tab.url};
        if (message.type === "hub-request" || message.type === "hub-api-request") { sent.push(structuredClone(message)); return {accepted: true}; }
        if (message.type === "hub-cancel") { cancels.push(message.id); return {ok: true}; }
        if (message.type === "hub-navigation") { navigations.push(structuredClone(message)); return {allowed: options.allowNavigation !== false, nonce, url: tab.url}; }
        throw new Error("Unexpected content command: " + message.type);
      }}
  };
  let context;
  context = vm.createContext({importScripts: name => vm.runInContext(fs.readFileSync(path.join(__dirname, "../browser-extension/", name), "utf8"), context), URLSearchParams, chrome, crypto: {randomUUID}, URL, Date, setTimeout, clearTimeout, structuredClone});
  vm.runInContext(source, context, {filename: "background.js"});
  const popup = message => new Promise(resolve => listener(message, {id: chrome.runtime.id}, resolve));
  return {tab, ports, sent, cancels, navigations, session, popup, content: message => listener(message, {id: chrome.runtime.id, tab: structuredClone(tab), frameId: 0, url: tab.url}, () => {}), setNonce: value => { nonce = value; },
    update: async change => { if (change.url) tab.url = change.url; chrome.tabs.onUpdated.emit(tab.id, change); await settle(); },
    connect: (model = "Argon") => popup({type: "popup-connect", tabId: tab.id, confirmed: true, model, selectors: {}})};
}

test("connecting the same document and model again keeps the Hub binding ID", async () => {
  const b = browser(); assert.equal((await b.connect()).ok, true);
  const first = b.session.bindings[0].id;
  assert.equal((await b.connect("argon")).ok, true);
  assert.equal(b.session.bindings.length, 1); assert.equal(b.session.bindings[0].id, first);
  assert.equal(b.ports.length, 1);
});
test("a different document or confirmed model gets a different binding", async () => {
  const b = browser(); await b.connect(); const first = b.session.bindings[0].id;
  b.setNonce("reloaded-document"); await b.connect(); const second = b.session.bindings[0].id;
  assert.notEqual(second, first);
  await b.connect("Other model"); assert.notEqual(b.session.bindings[0].id, second);
});
test("opening the popup restores a saved existing conversation without rebinding a fresh chat", async () => {
  const url = "https://gemini.google.com/app/existing-conversation";
  const saved = {id: "saved-id", provider: "gemini-web", tabId: 1, url, title: "Existing", model: "Argon", nonce: "document-one", selectors: {}};
  const b = browser([saved], {url}); await b.popup({type: "popup-status"}); await settle();
  const state = await b.popup({type: "popup-status"});
  assert.equal(state.connected, true); assert.equal(state.canBindNew, false);
  assert.equal(state.bindings[0].id, "saved-id"); assert.equal(b.ports.length, 1);
  assert.ok(b.ports[0].frames.some(frame => frame.type === "state" && frame.bindings[0].id === "saved-id"));
});
test("resynchronization after native disconnect republishes tabs without replaying a request", async () => {
  const b = browser(); await b.connect(); const id = b.session.bindings[0].id;
  b.ports[0].onMessage.emit({type: "request", id: "request-once", bindingId: id, expectedModel: "Argon", prompt: "Offline test"});
  await settle(); assert.equal(b.sent.length, 1);
  b.ports[0].disconnect(); await settle();
  assert.equal((await b.popup({type: "popup-resync"})).ok, true); await settle();
  assert.equal(b.ports.length, 2); assert.equal(b.sent.length, 1); assert.deepEqual(b.cancels, ["request-once"]);
  assert.equal(b.session.bindings[0].id, id);
  assert.ok(b.ports[1].frames.every(frame => frame.type === "state"));
});
test("a stale document is removed instead of being attached to another conversation", async () => {
  const saved = {id: "old-id", provider: "gemini-web", tabId: 1, url: "https://gemini.google.com/app", title: "Old", model: "Argon", nonce: "old-document", selectors: {}};
  const b = browser([saved]); const response = await b.popup({type: "popup-resync"});
  assert.equal(response.ok, false); assert.equal(b.session.bindings.length, 0); assert.equal(b.ports.length, 0);
});
test("manual synchronization cannot interrupt an active request", async () => {
  const b = browser(); await b.connect(); const id = b.session.bindings[0].id;
  b.ports[0].onMessage.emit({type: "request", id: "still-running", bindingId: id, expectedModel: "Argon", prompt: "Offline test"});
  await settle();
  const result = await b.popup({type: "popup-resync"});
  assert.equal(result.ok, false); assert.equal(b.sent.length, 1); assert.equal(b.cancels.length, 0); assert.equal(b.ports.length, 1);
});

for (const url of ["https://gemini.google.com/app", "https://chatgpt.com/"]) {
  test("loading and complete in the same document keep the pending request: " + url, async () => {
    const b = browser([], {url}); await b.connect(); const id = b.session.bindings[0].id;
    b.ports[0].onMessage.emit({type: "request", id: "working", bindingId: id, expectedModel: "Argon", prompt: "Offline test"});
    await settle(); await b.update({status: "loading"}); await b.update({status: "complete"});
    assert.equal(b.session.bindings[0].id, id); assert.equal(b.cancels.length, 0); assert.equal(b.sent.length, 1);
    assert.ok(!b.ports[0].frames.some(frame => frame.type === "error"));
  });
}
test("own initial conversation URL survives a combined loading and URL event", async () => {
  const b = browser(); await b.connect(); const id = b.session.bindings[0].id;
  b.ports[0].onMessage.emit({type: "request", id: "first-message", bindingId: id, expectedModel: "Argon", prompt: "Offline test"});
  await settle(); await b.update({status: "loading", url: "https://gemini.google.com/app/assigned-id"});
  await b.update({status: "complete"});
  assert.equal(b.session.bindings[0].id, id); assert.equal(b.session.bindings[0].url, b.tab.url);
  assert.equal(b.navigations.length, 1); assert.equal(b.navigations[0].id, "first-message");
  assert.equal(b.cancels.length, 0); assert.equal(b.sent.length, 1);
});
test("real document replacement after loading fails the pending request without replay", async () => {
  const b = browser(); await b.connect(); const id = b.session.bindings[0].id;
  b.ports[0].onMessage.emit({type: "request", id: "not-replayed", bindingId: id, expectedModel: "Argon", prompt: "Offline test"});
  await settle(); await b.update({status: "loading"}); b.setNonce("new-document"); await b.update({status: "complete"});
  assert.equal(b.session.bindings.length, 0); assert.equal(b.sent.length, 1); assert.deepEqual(b.cancels, ["not-replayed"]);
  assert.ok(b.ports[0].frames.some(frame => frame.type === "error" && frame.id === "not-replayed"));
});
test("switching conversations outside an active request does not adopt the new URL", async () => {
  const b = browser(); await b.connect(); await b.update({status: "loading", url: "https://gemini.google.com/app/unrelated"});
  assert.equal(b.session.bindings.length, 0); assert.equal(b.sent.length, 0); assert.equal(b.navigations.length, 0);
});
test("discarding a tab reports loss of the document instead of silently reconnecting", async () => {
  const b = browser(); await b.connect(); await b.update({discarded: true});
  assert.equal(b.session.bindings.length, 0); assert.equal(b.sent.length, 0);
});
test("an unapproved URL change during a request is rejected without replay", async () => {
  const b = browser([], {allowNavigation: false}); await b.connect(); const id = b.session.bindings[0].id;
  b.ports[0].onMessage.emit({type: "request", id: "do-not-move", bindingId: id, expectedModel: "Argon", prompt: "Offline test"});
  await settle(); await b.update({status: "loading", url: "https://gemini.google.com/app/other-chat"});
  const until = Date.now() + 12000;
  while (b.session.bindings.length && Date.now() < until) await new Promise(resolve => setTimeout(resolve, 20));
  assert.equal(b.session.bindings.length, 0); assert.equal(b.sent.length, 1); assert.deepEqual(b.cancels, ["do-not-move"]);
});

test("a selected generic HTTPS chatbot binds without a fixed model name and forwards observed metadata", async () => {
  const b = browser([], {url: "https://research.example/chat/new", detectedModel: "Website picker"});
  assert.equal((await b.connect("")).ok, true);
  const binding = b.session.bindings[0];
  assert.equal(binding.provider, "browser-web"); assert.equal(binding.model, "Theo website");
  b.ports[0].onMessage.emit({type: "request", id: "generic-turn", bindingId: binding.id, expectedModel: "Theo website", prompt: "Offline test"});
  await settle();
  b.content({type: "hub-observation", id: "generic-turn", nonce: binding.nonce, url: b.tab.url,
    detectedModel: "arbitrary-response-model", modelSource: "response", transport: "chat-completions"});
  b.content({type: "hub-result", id: "generic-turn", nonce: binding.nonce, url: b.tab.url, text: "Answer"});
  await settle();
  const state = b.ports[0].frames.filter(f => f.type === "state").at(-1).bindings[0];
  assert.equal(state.detectedModel, "arbitrary-response-model"); assert.equal(state.siteName, "research.example");
  assert.equal(state.model, "Theo website"); assert.equal(state.modelSource, "response");
  assert.equal(b.sent.length, 1); assert.ok(b.ports[0].frames.some(f => f.type === "result" && f.text === "Answer"));
});

test("unknown request and malformed model metadata cannot change a binding", async () => {
  const b = browser(); await b.connect(); const binding = b.session.bindings[0];
  b.ports[0].onMessage.emit({type: "request", id: "real-turn", bindingId: binding.id, expectedModel: "Argon", prompt: "Offline test"});
  await settle();
  for (const [id, model] of [["other-turn", "ignored"], ["real-turn", "bad\nmodel"]])
    b.content({type: "hub-observation", id, nonce: binding.nonce, url: b.tab.url, detectedModel: model, modelSource: "response", transport: "responses"});
  assert.equal(b.session.bindings[0].detectedModel, "");
  b.ports[0].disconnect();
});


test("gateway API calls require discovered models and do not fall back to composer automation", async () => {
  const b = browser(); await b.connect(); const binding = b.session.bindings[0];
  const model = {id: "web-0123456789abcdef01234567", name: "Website chosen", protocol: "gemini-rpc", source: "page"};
  b.content({type: "hub-api-models", nonce: binding.nonce, models: [model]}); await settle();
  assert.equal(b.session.bindings[0].apiModels.length, 1);
  b.ports[0].onMessage.emit({type: "api-request", id: "direct", bindingId: binding.id, modelId: model.id, prompt: "Hello"});
  await settle(); assert.equal(b.sent.length, 1); assert.equal(b.sent[0].type, "hub-api-request");
  b.content({type: "hub-result", id: "direct", nonce: binding.nonce, url: b.tab.url, text: "Hello teammate"}); await settle();
  assert.ok(b.ports[0].frames.some(f => f.type === "result" && f.id === "direct"));
  b.ports[0].onMessage.emit({type: "api-request", id: "unknown-api", bindingId: binding.id, modelId: "missing", prompt: "Hello"});
  await settle(); assert.equal(b.sent.length, 1);
  assert.ok(b.ports[0].frames.some(f => f.type === "error" && f.id === "unknown-api"));
});
