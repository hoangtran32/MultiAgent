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
  const session = {bindings: structuredClone(saved)}, local = {}, ports = [], sent = [], cancels = [];
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
        if (message.type === "hub-bind") return {ready: true, nonce, url: tab.url};
        if (message.type === "hub-ping") return {nonce, url: tab.url};
        if (message.type === "hub-request") { sent.push(structuredClone(message)); return {accepted: true}; }
        if (message.type === "hub-cancel") { cancels.push(message.id); return {ok: true}; }
        throw new Error("Unexpected content command: " + message.type);
      }}
  };
  const context = vm.createContext({chrome, crypto: {randomUUID}, URL, Date, setTimeout, clearTimeout, structuredClone});
  vm.runInContext(source, context, {filename: "background.js"});
  const popup = message => new Promise(resolve => listener(message, {id: chrome.runtime.id}, resolve));
  return {tab, ports, sent, cancels, session, popup, setNonce: value => { nonce = value; },
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
