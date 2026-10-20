"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const rules = require("../browser-extension/discovery.js");
const input = values => ({visible: true, enabled: true, editable: true, multiline: true, ...values});
const send = values => ({visible: true, enabled: true, ...values});

test("hidden responsive editor cannot make the visible composer ambiguous", () => {
  const visible = input({trusted: true});
  assert.equal(rules.input([input({trusted: true, visible: false}), visible]), visible);
});
test("search field is excluded even when it matches an old selector", () => {
  const composer = input({composer: true, main: true});
  assert.equal(rules.input([input({trusted: true, excluded: true}), composer]), composer);
});
test("semantic main composer works after the vendor selector changes", () => {
  const composer = input({main: true, semantic: true, large: true});
  assert.equal(rules.input([input({main: true, large: true}), composer]), composer);
});
test("two equally plausible visible composers require explicit selection", () => {
  assert.equal(rules.input([input({trusted: true}), input({trusted: true})]), null);
});
test("unrelated visible textarea alone is insufficient evidence", () => {
  assert.equal(rules.input([input({main: true, large: true})]), null);
});
test("disabled and non-editable matches cannot be selected", () => {
  assert.equal(rules.input([input({trusted: true, enabled: false}), input({trusted: true, editable: false})]), null);
});
test("composer send button beats another visible send button", () => {
  const local = send({local: true, semantic: true, trusted: true});
  assert.equal(rules.send([send({trusted: true, semantic: true}), local]), local);
});
test("ambiguous send buttons do not trigger a click", () => {
  assert.equal(rules.send([send({local: true, semantic: true}), send({local: true, semantic: true})]), null);
});
test("stop controls and disabled controls cannot be mistaken for send", () => {
  assert.equal(rules.send([send({trusted: true, excluded: true}), send({trusted: true, enabled: false})]), null);
});
