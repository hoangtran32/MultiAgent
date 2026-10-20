"use strict";
const test = require("node:test"), assert = require("node:assert/strict"), vm = require("node:vm"), fs = require("node:fs"), path = require("node:path");
function page() {
  const listeners = [], frames = [], calls = [];
  const origin = "https://research.example", location = {origin, href: origin + "/chat"};
  class Xhr { open() {} send() {} }
  let payload = 'data: {"choices":[{"index":0,"delta":{"content":"Answer"},"finish_reason":"stop"}],"model":"discovered-model","auth":"never-export"}\n\n';
  let cloned = 0;
  const window = {addEventListener: (name, fn) => { if (name === "message") listeners.push(fn); },
    postMessage(value, target) { assert.equal(target, origin); frames.push(structuredClone(value)); for (const fn of listeners) fn({source: window, origin, data: value}); },
    fetch(input, init) {
      calls.push({input, init});
      const response = new Response(payload, {headers: {"Content-Type": "text/event-stream"}});
      const clone = response.clone.bind(response); response.clone = () => { cloned++; return clone(); };
      return Promise.resolve(response);
    }};
  const context = vm.createContext({window, location, XMLHttpRequest: Xhr, Request, Response, URL, URLSearchParams, TextDecoder, Date, Reflect, WeakMap, Set, Promise});
  for (const name of ["network-protocol.js", "network.js"]) vm.runInContext(fs.readFileSync(path.join(__dirname, "../browser-extension/", name), "utf8"), context);
  const control = type => window.postMessage({source: "multi-agent-hub-control", type, channel: "document-channel", id: "turn-one", prompt: "Unique prompt"}, origin);
  const finish = async () => {
    const until = Date.now() + 2000;
    while (!frames.some(f => f.source === "multi-agent-hub-network" && f.done) && Date.now() < until) await new Promise(resolve => setTimeout(resolve, 5));
  };
  return {window, control, frames, calls, finish, clones: () => cloned, setPayload: value => { payload = value; }};
}
test("observer is dormant outside a Hub turn and never changes the website response", async () => {
  const p = page(), response = await p.window.fetch("/chat", {method: "POST", body: JSON.stringify({prompt: "Unique prompt"})});
  assert.equal(p.clones(), 0); assert.ok((await response.text()).includes("Answer")); assert.equal(p.calls.length, 1);
});
test("only matching same-origin prompt traffic yields normalized model/text and never replays", async () => {
  const p = page(); p.control("arm");
  await (await p.window.fetch("https://other.example/chat", {method: "POST", body: JSON.stringify({prompt: "Unique prompt"})})).text();
  await (await p.window.fetch("/chat", {method: "POST", body: JSON.stringify({prompt: "Unrelated"})})).text();
  await (await p.window.fetch("/chat", {method: "POST", headers: {Authorization: "credential-stays-in-page"}, body: JSON.stringify({prompt: "Unique prompt"})})).text();
  await p.finish();
  const frames = p.frames.filter(f => f.source === "multi-agent-hub-network" && f.protocol !== "armed");
  assert.equal(p.clones(), 1); assert.equal(p.calls.length, 3);
  assert.equal(frames.at(-1).text, "Answer"); assert.equal(frames.at(-1).model, "discovered-model"); assert.equal(frames.at(-1).done, true);
  assert.ok(!JSON.stringify(frames).includes("never-export")); assert.ok(!JSON.stringify(frames).includes("credential-stays-in-page"));
  p.control("disarm");
  await (await p.window.fetch("/chat", {method: "POST", body: JSON.stringify({prompt: "Unique prompt"})})).text();
  assert.equal(p.clones(), 1); assert.equal(p.calls.length, 4);
});

test("textual Request objects without content-length are read through a bounded clone", async () => {
  const p = page(); p.control("arm");
  const request = new Request("https://research.example/chat", {method: "POST", headers: {"content-type": "application/json"}, body: JSON.stringify({prompt: "Unique prompt"})});
  await (await p.window.fetch(request)).text(); await request.text(); await p.finish();
  assert.equal(p.clones(), 1);
  assert.ok(p.frames.some(f => f.done && f.model === "discovered-model"));
  assert.equal(p.calls.length, 1);
});
