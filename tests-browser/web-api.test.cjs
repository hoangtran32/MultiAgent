"use strict";
const test = require("node:test"), assert = require("node:assert/strict"), vm = require("node:vm"), fs = require("node:fs"), path = require("node:path");
const {webcrypto, randomUUID} = require("node:crypto");
globalThis.crypto ||= webcrypto;
const api = require("../browser-extension/web-api-protocol.js");

test("API adapters recognize generation routes only on the linked origin", () => {
  assert.equal(api.kind("https://gemini.google.com/_/BardChatUi/data/assistant.lamda.BardFrontendService/StreamGenerate", "https://gemini.google.com"), "gemini-rpc");
  assert.equal(api.kind("/u/1/_/BardChatUi/data/assistant.lamda.BardFrontendService/StreamGenerate", "https://gemini.google.com"), "gemini-rpc");
  assert.equal(api.kind("/backend-api/f/conversation", "https://chatgpt.com"), "chatgpt-message");
  for (const url of ["https://evil.example/v1/chat/completions", "/api/auth/session", "/backend-api/f/conversation/prepare", "/login"])
    assert.equal(api.kind(url, "https://chatgpt.com"), "");
});
test("Gemini template replaces exactly one nested prompt and preserves model/session fields in browser", () => {
  const original = new URLSearchParams({"f.req": JSON.stringify([null, JSON.stringify([["hello"], ["en"], null, {flag: 7}])]), at: "private-csrf"});
  const prepared = api.prepare(original.toString(), "hello", "Xin chào\nĐồng đội", "gemini-rpc");
  const form = new URLSearchParams(prepared.body), inner = JSON.parse(JSON.parse(form.get("f.req"))[1]);
  assert.equal(inner[0][0], "Xin chào\nĐồng đội"); assert.equal(inner[3].flag, 7); assert.equal(form.get("at"), "private-csrf");
  for (const raw of [["hello", "hello"], ["hello", "c_existing"], ["hello", "rc_previous"]])
    assert.throws(() => api.prepare(new URLSearchParams({"f.req": JSON.stringify(raw)}).toString(), "hello", "new", "gemini-rpc"));
  assert.throws(() => api.prepare(original.toString(), "absent", "new", "gemini-rpc"));
});
test("ChatGPT templates keep the selected model and start independent message IDs without captured conversation history", () => {
  const original = {action: "next", model: "website-chosen", parent_message_id: "old-parent", messages: [{id: "old", author: {role: "user"}, content: {content_type: "text", parts: ["hello"]}}]};
  const a = api.prepare(JSON.stringify(original), "hello", "task", "chatgpt-message"), b = api.prepare(JSON.stringify(original), "hello", "other", "chatgpt-message");
  assert.equal(a.model, "website-chosen"); assert.equal(JSON.parse(a.body).messages[0].content.parts[0], "task");
  assert.notEqual(JSON.parse(a.body).parent_message_id, JSON.parse(b.body).parent_message_id);
  assert.throws(() => api.prepare(JSON.stringify({...original, conversation_id: "old-chat"}), "hello", "new", "chatgpt-message"));
  assert.throws(() => api.prepare(JSON.stringify({...original, messages: [{...original.messages[0], content: {content_type: "multimodal_text", parts: ["hello"]}}]}), "hello", "new", "chatgpt-message"));
});
test("Gemini completion requires complete UTF-8 length frames and rejects truncated tails", () => {
  const payload = JSON.stringify([["wrb.fr", null, JSON.stringify([null, ["rc_abc", ["Tiếng Việt 😀"]]])]]);
  const frame = ")]}'\n\n" + Buffer.byteLength(payload) + "\n" + payload + "\n";
  assert.equal(api.geminiComplete(frame), true);
  assert.equal(api.geminiComplete(frame.slice(0, -8)), false);
  assert.equal(api.geminiComplete(frame + "999\n["), false);
  assert.equal(api.geminiComplete(payload), false);
});
function page() {
  const listeners = [], frames = [], calls = [];
  const location = {origin: "https://research.example", href: "https://research.example/chat/new"};
  class Xhr { open() {} send() {} setRequestHeader() {} }
  const window = {addEventListener(name, fn) { if (name === "message") listeners.push(fn); },
    postMessage(value, origin) { assert.equal(origin, location.origin); frames.push(structuredClone(value)); for (const fn of listeners) fn({source: window, origin, data: value}); },
    fetch(input, init) { calls.push({input, init}); return Promise.resolve(new Response('data: {"choices":[{"index":0,"delta":{"content":"Answer"},"finish_reason":"stop"}]}\n\n', {headers: {"Content-Type": "text/event-stream"}})); }};
  const context = vm.createContext({window, location, XMLHttpRequest: Xhr, Request, Response, Headers, URL, URLSearchParams, TextDecoder, TextEncoder, crypto: {subtle: webcrypto.subtle, randomUUID}, AbortController, Date, Reflect, WeakMap, Set, Map, Promise});
  for (const name of ["network-protocol.js", "web-api-protocol.js", "web-api.js"]) vm.runInContext(fs.readFileSync(path.join(__dirname, "../browser-extension/", name), "utf8"), context);
  const control = (type, fields = {}) => window.postMessage({source: "multi-agent-hub-api-control", type, channel: "selected-document", ...fields}, location.origin);
  async function until(predicate) { const end = Date.now() + 2000; while (!predicate() && Date.now() < end) await new Promise(resolve => setTimeout(resolve, 5)); assert.ok(predicate()); }
  return {window, frames, calls, control, until};
}
test("web API broker stays dormant until linked and exports descriptors/text without browser credentials", async () => {
  const p = page();
  const request = {method: "POST", headers: {Authorization: "private-token", "Content-Type": "application/json", "openai-sentinel-proof-token": "never-reuse"}, body: JSON.stringify({model: "observed-model", messages: [{role: "user", content: "first"}]})};
  await p.window.fetch("/v1/chat/completions", request); assert.equal(p.frames.length, 0);
  p.control("link"); p.control("arm", {prompt: "first", label: "Chosen UI model"});
  await p.window.fetch("/v1/chat/completions", request);
  await p.until(() => p.frames.some(f => f.type === "models" && f.models?.length));
  const descriptor = p.frames.find(f => f.models?.length).models[0];
  assert.equal(descriptor.name, "Chosen UI model"); assert.equal(descriptor.source, "request");
  p.control("call", {id: "api-one", modelId: descriptor.id, prompt: "hello teammate"});
  await p.until(() => p.frames.some(f => f.id === "api-one" && f.type === "result"));
  assert.equal(p.calls.length, 3); assert.equal(JSON.parse(p.calls.at(-1).init.body).messages[0].content, "hello teammate");
  assert.equal(p.calls.at(-1).init.headers.get("authorization"), "private-token");
  assert.equal(p.calls.at(-1).init.headers.get("openai-sentinel-proof-token"), null);
  assert.equal(p.frames.find(f => f.id === "api-one" && f.type === "result").text, "Answer");
  const exported = JSON.stringify(p.frames.filter(f => f.source === "multi-agent-hub-web-api"));
  assert.ok(!exported.includes("private-token") && !exported.includes("never-reuse") && !exported.includes("first"));
  p.control("unlink"); p.control("link"); p.control("call", {id: "after-unlink", modelId: descriptor.id, prompt: "hello"});
  await p.until(() => p.frames.some(f => f.id === "after-unlink" && f.type === "error"));
  assert.equal(p.calls.length, 3);
});
test("unknown API model fails before any fetch, without fallback or model substitution", async () => {
  const p = page(); p.control("link"); p.control("call", {id: "missing", modelId: "unknown", prompt: "hello"});
  await p.until(() => p.frames.some(f => f.type === "error")); assert.equal(p.calls.length, 0);
});
