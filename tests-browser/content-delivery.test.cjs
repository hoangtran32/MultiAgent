"use strict";
// Offline DOM/IPC fixture. No browser account, live website, inference or screenshot is used.
const test = require("node:test"), assert = require("node:assert/strict"), vm = require("node:vm"), fs = require("node:fs"), path = require("node:path");
const rules = require("../browser-extension/discovery.js");
function fixture({provider = "chatgpt-web", fast = true} = {}) {
  const location = {origin: provider === "chatgpt-web" ? "https://chatgpt.com" : "https://gemini.google.com"};
  location.href = location.origin + (provider === "chatgpt-web" ? "/" : "/app");
  const frames = [], events = new Map(), windowEvents = new Map(); let listener, user = null, assistant = null, generating = false;
  const prompt = "Bạn là một thành viên trong nhóm, hãy nói chuyện cùng người dùng và đồng đội. ".repeat(20) + "hello";
  class Element {
    constructor(kind, text = "") { this.kind = kind; this.textContent = text; this.innerText = text; this.isConnected = true; this.children = []; this.clientHeight = 60; this.scrollHeight = 60; }
    getClientRects() { return [{}]; }
    getBoundingClientRect() { return {width: 500, height: 90}; }
    getAttribute(key) { if (key === "aria-label") return this.kind === "send" ? "Send message" : this.kind === "toggle" ? "Xem thêm" : this.kind === "copy" ? "Sao chép" : this.kind === "input" ? "Message" : null; return null; }
    contains(other) { return other === this || this.children.some(c => c.contains(other)); }
    closest(selector) { if (selector === "form" && this === input) return form; return null; }
    matches() { return false; }
    querySelectorAll(selector) {
      if (this.kind === "user" && selector.includes('aria-expanded')) return [toggle];
      if (this.kind === "assistant" && selector === "response") return [response];
      if (this.kind === "assistant" && selector === "actions") return generating ? [] : [copy];
      return [];
    }
    cloneNode() { return new Element("clone", this.textContent); }
    compareDocumentPosition(other) { return this === user && other === assistant ? 4 : 0; }
    append(value) { this.textContent += value; }
    remove() { this.isConnected = false; }
  }
  class TextArea extends Element {
    constructor() { super("input"); this._value = ""; }
    get value() { return this._value; } set value(value) { this._value = value; }
    focus() {} dispatchEvent() { return true; }
  }
  class Input extends Element {}
  const input = new TextArea(), send = new Element("send"), form = new Element("form"), toggle = new Element("toggle", "Xem thêm"), copy = new Element("copy");
  const response = new Element("response", "Xin chào bạn và chatbot đồng đội!");
  input.parentElement = form; form.children = [input, send];
  send.click = () => {
    user = new Element("user", prompt.slice(0, 240) + "…"); user.children = [toggle];
    assistant = new Element("assistant"); input.value = ""; generating = !fast;
    location.href = location.origin + (provider === "chatgpt-web" ? "/c/assigned" : "/app/assigned");
  };
  const selectors = {input: "input", send: "send", user: "user", assistant: "assistant", response: "response", stop: "stop", actions: "actions", busy: "busy"};
  const document = {
    addEventListener(name, fn) { if (!events.has(name)) events.set(name, []); events.get(name).push(fn); },
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; },
    querySelectorAll(selector) {
      if (selector === "input") return [input]; if (selector === "send") return [send];
      if (selector === "user") return user ? [user] : []; if (selector === "assistant") return assistant ? [assistant] : [];
      if (selector === "stop") return generating ? [send] : []; return [];
    }
  };
  const window = {
    addEventListener(name, fn) { if (!windowEvents.has(name)) windowEvents.set(name, []); windowEvents.get(name).push(fn); },
    postMessage(value) {
      const deliver = data => { for (const fn of windowEvents.get("message") || []) fn({source: window, origin: location.origin, data}); };
      deliver(value);
      if (value.source === "multi-agent-hub-control" && value.type === "arm")
        deliver({source: "multi-agent-hub-network", protocol: "armed", channel: value.channel, id: value.id});
    }
  };
  const chrome = {runtime: {id: "fixture-extension", onMessage: {addListener(fn) { listener = fn; }}, sendMessage: async frame => { frames.push(structuredClone(frame)); }}};
  class Event { constructor(type, values) { this.type = type; Object.assign(this, values); } }
  const context = vm.createContext({window, document, chrome, location, URL, Date, Set, Map, Node: {DOCUMENT_POSITION_FOLLOWING: 4}, HTMLTextAreaElement: TextArea,
    HTMLInputElement: Input, InputEvent: Event, Event, crypto: {randomUUID: () => "document-nonce"}, getComputedStyle: () => ({visibility: "visible", display: "block"}),
    setTimeout, clearTimeout, MultiAgentHubDiscovery: rules,
    MultiAgentHubProviders: {providerOf: () => provider, selectors: () => ({...selectors}), fresh: url => new URL(url).pathname === (provider === "chatgpt-web" ? "/" : "/app")}});
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../browser-extension/content.js"), "utf8"), context);
  const message = data => new Promise(resolve => listener(data, {id: chrome.runtime.id}, resolve));
  async function until(predicate) { const end = Date.now() + 3500; while (!predicate() && Date.now() < end) await new Promise(resolve => setTimeout(resolve, 10)); assert.ok(predicate()); }
  return {frames, prompt, selectors, message, until, location, documentEvents: events, input, done() { generating = false; }};
}
for (const provider of ["chatgpt-web", "gemini-web"]) {
  test("folded first prompt and already completed answer are delivered after URL commit: " + provider, async () => {
    const f = fixture({provider}), initial = f.location.href;
    const binding = await f.message({type: "hub-bind", provider, selectors: f.selectors}); assert.equal(binding.ready, true);
    const request = await f.message({type: "hub-request", id: "turn", nonce: binding.nonce, url: initial, prompt: f.prompt, selectors: f.selectors});
    assert.equal(request.accepted, true); await f.until(() => f.location.href !== initial);
    const navigation = {id: "turn", nonce: binding.nonce, from: initial, to: f.location.href};
    const proof = await f.message({type: "hub-navigation", ...navigation}); assert.equal(proof.allowed, true);
    await new Promise(resolve => setTimeout(resolve, 100)); assert.ok(!f.frames.some(frame => frame.type === "hub-result"));
    const commit = await f.message({type: "hub-navigation-commit", ...navigation}); assert.equal(commit.committed, true);
    await f.until(() => f.frames.some(frame => frame.type === "hub-result" || frame.type === "hub-error"));
    assert.equal(f.frames.find(frame => frame.type === "hub-result")?.text, "Xin chào bạn và chatbot đồng đội!");
    assert.ok(!f.frames.some(frame => frame.type === "hub-error"));
  });
}
test("a trusted conversation switch invalidates a folded-prefix navigation proof", async () => {
  const f = fixture(), initial = f.location.href;
  const binding = await f.message({type: "hub-bind", provider: "chatgpt-web", selectors: f.selectors});
  await f.message({type: "hub-request", id: "turn", nonce: binding.nonce, url: initial, prompt: f.prompt, selectors: f.selectors});
  await f.until(() => f.location.href !== initial);
  const anchor = {getAttribute: () => "/c/unrelated"};
  for (const handler of f.documentEvents.get("click")) handler({isTrusted: true, target: {closest: selector => selector === 'a[href]' ? anchor : null}});
  const proof = await f.message({type: "hub-navigation", id: "turn", nonce: binding.nonce, from: initial, to: f.location.href}); assert.equal(proof.allowed, false);
  await f.until(() => f.frames.some(frame => frame.type === "hub-error")); assert.ok(!f.frames.some(frame => frame.type === "hub-result"));
});
