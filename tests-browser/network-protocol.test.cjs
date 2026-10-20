"use strict";
const test = require("node:test"), assert = require("node:assert/strict");
const {decoder, matchesPrompt, MAX_BODY} = require("../browser-extension/network-protocol.js");
const providers = require("../browser-extension/providers.js");
function parse(frames) {
  const seen = [], d = decoder(value => seen.push(value));
  for (const frame of frames) d.push(frame);
  d.end(); return {seen, state: d.snapshot()};
}
test("models come from a selected HTTPS site, not an agent/model catalog", () => {
  assert.equal(providers.site("https://research.example/chat").id, "browser-web");
  assert.equal(providers.site("https://research.example/chat").name, "research.example");
  for (const url of ["http://research.example", "https://user:secret@research.example", "https://research.example:444", "chrome://extensions", "https://gemini.google.com/settings"])
    assert.equal(providers.site(url), null);
  assert.equal(providers.site("https://gemini.google.com/app").id, "gemini-web");
  assert.equal(providers.site("https://chatgpt.com/c/test").id, "chatgpt-web");
});
test("only the armed prompt in JSON or nested form payload correlates a response", () => {
  const prompt = "Nghiên cứu \"nhân quả\"\nÝ hai 🌏";
  assert.ok(matchesPrompt(JSON.stringify({messages: [{content: prompt}]}), prompt));
  assert.ok(matchesPrompt(new URLSearchParams({"f.req": JSON.stringify([JSON.stringify([prompt])])}).toString(), prompt));
  assert.equal(matchesPrompt(JSON.stringify({content: "Other prompt", token: "secret"}), prompt), false);
  assert.equal(matchesPrompt("x".repeat(MAX_BODY + 1), prompt), false);
});
test("split SSE chunks normalize answer and arbitrary observed model without leaking other fields", () => {
  const frame = 'data: {"choices":[{"index":0,"delta":{"content":"Chào 🌏"}}],"model":"future-model-42","token":"secret","usage":{"key":"hidden"}}\r\n\r\n';
  const {seen, state} = parse([frame.slice(0, 20), frame.slice(20), "data: [DONE]\n\n"]);
  assert.equal(state.text, "Chào 🌏"); assert.equal(state.model, "future-model-42"); assert.equal(state.done, true);
  assert.equal(JSON.stringify(seen).includes("secret"), false); assert.equal(JSON.stringify(seen).includes("hidden"), false);
});
test("EOF and an unrecognized DONE never certify completion", () => {
  assert.equal(parse(['data: {"choices":[{"index":0,"delta":{"content":"Partial"}}]}\n']).state.done, false);
  assert.equal(parse(['data: {"content":"Not an assistant message","model":"guess"}\ndata: [DONE]\n']).state.done, false);
});
test("truncated output fails instead of accepting a following DONE", () => {
  const {seen, state} = parse(['data: {"choices":[{"index":0,"delta":{"content":"Cut off"},"finish_reason":"length"}]}\ndata: [DONE]\n']);
  assert.equal(state.done, false); assert.ok(seen.some(v => v.error));
});
test("alternative choices and tool calls are not merged into a final answer", () => {
  const {state} = parse(['{"choices":[{"index":1,"delta":{"content":"Wrong"}},{"index":0,"delta":{"content":"Right"},"finish_reason":"tool_calls"}]}\n']);
  assert.equal(state.text, "Right"); assert.equal(state.done, false);
});
test("ChatGPT snapshots carry observed model and require successful end_turn", () => {
  const first = {message: {author: {role: "assistant"}, content: {content_type: "text", parts: ["Hello"]}, metadata: {model_slug: "web-model"}}};
  const last = structuredClone(first); last.message.content.parts = ["Hello world"]; last.message.end_turn = true; last.message.status = "finished_successfully";
  const {state} = parse([JSON.stringify(first) + "\n", JSON.stringify(last) + "\n"]);
  assert.equal(state.text, "Hello world"); assert.equal(state.model, "web-model"); assert.equal(state.done, true);
});
test("message streams and responses streams accept explicit lifecycle events", () => {
  const messages = parse(['data: {"type":"message_start","message":{"role":"assistant","model":"observed-id"}}\n',
    'data: {"type":"content_block_delta","delta":{"type":"thinking_delta","thinking":"private"}}\n',
    'data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"Answer"}}\n', 'data: {"type":"message_stop"}\n']);
  assert.equal(messages.state.model, "observed-id"); assert.equal(messages.state.text, "Answer"); assert.equal(messages.state.done, true);
  const responses = parse(['data: {"type":"response.output_text.delta","delta":"Answer"}\n',
    'data: {"type":"response.completed","response":{"model":"another-id"}}\n']);
  assert.equal(responses.state.model, "another-id"); assert.equal(responses.state.done, true);
});
test("pretty JSON generate-content ignores thought parts and captures response modelVersion", () => {
  const {state} = parse([JSON.stringify({candidates: [{index: 0, content: {parts: [{thought: true, text: "Hidden"}, {text: "Answer"}]}, finishReason: "STOP"}], modelVersion: "future-gemini"}, null, 2)]);
  assert.equal(state.text, "Answer"); assert.equal(state.model, "future-gemini"); assert.equal(state.done, true);
});
test("Gemini RPC framing decodes first candidate without guessing a model or treating EOF as completion", () => {
  const payload = [null, null, null, null, [["rc_one", ["First candidate"]], ["rc_two", ["Alternative"]]]];
  const {state} = parse([")]}\u0027\n123\n", JSON.stringify([["wrb.fr", "rpc", JSON.stringify(payload)]]) + "\n"]);
  assert.equal(state.text, "First candidate"); assert.equal(state.model, ""); assert.equal(state.done, false);
});
test("stream and answer limits reject oversized website data", () => {
  assert.throws(() => parse(["x".repeat(MAX_BODY + 1)]), /4 MiB/);
  assert.throws(() => parse([JSON.stringify({choices: [{index: 0, delta: {content: "x".repeat(64001)}}]}) + "\n"]), /64.000/);
});
